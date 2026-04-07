using PipelogiqSDK.Abstractions;

namespace PipelogiqSDK.Agent.Services;

/// <summary>
/// Controls which tools an agent is allowed to execute in a given context.
/// Register a custom implementation to enforce role-based or context-based access.
///
/// The default implementation (<see cref="AllowAllToolPolicy"/>) permits every tool call.
///
/// Example — allow mutating tools only for managers:
/// <code>
/// public class RoleBasedToolPolicy : IAgentToolPolicy
/// {
///     public Task&lt;bool&gt; CanExecuteAsync(AgentToolExecutionContext ctx, CancellationToken ct)
///     {
///         if (!ctx.IsMutating) return Task.FromResult(true);
///         var role = ctx.StageContext?.TryGetValue&lt;string&gt;("agent:userRole");
///         return Task.FromResult(role == "manager");
///     }
/// }
///
/// // Registration:
/// services.AddPipelogiqAgent(...).UseToolPolicy&lt;RoleBasedToolPolicy&gt;(services);
/// </code>
/// </summary>
public interface IAgentToolPolicy
{
    /// <summary>
    /// Returns true if the tool call is permitted; false to block it.
    /// When false, the tool is not executed and the agent receives a
    /// "Permission denied" error message it can relay to the user.
    /// </summary>
    Task<bool> CanExecuteAsync(AgentToolExecutionContext context, CancellationToken ct = default);
}

/// <summary>
/// Context passed to <see cref="IAgentToolPolicy.CanExecuteAsync"/> for each tool invocation.
/// </summary>
public sealed class AgentToolExecutionContext
{
    /// <summary>Name of the tool being invoked.</summary>
    public required string ToolName { get; init; }

    /// <summary>Whether the tool is marked as mutating (POST/PATCH/PUT/DELETE or IsMutating = true).</summary>
    public bool IsMutating { get; init; }

    /// <summary>Resolved parameters that will be passed to the tool.</summary>
    public IReadOnlyDictionary<string, object?> Parameters { get; init; } = new Dictionary<string, object?>();

    /// <summary>Current pipeline stage context. Use to read user identity, roles, or other context values.</summary>
    public IStageContext? StageContext { get; init; }
}
