namespace Reservation.Data;

public sealed class SeatUpdate
{
    public long Id { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public string Payload { get; set; } = "";
}
