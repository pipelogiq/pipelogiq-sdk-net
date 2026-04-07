using System.Diagnostics.Metrics;

namespace PipelogiqSDK.Agent.Services;

/// <summary>
/// Lifecycle observer that emits <c>System.Diagnostics.Metrics</c> counters and histograms.
/// These metrics are scraped by any OpenTelemetry-compatible collector (Prometheus, OTLP, etc.).
///
/// Emitted instruments:
/// <list type="bullet">
///   <item><c>pipelogiq.agent.tool_calls</c> — counter, tags: tool, success, mutating</item>
///   <item><c>pipelogiq.agent.sessions_completed</c> — counter, tags: (none)</item>
///   <item><c>pipelogiq.agent.session_cost_usd</c> — histogram, tags: (none)</item>
///   <item><c>pipelogiq.agent.session_tool_count</c> — histogram, tags: (none)</item>
///   <item><c>pipelogiq.agent.budget_warnings</c> — counter, tags: (none)</item>
///   <item><c>pipelogiq.agent.approval_requests</c> — counter, tags: (none)</item>
/// </list>
///
/// Registration:
/// <code>
/// services.AddPipelogiqAgent(...)
///     .AddLifecycleObserver&lt;MetricsLifecycleObserver&gt;(services);
/// </code>
/// </summary>
public sealed class MetricsLifecycleObserver : IAgentLifecycleObserver
{
    private static readonly Meter AgentMeter = new("PipelogiqSDK.Agent", "1.0.0");

    private static readonly Counter<long> ToolCallsCounter =
        AgentMeter.CreateCounter<long>("pipelogiq.agent.tool_calls", "calls",
            "Total number of tool calls executed by the agent");

    private static readonly Counter<long> SessionsCompletedCounter =
        AgentMeter.CreateCounter<long>("pipelogiq.agent.sessions_completed", "sessions",
            "Total number of completed agent sessions");

    private static readonly Histogram<double> SessionCostHistogram =
        AgentMeter.CreateHistogram<double>("pipelogiq.agent.session_cost_usd", "USD",
            "Estimated LLM cost per agent session");

    private static readonly Histogram<int> SessionToolCountHistogram =
        AgentMeter.CreateHistogram<int>("pipelogiq.agent.session_tool_count", "tools",
            "Number of tool calls per agent session");

    private static readonly Counter<long> BudgetWarningsCounter =
        AgentMeter.CreateCounter<long>("pipelogiq.agent.budget_warnings", "warnings",
            "Number of budget threshold warnings");

    private static readonly Counter<long> ApprovalRequestsCounter =
        AgentMeter.CreateCounter<long>("pipelogiq.agent.approval_requests", "requests",
            "Number of human-approval requests");

    public Task OnToolExecutedAsync(AgentToolLifecycleEvent e, CancellationToken ct = default)
    {
        ToolCallsCounter.Add(1,
            new KeyValuePair<string, object?>("tool", e.ToolName),
            new KeyValuePair<string, object?>("success", e.IsSuccess),
            new KeyValuePair<string, object?>("mutating", e.IsMutating));
        return Task.CompletedTask;
    }

    public Task OnSessionCompletedAsync(AgentSessionLifecycleEvent e, CancellationToken ct = default)
    {
        SessionsCompletedCounter.Add(1);

        if (e.EstimatedCostUsd.HasValue)
            SessionCostHistogram.Record((double)e.EstimatedCostUsd.Value);

        SessionToolCountHistogram.Record(e.ToolCallCount);
        return Task.CompletedTask;
    }

    public Task OnBudgetWarningAsync(AgentBudgetLifecycleEvent e, CancellationToken ct = default)
    {
        BudgetWarningsCounter.Add(1);
        return Task.CompletedTask;
    }

    public Task OnApprovalRequestedAsync(AgentApprovalLifecycleEvent e, CancellationToken ct = default)
    {
        ApprovalRequestsCounter.Add(1);
        return Task.CompletedTask;
    }
}
