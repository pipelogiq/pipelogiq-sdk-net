using PipelogiqSDK.Agent.Models;
using PipelogiqSDK.Builders;
using PipelogiqSDK.Configuration;

namespace PipelogiqSDK.Agent.Extensions;

/// <summary>
/// Pipeline builder extensions for creating AI agent pipelines.
/// </summary>
public static class AgentPipelineBuilderExtensions
{
    /// <summary>
    /// Creates an AI agent pipeline for the given user message.
    /// The SDK automatically appends tool call stages, confirmation, and response stages
    /// based on LLM planning.
    /// </summary>
    /// <param name="message">Natural language message from the user.</param>
    /// <param name="replyTo">Optional explicit outbound notification route.</param>
    /// <param name="sessionId">Client session ID for sending responses back.</param>
    /// <param name="userId">Optional user identifier.</param>
    /// <param name="options">Optional runner options override.</param>
    /// <returns>Configured pipeline builder ready to send.</returns>
    public static PipelineBuilder CreateAiAgent(
        string message,
        AgentReplyTarget? replyTo = null,
        string? sessionId = null,
        string? userId = null,
        PipelogiqRunnerOptions? options = null)
    {
        var input = new AgentOrchestratorInput
        {
            Message = message,
            ReplyTo = replyTo,
            SessionId = sessionId,
            UserId = userId,
        };

        return PipelineBuilder
            .Create("ai-agent", options)
            .WithAction(
                stageName: "agent:orchestrator",
                stageHandlerName: AgentConstants.OrchestratorHandlerName,
                input: input);
    }
}
