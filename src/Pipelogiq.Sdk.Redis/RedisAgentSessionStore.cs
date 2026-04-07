using System.Text.Json;
using PipelogiqSDK.Agent.Models;
using PipelogiqSDK.Agent.Services;
using StackExchange.Redis;

namespace PipelogiqSDK.Redis;

/// <summary>
/// Redis-backed conversation session store.
/// Safe for multi-instance deployments — all workers share the same Redis instance.
/// Each session is stored as a JSON string under key "pipelogiq:session:{sessionId}"
/// with automatic TTL expiry.
/// </summary>
public sealed class RedisAgentSessionStore : IAgentSessionStore
{
    private readonly IDatabase _db;
    private readonly TimeSpan _ttl;
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    /// <param name="redis">Connected Redis multiplexer.</param>
    /// <param name="sessionTtl">How long a session survives without activity. Default: 2 hours.</param>
    public RedisAgentSessionStore(IConnectionMultiplexer redis, TimeSpan? sessionTtl = null)
    {
        _db = redis.GetDatabase();
        _ttl = sessionTtl ?? TimeSpan.FromHours(2);
    }

    /// <inheritdoc />
    public async Task<List<AgentConversationTurn>?> LoadAsync(string sessionId, CancellationToken ct = default)
    {
        var json = await _db.StringGetAsync(BuildKey(sessionId));
        if (json.IsNullOrEmpty) return null;

        return JsonSerializer.Deserialize<List<AgentConversationTurn>>(json!, JsonOptions);
    }

    /// <inheritdoc />
    public async Task SaveAsync(string sessionId, List<AgentConversationTurn> history, CancellationToken ct = default)
    {
        var json = JsonSerializer.Serialize(history, JsonOptions);
        await _db.StringSetAsync(BuildKey(sessionId), json, _ttl);
    }

    /// <inheritdoc />
    public async Task ClearAsync(string sessionId, CancellationToken ct = default)
    {
        await _db.KeyDeleteAsync(BuildKey(sessionId));
    }

    private static string BuildKey(string sessionId) => $"pipelogiq:session:{sessionId}";
}
