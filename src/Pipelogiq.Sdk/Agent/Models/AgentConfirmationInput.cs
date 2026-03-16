namespace PipelogiqSDK.Agent.Models;

/// <summary>
/// Input for the AgentConfirmationHandler stage.
/// </summary>
public class AgentConfirmationInput
{
    /// <summary>List of mutating tool calls pending confirmation.</summary>
    public List<AgentToolCall> PendingMutations { get; set; } = new();
}
