using PipelogiqSDK.Agent.Configuration;

namespace Pipelogiq.Sdk.Examples.WorkerAndPipelineHost.Configuration;

internal static class ExampleSettingsLoader
{
    public static ExampleSettings Load(string[] args)
    {
        var mode = ParseMode(args);
        var apiKey = Environment.GetEnvironmentVariable("PIPELOGIQ_API_KEY");
        var llmProvider = ParseLlmProvider(Environment.GetEnvironmentVariable("PIPELOGIQ_AGENT_LLM_PROVIDER"));
        var primaryApiKey = ResolvePrimaryLlmApiKey(llmProvider);

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new InvalidOperationException(
                "Set PIPELOGIQ_API_KEY before running this example. Supported modes: worker, pipeline.");
        }

        var settings = new ExampleSettings
        {
            ApiKey = apiKey,
            ApiUrl = Environment.GetEnvironmentVariable("PIPELOGIQ_API_URL") ?? "http://localhost:8081",
            WorkerName = Environment.GetEnvironmentVariable("PIPELOGIQ_WORKER_NAME") ?? "checkout-worker-example",
            PipelineName = Environment.GetEnvironmentVariable("PIPELOGIQ_PIPELINE_NAME") ?? "checkout-demo",
            Mode = mode,
            TelegramBotToken = Environment.GetEnvironmentVariable("PIPELOGIQ_TELEGRAM_BOT_TOKEN"),
            TelegramPollTimeoutSeconds = ParseInt(
                Environment.GetEnvironmentVariable("PIPELOGIQ_TELEGRAM_POLL_TIMEOUT_SECONDS"),
                fallback: 25,
                min: 1,
                max: 60,
                variableName: "PIPELOGIQ_TELEGRAM_POLL_TIMEOUT_SECONDS"),
            TelegramAllowedChatIds = ParseLongList(Environment.GetEnvironmentVariable("PIPELOGIQ_TELEGRAM_ALLOWED_CHAT_IDS")),
            AgentLlmProvider = llmProvider,
            AgentLlmApiKey = primaryApiKey,
            AgentLlmModel = Environment.GetEnvironmentVariable("PIPELOGIQ_AGENT_LLM_MODEL")
                            ?? GetDefaultLlmModel(llmProvider),
            AgentLlmApiBaseUrl = Environment.GetEnvironmentVariable("PIPELOGIQ_AGENT_LLM_API_BASE_URL")
                                 ?? GetDefaultLlmApiBaseUrl(llmProvider),
            AgentStepRouter = BuildStepRouterFromEnvironment(),
            AgentProviders = new AgentProviderCatalog
            {
                Anthropic = new AgentProviderConnection
                {
                    ApiKey = Environment.GetEnvironmentVariable("PIPELOGIQ_AGENT_ANTHROPIC_API_KEY")
                             ?? Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY"),
                    ApiBaseUrl = Environment.GetEnvironmentVariable("PIPELOGIQ_AGENT_ANTHROPIC_API_BASE_URL")
                                 ?? GetDefaultLlmApiBaseUrl(AgentLlmProvider.Anthropic),
                },
                OpenAI = new AgentProviderConnection
                {
                    ApiKey = Environment.GetEnvironmentVariable("PIPELOGIQ_AGENT_OPENAI_API_KEY")
                             ?? Environment.GetEnvironmentVariable("OPENAI_API_KEY"),
                    ApiBaseUrl = Environment.GetEnvironmentVariable("PIPELOGIQ_AGENT_OPENAI_API_BASE_URL")
                                 ?? GetDefaultLlmApiBaseUrl(AgentLlmProvider.OpenAI),
                },
                Ollama = new AgentProviderConnection
                {
                    ApiBaseUrl = Environment.GetEnvironmentVariable("PIPELOGIQ_AGENT_OLLAMA_API_BASE_URL")
                                 ?? GetDefaultLlmApiBaseUrl(AgentLlmProvider.Ollama),
                },
            },
            AgentAnthropicMaxTokens = ParseInt(
                Environment.GetEnvironmentVariable("PIPELOGIQ_AGENT_ANTHROPIC_MAX_TOKENS"),
                fallback: 4096,
                min: 1,
                max: 128000,
                variableName: "PIPELOGIQ_AGENT_ANTHROPIC_MAX_TOKENS"),
            AgentOllamaContextWindow = ParseNullableInt(
                Environment.GetEnvironmentVariable("PIPELOGIQ_AGENT_OLLAMA_CONTEXT_WINDOW"),
                min: 1,
                max: 1048576,
                variableName: "PIPELOGIQ_AGENT_OLLAMA_CONTEXT_WINDOW"),
            AgentOllamaMaxOutputTokens = ParseNullableInt(
                Environment.GetEnvironmentVariable("PIPELOGIQ_AGENT_OLLAMA_MAX_OUTPUT_TOKENS"),
                min: -1,
                max: 1048576,
                variableName: "PIPELOGIQ_AGENT_OLLAMA_MAX_OUTPUT_TOKENS"),
            AgentUseReActMode = ParseBool(Environment.GetEnvironmentVariable("PIPELOGIQ_AGENT_USE_REACT_MODE"), fallback: true),
            AgentRequireConfirmationForMutations = ParseBool(Environment.GetEnvironmentVariable("PIPELOGIQ_AGENT_REQUIRE_CONFIRMATION"), fallback: false),
            AgentSystemPrompt = Environment.GetEnvironmentVariable("PIPELOGIQ_AGENT_SYSTEM_PROMPT"),
            AgentTargetApiBaseUrl = Environment.GetEnvironmentVariable("PIPELOGIQ_AGENT_TARGET_API_BASE_URL"),
            AgentTargetApiBearerToken = Environment.GetEnvironmentVariable("PIPELOGIQ_AGENT_TARGET_API_BEARER_TOKEN"),
        };

