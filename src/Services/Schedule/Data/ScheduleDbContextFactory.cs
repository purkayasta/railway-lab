using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Schedule.Data;

public sealed class ScheduleDbContextFactory : IDesignTimeDbContextFactory<ScheduleDbContext>
{
    public ScheduleDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<ScheduleDbContext>()
            .UseSqlServer("Server=localhost;Database=RailwayLab;Integrated Security=true;TrustServerCertificate=true", sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", ScheduleDbContext.Schema)).Options;
        return new ScheduleDbContext(options);
    }
}
