using PipelogiqSDK.Agent.Handlers;
using PipelogiqSDK.Agent.Models;
using PipelogiqSDK.Contracts;

using Xunit;

namespace PipelogiqSDK.Tests.Agent;

public sealed class AgentConfirmationHandlerTests
{
    [Fact]
    public async Task ExecuteAsync_WhenRejectedAndResponderExists_JumpsToResponderStage()
    {
        var handler = new AgentConfirmationHandler();
        var input = new AgentConfirmationInput
        {
            PendingMutations =
            [
                new AgentToolCall
                {
                    Tool = "updateOrder",
                    Params = new Dictionary<string, object?> { ["id"] = 42 },
                    ResultKey = "updateResult"
                }
            ]
        };

        var context = new StageContext
        {
            PipelineId = 10,
            StageId = 20,
            Payload = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
            {
                ["agent:approved"] = false,
                ["agent:rejectionReason"] = "Denied by user",
                ["agent:responderStageId"] = 777,
            }
        };

        var result = await handler.ExecuteAsync(input, context);

        var dto = Assert.IsType<StageResultDto>(result);
        Assert.True(dto.IsSuccess);
        Assert.Equal(777, dto.NextStageId);
        Assert.Equal("Denied by user", context.Payload!["agent:directAnswer"]);
    }

    [Fact]
    public async Task ExecuteAsync_WhenRejectedAndResponderIsPresentInAppendedStageMap_JumpsToResponderStage()
    {
        var handler = new AgentConfirmationHandler();
        var input = new AgentConfirmationInput
        {
            PendingMutations =
            [
                new AgentToolCall
                {
                    Tool = "updateOrder",
                    Params = new Dictionary<string, object?> { ["id"] = 42 },
                    ResultKey = "updateResult"
                }
            ]
        };

        var context = new StageContext
        {
            PipelineId = 11,
            StageId = 21,
            Payload = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
            {
                ["agent:approved"] = false,
                ["agent:rejectionReason"] = "Denied by user",
                ["pipelogiq:appendedStageIds"] = new Dictionary<string, int>
                {
                    ["agent:responder"] = 778
                }
            }
        };

        var result = await handler.ExecuteAsync(input, context);

        var dto = Assert.IsType<StageResultDto>(result);
        Assert.True(dto.IsSuccess);
        Assert.Equal(778, dto.NextStageId);
        Assert.Equal("Denied by user", context.Payload!["agent:directAnswer"]);
    }

    [Fact]
    public async Task ExecuteAsync_WithoutRoute_ReturnsErrorAndStoresPendingNotification()
    {
        var handler = new AgentConfirmationHandler(notificationRouter: null);
        var input = new AgentConfirmationInput
        {
            PendingMutations =
            [
                new AgentToolCall
                {
                    Tool = "updateOrder",
                    Params = new Dictionary<string, object?> { ["id"] = 42 },
                    ResultKey = "updateResult"
                }
            ]
        };

        var context = new StageContext
        {
            PipelineId = 1,
            StageId = 2,
            Payload = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
        };

        var result = await handler.ExecuteAsync(input, context);

        Assert.False(result.IsSuccess);
        Assert.Contains("Unable to deliver confirmation request", result.Result, StringComparison.OrdinalIgnoreCase);
        var notification = Assert.IsType<AgentNotification>(context.Payload!["agent:pendingNotification"]);
        Assert.Equal("confirmation_required", notification.Type);
    }
}
