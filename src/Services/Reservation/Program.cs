using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Platform;
using Platform.Auth;
using Reservation;
using Reservation.Data;
using Reservation.Services;

var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults("reservation");
builder.AddServiceAuth();
builder.Services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
{
    options.Events ??= new JwtBearerEvents();
    options.Events.OnMessageReceived = context =>
    {
        if (context.Request.Path.StartsWithSegments("/hubs/seats") &&
            context.Request.Query.TryGetValue("access_token", out var token))
        {
            context.Token = token;
        }

        return Task.CompletedTask;
    };
});

var connection = builder.Configuration.GetConnectionString("Default")
    ?? throw new InvalidOperationException("ConnectionStrings:Default is required.");

builder.Services.AddDbContext<ReservationDbContext>(options => options.UseSqlServer(
    connection, sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", "reservation")));
builder.Services.AddHostedService<ReservationInitializer>();
builder.Services.AddSignalR();
builder.Services.AddSingleton<SeatUpdateBuffer>();
builder.Services.AddSingleton<SeatUpdateReader>();
builder.Services.AddHostedService<SeatUpdateReaderWorker>();
builder.Services.AddScoped<ExpiredHoldSweeper>();
builder.Services.AddHostedService<SeatUpdateDispatcher>();
builder.Services.AddHostedService<ExpiredHoldSweepWorker>();
builder.Services.AddHttpClient<SeatInventoryService>(client =>
    client.BaseAddress = new Uri(builder.Configuration["Schedule:BaseAddress"] ?? "http://schedule:8080/"));
builder.Services.AddHttpClient<ScheduleFareClient>(client =>
    client.BaseAddress = new Uri(builder.Configuration["Schedule:BaseAddress"] ?? "http://schedule:8080/"));
builder.Services.AddScoped<HoldService>();
builder.Services.AddScoped<BookingService>();
builder.Services.AddScoped<PaymentApprovalService>();
builder.Services.AddScoped<SimulatedPaymentService>();
builder.Services.AddScoped<PolicyService>();

var app = builder.Build();

app.UseServiceDefaults();
app.UseServiceAuth();
app.MapReservationEndpoints();
app.MapHub<SeatHub>("/hubs/seats").AllowAnonymous();

await app.RunAsync();
