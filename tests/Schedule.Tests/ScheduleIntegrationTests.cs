using Schedule.Services;
using Schedule.Availability;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Schedule.Data;
using Testcontainers.MsSql;

namespace Schedule.Tests;

[Collection("SQL integration")]
public class ScheduleIntegrationTests
{
    [Fact]
    public async Task Fresh_database_seeds_idempotently_and_search_respects_direction_and_class()
    {
        await using var sql = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2025-latest").Build();
        await sql.StartAsync();
        var options = new DbContextOptionsBuilder<ScheduleDbContext>()
            .UseSqlServer(sql.GetConnectionString(), o => o.MigrationsHistoryTable("__EFMigrationsHistory", ScheduleDbContext.Schema)).Options;
        await using var db = new ScheduleDbContext(options);
        await ScheduleSeed.Initialize(db);
        var initialSeats = await db.Seats.CountAsync();
        await ScheduleSeed.Initialize(db);
        Assert.Equal(initialSeats, await db.Seats.CountAsync());
        Assert.Equal(12, await db.Stations.CountAsync());
        Assert.Equal(6 * 14 * 8 * 40, initialSeats);
        var date = DateOnly.FromDateTime(DateTime.UtcNow);
        var economy = await new SearchService(db).SearchAsync("DHA", "CTG", date, "economy", default);
        var business = await new SearchService(db).SearchAsync("DHA", "CTG", date, "business", default);
        Assert.NotEmpty(economy);
        Assert.All(economy, trip => { Assert.Equal(600, trip.Fare); Assert.True(trip.AvailabilityUnknown); });
        Assert.All(business, trip => Assert.Equal(1200, trip.Fare));
        var reverse = await new SearchService(db).SearchAsync("CTG", "DHA", date, "economy", default);
        Assert.NotEmpty(reverse);
        Assert.DoesNotContain(reverse, trip => economy.Any(e => e.PublicId == trip.PublicId));
        var layout = await new LayoutService(db).FindAsync(economy[0].PublicId, default);
        Assert.NotNull(layout);
        Assert.Equal(8, layout.Coaches.Length);
        Assert.All(layout.Coaches, coach => Assert.Equal(40, coach.Seats.Length));

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddDbContext<ScheduleDbContext>(o => o.UseSqlServer(sql.GetConnectionString()));
        builder.Services.AddScoped<StationsService>();
        builder.Services.AddScoped<SearchService>();
        builder.Services.AddScoped<TripService>();
        builder.Services.AddScoped<LayoutService>();
        builder.Services.AddReservationAvailability(new Uri("http://reservation/"))
            .ConfigurePrimaryHttpMessageHandler(() => new ReservationUnavailable());
        await using var app = builder.Build();
        app.MapScheduleEndpoints();
        await app.StartAsync();
        var client = app.GetTestClient();
        var results = await client.GetFromJsonAsync<TripResult[]>($"/trips/search?from=DHA&to=CTG&date={date:yyyy-MM-dd}&class=economy");
        Assert.NotNull(results);
        Assert.NotEmpty(results);
        Assert.All(results, trip => Assert.True(trip.AvailabilityUnknown));
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/trips/{results[0].PublicId}/layout")).StatusCode);
        var fareResponse = await client.GetFromJsonAsync<InternalFare>($"/internal/trips/{results[0].PublicId}/fare?from=DHA&to=CTG&class=economy");
        Assert.NotNull(fareResponse);
        Assert.Equal(600m, fareResponse.Fare);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/internal/trips/{results[0].PublicId}/fare?from=CTG&to=DHA&class=economy")).StatusCode);
        Assert.Equal(HttpStatusCode.MethodNotAllowed, (await client.PostAsync("/stations", null)).StatusCode);
        await app.StopAsync();
    }
    private sealed record InternalFare(decimal Fare);
    private sealed class ReservationUnavailable : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
    }
}
