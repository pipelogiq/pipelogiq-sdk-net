namespace PipelogiqSDK.Agent.Services;

/// <summary>
/// Broadcasts lifecycle events to all registered observers.
/// Errors in individual observers are swallowed — one failing observer
/// must never prevent other observers from receiving events.
/// </summary>
internal sealed class CompositeLifecycleObserver(IEnumerable<IAgentLifecycleObserver> observers)
    : IAgentLifecycleObserver
{
    private readonly IAgentLifecycleObserver[] _observers = observers.ToArray();

    public async Task OnToolExecutedAsync(AgentToolLifecycleEvent e, CancellationToken ct = default)
    {
        foreach (var obs in _observers)
            try { await obs.OnToolExecutedAsync(e, ct); } catch { /* swallow */ }
    }

    public async Task OnApprovalRequestedAsync(AgentApprovalLifecycleEvent e, CancellationToken ct = default)
    {
        foreach (var obs in _observers)
            try { await obs.OnApprovalRequestedAsync(e, ct); } catch { /* swallow */ }
    }

    public async Task OnSessionCompletedAsync(AgentSessionLifecycleEvent e, CancellationToken ct = default)
    {
        foreach (var obs in _observers)
            try { await obs.OnSessionCompletedAsync(e, ct); } catch { /* swallow */ }
    }

    public async Task OnBudgetWarningAsync(AgentBudgetLifecycleEvent e, CancellationToken ct = default)
    {
        foreach (var obs in _observers)
            try { await obs.OnBudgetWarningAsync(e, ct); } catch { /* swallow */ }
    }
}
