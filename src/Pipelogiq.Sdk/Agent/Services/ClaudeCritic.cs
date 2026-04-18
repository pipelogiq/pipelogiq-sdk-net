using System.Text;
using System.Text.Json;
using PipelogiqSDK.Agent.Configuration;
using PipelogiqSDK.Agent.Models;

namespace PipelogiqSDK.Agent.Services;

/// <summary>
/// Critic implementation backed by the Anthropic Messages API.
/// Use this when the primary thinker is OpenAI/Ollama and a Claude second opinion is preferred,
/// or when you want Claude-as-critic for consistency with the rest of the agent stack.
/// </summary>
public class ClaudeCritic(IHttpClientFactory httpClientFactory) : IAgentCritic
{
    private const string DefaultModel = "claude-sonnet-4-6";
    private const string DefaultBaseUrl = "https://api.anthropic.com";
    private const int MaxTokens = 1_024;
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private static readonly Dictionary<string, (decimal InputPer1M, decimal OutputPer1M)> AnthropicPricing =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["claude-opus-4-6"]           = (15m,   75m),
            ["claude-opus-4-5"]           = (15m,   75m),
            ["claude-sonnet-4-6"]         = (3m,    15m),
            ["claude-sonnet-4-5"]         = (3m,    15m),
            ["claude-haiku-4-5-20251001"] = (0.80m, 4m),
            ["claude-haiku-4-5"]          = (0.80m, 4m),
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
            throw new InvalidOperationException("AgentOptions.Critic.ApiKey is required for the Claude critic.");

        var systemPrompt = AgentCriticBase.DefaultSystemPrompt + AgentCriticBase.BuildRubricBlock(criticOptions.Rubric);
        var userPrompt = AgentCriticBase.BuildReviewUserMessage(originalMessage, history, proposal, tools);
        var model = string.IsNullOrWhiteSpace(criticOptions.Model) ? DefaultModel : criticOptions.Model!;
        var baseUrl = string.IsNullOrWhiteSpace(criticOptions.ApiBaseUrl) ? DefaultBaseUrl : criticOptions.ApiBaseUrl!;

        var requestBody = new Dictionary<string, object?>
        {
            ["model"] = model,
            ["max_tokens"] = MaxTokens,
            ["system"] = systemPrompt,
            ["messages"] = new object[]
            {
                new { role = "user", content = userPrompt },
            },
            ["temperature"] = 0,
        };

        var http = httpClientFactory.CreateClient("pipelogiq-agent-llm");
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl.TrimEnd('/')}/v1/messages");
        request.Headers.Add("x-api-key", criticOptions.ApiKey);
        request.Headers.Add("anthropic-version", "2023-06-01");
        request.Content = new StringContent(JsonSerializer.Serialize(requestBody, JsonOptions), Encoding.UTF8, "application/json");

        using var response = await http.SendAsync(request, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(
                $"Claude critic request failed ({(int)response.StatusCode} {response.ReasonPhrase}). Body: {Truncate(body, 2_000)}",
                null,
                response.StatusCode);
        }

        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;

        var content = ExtractAnthropicText(root);
        var verdict = AgentCriticBase.ParseVerdict(content);
        verdict.TokenUsage = ExtractUsage(root, model);
        return verdict;
    }

    private static string ExtractAnthropicText(JsonElement root)
    {
        if (!root.TryGetProperty("content", out var contentEl) || contentEl.ValueKind != JsonValueKind.Array)
            return string.Empty;

        var parts = new List<string>();
        foreach (var block in contentEl.EnumerateArray())
        {
            if (!block.TryGetProperty("type", out var typeEl) ||
                !string.Equals(typeEl.GetString(), "text", StringComparison.Ordinal) ||
                !block.TryGetProperty("text", out var textEl))
                continue;

            var text = textEl.GetString();
            if (!string.IsNullOrWhiteSpace(text))
                parts.Add(text);
        }

        return parts.Count == 1 ? parts[0] : string.Join("\n", parts);
    }

    private static AgentLlmUsage? ExtractUsage(JsonElement root, string model)
    {
        if (!root.TryGetProperty("usage", out var usageEl))
            return null;

        var inputTokens = usageEl.TryGetProperty("input_tokens", out var inp) && inp.ValueKind == JsonValueKind.Number ? inp.GetInt32() : 0;
        var outputTokens = usageEl.TryGetProperty("output_tokens", out var outp) && outp.ValueKind == JsonValueKind.Number ? outp.GetInt32() : 0;

        if (!AnthropicPricing.TryGetValue(model, out var pricing))
            pricing = (3m, 15m);

        var cost = Math.Round(
            (inputTokens * pricing.InputPer1M / 1_000_000m) + (outputTokens * pricing.OutputPer1M / 1_000_000m),
            6);

        return new AgentLlmUsage
        {
            Provider = "Anthropic",
            Model = model,
            InputTokens = inputTokens,
            OutputTokens = outputTokens,
            EstimatedCostUsd = cost,
        };
    }

    private static string Truncate(string value, int max)
        => string.IsNullOrEmpty(value) || value.Length <= max ? (value ?? string.Empty) : value.Substring(0, max) + "…";
}
