namespace Reservation.Data;

public sealed class PolicySetting
{
    public string Key { get; set; } = "";
    public int Value { get; set; }
    public DateTime? UpdatedAtUtc { get; set; }
    public Guid? UpdatedBy { get; set; }
}
