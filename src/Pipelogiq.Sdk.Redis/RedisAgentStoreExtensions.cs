using Microsoft.Extensions.DependencyInjection;
using PipelogiqSDK.Agent.Extensions;
using PipelogiqSDK.Agent.Services;
using StackExchange.Redis;

namespace PipelogiqSDK.Redis;

/// <summary>
/// Extension methods to register Redis-backed agent stores.
/// Call after <c>AddPipelogiqAgent()</c>.
/// </summary>
public static class RedisAgentStoreExtensions
{
    /// <summary>
    /// Replaces the default in-memory session store with a Redis-backed implementation.
    /// Safe for multi-instance deployments.
    /// </summary>
    /// <param name="builder">Agent builder returned by AddPipelogiqAgent().</param>
    /// <param name="services">Service collection (same one used by AddPipelogiqAgent).</param>
    /// <param name="connectionString">Redis connection string, e.g. "localhost:6379".</param>
    /// <param name="sessionTtl">How long a session survives without activity. Default: 2 hours.</param>
    /// <returns>The agent builder for further chaining.</returns>
    public static AgentBuilder UseRedisSessionStore(
        this AgentBuilder builder,
        IServiceCollection services,
        string connectionString,
        TimeSpan? sessionTtl = null)
    {
        var multiplexer = ConnectionMultiplexer.Connect(connectionString);
        services.AddSingleton<IConnectionMultiplexer>(multiplexer);
        services.AddSingleton<IAgentSessionStore>(
            sp => new RedisAgentSessionStore(sp.GetRequiredService<IConnectionMultiplexer>(), sessionTtl));
        return builder;
    }

    /// <summary>
    /// Replaces the default in-memory session store with a Redis-backed implementation,
    /// using an already-registered <see cref="IConnectionMultiplexer"/>.
    /// </summary>
    public static AgentBuilder UseRedisSessionStore(
        this AgentBuilder builder,
        IServiceCollection services,
        TimeSpan? sessionTtl = null)
    {
        services.AddSingleton<IAgentSessionStore>(
            sp => new RedisAgentSessionStore(sp.GetRequiredService<IConnectionMultiplexer>(), sessionTtl));
        return builder;
    }

    /// <summary>
    /// Replaces the default in-memory memory store with a Redis-backed implementation.
    /// </summary>
    /// <param name="builder">Agent builder.</param>
    /// <param name="services">Service collection.</param>
    /// <param name="connectionString">Redis connection string.</param>
    /// <param name="entryTtl">Optional TTL for memory entries. Null means no expiry.</param>
    public static AgentBuilder UseRedisMemoryStore(
        this AgentBuilder builder,
        IServiceCollection services,
        string connectionString,
        TimeSpan? entryTtl = null)
    {
        if (!services.Any(s => s.ServiceType == typeof(IConnectionMultiplexer)))
        {
            var multiplexer = ConnectionMultiplexer.Connect(connectionString);
            services.AddSingleton<IConnectionMultiplexer>(multiplexer);
        }

        services.AddSingleton<IAgentMemoryStore>(
            sp => new RedisAgentMemoryStore(sp.GetRequiredService<IConnectionMultiplexer>(), entryTtl));
        return builder;
    }

    /// <summary>
    /// Registers both Redis session store and Redis memory store in one call.
    /// </summary>
    public static AgentBuilder UseRedisStores(
        this AgentBuilder builder,
        IServiceCollection services,
        string connectionString,
        TimeSpan? sessionTtl = null,
        TimeSpan? memoryEntryTtl = null)
    {
        var multiplexer = ConnectionMultiplexer.Connect(connectionString);
        services.AddSingleton<IConnectionMultiplexer>(multiplexer);
        services.AddSingleton<IAgentSessionStore>(
            sp => new RedisAgentSessionStore(sp.GetRequiredService<IConnectionMultiplexer>(), sessionTtl));
        services.AddSingleton<IAgentMemoryStore>(
            sp => new RedisAgentMemoryStore(sp.GetRequiredService<IConnectionMultiplexer>(), memoryEntryTtl));
        return builder;
    }
}
