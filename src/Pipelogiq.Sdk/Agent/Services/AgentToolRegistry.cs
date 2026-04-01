using PipelogiqSDK.Agent.Configuration;
using PipelogiqSDK.Agent.Models;

namespace PipelogiqSDK.Agent.Services;

internal sealed class AgentToolRegistry(AgentOptions options) : IAgentToolRegistry
{
    private readonly Dictionary<string, IAgentToolHandler> _nativeHandlers =
        new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<AgentToolDefinition> GetAll() => options.Tools.AsReadOnly();

    public AgentToolDefinition? Find(string name) =>
        options.Tools.FirstOrDefault(t => string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase));

    public IAgentToolHandler? FindNativeHandler(string name) =>
        _nativeHandlers.GetValueOrDefault(name);

    internal void RegisterNativeHandler(string name, IAgentToolHandler handler) =>
        _nativeHandlers[name] = handler;
}
