using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Platform.Auth;
using Reservation;
using Reservation.Data;
using Reservation.Services;
using Testcontainers.MsSql;

namespace Schedule.Tests;

[Collection("SQL integration")]
public class BookingEndpointTests
{
    [Fact]
    public async Task Booking_reads_require_permissions_and_enforce_ownership()
    {
        await using var sql = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2025-latest").Build();
        await sql.StartAsync();

        var owner = Guid.NewGuid();
        var other = Guid.NewGuid();
        var bookingId = Guid.NewGuid();
        var groupId = Guid.NewGuid();
        var agentGroupId = Guid.NewGuid();
        var tripId = Guid.NewGuid();
        var options = new DbContextOptionsBuilder<ReservationDbContext>()
            .UseSqlServer(sql.GetConnectionString(), o => o.MigrationsHistoryTable("__EFMigrationsHistory", "reservation")).Options;
        await using (var db = new ReservationDbContext(options))
        {
            await db.Database.MigrateAsync();
            var seat = new TripSeat { TripPublicId = tripId, SeatPublicId = Guid.NewGuid(), ClassCode = "economy", Status = "Held", HoldExpiresAtUtc = DateTime.UtcNow.AddMinutes(5) };
            db.Trips.Add(new InventoryTrip { TripPublicId = tripId, Seats = [seat] });
            await db.SaveChangesAsync();
            db.Holds.Add(new SeatHold
            {
                TripSeatId = seat.Id,
                UserId = owner,
                HoldGroupPublicId = groupId,
                ExpiresAtUtc = seat.HoldExpiresAtUtc.Value,
                Origin = "DHA",
                Destination = "CTG",
                UnitPrice = 123
            });
            var agentSeat = new TripSeat { TripPublicId = tripId, SeatPublicId = Guid.NewGuid(), ClassCode = "economy", Status = "Held", HoldExpiresAtUtc = DateTime.UtcNow.AddMinutes(5) };
            db.Seats.Add(agentSeat);
            await db.SaveChangesAsync();
            db.Holds.Add(new SeatHold
            {
                TripSeatId = agentSeat.Id,
                UserId = owner,
                HoldGroupPublicId = agentGroupId,
                ExpiresAtUtc = agentSeat.HoldExpiresAtUtc.Value,
                Origin = "DHA",
                Destination = "CTG",
                UnitPrice = 123
            });
            db.Bookings.Add(new Booking
            {
                PublicId = bookingId,
                UserId = owner,
                TripPublicId = tripId,
                HoldGroupPublicId = groupId,
                Origin = "DHA",
                Destination = "CTG",
                TotalAmount = 123,
                CreatedAtUtc = DateTime.UtcNow,
                SeatCount = 1
            });
            await db.SaveChangesAsync();
        }

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddAuthentication("test").AddScheme<AuthenticationSchemeOptions, TestAuthenticationHandler>("test", _ => { });
        builder.AddPermissionAuthorization();
        builder.Services.AddDbContext<ReservationDbContext>(o => o.UseSqlServer(sql.GetConnectionString()));
        builder.Services.AddScoped<BookingService>();
        builder.Services.AddHttpClient<SeatInventoryService>();
        builder.Services.AddHttpClient<ScheduleFareClient>();
        builder.Services.AddScoped<HoldService>();
        builder.Services.AddScoped<PaymentApprovalService>();
        builder.Services.AddScoped<SimulatedPaymentService>();
        builder.Services.AddScoped<PolicyService>();

        await using var app = builder.Build();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapReservationEndpoints();
        await app.StartAsync();
        var client = app.GetTestClient();

        var limit = await client.GetFromJsonAsync<SeatLimit>("/seat-limit");
        Assert.Equal(4, limit!.MaxSeatsPerUserPerTrip);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/bookings")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync($"/holds/{groupId}/checkout", new { })).StatusCode);
        client.DefaultRequestHeaders.Add("X-Test-User", other.ToString());
        client.DefaultRequestHeaders.Add("X-Test-Role", "passenger");
        Assert.Empty((await client.GetFromJsonAsync<BookingDetails[]>("/bookings"))!);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/bookings/{bookingId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsJsonAsync($"/holds/{groupId}/checkout", new { })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/admin/bookings")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync($"/admin/bookings/{bookingId}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync($"/holds/{agentGroupId}/checkout", new { onBehalfOf = new { passengerName = "Passenger" } })).StatusCode);

        client.DefaultRequestHeaders.Remove("X-Test-User");
        client.DefaultRequestHeaders.Add("X-Test-User", owner.ToString());
        Assert.Equal(bookingId, Assert.Single((await client.GetFromJsonAsync<BookingDetails[]>("/bookings"))!).PublicId);
        Assert.Equal(bookingId, (await client.GetFromJsonAsync<BookingDetails>($"/bookings/{bookingId}"))!.PublicId);
        var checkout = await client.PostAsJsonAsync($"/holds/{groupId}/checkout", new { });
        Assert.Equal(HttpStatusCode.OK, checkout.StatusCode);
        Assert.Equal("Confirmed", (await checkout.Content.ReadFromJsonAsync<BookingResult>())!.Status);
        var retry = await client.PostAsJsonAsync($"/holds/{groupId}/checkout", new { });
        Assert.Equal("Confirmed", (await retry.Content.ReadFromJsonAsync<BookingResult>())!.Status);
        await using (var verification = new ReservationDbContext(options))
        {
            Assert.Single(await verification.Tickets.ToArrayAsync());
        }

        client.DefaultRequestHeaders.Remove("X-Test-Role");
        client.DefaultRequestHeaders.Add("X-Test-Role", "operations");
        Assert.Equal(bookingId, Assert.Single((await client.GetFromJsonAsync<BookingDetails[]>($"/admin/bookings?search={bookingId}"))!).PublicId);
        Assert.Equal(bookingId, (await client.GetFromJsonAsync<BookingDetails>($"/admin/bookings/{bookingId}"))!.PublicId);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/admin/bookings/{Guid.NewGuid()}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/admin/policy")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PutAsJsonAsync("/admin/policy/HoldMinutes", new { value = 5 })).StatusCode);

        client.DefaultRequestHeaders.Remove("X-Test-Role");
        client.DefaultRequestHeaders.Add("X-Test-Role", "station-agent");
        var onBehalf = await client.PostAsJsonAsync($"/holds/{agentGroupId}/checkout", new { onBehalfOf = new { passengerName = "Passenger" } });
        Assert.Equal(HttpStatusCode.OK, onBehalf.StatusCode);
        var agentBooking = await onBehalf.Content.ReadFromJsonAsync<BookingResult>();
        var details = await client.GetFromJsonAsync<BookingDetails>($"/admin/bookings/{agentBooking!.PublicId}");
        Assert.Equal("Passenger", details!.PassengerName);
        Assert.Equal(owner, details.BookedByUserId);
    }

    private sealed record SeatLimit(int MaxSeatsPerUserPerTrip);

    private sealed class TestAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.TryGetValue("X-Test-User", out var user))
            {
                return Task.FromResult(AuthenticateResult.NoResult());
            }

            var claims = new[] { new Claim("sub", user.ToString()), new Claim("role", Request.Headers["X-Test-Role"].ToString()) };
            var identity = new ClaimsIdentity(claims, Scheme.Name);
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name)));
        }
    }
}
