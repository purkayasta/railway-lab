using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Platform;

internal sealed class SqlServerHealthCheck(string connectionString) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken ct = default)
    {
        try
        {
            // Fail fast: a readiness probe should not hang on an unreachable server.
            var builder = new SqlConnectionStringBuilder(connectionString) { ConnectTimeout = 3 };
            await using var conn = new SqlConnection(builder.ConnectionString);
            await conn.OpenAsync(ct);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT 1";
            await cmd.ExecuteScalarAsync(ct);
            return HealthCheckResult.Healthy();
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("Database unreachable", ex);
        }
    }
}
