using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Scalar.AspNetCore;
using Serilog;

namespace Platform;

/// <summary>Technical defaults shared by every service. No domain code here.</summary>
public static class ServiceDefaults
{
    /// <param name="serviceName">Name shown in logs and traces, for example "schedule".</param>
    /// <param name="connectionStringName">Connection string used by the readiness check; null skips the DB check.</param>
    public static WebApplicationBuilder AddServiceDefaults(
        this WebApplicationBuilder builder, string serviceName, string? connectionStringName = "Default")
    {
        builder.Host.UseSerilog((ctx, services, cfg) => cfg
            .ReadFrom.Configuration(ctx.Configuration)
            .Enrich.FromLogContext()
            .Enrich.WithProperty("Service", serviceName)
            .WriteTo.Console(outputTemplate:
                "[{Timestamp:HH:mm:ss} {Level:u3}] {Service} trace={TraceId} {Message:lj}{NewLine}{Exception}"));

        builder.Services.AddProblemDetails(o => o.CustomizeProblemDetails = c =>
            c.ProblemDetails.Extensions["traceId"] = System.Diagnostics.Activity.Current?.TraceId.ToString()
                                                     ?? c.HttpContext.TraceIdentifier);
        builder.Services.AddExceptionHandler<ProblemDetailsExceptionHandler>();
        builder.Services.AddOpenApi();

        var health = builder.Services.AddHealthChecks();
        var connectionString = connectionStringName is null ? null : builder.Configuration.GetConnectionString(connectionStringName);
        if (connectionString is not null)
            health.AddCheck("database", new SqlServerHealthCheck(connectionString), tags: ["ready"]);

        var otel = builder.Services.AddOpenTelemetry()
            .ConfigureResource(r => r.AddService(serviceName))
            .WithTracing(t => t
                .AddAspNetCoreInstrumentation(o => o.Filter = ctx => !ctx.Request.Path.StartsWithSegments("/health"))
                .AddHttpClientInstrumentation()
                .AddSqlClientInstrumentation())
            .WithMetrics(m => m
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()
                .AddRuntimeInstrumentation());

        // Export only when a collector is configured (the observability profile sets this variable).
        if (!string.IsNullOrWhiteSpace(builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]))
            otel.UseOtlpExporter();

        return builder;
    }

    public static WebApplication UseServiceDefaults(this WebApplication app)
    {
        app.UseExceptionHandler();
        app.UseStatusCodePages();
        app.UseSerilogRequestLogging();

        // Liveness: process is up. Readiness: dependencies (database) reachable.
        app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false })
            .AllowAnonymous();
        app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = c => c.Tags.Contains("ready") })
            .AllowAnonymous();

        if (app.Environment.IsDevelopment())
        {
            app.MapOpenApi();
            app.MapScalarApiReference();
        }
        return app;
    }
}
