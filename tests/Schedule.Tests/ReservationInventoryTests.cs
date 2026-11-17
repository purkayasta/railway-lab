using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Reservation.Services;
using Reservation.Data;
using Schedule.Availability;
using Schedule.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Testcontainers.MsSql;
using ClassAvailability = Reservation.Services.ClassAvailability;

namespace Schedule.Tests;

[Collection("SQL integration")]
public class ReservationInventoryTests
{
    [Fact]
    public async Task Inventory_is_initialized_once_and_counts_expired_holds_as_available()
    {
        await using var sql = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2025-latest").Build();
        await sql.StartAsync();
        var options = new DbContextOptionsBuilder<ReservationDbContext>().UseSqlServer(sql.GetConnectionString(), o => o.MigrationsHistoryTable("__EFMigrationsHistory", "reservation")).Options;
        await using (var setup = new ReservationDbContext(options)) await setup.Database.MigrateAsync();
        var trip = Guid.NewGuid();
        var seats = Enumerable.Range(0, 4).Select(_ => Guid.NewGuid()).ToArray();
        var layout = new Layout(trip, [new Coach("economy", seats.Select(s => new Seat(s)).ToArray())]);
        async Task<ClassAvailability[]?> Request()
        {
            await using var db = new ReservationDbContext(options);
            return await new SeatInventoryService(db, new HttpClient(new LayoutHandler(layout)) { BaseAddress = new Uri("http://schedule/") }).GetAvailability(trip, default);
        }
        var responses = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => Request()));
        Assert.All(responses, result => Assert.Equal(new ClassAvailability("economy", 4, 4, 0, 0), Assert.Single(result!)));
        await using (var db = new ReservationDbContext(options))
        {
            var rows = await db.Seats.OrderBy(x => x.Id).ToArrayAsync();
            Assert.Equal(4, rows.Length);
            rows[0].Status = "Sold";
            rows[1].Status = "Held"; rows[1].HoldExpiresAtUtc = DateTime.UtcNow.AddMinutes(2);
            rows[2].Status = "Held"; rows[2].HoldExpiresAtUtc = DateTime.UtcNow.AddMinutes(-2);
            await db.SaveChangesAsync();
        }
        var counts = Assert.Single((await Request())!);
        Assert.Equal(new ClassAvailability("economy", 4, 2, 1, 1), counts);
        var client = new ReservationAvailabilityClient(new HttpClient(new AvailabilityHandler(counts)) { BaseAddress = new Uri("http://reservation/") }, NullLogger<ReservationAvailabilityClient>.Instance);
        var input = new TripResult(trip, "1", "Train", DateTime.UtcNow, DateTime.UtcNow, "economy", 100, null, true);
        var enriched = Assert.Single(await client.EnrichAsync([input], default));
        Assert.Equal(2, enriched.AvailableSeats);
        Assert.False(enriched.AvailabilityUnknown);

        Guid availableSeat;
        await using (var db = new ReservationDbContext(options))
            availableSeat = await db.Seats.Where(x => x.Status == "Available").Select(x => x.SeatPublicId).SingleAsync();
        async Task<HoldOutcome> Hold(Guid[] requested)
        {
            await using var db = new ReservationDbContext(options);
            var inventory = new SeatInventoryService(db, new HttpClient(new LayoutHandler(layout)) { BaseAddress = new Uri("http://schedule/") });
            return await new HoldService(db, inventory, FareClient()).Create(trip, Guid.NewGuid(), requested, "DHA", "CTG", default);
        }
        var contenders = await Task.WhenAll(Enumerable.Range(0, 50).Select(_ => Hold([availableSeat])));
        Assert.Single(contenders, x => x.Status == 200);
        Assert.Equal(49, contenders.Count(x => x.Status == 409));
        await using (var db = new ReservationDbContext(options))
            Assert.Equal(1, await db.Holds.CountAsync());

        await using (var blocker = new ReservationDbContext(options))
        await using (var transaction = await blocker.Database.BeginTransactionAsync())
        {
            await blocker.Trips.FromSqlInterpolated($"SELECT * FROM reservation.Trips WITH (UPDLOCK, HOLDLOCK) WHERE TripPublicId = {trip}").SingleAsync();
            var timer = System.Diagnostics.Stopwatch.StartNew();
            var error = await Assert.ThrowsAsync<Microsoft.Data.SqlClient.SqlException>(() => Hold([availableSeat]));
            Assert.Equal(1222, error.Number);
            Assert.InRange(timer.Elapsed, TimeSpan.FromSeconds(4), TimeSpan.FromSeconds(15));
        }

        var overlapTrip = Guid.NewGuid();
        var overlapSeats = Enumerable.Range(0, 3).Select(_ => Guid.NewGuid()).ToArray();
        var overlapLayout = new Layout(overlapTrip, [new Coach("economy", overlapSeats.Select(x => new Seat(x)).ToArray())]);
        async Task<HoldOutcome> HoldOverlap(Guid[] requested)
        {
            await using var overlapDb = new ReservationDbContext(options);
            var overlapInventory = new SeatInventoryService(overlapDb, new HttpClient(new LayoutHandler(overlapLayout)) { BaseAddress = new Uri("http://schedule/") });
            return await new HoldService(overlapDb, overlapInventory, FareClient()).Create(overlapTrip, Guid.NewGuid(), requested, "DHA", "CTG");
        }

        await using (var setup = new ReservationDbContext(options))
        {
            var overlapInventory = new SeatInventoryService(setup, new HttpClient(new LayoutHandler(overlapLayout)) { BaseAddress = new Uri("http://schedule/") });
            await overlapInventory.GetAvailability(overlapTrip);
        }

        var overlapping = await Task.WhenAll(HoldOverlap([overlapSeats[0], overlapSeats[1]]),
            HoldOverlap([overlapSeats[1], overlapSeats[2]])).WaitAsync(TimeSpan.FromSeconds(15));
        Assert.Single(overlapping, x => x.Status == 200);
        Assert.Single(overlapping, x => x.Status == 409);
        await using (var verify = new ReservationDbContext(options))
        {
            var overlapCount = await (from hold in verify.Holds
                                      join seat in verify.Seats on hold.TripSeatId equals seat.Id
                                      where seat.TripPublicId == overlapTrip
                                      select hold).CountAsync();
            Assert.Equal(2, overlapCount);
        }

        var secondTrip = Guid.NewGuid();
        var secondSeat = Guid.NewGuid();
        var secondSeat2 = Guid.NewGuid();
        var secondLayout = new Layout(secondTrip, [new Coach("economy", [new Seat(secondSeat), new Seat(secondSeat2)])]);
        var owner = Guid.NewGuid();
        var otherUser = Guid.NewGuid();

        await using (var db = new ReservationDbContext(options))
        {
            var inventory = new SeatInventoryService(db, new HttpClient(new LayoutHandler(secondLayout))
            {
                BaseAddress = new Uri("http://schedule/")
            });
            var service = new HoldService(db, inventory, FareClient());

            var created = await service.Create(secondTrip, owner, [secondSeat], "DHA", "CTG", default);
            Assert.Equal(200, created.Status);
            var group = created.Hold!.HoldGroupPublicId;

            var anonymousSeat = Assert.Single((await inventory.GetSeats(secondTrip, null, default))!, seat => seat.SeatPublicId == secondSeat);
            var ownerSeat = Assert.Single((await inventory.GetSeats(secondTrip, owner, default))!, seat => seat.SeatPublicId == secondSeat);
            var otherSeat = Assert.Single((await inventory.GetSeats(secondTrip, otherUser, default))!, seat => seat.SeatPublicId == secondSeat);
            Assert.Equal("held", anonymousSeat.Status);
            Assert.False(anonymousSeat.Mine);
            Assert.True(ownerSeat.Mine);
            Assert.False(otherSeat.Mine);

            Assert.Equal(404, (await service.Extend(group, otherUser, default)).Status);
            Assert.Equal(404, await service.Release(group, otherUser, default));
            Assert.Equal(200, (await service.Extend(group, owner, default)).Status);
            Assert.Equal(409, (await service.Extend(group, owner, default)).Status);
            Assert.Equal(204, await service.Release(group, owner, default));
            Assert.Equal(404, await service.Release(group, owner, default));

            var afterRelease = Assert.Single((await inventory.GetAvailability(secondTrip, default))!);
            Assert.Equal(2, afterRelease.Available);
            Assert.Equal(0, afterRelease.Held);

            var expiring = await service.Create(secondTrip, owner, [secondSeat], "DHA", "CTG", default);
            var expiredGroup = expiring.Hold!.HoldGroupPublicId;
            var expiredHold = await db.Holds.SingleAsync(x => x.HoldGroupPublicId == expiredGroup);
            var expiredSeat = await db.Seats.SingleAsync(x => x.SeatPublicId == secondSeat);
            expiredHold.ExpiresAtUtc = DateTime.UtcNow.AddMinutes(-1);
            expiredSeat.HoldExpiresAtUtc = expiredHold.ExpiresAtUtc;
            await db.SaveChangesAsync();

            Assert.Equal(409, (await service.Extend(expiredGroup, owner, default)).Status);
            Assert.Equal(409, (await service.Checkout(expiredGroup, owner, null)).Status);
            var replacement = await service.Create(secondTrip, otherUser, [secondSeat], "DHA", "CTG", default);
            Assert.Equal(200, replacement.Status);
            Assert.Equal(123m, replacement.Hold!.TotalAmount);
            Assert.Equal("DHA", replacement.Hold.Origin);
            Assert.Equal("CTG", replacement.Hold.Destination);

            var replacementGroup = replacement.Hold.HoldGroupPublicId;
            Assert.Equal(404, (await service.GetConfirmation(replacementGroup, owner)).Status);
            Assert.Equal(123m, (await service.GetConfirmation(replacementGroup, otherUser)).Hold!.TotalAmount);

            var checkout = await service.Checkout(replacementGroup, otherUser, null);
            Assert.Equal(200, checkout.Status);
            Assert.Equal("PendingPayment", checkout.Booking!.Status);
            Assert.Equal(123m, checkout.Booking.TotalAmount);
            Assert.Equal(checkout.Booking.PublicId, (await service.Checkout(replacementGroup, otherUser, null)).Booking!.PublicId);
            var bookingService = new BookingService(db);
            Assert.Empty(await bookingService.GetOwn(owner));
            Assert.Single(await bookingService.GetOwn(otherUser));
            Assert.Null(await bookingService.GetOwn(owner, checkout.Booking.PublicId));
            Assert.Equal(123m, (await bookingService.GetOwn(otherUser, checkout.Booking.PublicId))!.TotalAmount);
            Assert.Equal(checkout.Booking.PublicId, (await bookingService.GetAny(checkout.Booking.PublicId))!.PublicId);
            Assert.Single(await bookingService.Search(checkout.Booking.PublicId.ToString()));
            Assert.Empty(await bookingService.Search(Guid.NewGuid().ToString()));
            Assert.Equal(409, (await service.Extend(replacementGroup, otherUser)).Status);
            Assert.Equal(409, await service.Release(replacementGroup, otherUser));

            var policyService = new PolicyService(db);
            var admin = Guid.NewGuid();
            Assert.Equal(204, await policyService.Update("MaxSeatsPerUserPerTrip", 1, admin, default));
            Assert.Equal(422, (await service.Create(secondTrip, owner, [secondSeat, secondSeat2], "DHA", "CTG", default)).Status);

            var audit = await db.PolicyAudits.SingleAsync();
            Assert.Equal(4, audit.OldValue);
            Assert.Equal(1, audit.NewValue);
            Assert.Equal(admin, audit.ChangedBy);
            Assert.Equal(404, await policyService.Update("unknown", 1, admin, default));
            Assert.Equal(400, await policyService.Update("HoldMinutes", 0, admin, default));

            var concurrentApprovals = await Task.WhenAll(Enumerable.Range(0, 10).Select(async _ =>
            {
                await using var concurrentDb = new ReservationDbContext(options);
                return await new PaymentApprovalService(concurrentDb).ApplyApproval(checkout.Booking.PublicId);
            }));
            Assert.All(concurrentApprovals, status => Assert.Equal("Confirmed", status));
            db.ChangeTracker.Clear();

            var approvals = new PaymentApprovalService(db);
            Assert.Equal("Confirmed", await approvals.ApplyApproval(checkout.Booking.PublicId));
            Assert.Single(await db.Tickets.ToArrayAsync());
            Assert.Equal("Sold", (await db.Seats.SingleAsync(x => x.SeatPublicId == secondSeat)).Status);
            var repeatedCheckout = await service.Checkout(replacementGroup, otherUser, null);
            Assert.Equal("Confirmed", repeatedCheckout.Booking!.Status);
            Assert.Equal([secondSeat], repeatedCheckout.Booking.SeatPublicIds);
            Assert.Equal(422, (await service.Create(secondTrip, otherUser, [secondSeat2], "DHA", "CTG")).Status);

            var lateHold = await service.Create(secondTrip, owner, [secondSeat2], "DHA", "CTG");
            var lateBooking = await service.Checkout(lateHold.Hold!.HoldGroupPublicId, owner, null);
            var lateRow = await db.Holds.SingleAsync(x => x.HoldGroupPublicId == lateHold.Hold.HoldGroupPublicId);
            lateRow.ExpiresAtUtc = DateTime.UtcNow.AddMinutes(-1);
            var lateSeat = await db.Seats.SingleAsync(x => x.SeatPublicId == secondSeat2);
            lateSeat.HoldExpiresAtUtc = lateRow.ExpiresAtUtc;
            await db.SaveChangesAsync();
            var updates = new SeatUpdateBuffer();
            var otherUpdates = new SeatUpdateBuffer();
            var sweepResults = await Task.WhenAll(Enumerable.Range(0, 2).Select(async _ =>
            {
                await using var sweepDb = new ReservationDbContext(options);
                return await new ExpiredHoldSweeper(sweepDb).Sweep();
            }));
            Assert.Equal(1, sweepResults.Sum());
            db.ChangeTracker.Clear();
            Assert.Equal(0, await new ExpiredHoldSweeper(db).Sweep());
            Assert.Equal("Available", (await db.Seats.SingleAsync(x => x.SeatPublicId == secondSeat2)).Status);
            Assert.Equal("Expired", (await db.Bookings.SingleAsync(x => x.PublicId == lateBooking.Booking!.PublicId)).Status);
            var reader = new SeatUpdateReader(updates);
            var otherReader = new SeatUpdateReader(otherUpdates);
            await reader.Poll(db);
            await otherReader.Poll(db);
            var releasedHere = new List<SeatChange>();
            var releasedThere = new List<SeatChange>();
            while (updates.TryRead(out var update)) releasedHere.Add(update!);
            while (otherUpdates.TryRead(out var update)) releasedThere.Add(update!);
            Assert.Contains(releasedHere, change => change.Seats.Any(x => x.SeatPublicId == secondSeat2 && x.Status == "available"));
            Assert.Contains(releasedThere, change => change.Seats.Any(x => x.SeatPublicId == secondSeat2 && x.Status == "available"));
            await reader.Poll(db);
            Assert.False(updates.TryRead(out _));
            Assert.Equal("PaymentOrphaned", await approvals.ApplyApproval(lateBooking.Booking!.PublicId));
            Assert.Equal("PaymentOrphaned", await approvals.ApplyApproval(lateBooking.Booking.PublicId));
            Assert.Single(await db.Tickets.ToArrayAsync());
        }

        var reclaimTrip = Guid.NewGuid();
        var reclaimSeat = Guid.NewGuid();
        var reclaimLayout = new Layout(reclaimTrip, [new Coach("economy", [new Seat(reclaimSeat)])]);
        Guid abandonedBooking;
        await using (var db = new ReservationDbContext(options))
        {
            var inventory = new SeatInventoryService(db, new HttpClient(new LayoutHandler(reclaimLayout))
            {
                BaseAddress = new Uri("http://schedule/")
            });
            var holds = new HoldService(db, inventory, FareClient());
            var ownerId = Guid.NewGuid();
            var held = await holds.Create(reclaimTrip, ownerId, [reclaimSeat], "DHA", "CTG");
            var pending = await holds.Checkout(held.Hold!.HoldGroupPublicId, ownerId, null);
            abandonedBooking = pending.Booking!.PublicId;

            var holdRow = await db.Holds.SingleAsync(x => x.HoldGroupPublicId == held.Hold.HoldGroupPublicId);
            holdRow.ExpiresAtUtc = DateTime.UtcNow.AddMinutes(-1);
            var seatRow = await db.Seats.SingleAsync(x => x.SeatPublicId == reclaimSeat);
            seatRow.HoldExpiresAtUtc = holdRow.ExpiresAtUtc;
            await db.SaveChangesAsync();
        }

        await using (var db = new ReservationDbContext(options))
        {
            var inventory = new SeatInventoryService(db, new HttpClient(new LayoutHandler(reclaimLayout))
            {
                BaseAddress = new Uri("http://schedule/")
            });
            var replacement = await new HoldService(db, inventory, FareClient())
                .Create(reclaimTrip, Guid.NewGuid(), [reclaimSeat], "DHA", "CTG");
            Assert.Equal(200, replacement.Status);
            var replacementOwner = await db.Holds.Where(x => x.HoldGroupPublicId == replacement.Hold!.HoldGroupPublicId)
                .Select(x => x.UserId).SingleAsync();
            db.ChangeTracker.Clear();
            var confirmation = await new HoldService(db, inventory, FareClient())
                .GetConfirmation(replacement.Hold!.HoldGroupPublicId, replacementOwner);
            Assert.Equal(DateTimeKind.Utc, confirmation.Hold!.ExpiresAtUtc.Kind);
            var bookingDetails = await new BookingService(db).GetAny(abandonedBooking);
            Assert.Equal(DateTimeKind.Utc, bookingDetails!.CreatedAtUtc.Kind);
            Assert.Equal("Expired", await db.Bookings.Where(x => x.PublicId == abandonedBooking)
                .Select(x => x.Status).SingleAsync());
            Assert.Equal(0, await new ExpiredHoldSweeper(db).Sweep());
        }
    }
    private static ScheduleFareClient FareClient() => new(new HttpClient(new FareHandler()) { BaseAddress = new Uri("http://schedule/") });

    private sealed class FareHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var tripId = Guid.Parse(request.RequestUri!.Segments[3].TrimEnd('/'));
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(new ScheduleFare(tripId, "economy", 123m)) });
        }
    }

    private sealed class LayoutHandler(Layout layout) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(layout) });
    }
    private sealed class AvailabilityHandler(ClassAvailability counts) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(new[] { counts }) });
    }
}
