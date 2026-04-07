namespace PipelogiqSDK.Agent.Services;

/// <summary>
/// Default tool policy that permits every tool call unconditionally.
/// Replace with a custom implementation to enforce access control.
/// </summary>
public sealed class AllowAllToolPolicy : IAgentToolPolicy
{
    /// <inheritdoc />
    public Task<bool> CanExecuteAsync(AgentToolExecutionContext context, CancellationToken ct = default)
        => Task.FromResult(true);
}
