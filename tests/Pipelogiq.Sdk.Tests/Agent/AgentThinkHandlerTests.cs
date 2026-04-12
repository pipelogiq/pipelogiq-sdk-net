using PipelogiqSDK.Abstractions;
using PipelogiqSDK.Agent.Configuration;
using PipelogiqSDK.Agent.Handlers;
using PipelogiqSDK.Agent.Models;
using PipelogiqSDK.Agent.Services;
using PipelogiqSDK.Api;
using PipelogiqSDK.Contracts;

using Xunit;

namespace PipelogiqSDK.Tests.Agent;

public sealed class AgentThinkHandlerTests
{
    [Fact]
    public async Task ExecuteAsync_MutatingCallWithoutPreApproval_ForcesConfirmationStage()
    {
        var planner = new StaticPlanner(new AgentThinkDecision
        {
            Action = AgentThinkAction.CallTool,
            ToolCall = new AgentToolCall
            {
                Tool = "updateOrder",
                ResultKey = "updateResult",
                Params = new Dictionary<string, object?> { ["id"] = 42, ["status"] = "paid" },
            },
            RawDecisionJson = """{"action":"call_tool"}""",
        });

        var handler = new CapturingThinkHandler(
            planner,
            new StaticToolRegistry([
                new AgentToolDefinition
                {
                    Name = "updateOrder",
                    Description = "Update order",
                    HttpMethod = "PATCH",
                    UrlTemplate = "/api/orders/{id}",
                }
            ]),
            new AgentOptions
            {
                RequireConfirmationForMutations = true,
                MaxThinkSteps = 10,
            },
            new PipelogiqApiClient("http://localhost:8081", "test"));

        var context = new StageContext
        {
            PipelineId = 101,
            StageId = 201,
            Payload = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
            {
                ["agent:originalMessage"] = "Mark order as paid",
                ["agent:conversationHistory"] = new List<AgentConversationTurn>(),
                ["agent:thinkStepCount"] = 0,
            }
        };

        var result = await handler.ExecuteAsync(context);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, handler.LastAppendedStages.Count);
        Assert.Equal("AgentConfirmationHandler", handler.LastAppendedStages[0].StageHandlerName);
        Assert.Equal("AgentThinkHandler", handler.LastAppendedStages[1].StageHandlerName);

