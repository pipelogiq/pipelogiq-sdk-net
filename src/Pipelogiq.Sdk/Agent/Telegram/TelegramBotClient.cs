using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using PipelogiqSDK.Agent.Configuration;

namespace PipelogiqSDK.Agent.Telegram;

internal sealed class TelegramBotClient(
    IHttpClientFactory httpClientFactory,
    TelegramAgentChannelOptions options,
    ILogger<TelegramBotClient> logger)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _httpClient = httpClientFactory.CreateClient("pipelogiq-telegram");

    public async Task<IReadOnlyList<TelegramUpdate>> GetUpdatesAsync(long? offset, CancellationToken ct)
    {
        var request = new TelegramGetUpdatesRequest
        {
            Offset = offset,
            Timeout = options.TelegramPollTimeoutSeconds,
            AllowedUpdates = ["message"]
        };

        using var response = await _httpClient.PostAsJsonAsync("getUpdates", request, JsonOptions, ct);
        var payload = await ReadPayloadAsync<TelegramApiResponse<List<TelegramUpdate>>>(response, ct);

        if (payload.Ok && payload.Result != null)
            return payload.Result;

        throw new InvalidOperationException($"Telegram getUpdates failed: {payload.Description ?? "Unknown error"}");
    }

    public async Task SendMessageAsync(long chatId, string text, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(text))
            return;

        foreach (var chunk in SplitMessage(text, chunkSize: 3500))
        {
            var request = new TelegramSendMessageRequest
            {
                ChatId = chatId,
                Text = chunk
            };

            using var response = await _httpClient.PostAsJsonAsync("sendMessage", request, JsonOptions, ct);
            var payload = await ReadPayloadAsync<TelegramApiResponse<TelegramSentMessage>>(response, ct);

            if (!payload.Ok)
            {
                logger.LogWarning(
                    "Telegram sendMessage returned non-ok response for chat {ChatId}: {Description}",
                    chatId,
                    payload.Description);
            }
        }
    }

    private static async Task<T> ReadPayloadAsync<T>(HttpResponseMessage response, CancellationToken ct)
    {
        var rawBody = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(
                $"Telegram API request failed with {(int)response.StatusCode} ({response.StatusCode}). Body: {rawBody}",
                null,
                response.StatusCode);
        }

        return JsonSerializer.Deserialize<T>(rawBody, JsonOptions)
               ?? throw new InvalidOperationException("Telegram API returned an empty or invalid response.");
    }

    private static IEnumerable<string> SplitMessage(string text, int chunkSize)
    {
        var normalized = text.Replace("\r\n", "\n");
        var chunks = new List<string>();
        var remaining = normalized.AsSpan();

        while (remaining.Length > chunkSize)
        {
            var chunk = remaining[..chunkSize];
            var splitAt = chunk.LastIndexOf('\n');
            if (splitAt <= 0)
                splitAt = chunkSize;

            chunks.Add(remaining[..splitAt].ToString());
            remaining = remaining[splitAt..].TrimStart('\n');
        }

        if (remaining.Length > 0)
            chunks.Add(remaining.ToString());

        return chunks;
    }
}

internal sealed class TelegramApiResponse<T>
{
    [JsonPropertyName("ok")]
    public bool Ok { get; set; }

    [JsonPropertyName("result")]
    public T? Result { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }
}

internal sealed class TelegramUpdate
{
    [JsonPropertyName("update_id")]
    public long UpdateId { get; set; }

    [JsonPropertyName("message")]
    public TelegramMessage? Message { get; set; }
}

internal sealed class TelegramMessage
{
    [JsonPropertyName("message_id")]
    public long MessageId { get; set; }

    [JsonPropertyName("chat")]
    public TelegramChat? Chat { get; set; }

    [JsonPropertyName("from")]
    public TelegramUser? From { get; set; }

    [JsonPropertyName("text")]
    public string? Text { get; set; }
}

internal sealed class TelegramChat
{
    [JsonPropertyName("id")]
    public long Id { get; set; }

    [JsonPropertyName("type")]
    public string? Type { get; set; }
}

internal sealed class TelegramUser
{
    [JsonPropertyName("id")]
    public long Id { get; set; }

    [JsonPropertyName("is_bot")]
    public bool IsBot { get; set; }

    [JsonPropertyName("username")]
    public string? Username { get; set; }
}

internal sealed class TelegramGetUpdatesRequest
{
    [JsonPropertyName("offset")]
    public long? Offset { get; set; }

    [JsonPropertyName("timeout")]
    public int Timeout { get; set; }

    [JsonPropertyName("allowed_updates")]
    public string[] AllowedUpdates { get; set; } = [];
}

internal sealed class TelegramSendMessageRequest
{
    [JsonPropertyName("chat_id")]
    public long ChatId { get; set; }

    [JsonPropertyName("text")]
    public string Text { get; set; } = string.Empty;
}

internal sealed class TelegramSentMessage
{
    [JsonPropertyName("message_id")]
    public long MessageId { get; set; }
}
