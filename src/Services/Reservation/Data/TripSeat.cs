namespace Reservation.Data;

public sealed class TripSeat
{
    public long Id { get; set; }
    public Guid TripPublicId { get; set; }
    public Guid SeatPublicId { get; set; }
    public string ClassCode { get; set; } = "";
    public string Status { get; set; } = "Available";
    public DateTime? HoldExpiresAtUtc { get; set; }
}
