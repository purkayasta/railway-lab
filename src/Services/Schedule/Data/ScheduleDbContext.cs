using Microsoft.EntityFrameworkCore;

namespace Schedule.Data;

public sealed class ScheduleDbContext(DbContextOptions<ScheduleDbContext> options) : DbContext(options)
{
    public const string Schema = "schedule";
    public DbSet<Station> Stations => Set<Station>();
    public DbSet<RailRoute> Routes => Set<RailRoute>();
    public DbSet<Stop> Stops => Set<Stop>();
    public DbSet<Train> Trains => Set<Train>();
    public DbSet<Trip> Trips => Set<Trip>();
    public DbSet<CoachClass> Classes => Set<CoachClass>();
    public DbSet<Coach> Coaches => Set<Coach>();
    public DbSet<Seat> Seats => Set<Seat>();
    protected override void OnModelCreating(ModelBuilder model)
    {
        base.OnModelCreating(model);
        model.HasDefaultSchema(Schema);
        model.Entity<Stop>().HasOne<RailRoute>().WithMany().HasForeignKey(x => x.RouteId);
        model.Entity<Stop>().HasOne<Station>().WithMany().HasForeignKey(x => x.StationId);
        model.Entity<Train>().HasOne<RailRoute>().WithMany().HasForeignKey(x => x.RouteId);
        model.Entity<Trip>().HasOne<Train>().WithMany().HasForeignKey(x => x.TrainId);
        model.Entity<Coach>().HasOne<Trip>().WithMany().HasForeignKey(x => x.TripId);
        model.Entity<Coach>().HasOne<CoachClass>().WithMany().HasForeignKey(x => x.ClassId);
        model.Entity<Seat>().HasOne<Coach>().WithMany().HasForeignKey(x => x.CoachId);
        model.Entity<Station>().HasIndex(x => x.Code).IsUnique();
        model.Entity<Stop>().HasIndex(x => new { x.RouteId, x.Sequence }).IsUnique();
        model.Entity<Trip>().HasIndex(x => x.PublicId).IsUnique();
        model.Entity<Trip>().HasIndex(x => new { x.TrainId, x.ServiceDate }).IsUnique();
        model.Entity<Seat>().HasIndex(x => x.PublicId).IsUnique();
        model.Entity<CoachClass>().Property(x => x.BaseFare).HasPrecision(12, 2);
    }
}
