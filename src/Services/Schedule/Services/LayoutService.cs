using Microsoft.EntityFrameworkCore;
using Schedule.Data;

namespace Schedule.Services;

public sealed class LayoutService(ScheduleDbContext db)
{
    public async Task<LayoutResult?> FindAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var trip = await db.Trips.AsNoTracking().SingleOrDefaultAsync(x => x.PublicId == id, cancellationToken);
        if (trip is null) return null;
        var coaches = await (from coach in db.Coaches.AsNoTracking()
                             join cls in db.Classes.AsNoTracking() on coach.ClassId equals cls.Id
                             where coach.TripId == trip.Id
                             select new { coach.Id, coach.Label, ClassCode = cls.Code, cls.Name, coach.SeatCount }).ToArrayAsync(cancellationToken);
        var ids = coaches.Select(c => c.Id).ToArray();
        var seats = await db.Seats.AsNoTracking().Where(s => ids.Contains(s.CoachId)).OrderBy(s => s.Row).ThenBy(s => s.Column).ToArrayAsync(cancellationToken);
        return new LayoutResult(trip.PublicId, coaches.Select(c => new CoachResult(c.Label, c.ClassCode, c.Name, c.SeatCount,
            seats.Where(s => s.CoachId == c.Id).Select(s => new SeatResult(s.PublicId, s.Label, s.Row, s.Column)).ToArray())).ToArray());
    }
}
