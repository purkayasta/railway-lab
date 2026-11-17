using System.Diagnostics;
using Microsoft.Data.SqlClient;
using Testcontainers.MsSql;
using Xunit.Abstractions;

namespace Schedule.Tests;

[Collection("SQL integration")]
public class LockingComparisonTests(ITestOutputHelper output)
{
    [Fact]
    public async Task Compare_update_lock_with_optimistic_rowversion_under_contention()
    {
        await using var sql = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2025-latest").Build();
        await sql.StartAsync();
        await using (var setup = new SqlConnection(sql.GetConnectionString()))
        {
            await setup.OpenAsync();
            await using var command = new SqlCommand("CREATE TABLE dbo.LockComparison (Id int NOT NULL PRIMARY KEY, Claimed bit NOT NULL, Version rowversion); INSERT INTO dbo.LockComparison (Id, Claimed) VALUES (1, 0)", setup);
            await command.ExecuteNonQueryAsync();
        }

        foreach (var optimistic in new[] { false, true })
        {
            await using (var reset = new SqlConnection(sql.GetConnectionString()))
            {
                await reset.OpenAsync();
                await using var resetCommand = new SqlCommand("UPDATE dbo.LockComparison SET Claimed = 0 WHERE Id = 1", reset);
                await resetCommand.ExecuteNonQueryAsync();
            }

            var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var arrivals = 0;
            var timer = Stopwatch.StartNew();
            async Task<bool> Claim()
            {
                await using var connection = new SqlConnection(sql.GetConnectionString());
                await connection.OpenAsync();
                byte[]? version = null;
                if (optimistic)
                {
                    await using var read = new SqlCommand("SELECT Version FROM dbo.LockComparison WHERE Id = 1", connection);
                    version = (byte[])(await read.ExecuteScalarAsync())!;
                }

                if (Interlocked.Increment(ref arrivals) == 50)
                {
                    ready.SetResult();
                }

                await ready.Task.WaitAsync(TimeSpan.FromSeconds(30));
                if (optimistic)
                {
                    await using var update = new SqlCommand("UPDATE dbo.LockComparison SET Claimed = 1 WHERE Id = 1 AND Claimed = 0 AND Version = @version", connection);
                    update.Parameters.AddWithValue("@version", version!);
                    return await update.ExecuteNonQueryAsync() == 1;
                }

                await using var transaction = await connection.BeginTransactionAsync();
                await using var claim = new SqlCommand("UPDATE dbo.LockComparison WITH (UPDLOCK, ROWLOCK) SET Claimed = 1 WHERE Id = 1 AND Claimed = 0", connection, (SqlTransaction)transaction);
                var success = await claim.ExecuteNonQueryAsync() == 1;
                await transaction.CommitAsync();
                return success;
            }

            var results = await Task.WhenAll(Enumerable.Range(0, 50).Select(_ => Claim()));
            timer.Stop();
            Assert.Single(results, x => x);
            output.WriteLine($"{(optimistic ? "rowversion" : "update lock")}: {timer.Elapsed.TotalMilliseconds:F0} ms, {50 / timer.Elapsed.TotalSeconds:F1} attempts/s, {results.Count(x => !x)} conflicts / 50 attempts");
        }
    }
}
