namespace Reservation.Data;

public sealed class PolicyAudit
{
    public long Id { get; set; }
    public string Key { get; set; } = "";
    public int OldValue { get; set; }
    public int NewValue { get; set; }
    public Guid ChangedBy { get; set; }
    public DateTime ChangedAtUtc { get; set; }
}
