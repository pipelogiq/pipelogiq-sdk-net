using PipelogiqSDK.Abstractions;
using PipelogiqSDK.Agent;
using PipelogiqSDK.Agent.Configuration;
using PipelogiqSDK.Agent.Handlers;
using PipelogiqSDK.Agent.Models;
using PipelogiqSDK.Agent.Services;
using PipelogiqSDK.Api;
using PipelogiqSDK.Contracts;

using Xunit;

namespace PipelogiqSDK.Tests.Agent;

public sealed class AgentCriticHandlerTests
{
    [Fact]
    public async Task Approve_CallTool_SchedulesToolThenThinkAndRecordsHistory()
    {
        var proposal = new AgentThinkDecision
        {
            Action = AgentThinkAction.CallTool,
            ToolCall = new AgentToolCall
            {
                Tool = "saveBudgetResult",
                ResultKey = "budget",
                Params = new() { ["projectId"] = "abc" },
            },
            RawDecisionJson = """{"action":"call_tool","tool":"saveBudgetResult"}""",
        };

        var handler = BuildHandler(
            AgentCriticDecision.Approve,
            feedback: "Looks fine — totals and line items match the SoW.",
            tools: [BudgetTool()]);

        var context = BuildContext(
            pipelineId: 501,
            proposal: proposal,
            overrides: new AgentRunOverrides { CriticMode = AgentCriticMode.CriticOnMutating });

        var result = await handler.ExecuteAsync(context);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, handler.LastAppendedStages.Count);
        Assert.Equal(AgentConstants.ToolHandlerName, handler.LastAppendedStages[0].StageHandlerName);
        Assert.Equal(AgentConstants.ThinkHandlerName, handler.LastAppendedStages[1].StageHandlerName);

