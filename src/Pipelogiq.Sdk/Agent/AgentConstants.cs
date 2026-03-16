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
    public const string ApprovalDecision = "agent:approved";
    public const string RejectionReason = "agent:rejectionReason";
    public const string ApprovedMutations = "agent:approvedMutations";
    public const string FinalResponse = "agent:finalResponse";
    public const string PendingNotification = "agent:pendingNotification";

    // ReAct loop context keys
    public const string ConversationHistory = "agent:conversationHistory";
    public const string ThinkStepCount = "agent:thinkStepCount";

    // Handler names (used for registration and routing)
    public const string OrchestratorHandlerName = "AgentOrchestratorHandler";
    public const string ToolHandlerName = "AgentToolHandler";
    public const string ConfirmationHandlerName = "AgentConfirmationHandler";
    public const string ResponderHandlerName = "AgentResponderHandler";
    public const string ThinkHandlerName = "AgentThinkHandler";
}
