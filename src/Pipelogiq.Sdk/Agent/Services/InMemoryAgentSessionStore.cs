using System.Collections.Concurrent;
using PipelogiqSDK.Agent.Models;

namespace PipelogiqSDK.Agent.Services;

/// <summary>
/// In-process session store with automatic TTL expiry.
/// Suitable for single-instance deployments and testing.
/// For multi-instance deployments replace with Redis / Postgres.
/// </summary>
public sealed class InMemoryAgentSessionStore : IAgentSessionStore
{
    /// <summary>How long a session survives without activity. Default: 2 hours.</summary>
    public TimeSpan SessionTtl { get; init; } = TimeSpan.FromHours(2);

    private readonly ConcurrentDictionary<string, Entry> _sessions = new(StringComparer.Ordinal);

    /// <inheritdoc />
    public Task<List<AgentConversationTurn>?> LoadAsync(string sessionId, CancellationToken ct = default)
    {
        if (_sessions.TryGetValue(sessionId, out var entry))
        {
            if (DateTimeOffset.UtcNow - entry.SavedAt < SessionTtl)
                return Task.FromResult<List<AgentConversationTurn>?>(entry.History);

            _sessions.TryRemove(sessionId, out _);
        }

        return Task.FromResult<List<AgentConversationTurn>?>(null);
    }

    /// <inheritdoc />
    public Task SaveAsync(string sessionId, List<AgentConversationTurn> history, CancellationToken ct = default)
    {
        _sessions[sessionId] = new Entry(history, DateTimeOffset.UtcNow);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task ClearAsync(string sessionId, CancellationToken ct = default)
    {
        _sessions.TryRemove(sessionId, out _);
        return Task.CompletedTask;
    }

    private sealed record Entry(List<AgentConversationTurn> History, DateTimeOffset SavedAt);
}
