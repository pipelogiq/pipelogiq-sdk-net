using Microsoft.Extensions.Logging.Abstractions;
using PipelogiqSDK.Agent.Handlers;
using PipelogiqSDK.Agent.Models;
using PipelogiqSDK.Agent.Services;
using PipelogiqSDK.Contracts;

using Xunit;

namespace PipelogiqSDK.Tests.Agent;

public sealed class AgentResponderHandlerTests
{
    [Fact]
    public async Task ExecuteAsync_WithoutNotifier_StoresFinalResponseInContext()
    {
        var handler = new AgentResponderHandler(new StaticPlanner("Final synthesized response"), notificationRouter: null);

        var context = new StageContext
        {
            PipelineId = 300,
            StageId = 400,
            Payload = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
            {
                ["agent:originalMessage"] = "Show order status",
                ["agent:toolResults"] = new List<AgentToolResult>
                {
                    new()
                    {
                        ToolName = "getOrder",
                        ResultKey = "order",
                        StatusCode = 200,
                        IsSuccess = true,
                        ResponseBody = "{\"id\":1,\"status\":\"paid\"}",
                    }
                }
            }
        };

        var result = await handler.ExecuteAsync(context);

        Assert.True(result.IsSuccess);
        Assert.Contains("router unavailable", result.Result, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("Final synthesized response", context.Payload!["agent:finalResponse"]);
    }

    [Fact]
    public async Task ExecuteAsync_WithReplyTarget_DeliversViaMatchingChannel()
    {
        var channel = new CaptureChannel("signalr");
        var router = new AgentNotificationRouter(
            [channel],
            Array.Empty<IAgentNotifier>(),
            NullLogger<AgentNotificationRouter>.Instance);
        var handler = new AgentResponderHandler(new StaticPlanner("Delivered response"), router);

        var context = new StageContext
        {
            PipelineId = 11,
            StageId = 12,
            Payload = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
            {
                ["agent:originalMessage"] = "Ping",
                ["agent:toolResults"] = new List<AgentToolResult>
                {
                    new()
                    {
                        ToolName = "lookup",
                        ResultKey = "lookup",
                        StatusCode = 200,
                        IsSuccess = true,
                        ResponseBody = "{\"ok\":true}"
                    }
                },
                ["agent:replyTarget"] = new AgentReplyTarget
                {
                    Channel = "signalr",
                    Address = "conn-42"
                }
            }
        };

        var result = await handler.ExecuteAsync(context);

        Assert.True(result.IsSuccess);
        Assert.Contains("signalr", result.Result, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("conn-42", channel.LastTarget!.Address);
        Assert.Equal("Delivered response", channel.LastNotification!.Message);
        Assert.False(context.Payload!.ContainsKey("agent:finalResponse"));
    }

    [Fact]
    public async Task ExecuteAsync_WhenTerminalFailureFlagSet_ReturnsFailedResult()
    {
        var handler = new AgentResponderHandler(new StaticPlanner("unused"), notificationRouter: null);

        var context = new StageContext
        {
            PipelineId = 301,
            StageId = 401,
            Payload = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
            {
                ["agent:originalMessage"] = "Generate budget",
                ["agent:directAnswer"] = "I'm sorry, I was unable to complete this step.",
                ["agent:terminalFailure"] = true,
                ["agent:terminalFailureCode"] = "TOOL_LOOP",
            }
        };

        var result = await handler.ExecuteAsync(context);

        Assert.False(result.IsSuccess);
        Assert.Equal("TOOL_LOOP", result.ErrorCode);
        Assert.Equal("I'm sorry, I was unable to complete this step.", context.Payload!["agent:finalResponse"]);
    }

    private sealed class StaticPlanner(string finalResponse) : ILlmPlanner
    {
        public Task<AgentPlan> PlanAsync(
            string userMessage,
            IReadOnlyList<AgentToolDefinition> tools,
            string? systemPrompt = null,
            CancellationToken ct = default)
        {
            return Task.FromResult(new AgentPlan());
        }

        public Task<AgentTextResult> SynthesizeAsync(
            string originalMessage,
            IReadOnlyList<AgentToolResult> results,
            CancellationToken ct = default)
        {
            return Task.FromResult(new AgentTextResult { Text = finalResponse });
        }

        public Task<AgentThinkDecision> ThinkAsync(
            string originalMessage,
            IReadOnlyList<AgentConversationTurn> history,
            IReadOnlyList<AgentToolDefinition> tools,
            bool requireConfirmationForMutations,
            string? systemPrompt = null,
            IReadOnlyList<AgentAttachment>? attachments = null,
            CancellationToken ct = default)
        {
            return Task.FromResult(new AgentThinkDecision
            {
                Action = AgentThinkAction.Done,
                FinalAnswer = finalResponse
            });
        }
    }

    private sealed class CaptureChannel(string channelName) : IAgentNotificationChannel
    {
        public AgentReplyTarget? LastTarget { get; private set; }
        public AgentNotification? LastNotification { get; private set; }

        public string Name => channelName;

        public bool CanHandle(AgentReplyTarget replyTarget) =>
            string.Equals(replyTarget.Channel, channelName, StringComparison.OrdinalIgnoreCase);

        public Task NotifyAsync(AgentReplyTarget replyTarget, AgentNotification notification, CancellationToken ct = default)
        {
            LastTarget = replyTarget;
            LastNotification = notification;
            return Task.CompletedTask;
        }
    }
}
