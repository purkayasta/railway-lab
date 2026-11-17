using System.Data;
using Microsoft.EntityFrameworkCore;
using Reservation.Data;

namespace Reservation.Services;

public sealed class HoldService(ReservationDbContext db, SeatInventoryService inventory, ScheduleFareClient fares)
{
    public async Task<HoldOutcome> Create(Guid tripId, Guid userId, Guid[]? seatIds, string? origin, string? destination, CancellationToken cancellationToken = default)
    {
        if (seatIds is null || seatIds.Length == 0 ||
            seatIds.Any(x => x == Guid.Empty) || seatIds.Distinct().Count() != seatIds.Length ||
            string.IsNullOrWhiteSpace(origin) || string.IsNullOrWhiteSpace(destination) ||
            origin.Trim().Length > 64 || destination.Trim().Length > 64)
        {
            return new(400);
        }

        if (await inventory.GetAvailability(tripId, cancellationToken) is null)
        {
            return new(404);
        }

        origin = origin.Trim().ToUpperInvariant();
        destination = destination.Trim().ToUpperInvariant();
        var selected = await db.Seats.AsNoTracking()
            .Where(x => x.TripPublicId == tripId && seatIds.Contains(x.SeatPublicId))
            .Select(x => new { x.SeatPublicId, x.ClassCode })
            .ToArrayAsync(cancellationToken);

        if (selected.Length != seatIds.Length)
        {
            return new(404);
        }

        var prices = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
        foreach (var classCode in selected.Select(x => x.ClassCode).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var price = await fares.GetFare(tripId, origin, destination, classCode, cancellationToken);
            if (price is null)
            {
                return new(404);
            }

            prices.Add(classCode, price.Value);
        }

        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
        await db.Database.ExecuteSqlRawAsync("SET LOCK_TIMEOUT 5000", cancellationToken);

        // Serialize decisions for a trip, including a user's cap and expired-hold reclamation.
        await LockTrip(tripId, cancellationToken);

        var now = DateTime.UtcNow;
        var policies = await db.Policies.AsNoTracking().ToDictionaryAsync(x => x.Key, x => x.Value, cancellationToken);
        var cap = policies["MaxSeatsPerUserPerTrip"];
        var minutes = policies["HoldMinutes"];

        if (cap < 1 || minutes < 1)
        {
            throw new InvalidOperationException("Reservation policy values must be positive.");
        }

        if (seatIds.Length > cap)
        {
            return new(422);
        }

        var ticketCount = await (from ticket in db.Tickets.AsNoTracking()
                                 join booking in db.Bookings.AsNoTracking() on ticket.BookingId equals booking.Id
                                 where booking.TripPublicId == tripId && booking.UserId == userId && booking.Status == "Confirmed"
                                 select ticket).CountAsync(cancellationToken);

        if (seatIds.Length + ticketCount > cap)
        {
            return new(422);
        }

        var hasActiveHold = await (from hold in db.Holds
                                   join seat in db.Seats on hold.TripSeatId equals seat.Id
                                   where seat.TripPublicId == tripId && hold.UserId == userId && hold.ExpiresAtUtc > now
                                   select hold).AnyAsync(cancellationToken);

        if (hasActiveHold)
        {
            return new(409);
        }

        var seats = await db.Seats
            .Where(x => x.TripPublicId == tripId && seatIds.Contains(x.SeatPublicId))
            .OrderBy(x => x.Id)
            .ToArrayAsync(cancellationToken);

        if (seats.Length != seatIds.Length)
        {
            return new(404);
        }

        if (seats.Any(x => x.Status == "Sold" ||
            (x.Status == "Held" && (x.HoldExpiresAtUtc is null || x.HoldExpiresAtUtc > now))))
        {
            return new(409);
        }

        var ids = seats.Select(x => x.Id).ToArray();
        var expiredGroups = await db.Holds
            .Where(x => ids.Contains(x.TripSeatId) && x.ExpiresAtUtc <= now)
            .Select(x => x.HoldGroupPublicId)
            .Distinct()
            .ToArrayAsync(cancellationToken);

        // Reclamation can beat the sweeper. Preserve the terminal booking state before
        // removing its holds, otherwise the sweeper can no longer discover the booking.
        await db.Bookings
            .Where(x => expiredGroups.Contains(x.HoldGroupPublicId) && x.Status == "PendingPayment")
            .ExecuteUpdateAsync(update => update.SetProperty(x => x.Status, "Expired"), cancellationToken);

        await db.Holds.Where(x => ids.Contains(x.TripSeatId) && x.ExpiresAtUtc <= now).ExecuteDeleteAsync(cancellationToken);

        var group = Guid.NewGuid();
        var expiry = now.AddMinutes(minutes);

        foreach (var seat in seats)
        {
            seat.Status = "Held";
            seat.HoldExpiresAtUtc = expiry;

            db.Holds.Add(new SeatHold
            {
                TripSeatId = seat.Id,
                UserId = userId,
                HoldGroupPublicId = group,
                ExpiresAtUtc = expiry,
                Origin = origin,
                Destination = destination,
                UnitPrice = prices[seat.ClassCode]
            });
        }

        db.QueueSeatUpdate(new SeatChange(tripId, seats.Select(x => new SeatDelta(x.SeatPublicId, "held")).ToArray(), userId, "hold-created"));
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return new(200, new HoldResult(group, tripId, expiry, seats.Select(x => x.SeatPublicId).ToArray(), origin, destination, seats.Sum(x => prices[x.ClassCode])));
    }

