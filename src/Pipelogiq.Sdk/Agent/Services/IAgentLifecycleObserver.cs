using PipelogiqSDK.Agent.Models;

namespace PipelogiqSDK.Agent.Services;

/// <summary>
/// Receives lifecycle events from the AI agent pipeline.
/// Register an implementation to build audit logs, dashboards, Slack alerts, or custom metrics.
///
/// All methods have default no-op implementations so you only override what you need.
///
/// Example — audit log observer:
/// <code>
/// public class AuditObserver : IAgentLifecycleObserver
/// {
///     public Task OnToolExecutedAsync(AgentToolLifecycleEvent e, CancellationToken ct)
///     {
///         _logger.LogInformation("Tool {Tool} {Status} in session {Session}",
///             e.ToolName, e.IsSuccess ? "succeeded" : "failed", e.SessionId);
///         return Task.CompletedTask;
///     }
/// }
///
/// // Registration:
/// services.AddPipelogiqAgent(...).AddLifecycleObserver&lt;AuditObserver&gt;(services);
/// </code>
/// </summary>
public interface IAgentLifecycleObserver
{
    /// <summary>
    /// Called after every tool execution (native or HTTP), regardless of success or failure.
    /// </summary>
    Task OnToolExecutedAsync(AgentToolLifecycleEvent e, CancellationToken ct = default)
        => Task.CompletedTask;

    /// <summary>
    /// Called when the agent requires human approval before executing a mutating tool.
    /// Use this to trigger external notifications (email, Slack, PagerDuty).
    /// </summary>
    Task OnApprovalRequestedAsync(AgentApprovalLifecycleEvent e, CancellationToken ct = default)
        => Task.CompletedTask;

    /// <summary>
    /// Called when the agent has finished a full conversation turn and sent a response.
    /// This is the right place to persist analytics, billing counters, or completion logs.
    /// </summary>
    Task OnSessionCompletedAsync(AgentSessionLifecycleEvent e, CancellationToken ct = default)
        => Task.CompletedTask;

    /// <summary>
    /// Called when the token budget for a session is near or at its limit.
    /// Use this to alert the team or automatically upgrade the session budget.
    /// </summary>
    Task OnBudgetWarningAsync(AgentBudgetLifecycleEvent e, CancellationToken ct = default)
        => Task.CompletedTask;
}

/// <summary>Event data for a tool execution.</summary>
public sealed class AgentToolLifecycleEvent
{
    public required string ToolName { get; init; }
    public required bool IsSuccess { get; init; }
    public bool IsMutating { get; init; }
    public string? SessionId { get; init; }
    public string? ResponseSummary { get; init; }
    public int? HttpStatusCode { get; init; }
    public DateTimeOffset OccurredAt { get; init; } = DateTimeOffset.UtcNow;
}

/// <summary>Event data for an approval request.</summary>
public sealed class AgentApprovalLifecycleEvent
{
    public required string StageId { get; init; }
    public required string Description { get; init; }
    public string? SessionId { get; init; }
    public DateTimeOffset OccurredAt { get; init; } = DateTimeOffset.UtcNow;
}

/// <summary>Event data for a completed agent session turn.</summary>
public sealed class AgentSessionLifecycleEvent
{
    public string? SessionId { get; init; }
    public required string OriginalMessage { get; init; }
    public required string FinalResponse { get; init; }
    public int ToolCallCount { get; init; }
    public decimal? EstimatedCostUsd { get; init; }
    public DateTimeOffset OccurredAt { get; init; } = DateTimeOffset.UtcNow;
}

/// <summary>Event data for a budget warning.</summary>
public sealed class AgentBudgetLifecycleEvent
{
    public string? SessionId { get; init; }
    public decimal SpentUsd { get; init; }
    public decimal? LimitUsd { get; init; }
    public int InputTokensUsed { get; init; }
    public int? InputTokensLimit { get; init; }
    public DateTimeOffset OccurredAt { get; init; } = DateTimeOffset.UtcNow;
}
