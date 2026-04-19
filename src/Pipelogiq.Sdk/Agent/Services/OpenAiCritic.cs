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
        verdict.TokenUsage = OpenAiUsageHelper.ExtractUsage(root, model);
        return verdict;
    }

    private static string Truncate(string value, int max)
        => string.IsNullOrEmpty(value) || value.Length <= max ? (value ?? string.Empty) : value.Substring(0, max) + "…";
}
