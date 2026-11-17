using System.Reflection;
using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Wolverine;
using Wolverine.EntityFrameworkCore;
using Wolverine.SqlServer;

namespace Platform.Messaging;

/// <summary>
/// Wolverine wiring shared by every service: in-process bus (<see cref="IMessageBus"/>), handler discovery,
/// one EF Core transaction per handler (SaveChanges + commit) and an optional durable SQL Server outbox/inbox
/// stored in the service's own schema. Technical only, no domain code.
/// </summary>
public static class MessagingDefaults
{
    /// <param name="handlersAssembly">Assembly scanned for handler classes (the service's assembly).</param>
    /// <param name="schema">The service's schema; Wolverine's outbox/inbox tables live there (EF transactions require durable persistence).</param>
    public static WebApplicationBuilder AddServiceMessaging(
        this WebApplicationBuilder builder,
        Assembly handlersAssembly,
        string schema,
        string connectionStringName = "Default")
    {
        var connectionString = builder.Configuration.GetConnectionString(connectionStringName)
            ?? throw new InvalidOperationException($"Connection string '{connectionStringName}' is required for the outbox.");
        builder.Host.UseWolverine(opts =>
        {
            opts.Discovery.IncludeAssembly(handlersAssembly);
            opts.UseEntityFrameworkCoreTransactions();
            // Handlers that use a Wolverine-integrated DbContext get SaveChanges + commit automatically.
            opts.Policies.AutoApplyTransactions();

            opts.PersistMessagesWithSqlServer(connectionString, schema);
            // Messages published inside a handler are stored in the same transaction, then sent.
            opts.Policies.UseDurableLocalQueues();
        });

        return builder;
    }

    /// <summary>Registers a service DbContext whose SaveChanges takes part in the Wolverine transaction and outbox.</summary>
    public static IServiceCollection AddServiceDbContext<TContext>(
        this IServiceCollection services, string connectionString, string schema)
        where TContext : DbContext
    {
        services.AddDbContextWithWolverineIntegration<TContext>(
            o => o.UseSqlServer(connectionString, sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", schema)),
            schema);
        return services;
    }
}
