using PipelogiqSDK.Agent.Configuration;
using PipelogiqSDK.Configuration;

namespace Pipelogiq.Sdk.Examples.WorkerAndPipelineHost.Configuration;

internal sealed class ExampleSettings
{
    public required string ApiKey { get; init; }
    public string ApiUrl { get; init; } = "http://localhost:8081";
    public string WorkerName { get; init; } = "checkout-worker-example";
    public string PipelineName { get; init; } = "checkout-demo";
    public ExampleMode Mode { get; init; } = ExampleMode.Worker;
    public string? TelegramBotToken { get; init; }
    public int TelegramPollTimeoutSeconds { get; init; } = 25;
    public IReadOnlyList<long> TelegramAllowedChatIds { get; init; } = Array.Empty<long>();
    public AgentLlmProvider AgentLlmProvider { get; init; } = AgentLlmProvider.Anthropic;
    public string? AgentLlmApiKey { get; init; }
    public string AgentLlmModel { get; init; } = "claude-opus-4-6";
    public string AgentLlmApiBaseUrl { get; init; } = "https://api.anthropic.com";
    public AgentLlmStepRouter? AgentStepRouter { get; init; }
    public AgentProviderCatalog AgentProviders { get; init; } = new();
    public int AgentAnthropicMaxTokens { get; init; } = 4096;
    public int? AgentOllamaContextWindow { get; init; }
    public int? AgentOllamaMaxOutputTokens { get; init; }
    public bool AgentUseReActMode { get; init; } = true;
    public bool AgentRequireConfirmationForMutations { get; init; } = false;
    public string? AgentSystemPrompt { get; init; }
    public string? AgentTargetApiBaseUrl { get; init; }
    public string? AgentTargetApiBearerToken { get; init; }

    public bool TelegramChannelEnabled => !string.IsNullOrWhiteSpace(TelegramBotToken);

    public PipelogiqRunnerOptions ToRunnerOptions()
    {
        return new PipelogiqRunnerOptions
        {
            ApiKey = ApiKey,
            ApiUrl = ApiUrl,
            WorkerName = WorkerName,
            Environment = Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT") ?? "Development",
            Metadata = new Dictionary<string, string>
            {
                ["exampleProject"] = "WorkerAndPipelineHost",
                ["exampleMode"] = Mode.ToString().ToLowerInvariant(),
            }
        };
    }

    public TelegramAgentChannelOptions ToTelegramAgentChannelOptions()
    {
        return new TelegramAgentChannelOptions
        {
            TelegramBotToken = TelegramBotToken ?? string.Empty,
            TelegramPollTimeoutSeconds = TelegramPollTimeoutSeconds,
            TelegramAllowedChatIds = TelegramAllowedChatIds,
        };
    }
}
