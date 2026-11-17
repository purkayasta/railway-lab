namespace Reservation.Data;

public sealed class SeatHold
{
    public long Id { get; set; }
    public long TripSeatId { get; set; }
    public Guid UserId { get; set; }
    public Guid HoldGroupPublicId { get; set; }
    public DateTime ExpiresAtUtc { get; set; }
    public bool ExtendedOnce { get; set; }
    public string? Origin { get; set; }
    public string? Destination { get; set; }
    public decimal? UnitPrice { get; set; }
}
