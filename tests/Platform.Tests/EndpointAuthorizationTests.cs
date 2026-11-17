using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Platform.Auth;
using Shouldly;

namespace Platform.Tests;

public class EndpointAuthorizationTests
{
    private static readonly HashSet<string> Allowlist = ["GET /health/live", "GET /api/schedule/stations"];

    private static StaticPermissionSource Seed() => StaticPermissionSource.FromFile(PermissionSourceTests.MappingPath());

    private static IReadOnlyList<string> Audit(Action<WebApplication> map)
    {
        var app = WebApplication.CreateBuilder().Build();
        map(app);
        var endpoints = ((IEndpointRouteBuilder)app).DataSources.SelectMany(d => d.Endpoints);
        return EndpointAuthorizationAudit.Audit(endpoints, Allowlist, Seed());
    }

    [Fact]
    public void Well_declared_endpoints_pass()
    {
        Audit(app =>
        {
            app.MapGet("/health/live", () => "ok").AllowAnonymous();
            app.MapGet("/api/schedule/stations", () => "ok").AllowAnonymous();
            app.MapPost("/api/reservation/holds", () => "ok").RequirePermission(Permissions.SeatsHold);
        }).ShouldBeEmpty();
    }

    [Fact]
    public void Endpoint_without_permission_or_anonymous_fails()
    {
        var v = Audit(app => app.MapGet("/api/oops", () => "x"));
        v.ShouldHaveSingleItem().ShouldContain("no permission");
    }

    [Fact]
    public void Endpoint_with_plain_RequireAuthorization_fails()
    {
        var v = Audit(app => app.MapGet("/api/oops", () => "x").RequireAuthorization());
        v.ShouldHaveSingleItem().ShouldContain("no permission");
    }

    [Fact]
    public void Anonymous_endpoint_not_on_allowlist_fails()
    {
        var v = Audit(app => app.MapGet("/api/new-public", () => "x").AllowAnonymous());
        v.ShouldHaveSingleItem().ShouldContain("allowlist");
    }

    [Fact]
    public void Anonymous_write_endpoint_fails()
    {
        var v = Audit(app => app.MapPost("/health/live", () => "x").AllowAnonymous());
        v.ShouldContain(x => x.Contains("GET/HEAD/OPTIONS only"));
    }

    [Fact]
    public void Permission_missing_from_catalog_fails()
    {
        var v = Audit(app => app.MapGet("/api/x", () => "x").RequirePermission("made:up"));
        v.ShouldHaveSingleItem().ShouldContain("not in the catalog");
    }

    [Fact]
    public void Permission_in_catalog_but_not_seeded_for_admin_fails()
    {
        var json = """
        {"permissions":[{"name":"a:b","description":"d"}],"roles":{"admin":[]}}
        """;
        var app = WebApplication.CreateBuilder().Build();
        app.MapGet("/api/x", () => "x").RequirePermission("a:b");
        var endpoints = ((IEndpointRouteBuilder)app).DataSources.SelectMany(d => d.Endpoints);

        EndpointAuthorizationAudit.Audit(endpoints, Allowlist, new StaticPermissionSource(json))
            .ShouldHaveSingleItem().ShouldContain("not granted");
    }

    [Fact]
    public void Seed_admin_holds_every_catalog_permission_and_every_constant()
    {
        EndpointAuthorizationAudit.AuditCatalog(Seed()).ShouldBeEmpty();
        var constants = typeof(Permissions).GetFields().Where(f => f.IsLiteral).Select(f => (string)f.GetRawConstantValue()!);
        constants.ShouldAllBe(c => Seed().Catalog.Contains(c));
    }
}
