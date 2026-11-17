namespace Schedule.Data;

public sealed class Trip
{
    public long Id { get; set; }
    public Guid PublicId { get; set; }
    public long TrainId { get; set; }
    public DateOnly ServiceDate { get; set; }
    public DateTime DepartureUtc { get; set; }
    public DateTime ArrivalUtc { get; set; }
    public string Status { get; set; } = "Scheduled";
}
