using Schedule;

namespace Schedule.Tests;

public class JourneyPricingTests
{
    private static readonly RouteStop[] Stops =
    [new("B", 2, 60), new("A", 1, 0), new("C", 3, 180)];

    [Fact]
    public void Finds_forward_segment_by_sequence_and_calculates_proportional_fare()
    {
        var segment = JourneyPricing.FindSegment(Stops, "b", "C");
        Assert.NotNull(segment);
        Assert.Equal(60, segment.DepartureOffsetMinutes);
        Assert.Equal(180, segment.ArrivalOffsetMinutes);
        Assert.Equal(200m, JourneyPricing.Fare(300m, segment));
    }

    [Theory]
    [InlineData("C", "A")]
    [InlineData("A", "A")]
    [InlineData("A", "missing")]
    [InlineData("missing", "C")]
    public void Rejects_non_forward_or_missing_stations(string from, string to) =>
        Assert.Null(JourneyPricing.FindSegment(Stops, from, to));

    [Fact]
    public void Rejects_invalid_offsets() =>
        Assert.Throws<InvalidOperationException>(() => JourneyPricing.FindSegment(
            [new("A", 1, 0), new("B", 2, 0)], "A", "B"));
}