    public async Task<HoldOutcome> Extend(Guid groupId, Guid userId, CancellationToken cancellationToken = default)
    {
        var tripId = await FindTripForGroup(groupId, userId, cancellationToken);
        if (tripId is null)
        {
            return new(404);
        }

        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
        await db.Database.ExecuteSqlRawAsync("SET LOCK_TIMEOUT 5000", cancellationToken);
        await LockTrip(tripId.Value, cancellationToken);

        if (await db.Bookings.AnyAsync(x => x.HoldGroupPublicId == groupId, cancellationToken))
        {
            return new(409);
        }

        var holds = await LoadGroup(groupId, userId, cancellationToken);
        if (holds.Length == 0)
        {
            return new(404);
        }

        var now = DateTime.UtcNow;
        if (holds.Any(x => x.Hold.ExpiresAtUtc <= now || x.Hold.ExtendedOnce))
        {
            return new(409);
        }

        var minutes = await db.Policies.Where(x => x.Key == "HoldMinutes").Select(x => x.Value).SingleAsync(cancellationToken);
        if (minutes < 1)
        {
            throw new InvalidOperationException("Reservation policy values must be positive.");
        }

        var expiry = now.AddMinutes(minutes);
        foreach (var item in holds)
        {
            item.Hold.ExpiresAtUtc = expiry;
            item.Hold.ExtendedOnce = true;
            item.Seat.HoldExpiresAtUtc = expiry;
        }

        db.QueueSeatUpdate(new SeatChange(tripId.Value, [], userId, "hold-extended"));
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return new(200, ToHoldResult(groupId, holds));
    }

    public async Task<int> Release(Guid groupId, Guid userId, CancellationToken cancellationToken = default)
    {
        var tripId = await FindTripForGroup(groupId, userId, cancellationToken);
        if (tripId is null)
        {
            return 404;
        }

        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
        await db.Database.ExecuteSqlRawAsync("SET LOCK_TIMEOUT 5000", cancellationToken);
        await LockTrip(tripId.Value, cancellationToken);

        if (await db.Bookings.AnyAsync(x => x.HoldGroupPublicId == groupId, cancellationToken))
        {
            return 409;
        }

        var holds = await LoadGroup(groupId, userId, cancellationToken);
        if (holds.Length == 0)
        {
            return 404;
        }

        foreach (var item in holds)
        {
            item.Seat.Status = "Available";
            item.Seat.HoldExpiresAtUtc = null;
            db.Holds.Remove(item.Hold);
        }

        db.QueueSeatUpdate(new SeatChange(tripId.Value, holds.Select(x => new SeatDelta(x.Seat.SeatPublicId, "available")).ToArray(), userId, "hold-released"));
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return 204;
    }

    public async Task<HoldOutcome> GetConfirmation(Guid groupId, Guid userId, CancellationToken cancellationToken = default)
    {
        var holds = await LoadGroup(groupId, userId, cancellationToken);
        if (holds.Length == 0)
        {
            return new(404);
        }

        if (!IsUsable(holds))
        {
            return new(409);
        }

        return new(200, ToHoldResult(groupId, holds));
    }