        var history = (List<AgentConversationTurn>)context.Payload![AgentConstants.ConversationHistory];
        Assert.Single(history);
        Assert.Equal("tool_call", history[0].Type);
        Assert.Equal("saveBudgetResult", history[0].ToolName);
        Assert.False(context.Payload!.ContainsKey(AgentConstants.PendingProposal));
        Assert.False(context.Payload!.ContainsKey(AgentConstants.CriticRejectionCount));
    }

    [Fact]
    public async Task Approve_Done_AppendsResponderAndSetsDirectAnswer()
    {
        var proposal = new AgentThinkDecision
        {
            Action = AgentThinkAction.Done,
            FinalAnswer = "Budget saved successfully.",
            RawDecisionJson = """{"action":"done"}""",
        };

        var handler = BuildHandler(AgentCriticDecision.Approve, feedback: "Final answer accurate.", tools: []);

        var context = BuildContext(
            pipelineId: 502,
            proposal: proposal,
            overrides: new AgentRunOverrides { CriticMode = AgentCriticMode.CriticOnFinal });

        var result = await handler.ExecuteAsync(context);

        Assert.True(result.IsSuccess);
        Assert.Single(handler.LastAppendedStages);
        Assert.Equal(AgentConstants.ResponderHandlerName, handler.LastAppendedStages[0].StageHandlerName);
        Assert.Equal("Budget saved successfully.", context.Payload!["agent:directAnswer"]);
        Assert.True((bool)context.Payload![AgentConstants.ResponderAppended]);
    }

    [Fact]
    public async Task Reject_BelowCap_LoopsBackToThinkWithFeedbackTurn()
    {
        var proposal = new AgentThinkDecision
        {
            Action = AgentThinkAction.CallTool,
            ToolCall = new AgentToolCall
            {
                Tool = "saveBudgetResult",
                ResultKey = "budget",
                Params = new() { ["projectId"] = "abc" },
            },
            RawDecisionJson = """{"action":"call_tool","tool":"saveBudgetResult"}""",
        };

        var handler = BuildHandler(
            AgentCriticDecision.Reject,
            feedback: "Missing lineItems — the SoW has 6 firm rows but the proposal supplied none.",
            concerns: ["lineItems empty", "totals exceed project cap"],
            tools: [BudgetTool()]);

        var context = BuildContext(
            pipelineId: 503,
            proposal: proposal,
            overrides: new AgentRunOverrides
            {
                CriticMode = AgentCriticMode.CriticOnMutating,
                MaxRejectionsPerStep = 2,
            });

        var result = await handler.ExecuteAsync(context);

        Assert.True(result.IsSuccess);
        Assert.Single(handler.LastAppendedStages);
        Assert.Equal(AgentConstants.ThinkHandlerName, handler.LastAppendedStages[0].StageHandlerName);

        var history = (List<AgentConversationTurn>)context.Payload![AgentConstants.ConversationHistory];
        Assert.Single(history);
        Assert.Equal("critic_feedback", history[0].Type);
        Assert.Contains("lineItems empty", history[0].Content);
        Assert.Equal(1, (int)context.Payload![AgentConstants.CriticRejectionCount]);
        Assert.False(context.Payload!.ContainsKey(AgentConstants.PendingProposal));
    }

    [Fact]
    public async Task Reject_AtCap_ApprovesAnywayToAvoidInfiniteLoop()
    {
        var proposal = new AgentThinkDecision
        {
            Action = AgentThinkAction.CallTool,
            ToolCall = new AgentToolCall
            {
                Tool = "saveBudgetResult",
                ResultKey = "budget",
                Params = new() { ["projectId"] = "abc" },
            },
            RawDecisionJson = """{"action":"call_tool","tool":"saveBudgetResult"}""",
        };

        var handler = BuildHandler(
            AgentCriticDecision.Reject,
            feedback: "Still missing lineItems.",
            tools: [BudgetTool()]);

        var context = BuildContext(
            pipelineId: 504,
            proposal: proposal,
            overrides: new AgentRunOverrides
            {
                CriticMode = AgentCriticMode.CriticOnMutating,
                MaxRejectionsPerStep = 1,
            });
        // Simulate we already rejected once — this is now the second rejection (== cap)
        context.Payload![AgentConstants.CriticRejectionCount] = 1;

        var result = await handler.ExecuteAsync(context);

        Assert.True(result.IsSuccess);
        // Approved, so appends [tool, think]
        Assert.Equal(2, handler.LastAppendedStages.Count);
        Assert.Equal(AgentConstants.ToolHandlerName, handler.LastAppendedStages[0].StageHandlerName);

        var history = (List<AgentConversationTurn>)context.Payload![AgentConstants.ConversationHistory];
        Assert.Single(history);
        Assert.Equal("tool_call", history[0].Type);
    }

    [Fact]
    public async Task Critic_Throws_ApprovesByDefaultToKeepLoopProgressing()
    {
        var proposal = new AgentThinkDecision
        {
            Action = AgentThinkAction.Done,
            FinalAnswer = "done",
            RawDecisionJson = """{"action":"done"}""",
        };

        var handler = BuildHandler(
            decision: AgentCriticDecision.Approve,
            feedback: null,
            tools: [],
            throwOnReview: new InvalidOperationException("API credentials missing"));

        var context = BuildContext(
            pipelineId: 505,
            proposal: proposal,
            overrides: new AgentRunOverrides { CriticMode = AgentCriticMode.CriticOnFinal });

        var result = await handler.ExecuteAsync(context);

        Assert.True(result.IsSuccess);
        Assert.Single(handler.LastAppendedStages);
        Assert.Equal(AgentConstants.ResponderHandlerName, handler.LastAppendedStages[0].StageHandlerName);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static AgentToolDefinition BudgetTool() => new()
    {
        Name = "saveBudgetResult",
        Description = "Save the generated budget",
        HttpMethod = "POST",
        UrlTemplate = "/api/projects/{projectId}/budget",
        IsMutating = true,
    };

    private static CapturingCriticHandler BuildHandler(
        AgentCriticDecision decision,
        string? feedback,
        IReadOnlyList<AgentToolDefinition> tools,
        List<string>? concerns = null,
        Exception? throwOnReview = null)
    {
        var verdict = new AgentCriticVerdict
        {
            Decision = decision,
            Feedback = feedback,
            Concerns = concerns,
            Confidence = 0.9m,
        };
        var critic = new StubCritic(verdict, throwOnReview);
        var resolver = new StubResolver(critic);
        var registry = new StaticToolRegistry(tools);
        return new CapturingCriticHandler(resolver, registry, new PipelogiqApiClient("http://localhost:8081", "test"));
    }

    private static StageContext BuildContext(int pipelineId, AgentThinkDecision proposal, AgentRunOverrides overrides) => new()
    {
        PipelineId = pipelineId,
        StageId = pipelineId + 100,
        Payload = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
        {
            [AgentConstants.OriginalMessage] = "Generate budget for project abc",
            [AgentConstants.ConversationHistory] = new List<AgentConversationTurn>(),
            [AgentConstants.PendingProposal] = proposal,
            [AgentConstants.RunOverrides] = overrides,
        }
    };

    private sealed class CapturingCriticHandler(
        IAgentCriticResolver resolver,
        IAgentToolRegistry toolRegistry,
        PipelogiqApiClient apiClient)
        : AgentCriticHandler(resolver, toolRegistry, apiClient)
    {
        public List<StageInfo> LastAppendedStages { get; private set; } = new();

        protected override Task AppendStagesAsync(int pipelineId, IEnumerable<StageInfo> stages)
        {
            LastAppendedStages = stages.ToList();
            return Task.CompletedTask;
        }
    }

    private sealed class StubCritic(AgentCriticVerdict verdict, Exception? throwOnReview) : IAgentCritic
    {
        public Task<AgentCriticVerdict> ReviewAsync(
            string originalMessage,
            IReadOnlyList<AgentConversationTurn> history,
            AgentThinkDecision proposal,
            IReadOnlyList<AgentToolDefinition> tools,
            AgentRunOverrides overrides,
            CancellationToken ct = default)
        {
            if (throwOnReview != null)
                throw throwOnReview;
            return Task.FromResult(verdict);
        }
    }

    private sealed class StubResolver(IAgentCritic critic) : IAgentCriticResolver
    {
        public IAgentCritic Resolve(AgentLlmProvider provider) => critic;
    }

    private sealed class StaticToolRegistry(IReadOnlyList<AgentToolDefinition> tools) : IAgentToolRegistry
    {
        public IReadOnlyList<AgentToolDefinition> GetAll() => tools;

        public AgentToolDefinition? Find(string name) =>
            tools.FirstOrDefault(t => string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase));

        public IAgentToolHandler? FindNativeHandler(string name) => null;
    }
}
