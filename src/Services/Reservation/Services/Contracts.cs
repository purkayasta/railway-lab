namespace Reservation.Services;

public sealed record Layout(Guid PublicId, Coach[] Coaches);
public sealed record Coach(string ClassCode, Seat[] Seats);
public sealed record Seat(Guid PublicId);
public sealed record ClassAvailability(string ClassCode, int Total, int Available, int Held, int Sold);
public sealed record SeatStatus(Guid SeatPublicId, string ClassCode, string Status, bool Mine);

public sealed record HoldResult(Guid HoldGroupPublicId, Guid TripPublicId, DateTime ExpiresAtUtc, Guid[] SeatPublicIds, string? Origin, string? Destination, decimal? TotalAmount);
public sealed record HoldOutcome(int Status, HoldResult? Hold = null);
public sealed record BookingResult(Guid PublicId, string Status, string Origin, string Destination, decimal TotalAmount, Guid[] SeatPublicIds);
public sealed record BookingOutcome(int Status, BookingResult? Booking = null);

