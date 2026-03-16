using PipelogiqSDK.Agent.Configuration;
using PipelogiqSDK.Agent.Models;

namespace PipelogiqSDK.Agent.Services;

internal sealed class AgentToolRegistry(AgentOptions options) : IAgentToolRegistry
{
    public IReadOnlyList<AgentToolDefinition> GetAll() => options.Tools.AsReadOnly();

    public AgentToolDefinition? Find(string name) =>
        options.Tools.FirstOrDefault(t => string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase));
}
