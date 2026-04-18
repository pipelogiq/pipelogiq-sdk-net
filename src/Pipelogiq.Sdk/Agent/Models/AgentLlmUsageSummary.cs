namespace PipelogiqSDK.Agent.Models;

/// <summary>
/// Aggregate LLM usage for the whole agent session/pipeline.
/// </summary>
public class AgentLlmUsageSummary
{
    /// <summary>Total number of LLM API calls made so far.</summary>
    public int TotalCalls { get; set; }

    /// <summary>Total input tokens consumed across all calls.</summary>
    public long InputTokens { get; set; }

    /// <summary>Total output tokens generated across all calls.</summary>
    public long OutputTokens { get; set; }

    /// <summary>Total cache read tokens across all calls.</summary>
    public long CacheReadTokens { get; set; }

    /// <summary>Total cache creation tokens across all calls.</summary>
    public long CacheCreationTokens { get; set; }

    /// <summary>Total estimated cost in USD across all calls.</summary>
    public decimal EstimatedCostUsd { get; set; }

    /// <summary>Breakdown by provider/model combination.</summary>
    public List<AgentLlmModelUsageSummary> Models { get; set; } = [];
}

/// <summary>
/// Aggregate LLM usage for a single provider/model combination.
/// </summary>
public class AgentLlmModelUsageSummary
{
    /// <summary>Provider that served the request.</summary>
    public string? Provider { get; set; }

    /// <summary>Concrete model used.</summary>
    public string? Model { get; set; }

    /// <summary>Total number of calls made with this provider/model.</summary>
    public int Calls { get; set; }

    /// <summary>Total input tokens consumed.</summary>
    public long InputTokens { get; set; }

    /// <summary>Total output tokens generated.</summary>
    public long OutputTokens { get; set; }

    /// <summary>Total cache read tokens consumed.</summary>
    public long CacheReadTokens { get; set; }

    /// <summary>Total cache creation tokens written.</summary>
    public long CacheCreationTokens { get; set; }

    /// <summary>Total estimated cost in USD.</summary>
    public decimal EstimatedCostUsd { get; set; }
}
