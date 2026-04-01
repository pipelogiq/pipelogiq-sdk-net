namespace PipelogiqSDK.Agent.Models;

/// <summary>
/// The result returned by a native tool handler (<see cref="Services.IAgentToolHandler"/>).
/// </summary>
public class AgentToolOutput
{
    /// <summary>Whether the tool executed successfully.</summary>
    public bool IsSuccess { get; init; }

    /// <summary>
    /// The tool's output — sent back to the LLM as the tool result.
    /// Use a concise, structured string (JSON, plain text, CSV) that the LLM can reason about.
    /// </summary>
    public string Output { get; init; } = string.Empty;

    /// <summary>Optional error message shown when <see cref="IsSuccess"/> is false.</summary>
    public string? ErrorMessage { get; init; }

    /// <summary>Creates a successful tool output.</summary>
    public static AgentToolOutput Success(string output) =>
        new() { IsSuccess = true, Output = output };

    /// <summary>Creates a failed tool output.</summary>
    public static AgentToolOutput Failure(string errorMessage) =>
        new() { IsSuccess = false, Output = string.Empty, ErrorMessage = errorMessage };
}
