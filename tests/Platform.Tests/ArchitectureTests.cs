using System.Text.RegularExpressions;
using Shouldly;

namespace Platform.Tests;

public partial class ArchitectureTests
{
    [GeneratedRegex(@"\b\w*DbContext\b|\b\w+Db\b")]
    private static partial Regex DbAccess();

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "RailwayLab.sln"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("RailwayLab.sln not found above the test output folder.");
    }

    /// <summary>Endpoint files are named *Endpoint.cs / *Endpoints.cs; they must talk to the bus, never to a DbContext.</summary>
    public static IReadOnlyList<string> FindEndpointFilesUsingDbContext(string folder) =>
        Directory.EnumerateFiles(folder, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                     && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            .Where(f => Regex.IsMatch(Path.GetFileName(f), @"Endpoints?\.cs$"))
            .Where(f => DbAccess().IsMatch(File.ReadAllText(f)))
            .ToList();

    [Fact]
    public void No_endpoint_file_in_src_references_a_DbContext()
    {
        FindEndpointFilesUsingDbContext(Path.Combine(RepoRoot(), "src")).ShouldBeEmpty();
    }

    [Fact]
    public void The_check_flags_an_endpoint_that_injects_a_DbContext()
    {
        var dir = Directory.CreateTempSubdirectory();
        try
        {
            File.WriteAllText(Path.Combine(dir.FullName, "BadEndpoint.cs"),
                "app.MapGet(\"/x\", (ReservationDb db) => db.Seats.Count());");
            File.WriteAllText(Path.Combine(dir.FullName, "GoodEndpoint.cs"),
                "app.MapGet(\"/x\", (IMessageBus bus) => bus.InvokeAsync<int>(new CountSeats()));");
            File.WriteAllText(Path.Combine(dir.FullName, "SomeHandler.cs"),
                "public static class SomeHandler { public static int Handle(CountSeats c, ReservationDb db) => 1; }");

            var offenders = FindEndpointFilesUsingDbContext(dir.FullName);

            offenders.Count.ShouldBe(1);
            Path.GetFileName(offenders[0]).ShouldBe("BadEndpoint.cs");
        }
        finally { dir.Delete(recursive: true); }
    }
}
