using Platform.Auth;
using Shouldly;

namespace Platform.Tests;

public class PermissionSourceTests
{
    internal static string MappingPath()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "RailwayLab.sln"))) dir = dir.Parent;
        return Path.Combine(dir!.FullName, "deploy", "auth", "default-role-permissions.json");
    }

    private static StaticPermissionSource Seed() => StaticPermissionSource.FromFile(MappingPath());

    private static IEnumerable<string> AllPermissionConstants() =>
        typeof(Permissions).GetFields().Where(f => f.IsLiteral).Select(f => (string)f.GetRawConstantValue()!);

    [Fact]
    public void Passenger_gets_only_passenger_permissions()
    {
        Seed().GetPermissions(["passenger"]).ShouldBe(
            [Permissions.SeatsHold, Permissions.BookingsReadOwn, Permissions.PaymentsPay, Permissions.HelpdeskChat,
             Permissions.SessionsRevokeOwn],
            ignoreOrder: true);
    }

    [Fact]
    public void Station_agent_is_passenger_plus_walk_in_and_support_reads()
    {
        var passenger = Seed().GetPermissions(["passenger"]);
        var agent = Seed().GetPermissions(["station-agent"]);

        passenger.All(agent.Contains).ShouldBeTrue();
        agent.Except(passenger).ShouldBe([Permissions.BookingsCreateOnBehalf, Permissions.BookingsReadAny], ignoreOrder: true);
    }

    [Fact]
    public void Operations_is_read_only()
    {
        Seed().GetPermissions(["operations"]).ShouldBe(
            [Permissions.ReportsRead, Permissions.PaymentsOpsRead, Permissions.BookingsReadAny, Permissions.PolicyRead, Permissions.SessionsRevokeOwn],
            ignoreOrder: true);
    }

    [Fact]
    public void Several_roles_give_the_union()
    {
        var union = Seed().GetPermissions(["passenger", "operations"]);

        union.ShouldContain(Permissions.SeatsHold);
        union.ShouldContain(Permissions.ReportsRead);
        union.Count.ShouldBe(9); // 5 + 5, sessions:revoke-own is shared
    }

    [Theory]
    [InlineData("unknown-role")]
    [InlineData("service")]   // machine role: grants the internal endpoint only, nothing in the mapping
    [InlineData("ADMIN")]     // role names are case-sensitive
    public void Unknown_roles_are_denied_everything(string role)
    {
        Seed().GetPermissions([role]).ShouldBeEmpty();
    }

    [Fact]
    public void No_roles_means_no_permissions()
    {
        Seed().GetPermissions([]).ShouldBeEmpty();
    }

    [Fact]
    public void Admin_holds_every_permission_in_the_catalog()
    {
        var seed = Seed();
        seed.GetPermissions(["admin"]).ShouldBe(seed.Catalog, ignoreOrder: true);
    }

    [Fact]
    public void Every_permission_constant_is_in_the_catalog_and_the_catalog_has_no_extras()
    {
        Seed().Catalog.ShouldBe(AllPermissionConstants(), ignoreOrder: true);
    }

    [Fact]
    public void A_role_that_references_an_unknown_permission_is_rejected_at_load()
    {
        const string json = """{ "permissions": [ { "name": "a:b", "service": "s", "description": "d" } ], "roles": { "r": ["a:b", "x:y"] } }""";
        Should.Throw<InvalidOperationException>(() => new StaticPermissionSource(json)).Message.ShouldContain("x:y");
    }
}
