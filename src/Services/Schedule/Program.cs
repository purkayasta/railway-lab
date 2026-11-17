using Microsoft.EntityFrameworkCore;
using Platform;
using Platform.Auth;
using Schedule;
using Schedule.Data;
using Schedule.Availability;
using Schedule.Services;

var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults("schedule");
builder.AddServiceAuth();
var connectionString = builder.Configuration.GetConnectionString("Default")
    ?? throw new InvalidOperationException("ConnectionStrings:Default is required.");
builder.Services.AddDbContext<ScheduleDbContext>(o => o.UseSqlServer(connectionString,
    sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", ScheduleDbContext.Schema)));
builder.Services.AddHostedService<ScheduleInitializer>();
builder.Services.AddReservationAvailability(new Uri(
    builder.Configuration["Reservation:BaseAddress"] ?? "http://reservation:8080/"));
builder.Services.AddScoped<StationsService>();
builder.Services.AddScoped<SearchService>();
builder.Services.AddScoped<TripService>();
builder.Services.AddScoped<LayoutService>();
var app = builder.Build();

app.UseServiceDefaults();
app.UseServiceAuth();
app.MapScheduleEndpoints();

await app.RunAsync();
