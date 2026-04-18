namespace PipelogiqSDK.Agent;

internal static class AgentConstants
{
    // Context keys
    public const string SessionId = "agent:sessionId";
    public const string ReplyTarget = "agent:replyTarget";
    public const string UserId = "agent:userId";
    public const string OriginalMessage = "agent:originalMessage";
    public const string ToolResults = "agent:toolResults";
    public const string ResponderStageId = "agent:responderStageId";
    public const string ResponderAppended = "agent:responderAppended";
    public const string AppendedStageIds = "pipelogiq:appendedStageIds";
    public const string ApprovalDecision = "agent:approved";
    public const string RejectionReason = "agent:rejectionReason";
    public const string ApprovedMutations = "agent:approvedMutations";
    public const string FinalResponse = "agent:finalResponse";
    public const string PendingNotification = "agent:pendingNotification";
    public const string Attachments = "agent:attachments";

    // ReAct loop context keys
    public const string ConversationHistory = "agent:conversationHistory";
    public const string ThinkStepCount = "agent:thinkStepCount";

    // Critic loop context keys
    public const string CriticMode = "agent:criticMode";
    public const string PendingProposal = "agent:pendingProposal";
    public const string CriticRejectionCount = "agent:criticRejectionCount";
    public const string LastCriticFeedback = "agent:lastCriticFeedback";

    // Loop detection: tracks consecutive failures per tool name.
    // Key format: "agent:toolFailures:{toolName}"
    public const string ToolFailureCountPrefix = "agent:toolFailures:";

    // Token budget / cost tracking context keys
    public const string SessionTotalInputTokens = "agent:session:inputTokens";
    public const string SessionTotalOutputTokens = "agent:session:outputTokens";
    public const string SessionCacheReadTokens = "agent:session:cacheReadTokens";
    public const string SessionCacheCreationTokens = "agent:session:cacheCreationTokens";
    public const string SessionEstimatedCostUsd = "agent:session:estimatedCostUsd";

    // Handler names (used for registration and routing)
    public const string OrchestratorHandlerName = "AgentOrchestratorHandler";
    public const string ToolHandlerName = "AgentToolHandler";
    public const string ConfirmationHandlerName = "AgentConfirmationHandler";
    public const string ResponderHandlerName = "AgentResponderHandler";
    public const string ThinkHandlerName = "AgentThinkHandler";
    public const string CriticHandlerName = "AgentCriticHandler";
}
