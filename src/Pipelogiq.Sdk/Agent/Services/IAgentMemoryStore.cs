using PipelogiqSDK.Agent.Models;

namespace PipelogiqSDK.Agent.Services;

/// <summary>
/// Persistent memory store for the AI agent.
/// Register your own implementation (PostgreSQL, Redis, etc.) or use the built-in
/// <see cref="InMemoryAgentMemoryStore"/> for testing.
/// </summary>
public interface IAgentMemoryStore
{
    /// <summary>
    /// Recalls the most relevant memory entries for the given query within the session.
    /// </summary>
    /// <param name="sessionId">Session or user identifier.</param>
    /// <param name="query">The current user message used to score relevance.</param>
    /// <param name="maxEntries">Maximum number of entries to return. Default is 5.</param>
    /// <param name="ct">Cancellation token.</param>
    Task<IReadOnlyList<AgentMemoryEntry>> RecallAsync(
        string sessionId,
        string query,
        int maxEntries = 5,
        CancellationToken ct = default);

    /// <summary>
    /// Stores a new memory entry for the session.
    /// </summary>
    Task StoreAsync(AgentMemoryEntry entry, CancellationToken ct = default);

    /// <summary>
    /// Removes all memory entries for a session.
    /// Call when a session ends to keep storage tidy.
    /// </summary>
    Task ClearSessionAsync(string sessionId, CancellationToken ct = default);
}
