namespace PipelogiqSDK.Agent.Models;

/// <summary>
/// Describes how an authentication value should be sent in an outbound request header.
/// </summary>
public class AgentAuthHeaderDefinition
{
    /// <summary>
    /// Header name used for the authentication value.
    /// Defaults to Authorization.
    /// </summary>
    public string HeaderName { get; set; } = "Authorization";

    /// <summary>
    /// Optional authentication scheme prefix, e.g. Bearer.
    /// Leave empty to send the raw value without a scheme.
    /// </summary>
    public string? Scheme { get; set; } = "Bearer";

    /// <summary>
    /// Static authentication value.
    /// </summary>
    public string? Value { get; set; }

    /// <summary>
    /// Runtime-resolved authentication value template.
    /// Supports placeholders such as {{context:userAccessToken}}.
    /// </summary>
    public string? ValueTemplate { get; set; }
}
