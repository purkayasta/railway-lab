using Microsoft.Extensions.Http.Resilience;
using Polly;

namespace Schedule.Availability;

public static class AvailabilityServiceCollectionExtensions
{
    public static IHttpClientBuilder AddReservationAvailability(this IServiceCollection services, Uri baseAddress)
    {
        var clientBuilder = services.AddHttpClient<ReservationAvailabilityClient>(client =>
        {
            client.BaseAddress = baseAddress;
            client.Timeout = Timeout.InfiniteTimeSpan;
        });
        clientBuilder.AddResilienceHandler("reservation-availability", pipeline => pipeline
            .AddRetry(new HttpRetryStrategyOptions { MaxRetryAttempts = 1, Delay = TimeSpan.Zero })
            .AddCircuitBreaker(new HttpCircuitBreakerStrategyOptions
            {
                MinimumThroughput = 3,
                SamplingDuration = TimeSpan.FromSeconds(10),
                BreakDuration = TimeSpan.FromSeconds(15)
            })
            .AddTimeout(TimeSpan.FromMilliseconds(75)));
        return clientBuilder;
    }
}
