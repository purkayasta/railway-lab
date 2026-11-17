namespace Reservation.Data;

public sealed class Ticket
{
    public long Id { get; set; }
    public long BookingId { get; set; }
    public long TripSeatId { get; set; }
    public string TicketNumber { get; set; } = "";
    public DateTime IssuedAtUtc { get; set; }
}
