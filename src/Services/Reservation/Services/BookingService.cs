using Microsoft.EntityFrameworkCore;
using Reservation.Data;

namespace Reservation.Services;

public sealed record BookingDetails(
    Guid PublicId, Guid TripPublicId, string Status, string Origin, string Destination,
    decimal TotalAmount, DateTime CreatedAtUtc, string? PassengerName, Guid? BookedByUserId);

public sealed class BookingService(ReservationDbContext db)
{
    public async Task<BookingDetails[]> GetOwn(Guid userId, CancellationToken cancellationToken = default)
    {
        var query = db.Bookings.AsNoTracking().Where(x => x.UserId == userId)
            .OrderByDescending(x => x.CreatedAtUtc);
        return await Project(query).ToArrayAsync(cancellationToken);
    }

    public async Task<BookingDetails?> GetOwn(Guid userId, Guid bookingId, CancellationToken cancellationToken = default)
    {
        return await Project(db.Bookings.AsNoTracking().Where(x => x.UserId == userId && x.PublicId == bookingId))
            .SingleOrDefaultAsync(cancellationToken);
    }

    public async Task<BookingDetails[]> Search(string? search, CancellationToken cancellationToken = default)
    {
        var query = db.Bookings.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            if (Guid.TryParse(term, out var id))
            {
                query = query.Where(x => x.PublicId == id);
            }
            else
            {
                query = query.Where(x => x.PassengerName != null && x.PassengerName.Contains(term));
            }
        }

        return await Project(query.OrderByDescending(x => x.CreatedAtUtc)).ToArrayAsync(cancellationToken);
    }

    public async Task<BookingDetails?> GetAny(Guid bookingId, CancellationToken cancellationToken = default)
    {
        return await Project(db.Bookings.AsNoTracking().Where(x => x.PublicId == bookingId))
            .SingleOrDefaultAsync(cancellationToken);
    }

    private static IQueryable<BookingDetails> Project(IQueryable<Booking> bookings) => bookings.Select(x =>
        new BookingDetails(x.PublicId, x.TripPublicId, x.Status, x.Origin, x.Destination,
            x.TotalAmount, DateTime.SpecifyKind(x.CreatedAtUtc, DateTimeKind.Utc), x.PassengerName, x.BookedByUserId));
}
