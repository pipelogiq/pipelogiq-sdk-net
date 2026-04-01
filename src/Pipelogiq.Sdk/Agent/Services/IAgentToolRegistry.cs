using PipelogiqSDK.Agent.Models;

namespace PipelogiqSDK.Agent.Services;

/// <summary>
/// Read-only registry of available agent tools.
/// </summary>
public interface IAgentToolRegistry
{
    /// <summary>Returns all registered tool definitions.</summary>
    IReadOnlyList<AgentToolDefinition> GetAll();

    /// <summary>Finds a tool definition by name. Returns null if not found.</summary>
    AgentToolDefinition? Find(string name);

    /// <summary>
    /// Finds the native handler registered for the tool, if any.
    /// Returns null when the tool is an HTTP tool (handled by <c>AgentToolHandler</c>).
    /// </summary>
    IAgentToolHandler? FindNativeHandler(string name);
}
