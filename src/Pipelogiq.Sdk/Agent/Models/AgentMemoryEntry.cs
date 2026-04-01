namespace PipelogiqSDK.Agent.Models;

/// <summary>
/// A single entry stored in the agent's long-term memory store.
/// </summary>
public class AgentMemoryEntry
{
    /// <summary>Unique identifier for this memory entry.</summary>
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    /// <summary>Session or user identifier that this memory belongs to.</summary>
    public string SessionId { get; set; } = string.Empty;

    /// <summary>Short label describing the type of memory (e.g. "preference", "fact", "summary").</summary>
    public string Category { get; set; } = "fact";

    /// <summary>The content to recall.</summary>
    public string Content { get; set; } = string.Empty;

    /// <summary>UTC timestamp when this entry was stored.</summary>
    public DateTimeOffset StoredAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>Optional relevance score computed during recall (higher = more relevant).</summary>
    public float RelevanceScore { get; set; }
}
