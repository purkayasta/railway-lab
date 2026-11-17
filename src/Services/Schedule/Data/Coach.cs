namespace Schedule.Data;

public sealed class Coach
{
    public long Id { get; set; }
    public long TripId { get; set; }
    public string Label { get; set; } = ""; public long ClassId { get; set; }
    public int SeatCount { get; set; }
}
