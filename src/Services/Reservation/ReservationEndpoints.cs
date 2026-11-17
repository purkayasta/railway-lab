using Microsoft.Data.SqlClient;
using Microsoft.AspNetCore.Authorization;
using Platform.Auth;
using Reservation.Services;

namespace Reservation;

public sealed record CreateHoldRequest(Guid[]? SeatPublicIds, string? Origin, string? Destination);
public sealed record UpdatePolicyRequest(int Value);
public sealed record OnBehalfOfRequest(string? PassengerName);
public sealed record CheckoutRequest(OnBehalfOfRequest? OnBehalfOf);

public static class ReservationEndpoints
{
    public static IEndpointRouteBuilder MapReservationEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/seat-limit", async (PolicyService policies, CancellationToken cancellationToken) =>
        {
            var settings = await policies.GetAll(cancellationToken);
            var limit = settings.Single(x => x.Key == "MaxSeatsPerUserPerTrip").Value;
            return Results.Ok(new { maxSeatsPerUserPerTrip = limit });
        }).AllowAnonymous();

        app.MapGet("/trips/{tripId:guid}/availability", async (Guid tripId, SeatInventoryService inventory, CancellationToken cancellationToken) =>
        {
            try
            {
                var availability = await inventory.GetAvailability(tripId, cancellationToken);
                return availability is not null ? Results.Ok(availability) : Results.NotFound();
            }
            catch (Exception ex) when (ex is HttpRequestException or InvalidDataException or System.Text.Json.JsonException)
            {
                return Results.StatusCode(503);
            }
        }).AllowAnonymous();

        app.MapGet("/trips/{tripId:guid}/seats", async (
            Guid tripId, HttpContext context, SeatInventoryService inventory, CancellationToken cancellationToken) =>
        {
            Guid? userId = Guid.TryParse(context.User.FindFirst("sub")?.Value, out var subject) ? subject : null;

            try
            {
                var seats = await inventory.GetSeats(tripId, userId, cancellationToken);
                return seats is not null ? Results.Ok(seats) : Results.NotFound();
            }
            catch (Exception ex) when (ex is HttpRequestException or InvalidDataException or System.Text.Json.JsonException)
            {
                return Results.StatusCode(503);
            }
        }).AllowAnonymous();

        app.MapPost("/trips/{tripId:guid}/holds", async (
            Guid tripId, CreateHoldRequest request, HttpContext context, HoldService holds, CancellationToken cancellationToken) =>
        {
            if (!Guid.TryParse(context.User.FindFirst("sub")?.Value, out var userId))
            {
                return Results.Unauthorized();
            }

            try
            {
                var result = await holds.Create(tripId, userId, request.SeatPublicIds, request.Origin, request.Destination, cancellationToken);
                return result.Status == 200 ? Results.Ok(result.Hold) : Results.StatusCode(result.Status);
            }
            catch (SqlException ex) when (ex.Number is 1222 or 1205)
            {
                context.Response.Headers.RetryAfter = "1";
                return Results.StatusCode(503);
            }
            catch (Exception ex) when (ex is HttpRequestException or InvalidDataException or System.Text.Json.JsonException)
            {
                return Results.StatusCode(503);
            }
        }).RequirePermission(Permissions.SeatsHold);

        app.MapGet("/holds/{groupId:guid}", async (
            Guid groupId, HttpContext context, HoldService holds, CancellationToken cancellationToken) =>
        {
            if (!Guid.TryParse(context.User.FindFirst("sub")?.Value, out var userId))
            {
                return Results.Unauthorized();
            }

            var result = await holds.GetConfirmation(groupId, userId, cancellationToken);
            return result.Status == 200 ? Results.Ok(result.Hold) : Results.StatusCode(result.Status);
        }).RequirePermission(Permissions.SeatsHold);

        app.MapPost("/holds/{groupId:guid}/checkout", async (
            Guid groupId, CheckoutRequest request, HttpContext context, HoldService holds, SimulatedPaymentService payment,
            IAuthorizationService authorization, CancellationToken cancellationToken) =>
        {
            if (!Guid.TryParse(context.User.FindFirst("sub")?.Value, out var userId))
            {
                return Results.Unauthorized();
            }

            if (request.OnBehalfOf is not null &&
                !(await authorization.AuthorizeAsync(context.User, null, new PermissionRequirement(Permissions.BookingsCreateOnBehalf))).Succeeded)
            {
                return Results.Forbid();
            }

            if (request.OnBehalfOf is not null && string.IsNullOrWhiteSpace(request.OnBehalfOf.PassengerName))
            {
                return Results.BadRequest();
            }

            try
            {
                var result = await holds.Checkout(groupId, userId, request.OnBehalfOf?.PassengerName, cancellationToken);
                if (result.Status != 200 || result.Booking is null)
                {
                    return Results.StatusCode(result.Status);
                }

                var status = await payment.Approve(result.Booking.PublicId, cancellationToken);
                return status is null ? Results.NotFound() : Results.Ok(result.Booking with { Status = status });
            }
            catch (SqlException ex) when (ex.Number is 1222 or 1205)
            {
                context.Response.Headers.RetryAfter = "1";
                return Results.StatusCode(503);
            }
        }).RequirePermission(Permissions.SeatsHold);

        app.MapGet("/bookings", async (HttpContext context, BookingService bookings, CancellationToken cancellationToken) =>
        {
            if (!Guid.TryParse(context.User.FindFirst("sub")?.Value, out var userId))
            {
                return Results.Unauthorized();
            }

            return Results.Ok(await bookings.GetOwn(userId, cancellationToken));
        }).RequirePermission(Permissions.BookingsReadOwn);

        app.MapGet("/bookings/{bookingId:guid}", async (
            Guid bookingId, HttpContext context, BookingService bookings, CancellationToken cancellationToken) =>
        {
            if (!Guid.TryParse(context.User.FindFirst("sub")?.Value, out var userId))
            {
                return Results.Unauthorized();
            }

            var booking = await bookings.GetOwn(userId, bookingId, cancellationToken);
            return booking is null ? Results.NotFound() : Results.Ok(booking);
        }).RequirePermission(Permissions.BookingsReadOwn);

        app.MapGet("/admin/bookings", async (string? search, BookingService bookings, CancellationToken cancellationToken) =>
            Results.Ok(await bookings.Search(search, cancellationToken))).RequirePermission(Permissions.BookingsReadAny);

        app.MapGet("/admin/bookings/{bookingId:guid}", async (
            Guid bookingId, BookingService bookings, CancellationToken cancellationToken) =>
        {
            var booking = await bookings.GetAny(bookingId, cancellationToken);
            return booking is null ? Results.NotFound() : Results.Ok(booking);
        }).RequirePermission(Permissions.BookingsReadAny);

        app.MapGet("/admin/policy", async (PolicyService policies, CancellationToken cancellationToken) =>
            Results.Ok(await policies.GetAll(cancellationToken))).RequirePermission(Permissions.PolicyRead);

        app.MapPut("/admin/policy/{key}", async (
            string key, UpdatePolicyRequest request, HttpContext context, PolicyService policies, CancellationToken cancellationToken) =>
        {
            if (!Guid.TryParse(context.User.FindFirst("sub")?.Value, out var userId))
            {
                return Results.Unauthorized();
            }

            try
            {
                return Results.StatusCode(await policies.Update(key, request.Value, userId, cancellationToken));
            }
            catch (SqlException ex) when (ex.Number is 1222 or 1205)
            {
                context.Response.Headers.RetryAfter = "1";
                return Results.StatusCode(503);
            }
        }).RequirePermission(Permissions.PolicyWrite);

        app.MapPost("/holds/{groupId:guid}/extend", async (
            Guid groupId, HttpContext context, HoldService holds, CancellationToken cancellationToken) =>
        {
            if (!Guid.TryParse(context.User.FindFirst("sub")?.Value, out var userId))
            {
                return Results.Unauthorized();
            }

            try
            {
                var result = await holds.Extend(groupId, userId, cancellationToken);
                return result.Status == 200 ? Results.Ok(result.Hold) : Results.StatusCode(result.Status);
            }
            catch (SqlException ex) when (ex.Number is 1222 or 1205)
            {
                context.Response.Headers.RetryAfter = "1";
                return Results.StatusCode(503);
            }
        }).RequirePermission(Permissions.SeatsHold);

        app.MapDelete("/holds/{groupId:guid}", async (
            Guid groupId, HttpContext context, HoldService holds, CancellationToken cancellationToken) =>
        {
            if (!Guid.TryParse(context.User.FindFirst("sub")?.Value, out var userId))
            {
                return Results.Unauthorized();
            }

            try
            {
                return Results.StatusCode(await holds.Release(groupId, userId, cancellationToken));
            }
            catch (SqlException ex) when (ex.Number is 1222 or 1205)
            {
                context.Response.Headers.RetryAfter = "1";
                return Results.StatusCode(503);
            }
        }).RequirePermission(Permissions.SeatsHold);

        return app;
    }
}
