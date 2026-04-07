using Microsoft.Extensions.DependencyInjection;
using PipelogiqSDK.Agent.Extensions;
using PipelogiqSDK.Agent.Services;

namespace PipelogiqSDK.Postgres;

/// <summary>
/// Extension methods to register PostgreSQL-backed agent stores.
/// Call after <c>AddPipelogiqAgent()</c>.
/// </summary>
public static class PostgresAgentStoreExtensions
{
    /// <summary>
    /// Replaces the default in-memory session store with a PostgreSQL-backed implementation.
    /// Call <c>PostgresAgentSessionStore.EnsureSchemaAsync()</c> at startup to create the table.
    /// </summary>
    /// <param name="builder">Agent builder returned by AddPipelogiqAgent().</param>
    /// <param name="services">Service collection.</param>
    /// <param name="connectionString">Npgsql connection string.</param>
    /// <param name="sessionTtl">How long a session survives without activity. Default: 2 hours.</param>
    public static AgentBuilder UsePostgresSessionStore(
        this AgentBuilder builder,
        IServiceCollection services,
        string connectionString,
        TimeSpan? sessionTtl = null)
    {
        var store = new PostgresAgentSessionStore(connectionString, sessionTtl);
        services.AddSingleton<IAgentSessionStore>(store);
        services.AddSingleton(store); // expose concrete type for EnsureSchemaAsync() calls
        return builder;
    }

    /// <summary>
    /// Replaces the default in-memory memory store with a PostgreSQL-backed implementation.
    /// Call <c>PostgresAgentMemoryStore.EnsureSchemaAsync()</c> at startup to create the table.
    /// </summary>
    public static AgentBuilder UsePostgresMemoryStore(
        this AgentBuilder builder,
        IServiceCollection services,
        string connectionString)
    {
        var store = new PostgresAgentMemoryStore(connectionString);
        services.AddSingleton<IAgentMemoryStore>(store);
        services.AddSingleton(store); // expose concrete type for EnsureSchemaAsync() calls
        return builder;
    }

    /// <summary>
    /// Registers both PostgreSQL session store and memory store in one call.
    /// </summary>
    public static AgentBuilder UsePostgresStores(
        this AgentBuilder builder,
        IServiceCollection services,
        string connectionString,
        TimeSpan? sessionTtl = null)
    {
        builder.UsePostgresSessionStore(services, connectionString, sessionTtl);
        builder.UsePostgresMemoryStore(services, connectionString);
        return builder;
    }
}
