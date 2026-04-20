using System.Text.Json;
using PipelogiqSDK.Agent.Models;

namespace PipelogiqSDK.Agent.Services;

internal static class OpenAiUsageHelper
{
    // OpenAI pricing per 1M text tokens (USD). Missing models fall back conservatively to gpt-4.1 rates.
    private static readonly Dictionary<string, (decimal InputPer1M, decimal CachedInputPer1M, decimal OutputPer1M)> OpenAiPricing =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["gpt-4.1"]      = (2.00m, 0.50m, 8.00m),
            ["gpt-4.1-mini"] = (0.40m, 0.10m, 1.60m),
            ["gpt-4.1-nano"] = (0.10m, 0.025m, 0.40m),
            ["gpt-4o"]       = (2.50m, 1.25m, 10.00m),
            ["gpt-4o-mini"]  = (0.15m, 0.075m, 0.60m),
            ["gpt-5"]        = (1.25m, 0.125m, 10.00m),
            ["gpt-5.1"]      = (1.25m, 0.125m, 10.00m),
            ["gpt-5.2"]      = (1.75m, 0.175m, 14.00m),
            ["gpt-5-mini"]   = (0.25m, 0.025m, 2.00m),
            ["gpt-5-nano"]   = (0.05m, 0.005m, 0.40m),
        };

    public static AgentLlmUsage? ExtractUsage(JsonElement root, string model)
    {
        if (!root.TryGetProperty("usage", out var usageEl))
            return null;

        var inputTokens = usageEl.TryGetProperty("prompt_tokens", out var p) && p.ValueKind == JsonValueKind.Number ? p.GetInt32() : 0;
        var outputTokens = usageEl.TryGetProperty("completion_tokens", out var c) && c.ValueKind == JsonValueKind.Number ? c.GetInt32() : 0;
        var cachedTokens = 0;
        if (usageEl.TryGetProperty("prompt_tokens_details", out var promptDetailsEl) &&
            promptDetailsEl.ValueKind == JsonValueKind.Object &&
            promptDetailsEl.TryGetProperty("cached_tokens", out var cachedTokensEl) &&
            cachedTokensEl.ValueKind == JsonValueKind.Number)
        {
            cachedTokens = cachedTokensEl.GetInt32();
        }

        cachedTokens = Math.Clamp(cachedTokens, 0, inputTokens);

        if (!OpenAiPricing.TryGetValue(model, out var pricing))
            pricing = (2.00m, 0.50m, 8.00m);

        var billableInputTokens = inputTokens - cachedTokens;

        var cost = Math.Round(
            (billableInputTokens * pricing.InputPer1M / 1_000_000m) +
            (cachedTokens * pricing.CachedInputPer1M / 1_000_000m) +
            (outputTokens * pricing.OutputPer1M / 1_000_000m),
            6);

        return new AgentLlmUsage
        {
            Provider = "OpenAI",
            Model = model,
            InputTokens = inputTokens,
            OutputTokens = outputTokens,
            CacheReadTokens = cachedTokens,
            EstimatedCostUsd = cost,
        };
    }
}
