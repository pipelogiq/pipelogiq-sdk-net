using Microsoft.Extensions.DependencyInjection;
using PipelogiqSDK.Agent.Configuration;
using PipelogiqSDK.Agent.Services;
using PipelogiqSDK.Agent.Telegram;

namespace PipelogiqSDK.Agent.Extensions;

/// <summary>
/// Service collection extensions for the built-in Telegram channel transport.
/// </summary>
public static class TelegramAgentChannelServiceCollectionExtensions
{
    /// <summary>
    /// Registers the Telegram channel using a bot token and optional transport configuration callback.
    /// Call <see cref="AgentServiceExtensions.AddPipelogiqAgent"/> separately to configure the AI agent itself.
    /// </summary>
    /// <param name="services">Service collection.</param>
    /// <param name="telegramBotToken">Telegram bot token.</param>
    /// <param name="configure">Optional Telegram/agent configuration callback.</param>
    /// <returns>Updated service collection.</returns>
    public static IServiceCollection AddTelegramAgentChannel(
        this IServiceCollection services,
        string telegramBotToken,
        Action<TelegramAgentChannelOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        if (string.IsNullOrWhiteSpace(telegramBotToken))
            throw new InvalidOperationException("Telegram bot token is required.");

        var options = new TelegramAgentChannelOptions
        {
            TelegramBotToken = telegramBotToken.Trim(),
        };

        configure?.Invoke(options);

        return services.AddTelegramAgentChannel(options);
    }

    /// <summary>
    /// Registers the Telegram channel transport using a fully constructed options object.
    /// Call <see cref="AgentServiceExtensions.AddPipelogiqAgent"/> separately to configure the AI agent itself.
    /// </summary>
    /// <param name="services">Service collection.</param>
    /// <param name="options">Telegram/agent channel options.</param>
    /// <returns>Updated service collection.</returns>
    public static IServiceCollection AddTelegramAgentChannel(
        this IServiceCollection services,
        TelegramAgentChannelOptions options)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(options);

        if (!services.Any(descriptor => descriptor.ServiceType == typeof(AgentOptions)))
        {
            throw new InvalidOperationException(
                "AddPipelogiqAgent(...) must be called before AddTelegramAgentChannel(...).");
        }

        NormalizeAndValidate(options);

        var registeredOptions = CloneOptions(options);
        services.AddSingleton(registeredOptions);
        services.AddHttpClient("pipelogiq-telegram", client =>
        {
            client.BaseAddress = new Uri($"https://api.telegram.org/bot{registeredOptions.TelegramBotToken}/");
        });

        services.AddSingleton<TelegramBotClient>();
        services.AddSingleton<IAgentNotificationChannel, TelegramAgentNotificationChannel>();
        services.AddHostedService<TelegramAgentChannelHostedService>();

        return services;
    }

    private static void NormalizeAndValidate(TelegramAgentChannelOptions options)
    {
        options.TelegramBotToken = options.TelegramBotToken?.Trim() ?? string.Empty;
        options.TelegramAllowedChatIds ??= Array.Empty<long>();

        if (string.IsNullOrWhiteSpace(options.TelegramBotToken))
            throw new InvalidOperationException("Telegram bot token is required.");

        if (options.TelegramPollTimeoutSeconds is < 1 or > 60)
            throw new InvalidOperationException("TelegramPollTimeoutSeconds must be between 1 and 60.");
    }

    private static TelegramAgentChannelOptions CloneOptions(TelegramAgentChannelOptions options)
    {
        return new TelegramAgentChannelOptions
        {
            TelegramBotToken = options.TelegramBotToken,
            TelegramPollTimeoutSeconds = options.TelegramPollTimeoutSeconds,
            TelegramAllowedChatIds = options.TelegramAllowedChatIds.ToArray(),
            VoiceTranscriber = options.VoiceTranscriber,
            MaxFileSizeBytes = options.MaxFileSizeBytes,
        };
    }
}
