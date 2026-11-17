using Microsoft.Data.SqlClient;
using Shouldly;
using Testcontainers.MsSql;

namespace Platform.Tests;

[CollectionDefinition(Name)]
public sealed class SqlServerCollection : ICollectionFixture<SqlServerFixture>
{
    public const string Name = "sqlserver";
}

public sealed class SqlServerFixture : IAsyncLifetime
{
    public static readonly string[] Services = ["schedule", "reservation", "payment", "analytics", "helpdesk", "auth"];

    private readonly MsSqlContainer _container = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2025-latest").Build();

    public string Password(string service) => $"Test_{service}_Pa55word!";

    public string ConnectionFor(string service)
    {
        var b = new SqlConnectionStringBuilder(_container.GetConnectionString())
        {
            InitialCatalog = "RailwayLab",
            UserID = service + "_svc",
            Password = Password(service),
            TrustServerCertificate = true,
        };
        return b.ConnectionString;
    }

    public async Task InitializeAsync()
    {
        await _container.StartAsync();

        var script = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "sql", "init.sql"));
        foreach (var s in Services)
            script = script.Replace($"$({s.ToUpperInvariant()}_DB_PASSWORD)", Password(s));

        // sqlcmd batches are separated by GO lines; SqlClient needs them split.
        var batches = System.Text.RegularExpressions.Regex.Split(script, @"^\s*GO\s*$", System.Text.RegularExpressions.RegexOptions.Multiline | System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        await using var conn = new SqlConnection(_container.GetConnectionString());
        await conn.OpenAsync();
        foreach (var batch in batches.Where(b => !string.IsNullOrWhiteSpace(b)))
        {
            // USE only affects the session; each batch runs on the same open connection.
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = batch;
            await cmd.ExecuteNonQueryAsync();
        }
    }

    public async Task DisposeAsync() => await _container.DisposeAsync();
}

[Collection(SqlServerCollection.Name)]
public class SchemaIsolationTests(SqlServerFixture db)
{
    public static IEnumerable<object[]> Services() =>
        SqlServerFixture.Services.Select(s => new object[] { s });

    [Theory]
    [MemberData(nameof(Services))]
    public async Task Login_can_create_and_use_tables_in_its_own_schema(string service)
    {
        await using var conn = new SqlConnection(db.ConnectionFor(service));
        await conn.OpenAsync();

        await Exec(conn, $"CREATE TABLE [{service}].Own (Id bigint IDENTITY PRIMARY KEY, Name nvarchar(20));");
        await Exec(conn, $"INSERT [{service}].Own (Name) VALUES (N'ok');");
        (await Scalar(conn, $"SELECT COUNT(*) FROM [{service}].Own;")).ShouldBe(1);
    }

    [Theory]
    [MemberData(nameof(Services))]
    public async Task Login_is_denied_on_every_other_schema(string service)
    {
        await using var owner = new SqlConnection(db.ConnectionFor(service));
        await owner.OpenAsync();

        foreach (var other in SqlServerFixture.Services.Where(s => s != service))
        {
            // The other service creates its table; this login must not see or write to it.
            await using var otherConn = new SqlConnection(db.ConnectionFor(other));
            await otherConn.OpenAsync();
            await Exec(otherConn, $"IF OBJECT_ID('[{other}].Secret') IS NULL CREATE TABLE [{other}].Secret (Id int);");

            await Should.ThrowAsync<SqlException>(() => Scalar(owner, $"SELECT COUNT(*) FROM [{other}].Secret;"))
                .ContinueWith(t => t.Result.Number.ShouldBe(229));   // SELECT permission denied
            await Should.ThrowAsync<SqlException>(() => Exec(owner, $"INSERT [{other}].Secret (Id) VALUES (1);"))
                .ContinueWith(t => t.Result.Number.ShouldBe(229));
            await Should.ThrowAsync<SqlException>(() => Exec(owner, $"CREATE TABLE [{other}].Intruder (Id int);"))
                .ContinueWith(t => t.Result.Number.ShouldBe(2760)); // schema does not exist or no permission
        }
    }

    private static async Task Exec(SqlConnection c, string sql)
    {
        await using var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task<int> Scalar(SqlConnection c, string sql)
    {
        await using var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        return (int)(await cmd.ExecuteScalarAsync())!;
    }
}
