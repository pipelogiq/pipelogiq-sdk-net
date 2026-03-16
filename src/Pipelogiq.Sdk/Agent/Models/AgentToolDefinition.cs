namespace PipelogiqSDK.Agent.Models;

/// <summary>
/// Describes one tool (API endpoint) available to the AI agent.
/// </summary>
public class AgentToolDefinition
{
    /// <summary>Unique tool name used by the LLM to reference it.</summary>
    public string Name { get; set; } = null!;

    /// <summary>Human-readable description for the LLM to understand when to use it.</summary>
    public string Description { get; set; } = null!;

    /// <summary>HTTP method: GET, POST, PATCH, PUT, DELETE.</summary>
    public string HttpMethod { get; set; } = "GET";

    /// <summary>
    /// Optional named target API connection to use for this tool.
    /// Enables multiple external APIs within one agent.
    /// </summary>
    public string? TargetApiName { get; set; }

    /// <summary>
    /// Optional base URL override for this tool.
    /// When omitted, the named target API or legacy agent default is used.
    /// </summary>
    public string? BaseUrl { get; set; }

    /// <summary>URL path template, e.g. "/api/persons" or "/api/persons/{id}".</summary>
    public string UrlTemplate { get; set; } = null!;

    /// <summary>
    /// Rich parameter definitions with full type info, format hints, and examples.
    /// When provided, takes precedence over QueryParams and BodyParams for LLM prompt generation.
    /// Key is the parameter name.
    /// </summary>
    public Dictionary<string, AgentToolParam>? Params { get; set; }

    /// <summary>
    /// Simple query parameter names for GET requests (backward compat).
    /// Use Params for richer definitions.
    /// </summary>
    public string[]? QueryParams { get; set; }

    /// <summary>
    /// Simple body parameter name → description mapping (backward compat).
    /// Use Params for richer definitions.
    /// </summary>
    public Dictionary<string, string>? BodyParams { get; set; }

    /// <summary>
    /// URL path parameters extracted from UrlTemplate (e.g. "id" from "/api/persons/{id}").
    /// If null, auto-detected from UrlTemplate.
    /// </summary>
    public string[]? PathParams { get; set; }

    /// <summary>
    /// An example request body object shown to the LLM.
    /// Helps the LLM understand the exact structure and field names to use.
    /// </summary>
    public object? BodyExample { get; set; }

    /// <summary>
    /// An example response body shown to the LLM.
    /// Helps the LLM write correct {{ref:key.field}} references for downstream steps.
    /// </summary>
    public object? ResponseExample { get; set; }

    /// <summary>
    /// Whether this tool performs mutations (POST/PATCH/PUT/DELETE).
    /// If null, auto-detected from HttpMethod.
    /// </summary>
    public bool? IsMutating { get; set; }

    /// <summary>Static headers added to each request for this tool.</summary>
    public Dictionary<string, string>? StaticHeaders { get; set; }

    /// <summary>
    /// Runtime-resolved header templates added to each request for this tool.
    /// Supports placeholders such as {{context:tenantId}} and {{ref:result.value}}.
    /// </summary>
    public Dictionary<string, string>? HeaderTemplates { get; set; }

    /// <summary>
    /// Authentication header configuration for this tool.
    /// Overrides target API or legacy agent authentication when provided.
    /// </summary>
    public AgentAuthHeaderDefinition? Authentication { get; set; }

    /// <summary>Returns effective IsMutating value (auto-detect if not set).</summary>
    public bool IsEffectivelyMutating =>
        IsMutating ?? HttpMethod.ToUpperInvariant() is "POST" or "PATCH" or "PUT" or "DELETE";
}
