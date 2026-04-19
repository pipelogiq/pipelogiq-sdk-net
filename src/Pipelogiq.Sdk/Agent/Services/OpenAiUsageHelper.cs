using System.Text.Json;
using PipelogiqSDK.Agent.Models;

namespace PipelogiqSDK.Agent.Services;

internal static class OpenAiUsageHelper
{
    // OpenAI pricing per 1M tokens (USD) as of 2025-Q1. Missing models fall back to gpt-4.1 pricing.
    private static readonly Dictionary<string, (decimal InputPer1M, decimal OutputPer1M)> OpenAiPricing =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["gpt-4.1"]      = (2m, 8m),
            ["gpt-4.1-mini"] = (0.40m, 1.60m),
            ["gpt-4o"]       = (2.50m, 10m),
            ["gpt-4o-mini"]  = (0.15m, 0.60m),
            ["gpt-5"]        = (5m, 20m),
            ["gpt-5-mini"]   = (1m, 4m),
            ["o3"]           = (2m, 8m),
            ["o3-mini"]      = (1.10m, 4.40m),
            ["o4-mini"]      = (1.10m, 4.40m),
        };

    public static AgentLlmUsage? ExtractUsage(JsonElement root, string model)
    {
        if (!root.TryGetProperty("usage", out var usageEl))
            return null;

        var inputTokens = usageEl.TryGetProperty("prompt_tokens", out var p) && p.ValueKind == JsonValueKind.Number ? p.GetInt32() : 0;
        var outputTokens = usageEl.TryGetProperty("completion_tokens", out var c) && c.ValueKind == JsonValueKind.Number ? c.GetInt32() : 0;

        if (!OpenAiPricing.TryGetValue(model, out var pricing))
            pricing = (2m, 8m);

        var cost = Math.Round(
            (inputTokens * pricing.InputPer1M / 1_000_000m) +
            (outputTokens * pricing.OutputPer1M / 1_000_000m),
            6);

        return new AgentLlmUsage
        {
            Provider = "OpenAI",
            Model = model,
            InputTokens = inputTokens,
            OutputTokens = outputTokens,
            EstimatedCostUsd = cost,
        };
    }
}
