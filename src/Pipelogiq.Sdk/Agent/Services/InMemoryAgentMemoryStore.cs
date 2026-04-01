using System.Collections.Concurrent;
using PipelogiqSDK.Agent.Models;

namespace PipelogiqSDK.Agent.Services;

/// <summary>
/// Thread-safe in-process memory store. Suitable for single-instance workers and testing.
/// Replace with a database-backed implementation for multi-instance or persistent memory.
/// </summary>
public sealed class InMemoryAgentMemoryStore : IAgentMemoryStore
{
    private readonly ConcurrentDictionary<string, List<AgentMemoryEntry>> _store = new();
    private readonly object _lock = new();

    /// <inheritdoc />
    public Task<IReadOnlyList<AgentMemoryEntry>> RecallAsync(
        string sessionId,
        string query,
        int maxEntries = 5,
        CancellationToken ct = default)
    {
        if (!_store.TryGetValue(sessionId, out var entries))
            return Task.FromResult<IReadOnlyList<AgentMemoryEntry>>(Array.Empty<AgentMemoryEntry>());

        List<AgentMemoryEntry> snapshot;
        lock (_lock)
            snapshot = [.. entries];

        // Simple keyword relevance: count how many query words appear in the content
        var queryWords = query.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        var scored = snapshot
            .Select(e =>
            {
                var hits = queryWords.Count(w => e.Content.Contains(w, StringComparison.OrdinalIgnoreCase));
                e.RelevanceScore = hits > 0 ? (float)hits / queryWords.Length : 0f;
                return e;
            })
            .OrderByDescending(e => e.RelevanceScore)
            .ThenByDescending(e => e.StoredAt)
            .Take(maxEntries)
            .ToList();

        return Task.FromResult<IReadOnlyList<AgentMemoryEntry>>(scored);
    }

    /// <inheritdoc />
    public Task StoreAsync(AgentMemoryEntry entry, CancellationToken ct = default)
    {
        var list = _store.GetOrAdd(entry.SessionId, _ => []);
        lock (_lock)
            list.Add(entry);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task ClearSessionAsync(string sessionId, CancellationToken ct = default)
    {
        _store.TryRemove(sessionId, out _);
        return Task.CompletedTask;
    }
}
