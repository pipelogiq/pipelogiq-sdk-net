using System.Text.Json;
using PipelogiqSDK.Agent.Models;

namespace PipelogiqSDK.Agent.Services;

/// <summary>
/// Shared helpers for critic implementations: building the review prompt,
/// rendering the conversation history and proposal compactly, and parsing the
/// critic's JSON response into an <see cref="AgentCriticVerdict"/>.
/// </summary>
internal static class AgentCriticBase
{
    private const int MaxHistoryTurnChars = 2_000;
    private const int MaxToolPreviewCount = 24;

    public const string DefaultSystemPrompt = """
        You are a rigorous reviewer of an AI agent's proposed next action.
        You receive: the user's original task, the full conversation history,
        the agent's proposed decision, and the available tools.

        Your job: decide if the proposal is correct, complete, and grounded in the history.
        Default to Approve when the proposal is reasonable — do not nitpick wording.
        Reject only when you can name a concrete defect: missing parameters, contradicted
        by prior tool output, wrong tool for the task, or violating the provided rubric.

        Respond with a single JSON object and no prose outside it:
        {
          "decision": "approve" | "reject",
          "feedback": "<one paragraph, addressed to the agent, explaining the verdict>",
          "concerns": ["<short bullet>", ...],
          "confidence": <number between 0 and 1>
        }
        """;

    public static string BuildRubricBlock(string? rubric)
    {
        if (string.IsNullOrWhiteSpace(rubric))
            return string.Empty;

        return "\n\nDomain rubric — apply strictly when relevant:\n" + rubric.Trim();
    }

    public static string BuildReviewUserMessage(
        string originalMessage,
        IReadOnlyList<AgentConversationTurn> history,
        AgentThinkDecision proposal,
        IReadOnlyList<AgentToolDefinition> tools)
    {
        var sb = new System.Text.StringBuilder();

        sb.AppendLine("# Original task");
        sb.AppendLine(originalMessage?.Trim() ?? "(empty)");

        sb.AppendLine();
        sb.AppendLine("# Available tools");
        var toolPreview = tools
            .Take(MaxToolPreviewCount)
            .Select(t => $"- {t.Name}{(t.IsEffectivelyMutating ? " [mutating]" : string.Empty)}: {t.Description}");
        foreach (var line in toolPreview)
            sb.AppendLine(line);
        if (tools.Count > MaxToolPreviewCount)
            sb.AppendLine($"(… {tools.Count - MaxToolPreviewCount} more tools not shown)");

        sb.AppendLine();
        sb.AppendLine("# Conversation history so far");
        if (history.Count == 0)
        {
            sb.AppendLine("(no prior turns)");
        }
        else
        {
            foreach (var turn in history)
            {
                var content = Shorten(turn.Content, MaxHistoryTurnChars);
                var label = string.IsNullOrWhiteSpace(turn.ToolName)
                    ? turn.Type
                    : $"{turn.Type}:{turn.ToolName}";
                sb.AppendLine($"[{label}] {content}");
            }
        }

        sb.AppendLine();
        sb.AppendLine("# Proposed next action (what you must review)");
        sb.AppendLine($"action: {proposal.Action}");
        if (proposal.ToolCall != null)
        {
            sb.AppendLine($"tool: {proposal.ToolCall.Tool}");
            var paramsJson = JsonSerializer.Serialize(proposal.ToolCall.Params);
            sb.AppendLine("params: " + Shorten(paramsJson, MaxHistoryTurnChars));
        }
        if (!string.IsNullOrWhiteSpace(proposal.Reasoning))
            sb.AppendLine("reasoning: " + Shorten(proposal.Reasoning, 1_000));
        if (!string.IsNullOrWhiteSpace(proposal.FinalAnswer))
            sb.AppendLine("finalAnswer: " + Shorten(proposal.FinalAnswer, 2_000));

        sb.AppendLine();
        sb.AppendLine("Return your JSON verdict now.");
        return sb.ToString();
    }

    public static AgentCriticVerdict ParseVerdict(string rawJson)
    {
        var verdict = new AgentCriticVerdict
        {
            Decision = AgentCriticDecision.Approve,
            RawVerdictJson = rawJson,
        };

        if (string.IsNullOrWhiteSpace(rawJson))
            return verdict;

        var trimmed = ExtractJsonObject(rawJson);
        if (trimmed == null)
            return verdict;

        try
        {
            using var doc = JsonDocument.Parse(trimmed);
            var root = doc.RootElement;

            if (root.TryGetProperty("decision", out var decisionEl) && decisionEl.ValueKind == JsonValueKind.String)
            {
                var decisionText = decisionEl.GetString()?.Trim().ToLowerInvariant();
                verdict.Decision = decisionText switch
                {
                    "reject" or "rejected" or "deny" or "denied" => AgentCriticDecision.Reject,
                    _ => AgentCriticDecision.Approve,
                };
            }

            if (root.TryGetProperty("feedback", out var feedbackEl) && feedbackEl.ValueKind == JsonValueKind.String)
                verdict.Feedback = feedbackEl.GetString();

            if (root.TryGetProperty("concerns", out var concernsEl) && concernsEl.ValueKind == JsonValueKind.Array)
            {
                verdict.Concerns = concernsEl.EnumerateArray()
                    .Where(e => e.ValueKind == JsonValueKind.String)
                    .Select(e => e.GetString() ?? string.Empty)
                    .Where(s => !string.IsNullOrWhiteSpace(s))
                    .ToList();
            }

            if (root.TryGetProperty("confidence", out var confEl))
            {
                if (confEl.ValueKind == JsonValueKind.Number && confEl.TryGetDecimal(out var confDec))
                    verdict.Confidence = Math.Clamp(confDec, 0m, 1m);
                else if (confEl.ValueKind == JsonValueKind.String && decimal.TryParse(confEl.GetString(), out var confParsed))
                    verdict.Confidence = Math.Clamp(confParsed, 0m, 1m);
            }
        }
        catch (JsonException)
        {
            // Malformed verdict — default to Approve to keep the loop progressing.
        }

        return verdict;
    }

    private static string? ExtractJsonObject(string raw)
    {
        var openIdx = raw.IndexOf('{');
        var closeIdx = raw.LastIndexOf('}');
        if (openIdx < 0 || closeIdx <= openIdx)
            return null;
        return raw.Substring(openIdx, closeIdx - openIdx + 1);
    }

    private static string Shorten(string? value, int max)
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;
        return value.Length <= max ? value : value.Substring(0, max) + "…";
    }
}
