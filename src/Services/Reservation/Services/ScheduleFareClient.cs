using System.Net;
using System.Net.Http.Json;

namespace Reservation.Services;

public sealed record ScheduleFare(Guid PublicId, string ClassCode, decimal Fare);

public sealed class ScheduleFareClient(HttpClient http)
{
    public async Task<decimal?> GetFare(Guid tripId, string origin, string destination, string classCode, CancellationToken cancellationToken = default)
    {
        var path = $"internal/trips/{tripId}/fare?from={Uri.EscapeDataString(origin)}&to={Uri.EscapeDataString(destination)}&class={Uri.EscapeDataString(classCode)}";
        using var response = await http.GetAsync(path, cancellationToken);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        var quote = await response.Content.ReadFromJsonAsync<ScheduleFare>(cancellationToken: cancellationToken);
        if (quote is null || quote.PublicId != tripId ||
            !string.Equals(quote.ClassCode, classCode, StringComparison.OrdinalIgnoreCase) || quote.Fare < 0)
        {
            throw new InvalidDataException("Schedule returned an invalid fare.");
        }

        return quote.Fare;
    }
}
