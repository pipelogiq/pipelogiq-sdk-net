using System.Text.Json;
using PipelogiqSDK.Abstractions;

namespace PipelogiqSDK.Agent.Configuration;

internal sealed class AgentResolvedLlmInvocation
{
    public required AgentLlmProvider Provider { get; init; }
    public required string Model { get; init; }
    public string? ApiKey { get; init; }
    public required string ApiBaseUrl { get; init; }
}

internal static class AgentLlmRuntime
{
    private static readonly AsyncLocal<AgentRunOverrides?> CurrentRunOverrides = new();

    public static IDisposable PushRunOverrides(AgentRunOverrides? overrides)
    {
        var previous = CurrentRunOverrides.Value;
        CurrentRunOverrides.Value = overrides;
        return new Scope(() => CurrentRunOverrides.Value = previous);
    }

    public static AgentRunOverrides? GetRunOverrides(IStageContext? context)
    {
        if (context?.Payload == null ||
            !context.Payload.TryGetValue(global::PipelogiqSDK.Agent.AgentConstants.RunOverrides, out var raw))
        {
            return null;
        }

        return DeserializeRunOverrides(raw);
    }

    public static AgentResolvedLlmInvocation ResolvePrimaryInvocation(
        AgentOptions options,
        AgentLlmStep step)
    {
        var runOverrides = CurrentRunOverrides.Value;
        var route = ResolveStepRoute(runOverrides?.StepRouter, options.StepRouter, step);
        var provider = route?.Provider ?? options.LlmProvider;
        var model = route?.Model
            ?? ResolveLegacyStepModel(options.ModelRouter, step)
            ?? options.LlmModel;

        return new AgentResolvedLlmInvocation
        {
            Provider = provider,
            Model = model,
            ApiKey = ResolveProviderApiKey(options, provider),
            ApiBaseUrl = ResolveProviderApiBaseUrl(options, provider),
        };
    }

    public static AgentCriticOptions ResolveCriticOptions(IStageContext? context, AgentOptions options)
    {
        var runOverrides = GetRunOverrides(context);
        var route = ResolveStepRoute(runOverrides?.StepRouter, options.StepRouter, AgentLlmStep.Critic);
        var provider = route?.Provider ?? options.Critic.Provider;
        var model = route?.Model
            ?? options.ModelRouter?.CriticModel
            ?? options.Critic.Model
            ?? options.LlmModel;

        return new AgentCriticOptions
        {
            Mode = AgentCriticRuntime.ResolveMode(context, options),
            Provider = provider,
            Model = model,
            ApiKey = ResolveCriticApiKey(options, provider),
            ApiBaseUrl = ResolveCriticApiBaseUrl(options, provider),
            Rubric = options.Critic.Rubric,
            MaxRejectionsPerStep = options.Critic.MaxRejectionsPerStep,
        };
    }

    public static bool HasAnyBuiltInProviderConfiguration(AgentOptions options)
    {
        return HasProviderConfiguration(options, AgentLlmProvider.Anthropic) ||
               HasProviderConfiguration(options, AgentLlmProvider.OpenAI) ||
               HasProviderConfiguration(options, AgentLlmProvider.Ollama);
    }

    private static AgentRunOverrides? DeserializeRunOverrides(object raw)
    {
        return raw switch
        {
            AgentRunOverrides typed => typed,
            JsonElement element when element.ValueKind == JsonValueKind.Object =>
                JsonSerializer.Deserialize<AgentRunOverrides>(element.GetRawText()),
            JsonElement element when element.ValueKind == JsonValueKind.String =>
                JsonSerializer.Deserialize<AgentRunOverrides>(element.GetString() ?? string.Empty),
            string json when !string.IsNullOrWhiteSpace(json) =>
                JsonSerializer.Deserialize<AgentRunOverrides>(json),
            _ => null,
        };
    }

