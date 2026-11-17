using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using Reservation.Services;

namespace Reservation.Data;

public sealed class ReservationDbContext(DbContextOptions<ReservationDbContext> options) : DbContext(options)
{
    public DbSet<InventoryTrip> Trips => Set<InventoryTrip>();
    public DbSet<TripSeat> Seats => Set<TripSeat>();
    public DbSet<SeatHold> Holds => Set<SeatHold>();
    public DbSet<PolicySetting> Policies => Set<PolicySetting>();
    public DbSet<PolicyAudit> PolicyAudits => Set<PolicyAudit>();
    public DbSet<Booking> Bookings => Set<Booking>();
    public DbSet<Ticket> Tickets => Set<Ticket>();
    public DbSet<SeatUpdate> SeatUpdates => Set<SeatUpdate>();

    public void QueueSeatUpdate(SeatChange change)
    {
        SeatUpdates.Add(new SeatUpdate { Payload = JsonSerializer.Serialize(change) });
    }

    protected override void OnModelCreating(ModelBuilder model)
    {
        model.HasDefaultSchema("reservation");

        model.Entity<InventoryTrip>().HasKey(x => x.TripPublicId);
        model.Entity<InventoryTrip>().HasMany(x => x.Seats).WithOne().HasForeignKey(x => x.TripPublicId);

        model.Entity<TripSeat>().HasIndex(x => new { x.TripPublicId, x.SeatPublicId }).IsUnique();
        model.Entity<TripSeat>().Property(x => x.ClassCode).HasMaxLength(64).IsRequired();
        model.Entity<TripSeat>().Property(x => x.Status).HasMaxLength(16).IsRequired();
        model.Entity<TripSeat>().ToTable(t => t.HasCheckConstraint("CK_TripSeat_Status", "[Status] IN ('Available', 'Held', 'Sold')"));

        model.Entity<SeatHold>().HasOne<TripSeat>().WithMany().HasForeignKey(x => x.TripSeatId);
        model.Entity<SeatHold>().HasIndex(x => x.TripSeatId).IsUnique();
        model.Entity<SeatHold>().HasIndex(x => x.HoldGroupPublicId);
        model.Entity<SeatHold>().Property(x => x.Origin).HasMaxLength(64);
        model.Entity<SeatHold>().Property(x => x.Destination).HasMaxLength(64);
        model.Entity<SeatHold>().Property(x => x.UnitPrice).HasPrecision(12, 2);

        model.Entity<Booking>().HasIndex(x => x.PublicId).IsUnique();
        model.Entity<Booking>().HasIndex(x => x.HoldGroupPublicId).IsUnique();
        model.Entity<Booking>().Property(x => x.TotalAmount).HasPrecision(12, 2);
        model.Entity<Booking>().Property(x => x.Origin).HasMaxLength(64);
        model.Entity<Booking>().Property(x => x.Destination).HasMaxLength(64);
        model.Entity<Booking>().Property(x => x.Status).HasMaxLength(32);
        model.Entity<Booking>().Property(x => x.PassengerName).HasMaxLength(200);

        model.Entity<Ticket>().HasOne<Booking>().WithMany().HasForeignKey(x => x.BookingId);
        model.Entity<Ticket>().HasOne<TripSeat>().WithMany().HasForeignKey(x => x.TripSeatId);
        model.Entity<Ticket>().HasIndex(x => x.TripSeatId).IsUnique();
        model.Entity<Ticket>().HasIndex(x => x.TicketNumber).IsUnique();
        model.Entity<Ticket>().Property(x => x.TicketNumber).HasMaxLength(64).IsRequired();

        model.Entity<SeatUpdate>().Property(x => x.CreatedAtUtc).HasDefaultValueSql("SYSUTCDATETIME()");
        model.Entity<SeatUpdate>().HasIndex(x => x.CreatedAtUtc);
        model.Entity<SeatUpdate>().Property(x => x.Payload).IsRequired();

        model.Entity<PolicySetting>().HasKey(x => x.Key);
        model.Entity<PolicySetting>().HasData(
            new PolicySetting { Key = "MaxSeatsPerUserPerTrip", Value = 4 },
            new PolicySetting { Key = "HoldMinutes", Value = 8 });
    }
}
