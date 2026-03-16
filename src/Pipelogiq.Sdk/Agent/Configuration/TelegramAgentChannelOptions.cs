namespace PipelogiqSDK.Agent.Configuration;

/// <summary>
/// Configuration for the built-in Telegram channel transport.
/// </summary>
public class TelegramAgentChannelOptions
{
    /// <summary>
    /// Telegram bot token used to poll updates and send responses.
    /// </summary>
    public string TelegramBotToken { get; set; } = string.Empty;

    /// <summary>
    /// Long-poll timeout for Telegram getUpdates requests. Must be between 1 and 60 seconds.
    /// </summary>
    public int TelegramPollTimeoutSeconds { get; set; } = 25;

    /// <summary>
    /// Optional allow-list of Telegram chat IDs. Empty means all chats are accepted.
    /// </summary>
    public IReadOnlyList<long> TelegramAllowedChatIds { get; set; } = Array.Empty<long>();
}
