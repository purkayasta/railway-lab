using Microsoft.EntityFrameworkCore;
using Schedule.Data;

namespace Schedule.Services;

public sealed class StationsService(ScheduleDbContext db)
{
    public async Task<StationResult[]> FindAsync(string? query, CancellationToken cancellationToken = default) =>
        await db.Stations.AsNoTracking().Where(s => query == null || s.Code.Contains(query) || s.Name.Contains(query) || s.City.Contains(query)).OrderBy(s => s.Name).Select(s => new StationResult(s.Code, s.Name, s.City)).ToArrayAsync(cancellationToken);
}
