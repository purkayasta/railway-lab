using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Platform.Messaging;
using Shouldly;
using Wolverine;

namespace Platform.Tests;

// Sample feature written the way every service is: command record -> static handler -> DbContext.
public record CreateNote(string Title, bool Fail = false);

public class Note
{
    public long Id { get; set; }
    public string Title { get; set; } = "";
}

public class SampleDb(DbContextOptions<SampleDb> options) : DbContext(options)
{
    public DbSet<Note> Notes => Set<Note>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.HasDefaultSchema("schedule");
        b.Entity<Note>().ToTable("Notes");
    }
}

public static class CreateNoteHandler
{
    public static async Task<long> Handle(CreateNote cmd, SampleDb db, CancellationToken ct)
    {
        var note = new Note { Title = cmd.Title };
        db.Notes.Add(note);
        await db.SaveChangesAsync(ct);
        // Fails after the row is written: the whole transaction must roll back.
        if (cmd.Fail) throw new InvalidOperationException("handler failed after saving");
        return note.Id;
    }
}

[Collection(SqlServerCollection.Name)]
public class MessagingTests(SqlServerFixture db)
{
    private async Task<WebApplication> StartAsync()
    {
        var cs = db.ConnectionFor("schedule");
        await using (var conn = new SqlConnection(cs))
        {
            await conn.OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                IF OBJECT_ID('schedule.Notes') IS NULL
                    CREATE TABLE schedule.Notes (Id bigint IDENTITY PRIMARY KEY, Title nvarchar(100) NOT NULL);
                """;
            await cmd.ExecuteNonQueryAsync();
        }

        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = Environments.Production });
        builder.WebHost.UseTestServer();
        builder.Configuration["ConnectionStrings:Default"] = cs;
        builder.AddServiceMessaging(typeof(CreateNoteHandler).Assembly, "schedule");
        builder.Services.AddServiceDbContext<SampleDb>(cs, "schedule");

        var app = builder.Build();
        await app.StartAsync();
        return app;
    }

    private async Task<int> CountAsync(string title)
    {
        await using var conn = new SqlConnection(db.ConnectionFor("schedule"));
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM schedule.Notes WHERE Title = @t";
        cmd.Parameters.AddWithValue("@t", title);
        return (int)(await cmd.ExecuteScalarAsync())!;
    }

    [Fact]
    public async Task Command_sent_through_the_bus_runs_its_handler_and_commits()
    {
        const string title = "commit";
        await using var app = await StartAsync();
        var bus = app.Services.GetRequiredService<IMessageBus>();

        var id = await bus.InvokeAsync<long>(new CreateNote(title));

        id.ShouldBeGreaterThan(0);
        (await CountAsync(title)).ShouldBe(1);
    }

    [Fact]
    public async Task Failing_handler_rolls_back_everything_it_saved()
    {
        const string title = "rollback";
        await using var app = await StartAsync();
        var bus = app.Services.GetRequiredService<IMessageBus>();

        var ex = await Should.ThrowAsync<InvalidOperationException>(() => bus.InvokeAsync<long>(new CreateNote(title, Fail: true)));
        ex.Message.ShouldBe("handler failed after saving");

        (await CountAsync(title)).ShouldBe(0);
    }
}
