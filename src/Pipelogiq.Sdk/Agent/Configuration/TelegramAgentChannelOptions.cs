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

    /// <summary>
    /// Optional voice transcription callback. Receives raw audio bytes and MIME type,
    /// returns the transcribed text. If not set, voice messages are forwarded to the agent
    /// as "[Voice message, N sec]" with the audio attachment omitted.
    /// Example (using OpenAI Whisper): options.VoiceTranscriber = async (bytes, mime, ct) => ...
    /// </summary>
    public Func<byte[], string, CancellationToken, Task<string>>? VoiceTranscriber { get; set; }

    /// <summary>
    /// Maximum file size in bytes to download from Telegram. Files larger than this are skipped.
    /// Defaults to 20 MB (Telegram's bot API limit for getFile).
    /// </summary>
    public long MaxFileSizeBytes { get; set; } = 20 * 1024 * 1024;
}
