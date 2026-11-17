using System.Net;
using System.Net.Http.Json;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Reservation.Data;

namespace Reservation.Services;

public sealed class SeatInventoryService(ReservationDbContext db, HttpClient schedule)
{
    public async Task<SeatStatus[]?> GetSeats(Guid tripId, Guid? userId, CancellationToken cancellationToken = default)
    {
        if (await GetAvailability(tripId, cancellationToken) is null)
        {
            return null;
        }

        var now = DateTime.UtcNow;
        var seats = await (from seat in db.Seats.AsNoTracking()
                           join hold in db.Holds.AsNoTracking() on seat.Id equals hold.TripSeatId into matchingHolds
                           from hold in matchingHolds.DefaultIfEmpty()
                           where seat.TripPublicId == tripId
                           orderby seat.Id
                           select new { seat.SeatPublicId, seat.ClassCode, seat.Status, seat.HoldExpiresAtUtc, Hold = hold })
            .ToArrayAsync(cancellationToken);

        return seats.Select(seat =>
        {
            var expired = seat.Status == "Held" && seat.HoldExpiresAtUtc <= now;
            var status = expired ? "available" : seat.Status.ToLowerInvariant();
            var mine = status == "held" && userId is not null && seat.Hold?.UserId == userId;

            return new SeatStatus(seat.SeatPublicId, seat.ClassCode, status, mine);
        }).ToArray();
    }

    public async Task<ClassAvailability[]?> GetAvailability(Guid tripId, CancellationToken cancellationToken = default)
    {
        if (!await db.Trips.AsNoTracking().AnyAsync(x => x.TripPublicId == tripId, cancellationToken))
        {
            // Let first-time initialization finish even if Schedule's short search deadline expires.
            using var initialization = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            var initCt = initialization.Token;

            using var response = await schedule.GetAsync($"trips/{tripId}/layout", initCt);
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return null;
            }

            response.EnsureSuccessStatusCode();
            var layout = await response.Content.ReadFromJsonAsync<Layout>(cancellationToken: initCt);

            if (layout?.PublicId != tripId || layout.Coaches is null || layout.Coaches.Length == 0 ||
                layout.Coaches.Any(c => c is null || string.IsNullOrWhiteSpace(c.ClassCode) ||
                    c.ClassCode.Length > 64 || c.Seats is null || c.Seats.Length == 0 ||
                    c.Seats.Any(s => s is null || s.PublicId == Guid.Empty)))
            {
                throw new InvalidDataException("Schedule returned an invalid layout.");
            }

            var seats = layout.Coaches.SelectMany(coach => coach.Seats.Select(seat => new TripSeat
            {
                TripPublicId = tripId,
                SeatPublicId = seat.PublicId,
                ClassCode = coach.ClassCode
            })).ToList();

            if (seats.Select(seat => seat.SeatPublicId).Distinct().Count() != seats.Count)
            {
                throw new InvalidDataException("Schedule returned duplicate seats.");
            }

            db.Trips.Add(new InventoryTrip { TripPublicId = tripId, Seats = seats });

            try
            {
                await db.SaveChangesAsync(initCt);
            }
            catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 2601 or 2627 })
            {
                // A concurrent insert is atomic, so its inventory is complete.
                db.ChangeTracker.Clear();

                if (!await db.Trips.AsNoTracking().AnyAsync(x => x.TripPublicId == tripId, initCt))
                {
                    throw;
                }
            }
        }

        var now = DateTime.UtcNow;

        return await db.Seats.AsNoTracking()
            .Where(x => x.TripPublicId == tripId)
            .GroupBy(x => x.ClassCode)
            .Select(group => new ClassAvailability(
                group.Key,
                group.Count(),
                group.Count(x => x.Status == "Available" || (x.Status == "Held" && x.HoldExpiresAtUtc <= now)),
                group.Count(x => x.Status == "Held" && (x.HoldExpiresAtUtc == null || x.HoldExpiresAtUtc > now)),
                group.Count(x => x.Status == "Sold")))
            .ToArrayAsync(cancellationToken);
    }
}
