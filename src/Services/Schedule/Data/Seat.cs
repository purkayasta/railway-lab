namespace Schedule.Data;

public sealed class Seat
{
    public long Id { get; set; }
    public Guid PublicId { get; set; }
    public long CoachId { get; set; }
    public string Label { get; set; } = ""; public int Row { get; set; }
    public string Column { get; set; } = "";
}
