using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using PipelogiqSDK.Agent.Configuration;
using PipelogiqSDK.Agent.Models;

namespace PipelogiqSDK.Agent.Services;

/// <summary>
/// Critic implementation backed by the OpenAI chat completions API.
/// Used as the default second opinion when the primary thinker is Claude — two different
/// vendors reduce the chance of a shared blind spot slipping through.
/// </summary>
public class OpenAiCritic(IHttpClientFactory httpClientFactory) : IAgentCritic
{
    private const string DefaultModel = "gpt-4.1";
    private const string DefaultBaseUrl = "https://api.openai.com";
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    // OpenAI pricing per 1M tokens (USD) as of 2025-Q1. Missing models fall back to gpt-4.1 pricing.
    private static readonly Dictionary<string, (decimal InputPer1M, decimal OutputPer1M)> OpenAiPricing =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["gpt-4.1"]           = (2m,   8m),
            ["gpt-4.1-mini"]      = (0.40m, 1.60m),
            ["gpt-4o"]            = (2.50m, 10m),
            ["gpt-4o-mini"]       = (0.15m, 0.60m),
            ["gpt-5"]             = (5m,   20m),
            ["gpt-5-mini"]        = (1m,   4m),
            ["o3"]                = (2m,   8m),
            ["o3-mini"]           = (1.10m, 4.40m),
            ["o4-mini"]           = (1.10m, 4.40m),
        };

    /// <inheritdoc />
    public async Task<AgentCriticVerdict> ReviewAsync(
        string originalMessage,
        IReadOnlyList<AgentConversationTurn> history,
        AgentThinkDecision proposal,
        IReadOnlyList<AgentToolDefinition> tools,
        AgentCriticOptions criticOptions,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(criticOptions.ApiKey))
            throw new InvalidOperationException("AgentOptions.Critic.ApiKey is required for the OpenAI critic.");

        var systemPrompt = AgentCriticBase.DefaultSystemPrompt + AgentCriticBase.BuildRubricBlock(criticOptions.Rubric);
        var userPrompt = AgentCriticBase.BuildReviewUserMessage(originalMessage, history, proposal, tools);
        var model = string.IsNullOrWhiteSpace(criticOptions.Model) ? DefaultModel : criticOptions.Model!;
        var baseUrl = string.IsNullOrWhiteSpace(criticOptions.ApiBaseUrl) ? DefaultBaseUrl : criticOptions.ApiBaseUrl!;

        var requestBody = new Dictionary<string, object?>
        {
            ["model"] = model,
            ["messages"] = new object[]
            {
                new { role = "system", content = systemPrompt },
                new { role = "user", content = userPrompt },
            },
            ["response_format"] = new { type = "json_object" },
            ["temperature"] = 0,
        };

        var http = httpClientFactory.CreateClient("pipelogiq-agent-llm");
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl.TrimEnd('/')}/v1/chat/completions");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", criticOptions.ApiKey);
        request.Content = new StringContent(JsonSerializer.Serialize(requestBody, JsonOptions), Encoding.UTF8, "application/json");

        using var response = await http.SendAsync(request, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(
                $"OpenAI critic request failed ({(int)response.StatusCode} {response.ReasonPhrase}). Body: {Truncate(body, 2_000)}",
                null,
                response.StatusCode);
        }

        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;

        var content = string.Empty;
        if (root.TryGetProperty("choices", out var choicesEl) &&
            choicesEl.ValueKind == JsonValueKind.Array &&
            choicesEl.GetArrayLength() > 0)
        {
            var first = choicesEl[0];
            if (first.TryGetProperty("message", out var msgEl) &&
                msgEl.TryGetProperty("content", out var contentEl) &&
                contentEl.ValueKind == JsonValueKind.String)
            {
                content = contentEl.GetString() ?? string.Empty;
            }
        }

        var verdict = AgentCriticBase.ParseVerdict(content);
        verdict.TokenUsage = ExtractUsage(root, model);
        return verdict;
    }

    private static AgentLlmUsage? ExtractUsage(JsonElement root, string model)
    {
        if (!root.TryGetProperty("usage", out var usageEl))
            return null;

        var inputTokens = usageEl.TryGetProperty("prompt_tokens", out var p) && p.ValueKind == JsonValueKind.Number ? p.GetInt32() : 0;
        var outputTokens = usageEl.TryGetProperty("completion_tokens", out var c) && c.ValueKind == JsonValueKind.Number ? c.GetInt32() : 0;

        if (!OpenAiPricing.TryGetValue(model, out var pricing))
            pricing = (2m, 8m);

        var cost = Math.Round(
            (inputTokens * pricing.InputPer1M / 1_000_000m) + (outputTokens * pricing.OutputPer1M / 1_000_000m),
            6);

        return new AgentLlmUsage
        {
            InputTokens = inputTokens,
            OutputTokens = outputTokens,
            EstimatedCostUsd = cost,
        };
    }

    private static string Truncate(string value, int max)
        => string.IsNullOrEmpty(value) || value.Length <= max ? (value ?? string.Empty) : value.Substring(0, max) + "…";
}
