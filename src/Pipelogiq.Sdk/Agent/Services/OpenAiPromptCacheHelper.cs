using System.Security.Cryptography;
using System.Text;
using PipelogiqSDK.Agent.Models;

namespace PipelogiqSDK.Agent.Services;

internal static class OpenAiPromptCacheHelper
{
    public static string BuildAgentCacheKey(
        string model,
        string system,
        IReadOnlyList<AgentToolDefinition> tools,
        string responseMode)
    {
        var toolSignature = string.Join(
            "|",
            tools
                .OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase)
                .Select(t =>
                {
                    var paramNames = t.Params == null
                        ? string.Empty
                        : string.Join(",", t.Params.Keys.OrderBy(k => k, StringComparer.OrdinalIgnoreCase));
                    return $"{t.Name}:{t.HttpMethod}:{t.UrlTemplate}:{paramNames}";
                }));

        return BuildKey("agent", model, responseMode.ToString(), system, toolSignature);
    }

    public static string BuildCriticCacheKey(
        string model,
        string systemPrompt,
        IReadOnlyList<AgentToolDefinition> tools)
    {
        var toolNames = string.Join(
            "|",
            tools
                .OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase)
                .Select(t => t.Name));

        return BuildKey("critic", model, systemPrompt, toolNames);
    }

    private static string BuildKey(params string?[] parts)
    {
        var normalized = string.Join(
            "\n---\n",
            parts.Where(part => !string.IsNullOrWhiteSpace(part)).Select(part => part!.Trim()));

        var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(normalized));
        return $"pipelogiq:{Convert.ToHexString(hashBytes).ToLowerInvariant()}";
    }
}
