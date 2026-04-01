using PipelogiqSDK.Agent.Models;

namespace PipelogiqSDK.Agent.Services;

/// <summary>
/// Persists conversation history across pipeline boundaries so the agent
/// can continue a multi-turn dialogue when the user replies in a new message.
///
/// Each session maps to a Telegram chat (or any other channel session).
/// Register your own implementation for durable storage (Redis, Postgres).
/// The built-in <see cref="InMemoryAgentSessionStore"/> is suitable for
/// single-instance deployments and testing.
/// </summary>
public interface IAgentSessionStore
{
    /// <summary>
    /// Loads the saved conversation history for a session.
    /// Returns null when the session has no history or has expired.
    /// </summary>
    Task<List<AgentConversationTurn>?> LoadAsync(string sessionId, CancellationToken ct = default);

    /// <summary>
    /// Saves or overwrites the conversation history for a session.
    /// </summary>
    Task SaveAsync(string sessionId, List<AgentConversationTurn> history, CancellationToken ct = default);

    /// <summary>
    /// Removes the conversation history for a session.
    /// Call when the user explicitly starts a new topic or logs out.
    /// </summary>
    Task ClearAsync(string sessionId, CancellationToken ct = default);
}
