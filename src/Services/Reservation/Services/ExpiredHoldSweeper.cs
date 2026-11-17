using System.Data;
using Microsoft.EntityFrameworkCore;
using Reservation.Data;

namespace Reservation.Services;

public sealed class ExpiredHoldSweeper(ReservationDbContext db)
{
    public async Task<int> Sweep(CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        var tripIds = await (from hold in db.Holds.AsNoTracking()
                             join seat in db.Seats.AsNoTracking() on hold.TripSeatId equals seat.Id
                             where hold.ExpiresAtUtc <= now
                             select seat.TripPublicId).Distinct().Take(100).ToArrayAsync(cancellationToken);
        var released = 0;

        foreach (var tripId in tripIds)
        {
            await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
            var lockedTrip = await db.Trips.FromSqlInterpolated($"SELECT * FROM reservation.Trips WITH (UPDLOCK, READPAST, ROWLOCK) WHERE TripPublicId = {tripId}")
                .SingleOrDefaultAsync(cancellationToken);
            if (lockedTrip is null)
            {
                continue;
            }

            now = DateTime.UtcNow;
            var expired = await (from hold in db.Holds
                                 join seat in db.Seats on hold.TripSeatId equals seat.Id
                                 where seat.TripPublicId == tripId && hold.ExpiresAtUtc <= now
                                 orderby seat.Id
                                 select new { Hold = hold, Seat = seat }).ToArrayAsync(cancellationToken);
            var groupIds = expired.Select(x => x.Hold.HoldGroupPublicId).Distinct().ToArray();
            var bookings = await db.Bookings.Where(x => groupIds.Contains(x.HoldGroupPublicId) && x.Status == "PendingPayment")
                .ToArrayAsync(cancellationToken);
            foreach (var booking in bookings)
            {
                booking.Status = "Expired";
            }

            foreach (var item in expired)
            {
                if (item.Seat.Status == "Held" && item.Seat.HoldExpiresAtUtc <= now)
                {
                    item.Seat.Status = "Available";
                    item.Seat.HoldExpiresAtUtc = null;
                }

                db.Holds.Remove(item.Hold);
            }

            var deltas = expired.Where(x => x.Seat.Status == "Available")
                .Select(x => new SeatDelta(x.Seat.SeatPublicId, "available")).ToArray();
            db.QueueSeatUpdate(new SeatChange(tripId, deltas));
            foreach (var group in expired.GroupBy(x => x.Hold.UserId))
            {
                db.QueueSeatUpdate(new SeatChange(tripId, [], group.Key, "hold-expired"));
            }

            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            released += expired.Length;
            db.ChangeTracker.Clear();
        }

        return released;
    }
}

public sealed class ExpiredHoldSweepWorker(IServiceScopeFactory scopes, ILogger<ExpiredHoldSweepWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(15));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<ExpiredHoldSweeper>().Sweep(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Unable to sweep expired holds");
            }
        }
    }
}
