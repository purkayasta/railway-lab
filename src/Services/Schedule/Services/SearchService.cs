using Microsoft.EntityFrameworkCore;
using Schedule.Data;

namespace Schedule.Services;

public sealed class SearchService(ScheduleDbContext db)
{
    public async Task<TripResult[]> SearchAsync(string fromStation, string toStation, DateOnly date, string travelClass, CancellationToken cancellationToken = default)
    {
        var cls = await db.Classes.AsNoTracking().SingleOrDefaultAsync(x => x.Code == travelClass, cancellationToken);
        if (cls is null) return [];
        var candidates = await (from trip in db.Trips.AsNoTracking()
                                join train in db.Trains.AsNoTracking() on trip.TrainId equals train.Id
                                join origin in db.Stops.AsNoTracking() on train.RouteId equals origin.RouteId
                                join destination in db.Stops.AsNoTracking() on train.RouteId equals destination.RouteId
                                join originStation in db.Stations.AsNoTracking() on origin.StationId equals originStation.Id
                                join destinationStation in db.Stations.AsNoTracking() on destination.StationId equals destinationStation.Id
                                where trip.ServiceDate == date && origin.Sequence < destination.Sequence &&
                                      (originStation.Code == fromStation || originStation.Name == fromStation) &&
                                      (destinationStation.Code == toStation || destinationStation.Name == toStation) &&
                                      db.Coaches.Any(c => c.TripId == trip.Id && c.ClassId == cls.Id)
                                select new
                                {
                                    trip.PublicId,
                                    trip.DepartureUtc,
                                    train.Number,
                                    train.Name,
                                    origin.OffsetMinutes,
                                    DestinationOffset = destination.OffsetMinutes,
                                    FirstOffset = db.Stops.Where(s => s.RouteId == train.RouteId).Min(s => s.OffsetMinutes),
                                    LastOffset = db.Stops.Where(s => s.RouteId == train.RouteId).Max(s => s.OffsetMinutes)
                                }).ToArrayAsync(cancellationToken);
        return candidates.Select(x => new TripResult(x.PublicId, x.Number, x.Name, DateTime.SpecifyKind(x.DepartureUtc.AddMinutes(x.OffsetMinutes), DateTimeKind.Utc), DateTime.SpecifyKind(x.DepartureUtc.AddMinutes(x.DestinationOffset), DateTimeKind.Utc), cls.Code,
            JourneyPricing.Fare(cls.BaseFare, new JourneySegment(x.OffsetMinutes, x.DestinationOffset, (decimal)(x.DestinationOffset - x.OffsetMinutes) / (x.LastOffset - x.FirstOffset))), null, true)).OrderBy(x => x.DepartureUtc).ToArray();
    }
}
