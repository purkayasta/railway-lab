using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Reservation.Services;

namespace Schedule.Tests;

public class SeatHubTests
{
    [Fact]
    public async Task Anonymous_clients_receive_trip_updates_and_cannot_join_user_groups()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddSignalR();
        builder.Services.AddSingleton<SeatUpdateBuffer>();
        builder.Services.AddHostedService<SeatUpdateDispatcher>();
        await using var app = builder.Build();
        app.Use((context, next) =>
        {
            if (context.Request.Headers.TryGetValue("X-Test-User", out var user))
            {
                context.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", user.ToString())], "test"));
            }

            return next(context);
        });
        app.MapHub<SeatHub>("/hubs/seats");
        await app.StartAsync();

        var client = app.GetTestServer().CreateWebSocketClient();
        using var first = await client.ConnectAsync(new Uri("ws://localhost/hubs/seats"), CancellationToken.None);
        using var second = await client.ConnectAsync(new Uri("ws://localhost/hubs/seats"), CancellationToken.None);
        var ownerId = Guid.NewGuid();
        var ownerClient = app.GetTestServer().CreateWebSocketClient();
        ownerClient.ConfigureRequest = request => request.Headers["X-Test-User"] = ownerId.ToString();
        using var owner = await ownerClient.ConnectAsync(new Uri("ws://localhost/hubs/seats"), CancellationToken.None);
        var trip = Guid.NewGuid();
        var seat = Guid.NewGuid();
        foreach (var socket in new[] { first, second })
        {
            await Send(socket, "{\"protocol\":\"json\",\"version\":1}");
            Assert.Equal("{}", await Receive(socket));
            await Send(socket, JsonSerializer.Serialize(new { type = 1, invocationId = "join", target = "JoinTrip", arguments = new[] { trip } }));
            using var joined = JsonDocument.Parse(await Receive(socket));
            Assert.Equal(3, joined.RootElement.GetProperty("type").GetInt32());
            Assert.False(joined.RootElement.TryGetProperty("error", out _));
        }

        await Send(owner, "{\"protocol\":\"json\",\"version\":1}");
        Assert.Equal("{}", await Receive(owner));

        await Send(first, JsonSerializer.Serialize(new { type = 1, invocationId = "private", target = "JoinUser", arguments = new[] { Guid.NewGuid() } }));
        using (var denied = JsonDocument.Parse(await Receive(first)))
        {
            Assert.True(denied.RootElement.TryGetProperty("error", out _));
        }

        var buffer = app.Services.GetRequiredService<SeatUpdateBuffer>();
        buffer.Publish(new SeatChange(trip, [], ownerId, "booking-changed"));
        buffer.Publish(new SeatChange(trip, [new SeatDelta(seat, "held")]));
        using (var privateUpdate = JsonDocument.Parse(await Receive(owner)))
        {
            Assert.Equal("ReservationChanged", privateUpdate.RootElement.GetProperty("target").GetString());
        }
        foreach (var socket in new[] { first, second })
        {
            using var message = JsonDocument.Parse(await Receive(socket));
            Assert.Equal("SeatDeltas", message.RootElement.GetProperty("target").GetString());
            Assert.Equal(seat, message.RootElement.GetProperty("arguments")[0][0].GetProperty("seatPublicId").GetGuid());
        }
    }

    private static async Task Send(WebSocket socket, string message)
    {
        var bytes = Encoding.UTF8.GetBytes(message + '\u001e');
        await socket.SendAsync(bytes, WebSocketMessageType.Text, true, CancellationToken.None);
    }

    private static async Task<string> Receive(WebSocket socket)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var bytes = new byte[8192];
        var result = await socket.ReceiveAsync(bytes, timeout.Token);
        return Encoding.UTF8.GetString(bytes, 0, result.Count).TrimEnd('\u001e');
    }
}
