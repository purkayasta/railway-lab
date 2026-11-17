using Schedule.Services;
using Schedule.Availability;

namespace Schedule;

public static class ScheduleEndpoints
{
    public static IEndpointRouteBuilder MapScheduleEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/internal/trips/{id:guid}/fare", async (
            Guid id, string? from, string? to, string? @class, TripService trips, SearchService search, CancellationToken cancellationToken) =>
        {
            if (string.IsNullOrWhiteSpace(from) || string.IsNullOrWhiteSpace(to) || string.IsNullOrWhiteSpace(@class))
            {
                return Results.BadRequest();
            }

            var trip = await trips.FindAsync(id, cancellationToken);
            if (trip is null)
            {
                return Results.NotFound();
            }

            var matches = await search.SearchAsync(from.Trim(), to.Trim(), trip.ServiceDate, @class.Trim().ToLowerInvariant(), cancellationToken);
            var match = matches.SingleOrDefault(x => x.PublicId == id);
            return match is null ? Results.NotFound() : Results.Ok(new { match.PublicId, match.ClassCode, match.Fare });
        }).AllowAnonymous();

        app.MapGet("/stations", async (string? query, StationsService stations, CancellationToken cancellationToken) => Results.Ok(await stations.FindAsync(query, cancellationToken))).AllowAnonymous();
        app.MapGet("/trips/search", async (string? from, string? to, DateOnly? date, string? @class, SearchService search, ReservationAvailabilityClient availability, CancellationToken cancellationToken) =>
        {
            if (string.IsNullOrWhiteSpace(from) || string.IsNullOrWhiteSpace(to) || date is null || string.IsNullOrWhiteSpace(@class)) return Results.BadRequest();
            var trips = await search.SearchAsync(from.Trim(), to.Trim(), date.Value, @class.Trim().ToLowerInvariant(), cancellationToken);
            return Results.Ok(await availability.EnrichAsync(trips, cancellationToken));
        }).AllowAnonymous();
        app.MapGet("/trips/{id:guid}", async (Guid id, TripService trips, CancellationToken cancellationToken) => (await trips.FindAsync(id, cancellationToken)) is { } trip ? Results.Ok(trip) : Results.NotFound()).AllowAnonymous();
        app.MapGet("/trips/{id:guid}/layout", async (Guid id, LayoutService layouts, CancellationToken cancellationToken) => (await layouts.FindAsync(id, cancellationToken)) is { } layout ? Results.Ok(layout) : Results.NotFound()).AllowAnonymous();
        return app;
    }
}
