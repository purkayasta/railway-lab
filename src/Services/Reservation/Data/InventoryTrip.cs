namespace Reservation.Data;

public sealed class InventoryTrip
{
    public Guid TripPublicId { get; set; }
    public List<TripSeat> Seats { get; set; } = [];
}