        var confirmationInput = Assert.IsType<AgentConfirmationInput>(handler.LastAppendedStages[0].Input);
        Assert.Single(confirmationInput.PendingMutations);
        Assert.Equal("updateOrder", confirmationInput.PendingMutations[0].Tool);
    }

    [Fact]
    public async Task ExecuteAsync_MutatingCallWithPreApproval_ExecutesToolStageWithoutExtraConfirmation()
    {
        var plannedCall = new AgentToolCall
        {
            Tool = "updateOrder",
            ResultKey = "updateResult",
            Params = new Dictionary<string, object?> { ["id"] = 42, ["status"] = "paid" },
        };

        var planner = new StaticPlanner(new AgentThinkDecision
        {
            Action = AgentThinkAction.CallTool,
            ToolCall = new AgentToolCall
            {
                Tool = "updateOrder",
                ResultKey = "updateResult",
                Params = new Dictionary<string, object?> { ["id"] = 42, ["status"] = "paid" },
            },
            RawDecisionJson = """{"action":"call_tool"}""",
        });

        var handler = new CapturingThinkHandler(
            planner,
            new StaticToolRegistry([
                new AgentToolDefinition
                {
                    Name = "updateOrder",
                    Description = "Update order",
                    HttpMethod = "PATCH",
                    UrlTemplate = "/api/orders/{id}",
                }
            ]),
            new AgentOptions
            {
                RequireConfirmationForMutations = true,
                MaxThinkSteps = 10,
            },
            new PipelogiqApiClient("http://localhost:8081", "test"));

        var context = new StageContext
        {
            PipelineId = 102,
            StageId = 202,
            Payload = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
            {
                ["agent:originalMessage"] = "Mark order as paid",
                ["agent:conversationHistory"] = new List<AgentConversationTurn>(),
                ["agent:thinkStepCount"] = 0,
                ["agent:approvedMutations"] = new List<AgentToolCall> { plannedCall },
            }
        };

        var result = await handler.ExecuteAsync(context);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, handler.LastAppendedStages.Count);
        Assert.Equal("AgentToolHandler", handler.LastAppendedStages[0].StageHandlerName);
        Assert.Equal("AgentThinkHandler", handler.LastAppendedStages[1].StageHandlerName);
        Assert.False(context.Payload!.ContainsKey("agent:approvedMutations"));
    }

    [Fact]
    public async Task ExecuteAsync_WhenToolLoopDetected_AppendsSingleResponderAndReturnsSuccessfulTerminalResult()
    {
        var handler = new CapturingThinkHandler(
            new StaticPlanner(new AgentThinkDecision { Action = AgentThinkAction.Done, FinalAnswer = "unused" }),
            new StaticToolRegistry([]),
            new AgentOptions
            {
                RequireConfirmationForMutations = true,
                MaxThinkSteps = 10,
            },
            new PipelogiqApiClient("http://localhost:8081", "test"));

        var context = new StageContext
        {
            PipelineId = 103,
            StageId = 203,
            Payload = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
            {
                ["agent:originalMessage"] = "Generate budget",
                ["agent:conversationHistory"] = new List<AgentConversationTurn>(),
                ["agent:thinkStepCount"] = 0,
                ["agent:toolFailures:saveBudgetResult"] = 3,
            }
        };

        var result = await handler.ExecuteAsync(context);

        Assert.True(result.IsSuccess);
        Assert.Equal("TOOL_LOOP", result.ErrorCode);
        Assert.Single(handler.LastAppendedStages);
        Assert.Equal("agent:responder", handler.LastAppendedStages[0].StageName);
        Assert.True(handler.LastAppendedStages[0].Options?.RunNextIfFailed);
        Assert.True(context.Payload!.ContainsKey("agent:responderAppended"));
    }

    [Fact]
    public async Task ExecuteAsync_WhenResponderAlreadyAppended_DoesNotAppendAnotherResponder()
    {
        var handler = new CapturingThinkHandler(
            new StaticPlanner(new AgentThinkDecision { Action = AgentThinkAction.Done, FinalAnswer = "unused" }),
            new StaticToolRegistry([]),
            new AgentOptions
            {
                RequireConfirmationForMutations = true,
                MaxThinkSteps = 10,
            },
            new PipelogiqApiClient("http://localhost:8081", "test"));

        var context = new StageContext
        {
            PipelineId = 104,
            StageId = 204,
            Payload = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
            {
                ["agent:originalMessage"] = "Generate budget",
                ["agent:conversationHistory"] = new List<AgentConversationTurn>(),
                ["agent:thinkStepCount"] = 0,
                ["agent:toolFailures:saveBudgetResult"] = 3,
                ["agent:responderAppended"] = true,
            }
        };

        var result = await handler.ExecuteAsync(context);

        Assert.True(result.IsSuccess);
        Assert.Equal("TOOL_LOOP", result.ErrorCode);
        Assert.Empty(handler.LastAppendedStages);
    }

    private sealed class CapturingThinkHandler(
        ILlmPlanner llmPlanner,
        IAgentToolRegistry toolRegistry,
        AgentOptions agentOptions,
        PipelogiqApiClient apiClient)
        : AgentThinkHandler(llmPlanner, toolRegistry, agentOptions, apiClient)
    {
        public List<StageInfo> LastAppendedStages { get; private set; } = new();

        protected override Task AppendStagesAsync(int pipelineId, IEnumerable<StageInfo> stages)
        {
            LastAppendedStages = stages.ToList();
            return Task.CompletedTask;
        }
    }

    private sealed class StaticPlanner(AgentThinkDecision decision) : ILlmPlanner
    {
        public Task<AgentPlan> PlanAsync(
            string userMessage,
            IReadOnlyList<AgentToolDefinition> tools,
            string? systemPrompt = null,
            CancellationToken ct = default)
        {
            return Task.FromResult(new AgentPlan());
        }

        public Task<string> SynthesizeAsync(
            string originalMessage,
            IReadOnlyList<AgentToolResult> results,
            CancellationToken ct = default)
        {
            return Task.FromResult("done");
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
            return Task.FromResult(decision);
        }
    }

    private sealed class StaticToolRegistry(IReadOnlyList<AgentToolDefinition> tools) : IAgentToolRegistry
    {
        public IReadOnlyList<AgentToolDefinition> GetAll() => tools;

        public AgentToolDefinition? Find(string name) =>
            tools.FirstOrDefault(t => string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase));

        public IAgentToolHandler? FindNativeHandler(string name) => null;
    }
}
