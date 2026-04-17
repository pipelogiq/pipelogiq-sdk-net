namespace PipelogiqSDK.Agent.Configuration;

/// <summary>
/// Controls when the second-model critic is invoked during the ReAct loop.
/// The critic reviews the think handler's proposed action and can either approve
/// it (letting the tool call proceed) or reject it with structured feedback
/// (forcing a new think step with the feedback appended to history).
/// </summary>
public enum AgentCriticMode
{
    /// <summary>Critic disabled. Default behaviour — single-model ReAct loop.</summary>
    Off = 0,

    /// <summary>Critic fires only on the final Done decision before the responder stage.</summary>
    CriticOnFinal = 1,

    /// <summary>
    /// Critic fires before any tool marked IsMutating=true (or IsEffectivelyMutating),
    /// and also before the final Done decision. Read-only tool calls skip the critic.
    /// Recommended default for production use cases with a clear terminal action.
    /// </summary>
    CriticOnMutating = 2,

    /// <summary>
    /// Critic fires on every think decision — maximum rigor, maximum latency.
    /// Use sparingly; pairs well with an aggressive rejection cap.
    /// </summary>
    CriticOnEveryStep = 3,
}
