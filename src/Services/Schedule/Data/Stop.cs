namespace Schedule.Data;

public sealed class Stop
{
    public long Id { get; set; }
    public long RouteId { get; set; }
    public long StationId { get; set; }
    public int Sequence { get; set; }
    public int OffsetMinutes { get; set; }
}
