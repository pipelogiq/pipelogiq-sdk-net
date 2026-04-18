using Microsoft.Extensions.DependencyInjection;
using PipelogiqSDK.Abstractions;
using PipelogiqSDK.Agent;
using PipelogiqSDK.Agent.Configuration;
using PipelogiqSDK.Agent.Handlers;
using PipelogiqSDK.Agent.Models;
using PipelogiqSDK.Agent.Services;
using PipelogiqSDK.Contracts;
using PipelogiqSDK.StageHelper;

namespace PipelogiqSDK.Testing;

/// <summary>
/// Test harness for AI agent flows. Runs the ReAct think loop in-process with a
/// scripted LLM planner — no Pipelogiq server or real LLM required.
///
/// Usage:
/// <code>
/// var harness = AgentTestHarness.Create(agent =>
/// {
///     agent.SystemPrompt = "You are an expense assistant";
///     agent.UseReActMode = true;
/// })
/// .WithNativeTool(def, new MockGetEmployeeInfoHandler())
/// .WithScenario(s => s
///     .ThenCallTool("getEmployeeInfo", new { employeeId = "anna" })
///     .ThenFinishWith("Your expense claim has been submitted.")
/// );
///
/// var result = await harness.RunAsync("Lunch with client €100");
///
/// Assert.True(result.IsSuccess);
/// Assert.Contains("expense claim", result.FinalResponse);
/// Assert.Single(result.ToolCallsExecuted.Where(t => t == "getEmployeeInfo"));
/// </code>
/// </summary>
public sealed class AgentTestHarness
{
    private readonly AgentOptions _options;
    private readonly MockLlmPlanner _mockPlanner;
    private readonly Dictionary<string, IAgentToolHandler> _nativeHandlers = new(StringComparer.OrdinalIgnoreCase);
    private IAgentToolPolicy? _toolPolicy;

    private AgentTestHarness(AgentOptions options, MockLlmPlanner planner)
    {
        _options = options;
        _mockPlanner = planner;
    }

    /// <summary>
    /// Creates a new test harness with the given agent configuration.
    /// </summary>
    public static AgentTestHarness Create(Action<AgentOptions>? configure = null)
    {
        var options = new AgentOptions { UseReActMode = true };
        configure?.Invoke(options);
        return new AgentTestHarness(options, new MockLlmPlanner());
    }

    /// <summary>
    /// Registers a native tool handler for use during the test run.
    /// </summary>
    public AgentTestHarness WithNativeTool(AgentToolDefinition definition, IAgentToolHandler handler)
    {
        _options.Tools.Add(definition);
        _options.NativeHandlers[definition.Name] = handler;
        _nativeHandlers[definition.Name] = handler;
        return this;
    }

    /// <summary>
    /// Configures the scripted LLM think-step sequence.
    /// </summary>
    public AgentTestHarness WithScenario(Action<AgentTestScenario> configure)
    {
        configure(new AgentTestScenario(_mockPlanner));
        return this;
    }

    /// <summary>
    /// Overrides the tool policy for this test run.
    /// </summary>
    public AgentTestHarness WithToolPolicy(IAgentToolPolicy policy)
    {
        _toolPolicy = policy;
        return this;
    }

