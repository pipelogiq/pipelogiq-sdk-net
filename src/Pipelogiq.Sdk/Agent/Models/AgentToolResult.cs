namespace PipelogiqSDK.Agent.Models;

/// <summary>
/// Result from executing one agent tool call.
/// </summary>
public class AgentToolResult
{
    /// <summary>Tool name that was called.</summary>
    public string ToolName { get; set; } = null!;

    /// <summary>Context key where the result is stored.</summary>
    public string ResultKey { get; set; } = null!;

    /// <summary>Raw response body from the API call.</summary>
    public string? ResponseBody { get; set; }

    /// <summary>HTTP status code returned.</summary>
    public int StatusCode { get; set; }

    /// <summary>Whether the call succeeded (2xx status).</summary>
    public bool IsSuccess { get; set; }

    /// <summary>Whether this was a mutating operation.</summary>
    public bool IsMutating { get; set; }
}
