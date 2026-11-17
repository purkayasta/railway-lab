using System.Net;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Schedule.Availability;
using Schedule.Services;

namespace Schedule.Tests;

public class AvailabilityClientTests
{
    private static readonly TripResult Trip = new(Guid.NewGuid(), "1", "Train", DateTime.UtcNow, DateTime.UtcNow, "economy", 100, null, true);

    [Fact]
    public async Task Uses_count_for_the_requested_class()
    {
        var client = Client(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("""
            [{"classCode":"business","total":40,"available":5,"held":5,"sold":30},
             {"classCode":"economy","total":40,"available":23,"held":7,"sold":10}]
            """, Encoding.UTF8, "application/json") });
        var result = Assert.Single(await client.EnrichAsync([Trip], default));
        Assert.Equal(23, result.AvailableSeats);
        Assert.False(result.AvailabilityUnknown);
    }

    [Theory]
    [InlineData(HttpStatusCode.ServiceUnavailable, "")]
    [InlineData(HttpStatusCode.OK, "invalid json")]
    [InlineData(HttpStatusCode.OK, "[]")]
    public async Task Failure_or_missing_class_preserves_unknown(HttpStatusCode status, string content)
    {
        var client = Client(new HttpResponseMessage(status) { Content = new StringContent(content, Encoding.UTF8, "application/json") });
        Assert.Equal(Trip, Assert.Single(await client.EnrichAsync([Trip], default)));
    }

    [Theory]
    [InlineData("[{\"classCode\":\"economy\",\"available\":3}]")]
    [InlineData("[{\"classCode\":\"economy\",\"total\":4,\"available\":3,\"held\":1,\"sold\":1}]")]
    [InlineData("[{\"classCode\":\"economy\",\"total\":4,\"available\":4,\"held\":-1,\"sold\":1}]")]
    [InlineData("[{\"classCode\":\"economy\",\"total\":4,\"available\":4,\"held\":0,\"sold\":0},{\"classCode\":\"ECONOMY\",\"total\":4,\"available\":4,\"held\":0,\"sold\":0}]")]
    public async Task Invalid_counts_or_duplicate_class_preserve_unknown(string content)
    {
        var client = Client(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(content, Encoding.UTF8, "application/json") });
        Assert.Equal(Trip, Assert.Single(await client.EnrichAsync([Trip], default)));
    }

    private static ReservationAvailabilityClient Client(HttpResponseMessage response) =>
        new(new HttpClient(new Handler(response)) { BaseAddress = new Uri("http://reservation/") }, NullLogger<ReservationAvailabilityClient>.Instance);

    private sealed class Handler(HttpResponseMessage response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(response);
    }
}

public class AvailabilityResilienceTests
{
    [Fact]
    public async Task Retries_transient_failures_and_opens_circuit()
    {
        var handler = new FailingHandler();
        var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
        Microsoft.Extensions.DependencyInjection.LoggingServiceCollectionExtensions.AddLogging(services);
        Microsoft.Extensions.DependencyInjection.HttpClientBuilderExtensions.ConfigurePrimaryHttpMessageHandler(
            AvailabilityServiceCollectionExtensions.AddReservationAvailability(services, new Uri("http://reservation/")), () => handler);
        using var provider = services.BuildServiceProvider();
        var client = provider.GetRequiredService<ReservationAvailabilityClient>();
        var trip = new TripResult(Guid.NewGuid(), "1", "Train", DateTime.UtcNow, DateTime.UtcNow, "economy", 100, null, true);
        for (var i = 0; i < 4; i++)
            Assert.True(Assert.Single(await client.EnrichAsync([trip], default)).AvailabilityUnknown);
        Assert.Equal(3, handler.Attempts);
    }

    [Fact]
    public async Task Slow_reservation_returns_unknown_within_the_deadline()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddReservationAvailability(new Uri("http://reservation/"))
            .ConfigurePrimaryHttpMessageHandler(() => new SlowHandler());
        using var provider = services.BuildServiceProvider();
        var client = provider.GetRequiredService<ReservationAvailabilityClient>();
        var trip = new TripResult(Guid.NewGuid(), "1", "Train", DateTime.UtcNow, DateTime.UtcNow, "economy", 100, null, true);
        var watch = System.Diagnostics.Stopwatch.StartNew();
        Assert.True(Assert.Single(await client.EnrichAsync([trip], default)).AvailabilityUnknown);
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(2));
    }

    private sealed class SlowHandler : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            await Task.Delay(TimeSpan.FromSeconds(5), ct);
            return new HttpResponseMessage(HttpStatusCode.OK);
        }
    }

    private sealed class FailingHandler : HttpMessageHandler
    {
        public int Attempts { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Attempts++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        }
    }
}