    /// <summary>
    /// Runs the agent with the given user message and returns the result.
    /// Executes the full ReAct loop in-process using the scripted LLM planner.
    /// </summary>
    public async Task<AgentTestResult> RunAsync(
        string userMessage,
        string? sessionId = null,
        Dictionary<string, object>? contextPayload = null,
        CancellationToken ct = default)
    {
        var services = new ServiceCollection();
        services.AddSingleton(_options);
        services.AddSingleton<ILlmPlanner>(_mockPlanner);
        services.AddSingleton<IAgentToolRegistry>(sp =>
        {
            var registry = new AgentToolRegistry(_options);
            foreach (var (name, handler) in _options.NativeHandlers)
                registry.RegisterNativeHandler(name, handler);
            return registry;
        });
        services.AddSingleton<IAgentSessionStore, InMemoryAgentSessionStore>();
        services.AddSingleton<IAgentMemoryStore, InMemoryAgentMemoryStore>();
        services.AddSingleton<IAgentToolPolicy>(_toolPolicy ?? new AllowAllToolPolicy());
        services.AddHttpClient();

        var sp = services.BuildServiceProvider();

        var context = new StageContext
        {
            PipelineId = 1,
            StageId = 1,
            Payload = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
            {
                [AgentConstants.OriginalMessage] = userMessage,
                [AgentConstants.ConversationHistory] = new List<AgentConversationTurn>(),
            },
        };

        if (sessionId != null)
            context.Payload[AgentConstants.SessionId] = sessionId;

        if (contextPayload != null)
            foreach (var (k, v) in contextPayload)
                context.Payload[k] = v;

        var toolCallsExecuted = new List<string>();
        var toolCallResults = new List<AgentToolResult>();

        // Run think steps until Done or no more queued steps
        int maxSteps = _options.MaxThinkSteps;
        bool done = false;

        for (int step = 0; step < maxSteps && !done; step++)
        {
            context.Payload[AgentConstants.ThinkStepCount] = step + 1;

            var history = GetOrCreateHistory(context);
            var decision = await _mockPlanner.ThinkAsync(
                userMessage, history, _options.Tools,
                _options.RequireConfirmationForMutations,
                _options.SystemPrompt, null, ct);

            switch (decision.Action)
            {
                case AgentThinkAction.Done:
                    if (!string.IsNullOrWhiteSpace(decision.FinalAnswer))
                        context.Payload["agent:directAnswer"] = decision.FinalAnswer;
                    done = true;
                    break;

                case AgentThinkAction.CallTool when decision.ToolCall != null:
                    var toolHandler = new AgentToolHandler(
                        sp.GetRequiredService<IAgentToolRegistry>(),
                        _options,
                        sp.GetRequiredService<IHttpClientFactory>(),
                        sp.GetRequiredService<IAgentToolPolicy>());

                    var toolInput = new AgentToolCallInput
                    {
                        ToolName = decision.ToolCall.Tool,
                        Params = decision.ToolCall.Params,
                        ResultKey = decision.ToolCall.ResultKey ?? decision.ToolCall.Tool,
                    };
                    await toolHandler.ExecuteAsync(toolInput, context);
                    toolCallsExecuted.Add(decision.ToolCall.Tool);

                    history.Add(new AgentConversationTurn
                    {
                        Type = "tool_call",
                        ToolName = decision.ToolCall.Tool,
                        Content = System.Text.Json.JsonSerializer.Serialize(decision.ToolCall.Params),
                    });
                    break;

                case AgentThinkAction.NeedConfirmation:
                    // In test context: auto-approve confirmations unless policy denies
                    done = false;
                    break;

                default:
                    done = true;
                    break;
            }
        }

        // Synthesize final response
        var directAnswer = context.TryGetValue<string>("agent:directAnswer");
        string finalResponse;

        if (!string.IsNullOrWhiteSpace(directAnswer))
        {
            finalResponse = directAnswer;
        }
        else
        {
            var toolResults = context.TryGetValue<List<AgentToolResult>>(AgentConstants.ToolResults) ?? [];
            toolCallResults.AddRange(toolResults);
            finalResponse = toolResults.Count > 0
                ? (await _mockPlanner.SynthesizeAsync(userMessage, toolResults, ct)).Text
                : "No response.";
        }

        return new AgentTestResult
        {
            IsSuccess = true,
            FinalResponse = finalResponse,
            ToolCallsExecuted = toolCallsExecuted,
            ContextSnapshot = context.Payload != null
                ? new Dictionary<string, object>(context.Payload)
                : new(),
        };
    }

    private static List<AgentConversationTurn> GetOrCreateHistory(IStageContext context)
    {
        var existing = context.TryGetValue<List<AgentConversationTurn>>(AgentConstants.ConversationHistory);
        if (existing != null) return existing;
        var list = new List<AgentConversationTurn>();
        context.Payload![AgentConstants.ConversationHistory] = list;
        return list;
    }
}

/// <summary>
/// Result of an <see cref="AgentTestHarness.RunAsync"/> call.
/// </summary>
public sealed class AgentTestResult
{
    /// <summary>Whether the agent run completed without an unhandled exception.</summary>
    public bool IsSuccess { get; init; }

    /// <summary>The final response text the agent produced.</summary>
    public string FinalResponse { get; init; } = string.Empty;

    /// <summary>Ordered list of tool names that were called during the run.</summary>
    public IReadOnlyList<string> ToolCallsExecuted { get; init; } = Array.Empty<string>();

    /// <summary>A snapshot of the stage context payload at the end of execution.</summary>
    public IReadOnlyDictionary<string, object> ContextSnapshot { get; init; } =
        new Dictionary<string, object>();

    /// <summary>Gets a context value by key from the final snapshot.</summary>
    public T? GetContextValue<T>(string key) =>
        ContextSnapshot.TryGetValue(key, out var val) && val is T typed ? typed : default;
}
