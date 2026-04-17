using PipelogiqSDK.Agent.Handlers;
using PipelogiqSDK.Runner;

namespace PipelogiqSDK.Agent.Extensions;

/// <summary>
/// Extensions for registering built-in agent handlers in the PipelineRunner.
/// </summary>
public static class AgentRunnerExtensions
{
    /// <summary>
    /// Registers all built-in AI agent handlers with the PipelineRunner.
    /// Call this after all regular RegisterHandler() calls.
    /// </summary>
    public static PipelineRunner RegisterAgentHandlers(this PipelineRunner runner)
    {
        // Typed handlers (IStageHandler<TInput>) must be registered by Type, not generic constraint
        runner.RegisterHandler(AgentConstants.OrchestratorHandlerName, typeof(AgentOrchestratorHandler));
        runner.RegisterHandler(AgentConstants.ToolHandlerName, typeof(AgentToolHandler));
        runner.RegisterHandler(AgentConstants.ConfirmationHandlerName, typeof(AgentConfirmationHandler));
        // AgentResponderHandler and AgentThinkHandler implement IStageHandler (no input)
        runner.RegisterHandler<AgentResponderHandler>(AgentConstants.ResponderHandlerName);
        runner.RegisterHandler<AgentThinkHandler>(AgentConstants.ThinkHandlerName);
        runner.RegisterHandler<AgentCriticHandler>(AgentConstants.CriticHandlerName);
        return runner;
    }
}
