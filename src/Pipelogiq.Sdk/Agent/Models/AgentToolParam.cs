namespace PipelogiqSDK.Agent.Models;

/// <summary>
/// Rich parameter definition for a tool, used to generate accurate LLM prompts.
/// </summary>
public class AgentToolParam
{
    /// <summary>
    /// Where this parameter is used:
    /// "path" — substituted into the URL template ({id}),
    /// "query" — appended as query string (?key=value),
    /// "body" — included in the JSON request body.
    /// </summary>
    public string In { get; set; } = "body";

    /// <summary>Human-readable description of this parameter's purpose.</summary>
    public string Description { get; set; } = null!;

    /// <summary>
    /// JSON Schema type: "string", "integer", "number", "boolean", "array", "object".
    /// </summary>
    public string Type { get; set; } = "string";

    /// <summary>Whether this parameter is required. Defaults to true.</summary>
    public bool Required { get; set; } = true;

    /// <summary>
    /// Value format hint shown to the LLM, e.g. "HH:mm", "yyyy-MM-dd", "uuid".
    /// </summary>
    public string? Format { get; set; }

    /// <summary>
    /// Allowed values. When provided the LLM will only use these values.
    /// </summary>
    public string[]? EnumValues { get; set; }

    /// <summary>
    /// For object type: nested property definitions for this object.
    /// </summary>
    public Dictionary<string, AgentToolParam>? Properties { get; set; }

    /// <summary>Concrete example value shown to the LLM.</summary>
    public string? Example { get; set; }

    /// <summary>
    /// For array type: JSON schema item type. Defaults to object when omitted.
    /// </summary>
    public string? ItemsType { get; set; }

    /// <summary>
    /// For array type: describes what each array item should look like.
    /// </summary>
    public string? ItemsDescription { get; set; }

    /// <summary>
    /// For array type when items are objects: nested property definitions for each item.
    /// </summary>
    public Dictionary<string, AgentToolParam>? ItemsProperties { get; set; }
}
