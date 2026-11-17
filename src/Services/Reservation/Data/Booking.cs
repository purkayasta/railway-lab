namespace Reservation.Data;

public sealed class Booking
{
    public long Id { get; set; }
    public Guid PublicId { get; set; }
    public Guid UserId { get; set; }
    public Guid TripPublicId { get; set; }
    public Guid HoldGroupPublicId { get; set; }
    public string Status { get; set; } = "PendingPayment";
    public decimal TotalAmount { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public string Origin { get; set; } = "";
    public string Destination { get; set; } = "";
    public string? PassengerName { get; set; }
    public Guid? BookedByUserId { get; set; }
    public int SeatCount { get; set; }
}
