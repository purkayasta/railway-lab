using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;

namespace Schedule.Data;

public static class ScheduleSeed
{
    public static async Task Initialize(ScheduleDbContext db, CancellationToken cancellationToken = default)
    {
        await db.Database.MigrateAsync(cancellationToken);

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        if (!await db.Stations.AnyAsync(cancellationToken))
        {
            string[] names = ["Dhaka", "Airport", "Joydebpur", "Bhairab", "Brahmanbaria", "Cumilla", "Chattogram", "Tongi", "Mymensingh", "Jamalpur", "Rajshahi", "Ishwardi"];
            string[] codes = ["DHA", "AIR", "JOY", "BHA", "BRA", "CUM", "CTG", "TON", "MYM", "JAM", "RAJ", "ISH"];
            var stations = names.Select((name, i) => new Station { Code = codes[i], Name = name, City = name }).ToArray();
            db.Stations.AddRange(stations);
            db.Routes.AddRange(Enumerable.Range(1, 4).Select(i => new RailRoute { Name = $"Route {i}" }));
            db.Classes.AddRange(new CoachClass { Code = "economy", Name = "Economy", BaseFare = 600 }, new CoachClass { Code = "business", Name = "Business", BaseFare = 1200 });
            await db.SaveChangesAsync(cancellationToken);
            var routes = await db.Routes.OrderBy(x => x.Id).ToArrayAsync(cancellationToken);
            int[][] stationIndices = [[0, 1, 3, 4, 5, 6], [6, 5, 4, 3, 1, 0], [0, 7, 8, 9], [0, 2, 11, 10]];
            for (var i = 0; i < routes.Length; i++)
                db.Stops.AddRange(stationIndices[i].Select((index, sequence) => new Stop { RouteId = routes[i].Id, StationId = stations[index].Id, Sequence = sequence + 1, OffsetMinutes = sequence * 60 }));
            db.Trains.AddRange(Enumerable.Range(0, 6).Select(i => new Train { Number = $"{701 + i}", Name = $"Railway Express {i + 1}", RouteId = routes[i % 4].Id }));
            await db.SaveChangesAsync(cancellationToken);
        }
        var trains = await db.Trains.OrderBy(x => x.Id).ToArrayAsync(cancellationToken);
        var classes = await db.Classes.OrderBy(x => x.Id).ToArrayAsync(cancellationToken);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var end = today.AddDays(14);
        var existing = await db.Trips.Where(x => x.ServiceDate >= today && x.ServiceDate < end).Select(x => new { x.TrainId, x.ServiceDate }).ToListAsync(cancellationToken);
        foreach (var date in Enumerable.Range(0, 14).Select(today.AddDays))
            foreach (var train in trains)
            {
                if (existing.Any(x => x.TrainId == train.Id && x.ServiceDate == date)) continue;
                var minutes = await db.Stops.Where(x => x.RouteId == train.RouteId).MaxAsync(x => x.OffsetMinutes, cancellationToken);
                var departure = date.ToDateTime(new TimeOnly(6 + (int)(train.Id % 6), 0), DateTimeKind.Utc);
                var trip = new Trip { PublicId = StableId($"trip:{train.Number}:{date:yyyy-MM-dd}"), TrainId = train.Id, ServiceDate = date, DepartureUtc = departure, ArrivalUtc = departure.AddMinutes(minutes) };
                db.Trips.Add(trip);
                await db.SaveChangesAsync(cancellationToken);
                for (var coachIndex = 0; coachIndex < 8; coachIndex++)
                {
                    var coach = new Coach { TripId = trip.Id, Label = $"{(char)('A' + coachIndex)}", ClassId = classes[coachIndex < 6 ? 0 : 1].Id, SeatCount = 40 };
                    db.Coaches.Add(coach);
                    await db.SaveChangesAsync(cancellationToken);
                    db.Seats.AddRange(Enumerable.Range(0, 40).Select(i => new Seat { PublicId = StableId($"seat:{trip.PublicId}:{coach.Label}:{i}"), CoachId = coach.Id, Label = $"{i / 4 + 1}{(char)('A' + i % 4)}", Row = i / 4 + 1, Column = ((char)('A' + i % 4)).ToString() }));
                }
                await db.SaveChangesAsync(cancellationToken);
                db.ChangeTracker.Clear();
            }
        await transaction.CommitAsync(cancellationToken);
    }
    private static Guid StableId(string value) => new(MD5.HashData(Encoding.UTF8.GetBytes(value)));
}
