using System.Data;
using Microsoft.EntityFrameworkCore;
using Reservation.Data;

namespace Reservation.Services;

public sealed class PolicyService(ReservationDbContext db)
{
    public Task<PolicySetting[]> GetAll(CancellationToken cancellationToken = default) =>
        db.Policies.AsNoTracking().OrderBy(x => x.Key).ToArrayAsync(cancellationToken);

    public async Task<int> Update(string key, int value, Guid userId, CancellationToken cancellationToken = default)
    {
        if (value < 1)
        {
            return 400;
        }

        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
        await db.Database.ExecuteSqlRawAsync("SET LOCK_TIMEOUT 5000", cancellationToken);

        var setting = await db.Policies
            .FromSqlInterpolated($"SELECT * FROM reservation.Policies WITH (UPDLOCK, HOLDLOCK) WHERE [Key] = {key}")
            .SingleOrDefaultAsync(cancellationToken);

        if (setting is null)
        {
            return 404;
        }

        var now = DateTime.UtcNow;
        db.PolicyAudits.Add(new PolicyAudit
        {
            Key = setting.Key,
            OldValue = setting.Value,
            NewValue = value,
            ChangedBy = userId,
            ChangedAtUtc = now
        });

        setting.Value = value;
        setting.UpdatedBy = userId;
        setting.UpdatedAtUtc = now;

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return 204;
    }
}
