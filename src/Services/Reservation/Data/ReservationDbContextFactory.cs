using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Reservation.Data;

public sealed class ReservationDbContextFactory : IDesignTimeDbContextFactory<ReservationDbContext>
{
    public ReservationDbContext CreateDbContext(string[] args) => new(new DbContextOptionsBuilder<ReservationDbContext>()
        .UseSqlServer("Server=localhost;Database=RailwayLab;Integrated Security=true;TrustServerCertificate=true", o => o.MigrationsHistoryTable("__EFMigrationsHistory", "reservation")).Options);
}
