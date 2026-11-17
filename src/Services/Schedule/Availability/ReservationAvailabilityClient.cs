using System.Net.Http.Json;
using System.Text.Json;
using Polly.CircuitBreaker;
using Polly.Timeout;
using Schedule.Services;

namespace Schedule.Availability;

/// <summary>Reads the public Reservation availability contract. Failures leave availability unknown.</summary>
public sealed class ReservationAvailabilityClient(HttpClient http, ILogger<ReservationAvailabilityClient> logger)
{
    public async Task<TripResult[]> EnrichAsync(TripResult[] trips, CancellationToken cancellationToken = default)
    {
        return await Task.WhenAll(trips.Select(async trip =>
        {
            try
            {
                using var requestTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                requestTimeout.CancelAfter(TimeSpan.FromMilliseconds(160));
                using var response = await http.GetAsync($"trips/{trip.PublicId}/availability", requestTimeout.Token);
                if (!response.IsSuccessStatusCode) return trip;
                var availability = await response.Content.ReadFromJsonAsync<ClassAvailability?[]>(cancellationToken: requestTimeout.Token);
                var matches = availability?.Where(item =>
                    item is not null && string.Equals(item.ClassCode, trip.ClassCode, StringComparison.OrdinalIgnoreCase)).ToArray();
                var matching = matches is { Length: 1 } ? matches[0] : null;
                return matching is not { Total: >= 0, Available: >= 0, Held: >= 0, Sold: >= 0 }
                    || (long)matching.Available.Value + matching.Held.Value + matching.Sold.Value != matching.Total.Value
                    ? trip
                    : trip with { AvailableSeats = matching.Available.Value, AvailabilityUnknown = false };
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                return trip;
            }
            catch (Exception ex) when (ex is HttpRequestException or TimeoutRejectedException or BrokenCircuitException or JsonException or NotSupportedException)
            {
                logger.LogWarning(ex, "Reservation availability unavailable for trip {TripId}", trip.PublicId);
                return trip;
            }
        }));
    }
}

// Reservation's public response is an array of counts keyed by class code.
public sealed record ClassAvailability(string ClassCode, int? Total, int? Available, int? Held, int? Sold);