        if (settings.TelegramChannelEnabled &&
            settings.AgentLlmProvider != AgentLlmProvider.Ollama &&
            string.IsNullOrWhiteSpace(settings.AgentLlmApiKey))
        {
            throw new InvalidOperationException(
                "Telegram channel is enabled. Set PIPELOGIQ_AGENT_LLM_API_KEY, ANTHROPIC_API_KEY, OPENAI_API_KEY, or switch PIPELOGIQ_AGENT_LLM_PROVIDER=Ollama.");
        }

        return settings;
    }

    private static ExampleMode ParseMode(string[] args)
    {
        if (args.Length == 0)
            return ExampleMode.Worker;

        var value = args[0].Trim().ToLowerInvariant();

        return value switch
        {
            "worker" or "--worker" => ExampleMode.Worker,
            "pipeline" or "submit" or "--pipeline" => ExampleMode.Pipeline,
            _ => throw new InvalidOperationException(
                $"Unknown mode '{args[0]}'. Use 'worker' or 'pipeline'.")
        };
    }

    private static int ParseInt(string? value, int fallback, int min, int max, string variableName)
    {
        if (string.IsNullOrWhiteSpace(value))
            return fallback;

        if (!int.TryParse(value, out var parsed))
            throw new InvalidOperationException($"{variableName} must be an integer.");

        if (parsed < min || parsed > max)
            throw new InvalidOperationException($"{variableName} must be between {min} and {max}.");

        return parsed;
    }

    private static bool ParseBool(string? value, bool fallback)
    {
        if (string.IsNullOrWhiteSpace(value))
            return fallback;

        if (bool.TryParse(value, out var parsed))
            return parsed;

        return value.Trim() switch
        {
            "1" => true,
            "0" => false,
            _ => fallback
        };
    }

    private static int? ParseNullableInt(string? value, int min, int max, string variableName)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        if (!int.TryParse(value, out var parsed))
            throw new InvalidOperationException($"{variableName} must be an integer.");

        if (parsed < min || parsed > max)
            throw new InvalidOperationException($"{variableName} must be between {min} and {max}.");

        return parsed;
    }

    private static IReadOnlyList<long> ParseLongList(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return Array.Empty<long>();

        var tokens = value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var result = new List<long>(tokens.Length);

        foreach (var token in tokens)
        {
            if (!long.TryParse(token, out var parsed))
                throw new InvalidOperationException("PIPELOGIQ_TELEGRAM_ALLOWED_CHAT_IDS must contain numeric chat IDs.");

            result.Add(parsed);
        }

        return result;
    }

    private static AgentLlmProvider ParseLlmProvider(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return AgentLlmProvider.Anthropic;

        return value.Trim().ToLowerInvariant() switch
        {
            "anthropic" or "claude" => AgentLlmProvider.Anthropic,
            "openai" or "gpt" => AgentLlmProvider.OpenAI,
            "ollama" => AgentLlmProvider.Ollama,
            _ => throw new InvalidOperationException(
                $"Unsupported PIPELOGIQ_AGENT_LLM_PROVIDER value '{value}'. Use 'Anthropic', 'OpenAI', or 'Ollama'.")
        };
    }

    private static string GetDefaultLlmModel(AgentLlmProvider provider)
    {
        return provider switch
        {
            AgentLlmProvider.Ollama => "gemma3",
            AgentLlmProvider.OpenAI => "gpt-4.1-mini",
            _ => "claude-opus-4-6",
        };
    }

    private static string GetDefaultLlmApiBaseUrl(AgentLlmProvider provider)
    {
        return provider switch
        {
            AgentLlmProvider.Ollama => "http://localhost:11434",
            AgentLlmProvider.OpenAI => "https://api.openai.com",
            _ => "https://api.anthropic.com",
        };
    }

    private static string? ResolvePrimaryLlmApiKey(AgentLlmProvider provider)
    {
        var generic = Environment.GetEnvironmentVariable("PIPELOGIQ_AGENT_LLM_API_KEY");
        if (!string.IsNullOrWhiteSpace(generic))
            return generic;

        return provider switch
        {
            AgentLlmProvider.OpenAI => Environment.GetEnvironmentVariable("OPENAI_API_KEY"),
            AgentLlmProvider.Anthropic => Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY"),
            _ => null,
        };
    }

    private static AgentLlmStepRouter? BuildStepRouterFromEnvironment()
    {
        var router = new AgentLlmStepRouter
        {
            Plan = BuildStepRoute("PLAN"),
            Think = BuildStepRoute("THINK"),
            Synthesize = BuildStepRoute("SYNTHESIZE"),
            Critic = BuildStepRoute("CRITIC"),
        };

        return router.Plan == null &&
               router.Think == null &&
               router.Synthesize == null &&
               router.Critic == null
            ? null
            : router;
    }

    private static AgentLlmStepRoute? BuildStepRoute(string step)
    {
        var providerValue = Environment.GetEnvironmentVariable($"PIPELOGIQ_AGENT_{step}_PROVIDER");
        var model = Environment.GetEnvironmentVariable($"PIPELOGIQ_AGENT_{step}_MODEL");

        if (string.IsNullOrWhiteSpace(providerValue) && string.IsNullOrWhiteSpace(model))
            return null;

        return new AgentLlmStepRoute
        {
            Provider = string.IsNullOrWhiteSpace(providerValue) ? null : ParseLlmProvider(providerValue),
            Model = string.IsNullOrWhiteSpace(model) ? null : model,
        };
    }
}
