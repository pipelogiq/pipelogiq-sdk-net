namespace PipelogiqSDK.Agent.Models;

/// <summary>
/// One turn in the agent's conversation history, used by the ReAct loop.
/// </summary>
public class AgentConversationTurn
{
    /// <summary>
    /// Turn type:
    /// "tool_call" — LLM decided to call a tool (stored as raw decision JSON in Content).
    /// "tool_result" — result received from a tool API call.
    /// "confirmation_requested" — LLM requested user confirmation (stored as raw decision JSON).
    /// "confirmation_result" — user approved or rejected.
    /// </summary>
    public string Type { get; set; } = null!;

    /// <summary>Tool name for tool_call and tool_result turns.</summary>
    public string? ToolName { get; set; }

    /// <summary>
    /// Raw content: JSON decision string for assistant turns,
    /// API response body for tool_result, status text for confirmation_result.
    /// </summary>
    public string? Content { get; set; }

    /// <summary>Approval decision for confirmation_result turns.</summary>
    public bool? Approved { get; set; }
}