    private static AgentLlmStepRoute? ResolveStepRoute(
        AgentLlmStepRouter? runRouter,
        AgentLlmStepRouter? optionsRouter,
        AgentLlmStep step)
    {
        var runRoute = GetRoute(runRouter, step);
        var optionsRoute = GetRoute(optionsRouter, step);

        if (runRoute == null && optionsRoute == null)
            return null;

        return new AgentLlmStepRoute
        {
            Provider = runRoute?.Provider ?? optionsRoute?.Provider,
            Model = runRoute?.Model ?? optionsRoute?.Model,
        };
    }

    private static AgentLlmStepRoute? GetRoute(AgentLlmStepRouter? router, AgentLlmStep step)
    {
        if (router == null)
            return null;

        return step switch
        {
            AgentLlmStep.Plan => router.Plan,
            AgentLlmStep.Think => router.Think,
            AgentLlmStep.Synthesize => router.Synthesize,
            AgentLlmStep.Critic => router.Critic,
            _ => null,
        };
    }

    private static string? ResolveLegacyStepModel(AgentModelRouter? router, AgentLlmStep step)
    {
        if (router == null)
            return null;

        return step switch
        {
            AgentLlmStep.Plan => router.PlanModel,
            AgentLlmStep.Think => router.ThinkModel,
            AgentLlmStep.Synthesize => router.SynthesizeModel,
            AgentLlmStep.Critic => router.CriticModel,
            _ => null,
        };
    }

    private static bool HasProviderConfiguration(AgentOptions options, AgentLlmProvider provider)
    {
        if (provider == options.LlmProvider)
        {
            if (provider == AgentLlmProvider.Ollama)
                return !string.IsNullOrWhiteSpace(options.LlmModel);

            if (!string.IsNullOrWhiteSpace(options.LlmApiKey))
                return true;
        }

        var configured = GetProviderConnection(options, provider);
        return provider switch
        {
            AgentLlmProvider.Ollama => !string.IsNullOrWhiteSpace(configured?.ApiBaseUrl),
            _ => !string.IsNullOrWhiteSpace(configured?.ApiKey),
        };
    }

    private static string? ResolveProviderApiKey(AgentOptions options, AgentLlmProvider provider)
    {
        if (provider == options.LlmProvider && !string.IsNullOrWhiteSpace(options.LlmApiKey))
            return options.LlmApiKey;

        return GetProviderConnection(options, provider)?.ApiKey;
    }

    private static string ResolveProviderApiBaseUrl(AgentOptions options, AgentLlmProvider provider)
    {
        if (provider == options.LlmProvider && !string.IsNullOrWhiteSpace(options.LlmApiBaseUrl))
            return options.LlmApiBaseUrl;

        return GetProviderConnection(options, provider)?.ApiBaseUrl ?? GetDefaultBaseUrl(provider);
    }

    private static string? ResolveCriticApiKey(AgentOptions options, AgentLlmProvider provider)
    {
        if (provider == options.Critic.Provider && !string.IsNullOrWhiteSpace(options.Critic.ApiKey))
            return options.Critic.ApiKey;

        return ResolveProviderApiKey(options, provider);
    }

    private static string ResolveCriticApiBaseUrl(AgentOptions options, AgentLlmProvider provider)
    {
        if (provider == options.Critic.Provider && !string.IsNullOrWhiteSpace(options.Critic.ApiBaseUrl))
            return options.Critic.ApiBaseUrl!;

        return ResolveProviderApiBaseUrl(options, provider);
    }

    private static AgentProviderConnection? GetProviderConnection(AgentOptions options, AgentLlmProvider provider)
    {
        return provider switch
        {
            AgentLlmProvider.Anthropic => options.Providers.Anthropic,
            AgentLlmProvider.OpenAI => options.Providers.OpenAI,
            AgentLlmProvider.Ollama => options.Providers.Ollama,
            _ => null,
        };
    }

    private static string GetDefaultBaseUrl(AgentLlmProvider provider)
    {
        return provider switch
        {
            AgentLlmProvider.OpenAI => "https://api.openai.com",
            AgentLlmProvider.Ollama => "http://localhost:11434",
            _ => "https://api.anthropic.com",
        };
    }

    private sealed class Scope(Action dispose) : IDisposable
    {
        public void Dispose() => dispose();
    }
}
