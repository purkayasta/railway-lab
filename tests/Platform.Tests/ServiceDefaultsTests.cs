using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Hosting;
using Shouldly;

namespace Platform.Tests;

[Collection(SqlServerCollection.Name)]
public class ServiceDefaultsTests(SqlServerFixture db)
{
    private static async Task<WebApplication> StartAsync(string connectionString)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = Environments.Production });
        builder.WebHost.UseTestServer();
        builder.Configuration["ConnectionStrings:Default"] = connectionString;
        builder.AddServiceDefaults("test");

        var app = builder.Build();
        app.UseServiceDefaults();
        app.MapGet("/boom", () => { throw new InvalidOperationException("secret detail"); });
        await app.StartAsync();
        return app;
    }

    [Fact]
    public async Task Live_is_200_even_when_database_is_down()
    {
        await using var app = await StartAsync("Server=127.0.0.1,1;User Id=x;Password=x;TrustServerCertificate=True");
        var res = await app.GetTestClient().GetAsync("/health/live");
        res.StatusCode.ShouldBe(System.Net.HttpStatusCode.OK);
    }

    [Fact]
    public async Task Ready_is_503_when_database_is_unreachable()
    {
        await using var app = await StartAsync("Server=127.0.0.1,1;User Id=x;Password=x;TrustServerCertificate=True");
        var res = await app.GetTestClient().GetAsync("/health/ready");
        res.StatusCode.ShouldBe(System.Net.HttpStatusCode.ServiceUnavailable);
    }

    [Fact]
    public async Task Ready_is_200_when_database_is_reachable()
    {
        await using var app = await StartAsync(db.ConnectionFor("schedule"));
        var res = await app.GetTestClient().GetAsync("/health/ready");
        res.StatusCode.ShouldBe(System.Net.HttpStatusCode.OK);
    }

    [Fact]
    public async Task Unhandled_exception_returns_problem_details_without_leaking_message()
    {
        await using var app = await StartAsync("Server=127.0.0.1,1;User Id=x;Password=x;TrustServerCertificate=True");
        var res = await app.GetTestClient().GetAsync("/boom");
        res.StatusCode.ShouldBe(System.Net.HttpStatusCode.InternalServerError);
        res.Content.Headers.ContentType!.MediaType.ShouldBe("application/problem+json");
        var body = await res.Content.ReadAsStringAsync();
        body.ShouldContain("\"status\":500");
        body.ShouldContain("traceId");
        body.ShouldNotContain("secret detail");
    }

    [Fact]
    public async Task Unknown_route_returns_problem_details_404()
    {
        await using var app = await StartAsync("Server=127.0.0.1,1;User Id=x;Password=x;TrustServerCertificate=True");
        var res = await app.GetTestClient().GetAsync("/nope");
        res.StatusCode.ShouldBe(System.Net.HttpStatusCode.NotFound);
        res.Content.Headers.ContentType!.MediaType.ShouldBe("application/problem+json");
    }
}
