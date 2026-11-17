using System.Data;
using Microsoft.EntityFrameworkCore;
using Reservation.Data;

namespace Reservation.Services;

public sealed class PaymentApprovalService(ReservationDbContext db)
{
    // Called only by trusted payment integration, never directly by the browser.
    public async Task<string?> ApplyApproval(Guid bookingId, CancellationToken cancellationToken = default)
    {
        var tripId = await db.Bookings.AsNoTracking().Where(x => x.PublicId == bookingId)
            .Select(x => (Guid?)x.TripPublicId).SingleOrDefaultAsync(cancellationToken);
        if (tripId is null)
        {
            return null;
        }

        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
        await db.Database.ExecuteSqlRawAsync("SET LOCK_TIMEOUT 5000", cancellationToken);
        await db.Trips.FromSqlInterpolated($"SELECT * FROM reservation.Trips WITH (UPDLOCK, HOLDLOCK) WHERE TripPublicId = {tripId.Value}")
            .SingleAsync(cancellationToken);

        var booking = await db.Bookings.SingleAsync(x => x.PublicId == bookingId, cancellationToken);
        if (booking.Status is "Confirmed" or "PaymentOrphaned")
        {
            return booking.Status;
        }

        var holds = await (from hold in db.Holds
                           join seat in db.Seats on hold.TripSeatId equals seat.Id
                           where hold.HoldGroupPublicId == booking.HoldGroupPublicId && hold.UserId == booking.UserId
                               && seat.TripPublicId == booking.TripPublicId
                           orderby seat.Id
                           select new { Hold = hold, Seat = seat }).ToArrayAsync(cancellationToken);

        var now = DateTime.UtcNow;
        if (booking.Status != "PendingPayment" || booking.SeatCount <= 0 || holds.Length != booking.SeatCount ||
            holds.Any(x => x.Hold.ExpiresAtUtc <= now || x.Seat.Status != "Held" ||
                x.Seat.HoldExpiresAtUtc is null || x.Seat.HoldExpiresAtUtc <= now))
        {
            booking.Status = "PaymentOrphaned";
        }
        else
        {
            foreach (var item in holds)
            {
                item.Seat.Status = "Sold";
                item.Seat.HoldExpiresAtUtc = null;
                db.Tickets.Add(new Ticket
                {
                    BookingId = booking.Id,
                    TripSeatId = item.Seat.Id,
                    TicketNumber = Guid.NewGuid().ToString("N").ToUpperInvariant(),
                    IssuedAtUtc = now
                });
                db.Holds.Remove(item.Hold);
            }

            booking.Status = "Confirmed";
        }

        var deltas = booking.Status == "Confirmed"
            ? holds.Select(x => new SeatDelta(x.Seat.SeatPublicId, "sold")).ToArray()
            : [];
        db.QueueSeatUpdate(new SeatChange(booking.TripPublicId, deltas, booking.UserId, "booking-changed"));
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return booking.Status;
    }
}
