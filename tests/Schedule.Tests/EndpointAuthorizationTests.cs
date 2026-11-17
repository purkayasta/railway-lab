using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Platform.Auth;
using Schedule.Availability;
using Schedule.Services;

namespace Schedule.Tests;

public class EndpointAuthorizationTests
{
    [Fact]
    public void Reservation_endpoints_have_reviewed_authorization()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddAuthorization();
        builder.Services.AddSingleton<Reservation.Services.SeatInventoryService>(_ => throw new InvalidOperationException());
        builder.Services.AddSingleton<Reservation.Services.HoldService>(_ => throw new InvalidOperationException());
        builder.Services.AddSingleton<Reservation.Services.BookingService>(_ => throw new InvalidOperationException());
        builder.Services.AddSingleton<Reservation.Services.SimulatedPaymentService>(_ => throw new InvalidOperationException());
        builder.Services.AddSingleton<Reservation.Services.PolicyService>(_ => throw new InvalidOperationException());
        var app = builder.Build();
        Reservation.ReservationEndpoints.MapReservationEndpoints(app);
        var permissions = StaticPermissionSource.FromFile(Path.Combine(AppContext.BaseDirectory, "default-role-permissions.json"));
        var endpoints = ((IEndpointRouteBuilder)app).DataSources.SelectMany(source => source.Endpoints);
        Assert.Empty(EndpointAuthorizationAudit.Audit(endpoints, new HashSet<string>
        {
            "GET /trips/{tripId:guid}/availability", "GET /trips/{tripId:guid}/seats", "GET /seat-limit"
        }, permissions));
    }

    [Fact]
    public void Schedule_endpoints_are_public_read_only_and_reviewed()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddScoped<StationsService>();
        builder.Services.AddScoped<SearchService>();
        builder.Services.AddScoped<TripService>();
        builder.Services.AddScoped<LayoutService>();
        builder.Services.AddSingleton<ReservationAvailabilityClient>(_ => throw new InvalidOperationException());
        var app = builder.Build();
        app.MapScheduleEndpoints();
        var allowlist = new HashSet<string>
        {
            "GET /stations", "GET /trips/search", "GET /trips/{id:guid}", "GET /trips/{id:guid}/layout",
            "GET /internal/trips/{id:guid}/fare"
        };
        var permissions = StaticPermissionSource.FromFile(Path.Combine(AppContext.BaseDirectory, "default-role-permissions.json"));
        var endpoints = ((IEndpointRouteBuilder)app).DataSources.SelectMany(source => source.Endpoints);
        Assert.Empty(EndpointAuthorizationAudit.Audit(endpoints, allowlist, permissions));
    }
}
