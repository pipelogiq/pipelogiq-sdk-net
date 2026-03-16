namespace PipelogiqSDK.Agent.Models;

/// <summary>
/// Reusable configuration for one outbound target API available to the AI agent.
/// </summary>
public class AgentTargetApiDefinition
{
    /// <summary>
    /// Unique target API name used by tools to reference this connection.
    /// </summary>
    public string Name { get; set; } = null!;

    /// <summary>
    /// Base URL of the target API.
    /// May be omitted when tools provide absolute URLs directly.
    /// </summary>
    public string? BaseUrl { get; set; }

    /// <summary>
    /// Authentication header configuration for this target API.
    /// </summary>
    public AgentAuthHeaderDefinition? Authentication { get; set; }

    /// <summary>
    /// Static headers added to every request for this target API.
    /// </summary>
    public Dictionary<string, string>? StaticHeaders { get; set; }

    /// <summary>
    /// Runtime-resolved header templates added to every request for this target API.
    /// Supports placeholders such as {{context:tenantId}} and {{ref:result.value}}.
    /// </summary>
    public Dictionary<string, string>? HeaderTemplates { get; set; }
}
