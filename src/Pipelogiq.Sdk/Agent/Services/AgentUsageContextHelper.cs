using System.Text.Json;
using System.Text.Json.Serialization;
using PipelogiqSDK.Abstractions;
using PipelogiqSDK.Agent.Models;
using PipelogiqSDK.StageHelper;

namespace PipelogiqSDK.Agent.Services;

internal static class AgentUsageContextHelper
{
    private static readonly JsonSerializerOptions OutputJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static void RecordLlmCall(IStageContext? context, AgentLlmUsage? usage)
    {
        if (context == null)
            return;

        context.Payload ??= new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);

        var summary = GetOrCreateSummary(context);
        summary.TotalCalls += 1;
        summary.InputTokens += usage?.InputTokens ?? 0;
        summary.OutputTokens += usage?.OutputTokens ?? 0;
        summary.CacheReadTokens += usage?.CacheReadTokens ?? 0;
        summary.CacheCreationTokens += usage?.CacheCreationTokens ?? 0;
        summary.EstimatedCostUsd = Math.Round(summary.EstimatedCostUsd + (usage?.EstimatedCostUsd ?? 0m), 6);

        if (!string.IsNullOrWhiteSpace(usage?.Provider) || !string.IsNullOrWhiteSpace(usage?.Model))
        {
            var provider = usage?.Provider?.Trim();
            var model = usage?.Model?.Trim();
            var modelSummary = summary.Models.FirstOrDefault(x =>
                string.Equals(x.Provider, provider, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(x.Model, model, StringComparison.OrdinalIgnoreCase));

            if (modelSummary == null)
            {
                modelSummary = new AgentLlmModelUsageSummary
                {
                    Provider = provider,
                    Model = model,
                };
                summary.Models.Add(modelSummary);
            }

            modelSummary.Calls += 1;
            modelSummary.InputTokens += usage?.InputTokens ?? 0;
            modelSummary.OutputTokens += usage?.OutputTokens ?? 0;
            modelSummary.CacheReadTokens += usage?.CacheReadTokens ?? 0;
            modelSummary.CacheCreationTokens += usage?.CacheCreationTokens ?? 0;
            modelSummary.EstimatedCostUsd = Math.Round(modelSummary.EstimatedCostUsd + (usage?.EstimatedCostUsd ?? 0m), 6);
        }

        summary.Models = summary.Models
            .OrderByDescending(x => x.EstimatedCostUsd)
            .ThenByDescending(x => x.InputTokens + x.OutputTokens)
            .ThenBy(x => x.Provider ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.Model ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            .ToList();

        context.Payload[AgentConstants.SessionLlmCallCount] = summary.TotalCalls;
        context.Payload[AgentConstants.SessionTotalInputTokens] = summary.InputTokens;
        context.Payload[AgentConstants.SessionTotalOutputTokens] = summary.OutputTokens;
        context.Payload[AgentConstants.SessionCacheReadTokens] = summary.CacheReadTokens;
        context.Payload[AgentConstants.SessionCacheCreationTokens] = summary.CacheCreationTokens;
        context.Payload[AgentConstants.SessionEstimatedCostUsd] = summary.EstimatedCostUsd;
        context.Payload[AgentConstants.SessionUsageSummary] = summary;
    }

    public static AgentLlmUsageSummary? GetSummary(IStageContext? context)
    {
        if (context == null)
            return null;

        if (context.TryGetValue<AgentLlmUsageSummary>(AgentConstants.SessionUsageSummary) is { } existing)
            return existing;

        var inputTokens = context.TryGetValue<long>(AgentConstants.SessionTotalInputTokens);
        var outputTokens = context.TryGetValue<long>(AgentConstants.SessionTotalOutputTokens);
        var cacheReadTokens = context.TryGetValue<long>(AgentConstants.SessionCacheReadTokens);
        var cacheCreationTokens = context.TryGetValue<long>(AgentConstants.SessionCacheCreationTokens);
        var estimatedCostUsd = context.TryGetValue<decimal>(AgentConstants.SessionEstimatedCostUsd);
        var totalCalls = context.TryGetValue<int>(AgentConstants.SessionLlmCallCount);

        if (inputTokens == 0 &&
            outputTokens == 0 &&
            cacheReadTokens == 0 &&
            cacheCreationTokens == 0 &&
            estimatedCostUsd == 0m &&
            totalCalls == 0)
        {
            return null;
        }

        return new AgentLlmUsageSummary
        {
            TotalCalls = totalCalls,
            InputTokens = inputTokens,
            OutputTokens = outputTokens,
            CacheReadTokens = cacheReadTokens,
            CacheCreationTokens = cacheCreationTokens,
            EstimatedCostUsd = estimatedCostUsd,
            Models = [],
        };
    }

    public static string BuildStageOutput(
        string raw,
        IStageContext? context,
        AgentLlmUsage? usage = null,
        string? operation = null)
    {
        var output = new AgentStageOutput
        {
            Raw = raw,
            LlmOperation = operation,
            LlmUsage = usage,
            SessionUsage = GetSummary(context),
        };

        return JsonSerializer.Serialize(output, OutputJsonOptions);
    }

    private static AgentLlmUsageSummary GetOrCreateSummary(IStageContext context)
    {
        if (context.TryGetValue<AgentLlmUsageSummary>(AgentConstants.SessionUsageSummary) is { } existing)
            return existing;

        var summary = new AgentLlmUsageSummary
        {
            TotalCalls = context.TryGetValue<int>(AgentConstants.SessionLlmCallCount),
            InputTokens = context.TryGetValue<long>(AgentConstants.SessionTotalInputTokens),
            OutputTokens = context.TryGetValue<long>(AgentConstants.SessionTotalOutputTokens),
            CacheReadTokens = context.TryGetValue<long>(AgentConstants.SessionCacheReadTokens),
            CacheCreationTokens = context.TryGetValue<long>(AgentConstants.SessionCacheCreationTokens),
            EstimatedCostUsd = context.TryGetValue<decimal>(AgentConstants.SessionEstimatedCostUsd),
            Models = [],
        };

        context.Payload![AgentConstants.SessionUsageSummary] = summary;
        return summary;
    }
}
