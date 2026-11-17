using System.Threading.Channels;
using System.Text.Json;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Reservation.Data;

namespace Reservation.Services;

public sealed record SeatDelta(Guid SeatPublicId, string Status);
public sealed record SeatChange(Guid TripId, SeatDelta[] Seats, Guid? UserId = null, string? UserEvent = null);

public sealed class SeatHub : Hub
{
    public static string TripGroup(Guid tripId) => $"trip:{tripId:D}";
    public static string UserGroup(Guid userId) => $"user:{userId:D}";

    public override async Task OnConnectedAsync()
    {
        if (Context.User?.Identity?.IsAuthenticated == true &&
            Guid.TryParse(Context.User.FindFirst("sub")?.Value, out var userId))
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, UserGroup(userId));
        }

        await base.OnConnectedAsync();
    }

    public Task JoinTrip(Guid tripId) => Groups.AddToGroupAsync(Context.ConnectionId, TripGroup(tripId));
    public Task LeaveTrip(Guid tripId) => Groups.RemoveFromGroupAsync(Context.ConnectionId, TripGroup(tripId));
}

public sealed class SeatUpdateBuffer
{
    private readonly Channel<SeatChange> changes = Channel.CreateUnbounded<SeatChange>();

    public void Publish(SeatChange change) => changes.Writer.TryWrite(change);

    public bool TryRead(out SeatChange? change) => changes.Reader.TryRead(out change);
}

// Each instance reads the recent committed window independently. A sliding window avoids
// missing a transaction that commits after a later identity value has already been read.
public sealed class SeatUpdateReader(SeatUpdateBuffer buffer)
{
    private readonly Dictionary<long, DateTime> seen = [];

    public async Task Poll(ReservationDbContext db, CancellationToken cancellationToken = default)
    {
        var rows = await db.SeatUpdates.FromSqlRaw(
            "SELECT * FROM reservation.SeatUpdates WHERE CreatedAtUtc >= DATEADD(minute, -5, SYSUTCDATETIME())")
            .AsNoTracking().OrderBy(x => x.Id).ToArrayAsync(cancellationToken);
        var now = DateTime.UtcNow;
        foreach (var id in seen.Where(x => x.Value < now.AddMinutes(-6)).Select(x => x.Key).ToArray())
        {
            seen.Remove(id);
        }

        foreach (var row in rows)
        {
            if (seen.ContainsKey(row.Id))
            {
                continue;
            }

            var change = JsonSerializer.Deserialize<SeatChange>(row.Payload)
                ?? throw new InvalidDataException("Invalid reservation update payload.");
            buffer.Publish(change);
            seen.Add(row.Id, now);
        }
    }
}

public sealed class SeatUpdateReaderWorker(
    IServiceScopeFactory scopes, SeatUpdateReader reader, ILogger<SeatUpdateReaderWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(250));
        var lastCleanup = DateTime.UtcNow;
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<ReservationDbContext>();
                await reader.Poll(db, stoppingToken);
                if (DateTime.UtcNow - lastCleanup >= TimeSpan.FromMinutes(1))
                {
                    await db.Database.ExecuteSqlRawAsync(
                        "DELETE FROM reservation.SeatUpdates WHERE CreatedAtUtc < DATEADD(minute, -10, SYSUTCDATETIME())", stoppingToken);
                    lastCleanup = DateTime.UtcNow;
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Unable to read reservation updates");
            }
        }
    }
}

public sealed class SeatUpdateDispatcher(SeatUpdateBuffer buffer, IHubContext<SeatHub> hub, ILogger<SeatUpdateDispatcher> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(250));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            var batch = new List<SeatChange>();
            while (buffer.TryRead(out var change))
            {
                batch.Add(change!);
            }

            try
            {
                foreach (var group in batch.GroupBy(x => x.TripId))
                {
                    var seats = group.SelectMany(x => x.Seats).ToArray();
                    if (seats.Length > 0)
                    {
                        await hub.Clients.Group(SeatHub.TripGroup(group.Key)).SendAsync("SeatDeltas", seats, stoppingToken);
                    }
                }

                foreach (var change in batch.Where(x => x.UserId.HasValue && x.UserEvent is not null))
                {
                    await hub.Clients.Group(SeatHub.UserGroup(change.UserId!.Value))
                        .SendAsync("ReservationChanged", change.UserEvent, stoppingToken);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Unable to broadcast reservation updates");
            }
        }
    }
}
