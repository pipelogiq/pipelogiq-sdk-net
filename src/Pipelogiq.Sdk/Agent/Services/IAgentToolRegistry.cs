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
}
