using PipelogiqSDK.Agent.Configuration;

namespace PipelogiqSDK.Agent.Models;

/// <summary>
/// Input for the AgentOrchestratorHandler stage.
/// </summary>
public class AgentOrchestratorInput
{
    /// <summary>The user's natural language message to the agent.</summary>
    public string Message { get; set; } = null!;

    /// <summary>Optional explicit reply route used for outbound notifications.</summary>
    public AgentReplyTarget? ReplyTo { get; set; }

    /// <summary>Client session identifier for sending responses back.</summary>
    public string? SessionId { get; set; }

    /// <summary>User identifier for context and authorization.</summary>
    public string? UserId { get; set; }

    /// <summary>Optional binary attachments (images, PDFs, audio) sent alongside the message.</summary>
    public List<AgentAttachment>? Attachments { get; set; }

    /// <summary>
    /// Per-pipeline overrides (e.g. second-model critic config). When null, the agent
    /// runs with the global <see cref="AgentOptions"/> defaults and no critic.
    /// </summary>
    public AgentRunOverrides? RunOverrides { get; set; }
}
