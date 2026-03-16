namespace PipelogiqSDK.Agent.OpenApi;

/// <summary>
/// Configuration for loading tool definitions from an OpenAPI / Swagger spec.
/// </summary>
public class OpenApiLoadOptions
{
    /// <summary>
    /// Only include operations with these operationIds.
    /// When null or empty, all operations are included (subject to other filters).
    /// </summary>
    public List<string>? IncludeOperations { get; set; }

    /// <summary>
    /// Exclude operations with these operationIds even if they pass other filters.
    /// </summary>
    public List<string>? ExcludeOperations { get; set; }

    /// <summary>
    /// Only include operations tagged with at least one of these tags.
    /// When null or empty, all tags are included.
    /// </summary>
    public List<string>? IncludeTags { get; set; }

    /// <summary>
    /// Exclude operations whose path starts with any of these prefixes.
    /// Useful for hiding admin or internal endpoints from the agent.
    /// Example: ["/api/admin", "/internal"]
    /// </summary>
    public List<string>? ExcludePathPrefixes { get; set; }

    /// <summary>
    /// HTTP methods treated as mutating (require confirmation if policy is enabled).
    /// Defaults to POST, PUT, PATCH, DELETE.
    /// </summary>
    public HashSet<string> MutatingMethods { get; set; } =
        new(StringComparer.OrdinalIgnoreCase) { "POST", "PUT", "PATCH", "DELETE" };

    /// <summary>
    /// Maximum depth for generating example objects from JSON Schema.
    /// Higher values produce richer examples but may be slower for deeply nested schemas.
    /// Default is 3.
    /// </summary>
    public int MaxExampleDepth { get; set; } = 3;

    /// <summary>
    /// Headers sent when fetching the spec (e.g. Authorization for protected specs).
    /// </summary>
    public Dictionary<string, string>? SpecFetchHeaders { get; set; }

    /// <summary>
    /// Optional named target API assigned to all tools loaded from this spec.
    /// </summary>
    public string? TargetApiName { get; set; }
}