    public async Task<BookingOutcome> Checkout(Guid groupId, Guid userId, string? passengerName, CancellationToken cancellationToken = default)
    {
        if (passengerName is not null && (string.IsNullOrWhiteSpace(passengerName) || passengerName.Trim().Length > 200))
        {
            return new(400);
        }

        var tripId = await FindTripForGroup(groupId, userId, cancellationToken) ??
            await db.Bookings.AsNoTracking().Where(x => x.HoldGroupPublicId == groupId && x.UserId == userId)
                .Select(x => (Guid?)x.TripPublicId).SingleOrDefaultAsync(cancellationToken);
        if (tripId is null)
        {
            return new(404);
        }

        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
        await db.Database.ExecuteSqlRawAsync("SET LOCK_TIMEOUT 5000", cancellationToken);
        await LockTrip(tripId.Value, cancellationToken);

        var existing = await db.Bookings.SingleOrDefaultAsync(x => x.HoldGroupPublicId == groupId && x.UserId == userId, cancellationToken);
        if (existing is not null)
        {
            if (existing.PassengerName != passengerName?.Trim())
            {
                return new(409);
            }

            var ticketSeats = await (from ticket in db.Tickets.AsNoTracking()
                                     join seat in db.Seats.AsNoTracking() on ticket.TripSeatId equals seat.Id
                                     where ticket.BookingId == existing.Id
                                     orderby seat.Id
                                     select seat.SeatPublicId).ToArrayAsync(cancellationToken);
            var existingHolds = await LoadGroup(groupId, userId, cancellationToken);
            var seatIds = ticketSeats.Length > 0 ? ticketSeats : existingHolds.Select(x => x.Seat.SeatPublicId).ToArray();
            return new(200, new BookingResult(existing.PublicId, existing.Status, existing.Origin,
                existing.Destination, existing.TotalAmount, seatIds));
        }

        var holds = await LoadGroup(groupId, userId, cancellationToken);
        if (holds.Length == 0)
        {
            return new(404);
        }

        if (!IsUsable(holds))
        {
            return new(409);
        }

        var booking = new Booking
        {
            PublicId = Guid.NewGuid(),
            UserId = userId,
            TripPublicId = tripId.Value,
            HoldGroupPublicId = groupId,
            TotalAmount = holds.Sum(x => x.Hold.UnitPrice!.Value),
            Origin = holds[0].Hold.Origin!,
            Destination = holds[0].Hold.Destination!,
            PassengerName = passengerName?.Trim(),
            BookedByUserId = passengerName is null ? null : userId,
            CreatedAtUtc = DateTime.UtcNow,
            SeatCount = holds.Length
        };

        db.Bookings.Add(booking);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new(200, ToBookingResult(booking, holds));
    }

    private static bool IsUsable(HeldSeat[] holds)
    {
        var now = DateTime.UtcNow;
        var origin = holds[0].Hold.Origin;
        var destination = holds[0].Hold.Destination;

        return !string.IsNullOrWhiteSpace(origin) && !string.IsNullOrWhiteSpace(destination) &&
            holds.All(x => x.Hold.ExpiresAtUtc > now && x.Seat.Status == "Held" &&
                x.Hold.UnitPrice is >= 0 && x.Hold.Origin == origin && x.Hold.Destination == destination);
    }

    private static HoldResult ToHoldResult(Guid groupId, HeldSeat[] holds)
    {
        decimal? total = holds.All(x => x.Hold.UnitPrice is not null) ? holds.Sum(x => x.Hold.UnitPrice!.Value) : null;
        var expiry = DateTime.SpecifyKind(holds.Min(x => x.Hold.ExpiresAtUtc), DateTimeKind.Utc);
        return new HoldResult(groupId, holds[0].Seat.TripPublicId, expiry,
            holds.Select(x => x.Seat.SeatPublicId).ToArray(), holds[0].Hold.Origin, holds[0].Hold.Destination, total);
    }

    private static BookingResult ToBookingResult(Booking booking, HeldSeat[] holds) =>
        new(booking.PublicId, booking.Status, booking.Origin, booking.Destination, booking.TotalAmount,
            holds.Select(x => x.Seat.SeatPublicId).ToArray());

    private async Task<Guid?> FindTripForGroup(Guid groupId, Guid userId, CancellationToken cancellationToken = default)
    {
        return await (from hold in db.Holds.AsNoTracking()
                      join seat in db.Seats.AsNoTracking() on hold.TripSeatId equals seat.Id
                      where hold.HoldGroupPublicId == groupId && hold.UserId == userId
                      select (Guid?)seat.TripPublicId).FirstOrDefaultAsync(cancellationToken);
    }

    private async Task<HeldSeat[]> LoadGroup(Guid groupId, Guid userId, CancellationToken cancellationToken = default)
    {
        return await (from hold in db.Holds
                      join seat in db.Seats on hold.TripSeatId equals seat.Id
                      where hold.HoldGroupPublicId == groupId && hold.UserId == userId
                      orderby seat.Id
                      select new HeldSeat(hold, seat)).ToArrayAsync(cancellationToken);
    }

    private sealed record HeldSeat(SeatHold Hold, TripSeat Seat);

    private async Task LockTrip(Guid tripId, CancellationToken cancellationToken = default)
    {
        await db.Trips
            .FromSqlInterpolated($"SELECT * FROM reservation.Trips WITH (UPDLOCK, HOLDLOCK) WHERE TripPublicId = {tripId}")
            .SingleAsync(cancellationToken);
    }
}
