using Microsoft.EntityFrameworkCore;
using Schedule.Data;

namespace Schedule.Services;

public sealed class TripService(ScheduleDbContext db)
{
    public async Task<TripDetails?> FindAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var result = await (from trip in db.Trips.AsNoTracking()
                            join train in db.Trains.AsNoTracking() on trip.TrainId equals train.Id
                            where trip.PublicId == id
                            select new TripDetails(trip.PublicId, train.Number, train.Name, trip.ServiceDate, trip.DepartureUtc, trip.ArrivalUtc, trip.Status)).SingleOrDefaultAsync(cancellationToken);
        return result is null ? null : result with { DepartureUtc = DateTime.SpecifyKind(result.DepartureUtc, DateTimeKind.Utc), ArrivalUtc = DateTime.SpecifyKind(result.ArrivalUtc, DateTimeKind.Utc) };
    }
}
