namespace Schedule.Data;

public sealed class ScheduleInitializer(IServiceProvider services) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await using var scope = services.CreateAsyncScope();
        await ScheduleSeed.Initialize(scope.ServiceProvider.GetRequiredService<ScheduleDbContext>(), cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
