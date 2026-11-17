namespace Schedule;

/// <summary>Calculates a journey over an ordered route and its proportional fare.</summary>
public static class JourneyPricing
{
    /// <summary>
    /// Returns the offsets of a forward journey, or null when either station is absent
    /// or the destination is not after the origin. Station codes are case-insensitive.
    /// </summary>
    public static JourneySegment? FindSegment(IEnumerable<RouteStop> stops, string from, string to)
    {
        var ordered = stops.OrderBy(stop => stop.Sequence).ToArray();
        var origin = Array.FindIndex(ordered, stop => string.Equals(stop.StationCode, from, StringComparison.OrdinalIgnoreCase));
        var destination = Array.FindIndex(ordered, stop => string.Equals(stop.StationCode, to, StringComparison.OrdinalIgnoreCase));
        if (origin < 0 || destination <= origin || ordered.Length < 2)
            return null;

        var routeMinutes = ordered[^1].OffsetMinutes - ordered[0].OffsetMinutes;
        var journeyMinutes = ordered[destination].OffsetMinutes - ordered[origin].OffsetMinutes;
        if (routeMinutes <= 0 || journeyMinutes <= 0)
            throw new InvalidOperationException("Route offsets must increase along the route.");

        return new JourneySegment(ordered[origin].OffsetMinutes, ordered[destination].OffsetMinutes,
            (decimal)journeyMinutes / routeMinutes);
    }

    public static decimal Fare(decimal baseFare, JourneySegment segment)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(baseFare);
        return decimal.Round(baseFare * segment.DistanceFactor, 2, MidpointRounding.AwayFromZero);
    }
}

public sealed record RouteStop(string StationCode, int Sequence, int OffsetMinutes);
public sealed record JourneySegment(int DepartureOffsetMinutes, int ArrivalOffsetMinutes, decimal DistanceFactor);
