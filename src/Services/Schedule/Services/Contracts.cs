namespace Schedule.Services;

public record StationResult(string Code, string Name, string City);
public record TripDetails(Guid PublicId, string Number, string Name, DateOnly ServiceDate, DateTime DepartureUtc, DateTime ArrivalUtc, string Status);
public record SeatResult(Guid PublicId, string Label, int Row, string Column);
public record CoachResult(string Label, string ClassCode, string Name, int SeatCount, SeatResult[] Seats);
public record LayoutResult(Guid PublicId, CoachResult[] Coaches);
public record TripResult(Guid PublicId, string TrainNumber, string TrainName, DateTime DepartureUtc, DateTime ArrivalUtc, string ClassCode, decimal Fare, int? AvailableSeats, bool AvailabilityUnknown);

