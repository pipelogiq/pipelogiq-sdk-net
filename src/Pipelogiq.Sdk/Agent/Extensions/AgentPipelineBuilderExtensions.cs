using PipelogiqSDK.Agent.Configuration;
using PipelogiqSDK.Agent.Models;
using PipelogiqSDK.Builders;
using PipelogiqSDK.Configuration;

namespace PipelogiqSDK.Agent.Extensions;

/// <summary>
/// Pipeline builder extensions for creating AI agent pipelines.
/// </summary>
public static class AgentPipelineBuilderExtensions
{
    private const int RateLimitRetryIntervalSeconds = 120;
    private const int RateLimitMaxRetries = 30;

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
    /// <param name="runOverrides">Optional per-pipeline overrides (e.g. second-model critic config).</param>
    /// <returns>Configured pipeline builder ready to send.</returns>
    public static PipelineBuilder CreateAiAgent(
        string message,
        AgentReplyTarget? replyTo = null,
        string? sessionId = null,
        string? userId = null,
        PipelogiqRunnerOptions? options = null,
        AgentRunOverrides? runOverrides = null)
    {
        var input = new AgentOrchestratorInput
        {
            Message = message,
            ReplyTo = replyTo,
            SessionId = sessionId,
            UserId = userId,
            RunOverrides = runOverrides,
        };

        return CreateAiAgent(input, options);
    }

    /// <summary>
    /// Creates an AI agent pipeline from a fully-configured <see cref="AgentOrchestratorInput"/>.
    /// Use this overload when you need to pass attachments (images, PDFs, audio).
    /// </summary>
    public static PipelineBuilder CreateAiAgent(
        AgentOrchestratorInput input,
        PipelogiqRunnerOptions? options = null)
    {
        return PipelineBuilder
            .Create("ai-agent", options)
            .WithAction(
                stageName: "agent:orchestrator",
                stageHandlerName: AgentConstants.OrchestratorHandlerName,
                input: input,
                options: new PipelogiqSDK.Contracts.StageOptions
                {
                    RetryInterval = RateLimitRetryIntervalSeconds,
                    MaxRetries = RateLimitMaxRetries,
                });
    }
}
