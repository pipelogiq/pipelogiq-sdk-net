using System.Text.Json;
using PipelogiqSDK.Agent.Models;
using PipelogiqSDK.Agent.Services;
using StackExchange.Redis;

namespace PipelogiqSDK.Redis;

/// <summary>
/// Redis-backed agent memory store.
/// Stores memory entries as a Redis List under key "pipelogiq:memory:{sessionId}".
/// Recall uses keyword matching (same algorithm as the in-memory store).
/// For semantic/embedding-based recall, implement <see cref="IAgentMemoryStore"/> with pgvector or similar.
/// </summary>
public sealed class RedisAgentMemoryStore : IAgentMemoryStore
{
    private readonly IDatabase _db;
    private readonly TimeSpan? _ttl;
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    /// <param name="redis">Connected Redis multiplexer.</param>
    /// <param name="entryTtl">Optional TTL applied to each memory list key. Null means no expiry.</param>
    public RedisAgentMemoryStore(IConnectionMultiplexer redis, TimeSpan? entryTtl = null)
    {
        _db = redis.GetDatabase();
        _ttl = entryTtl;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<AgentMemoryEntry>> RecallAsync(
        string sessionId,
        string query,
        int maxEntries = 5,
        CancellationToken ct = default)
    {
        var raw = await _db.ListRangeAsync(BuildKey(sessionId));
        if (raw.Length == 0) return Array.Empty<AgentMemoryEntry>();

        var entries = raw
            .Select(r => JsonSerializer.Deserialize<AgentMemoryEntry>(r!, JsonOptions))
            .Where(e => e != null)
            .Select(e => e!)
            .ToList();

        var queryWords = query.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        return entries
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
    }

    /// <inheritdoc />
    public async Task StoreAsync(AgentMemoryEntry entry, CancellationToken ct = default)
    {
        var key = BuildKey(entry.SessionId);
        var json = JsonSerializer.Serialize(entry, JsonOptions);
        await _db.ListRightPushAsync(key, json);

        if (_ttl.HasValue)
            await _db.KeyExpireAsync(key, _ttl.Value);
    }

    /// <inheritdoc />
    public async Task ClearSessionAsync(string sessionId, CancellationToken ct = default)
    {
        await _db.KeyDeleteAsync(BuildKey(sessionId));
    }

    private static string BuildKey(string sessionId) => $"pipelogiq:memory:{sessionId}";
}
