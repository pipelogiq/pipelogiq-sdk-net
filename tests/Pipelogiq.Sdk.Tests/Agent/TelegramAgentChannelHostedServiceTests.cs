using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using PipelogiqSDK.Agent.Configuration;
using PipelogiqSDK.Agent.Telegram;
using PipelogiqSDK.Api;
using PipelogiqSDK.Configuration;

using Xunit;

namespace PipelogiqSDK.Tests.Agent;

/// <summary>
/// Covers Telegram channel update handling and command failure behavior.
/// </summary>
public sealed class TelegramAgentChannelHostedServiceTests
{
    /// <summary>
    /// Conflicting stage approvals should reply with a non-fatal conflict message.
    /// </summary>
    [Fact]
    public async Task HandleUpdateSafelyAsync_ApproveConflict_SendsConflictMessage()
    {
        var telegramHandler = new RecordingTelegramHandler();
        var service = CreateService(
            telegramHandler,
            new PipelogiqRunnerOptions
            {
                ApiUrl = "https://api.example.com",
                ApiKey = "sdk-key"
            },
            new StaticResponseHandler(
                HttpStatusCode.Conflict,
                """
                {"type":"https://api.pipelogiq.dev/errors/conflict","title":"Conflict","status":409,"detail":"Stage is not in waiting-for-approval state or decision conflicts with previous resume"}
                """,
                "application/problem+json"));

        await service.HandleUpdateSafelyAsync(
            new TelegramUpdate
            {
                UpdateId = 1001,
                Message = CreateMessage("/approve 42")
            },
            CancellationToken.None);

        Assert.Single(telegramHandler.SentMessages);
        Assert.Contains(
            "Stage 42 is no longer waiting for approval",
            telegramHandler.SentMessages[0],
            StringComparison.Ordinal);
    }

    /// <summary>
    /// Unexpected message processing failures should be contained to the current update.
    /// </summary>
    [Fact]
    public async Task HandleUpdateSafelyAsync_PipelineStartFailure_SendsGenericFailureMessage()
    {
        var telegramHandler = new RecordingTelegramHandler();
        var service = CreateService(
            telegramHandler,
            new PipelogiqRunnerOptions
            {
                ApiUrl = string.Empty,
                ApiKey = "sdk-key"
            },
            new StaticResponseHandler(HttpStatusCode.NoContent));

        await service.HandleUpdateSafelyAsync(
            new TelegramUpdate
            {
                UpdateId = 1002,
                Message = CreateMessage("run the job")
            },
            CancellationToken.None);

        Assert.Single(telegramHandler.SentMessages);
        Assert.Equal(
            "Request failed while processing the message. Check worker logs and try again.",
            telegramHandler.SentMessages[0]);
    }

    private static TelegramAgentChannelHostedService CreateService(
        RecordingTelegramHandler telegramHandler,
        PipelogiqRunnerOptions runnerOptions,
        HttpMessageHandler apiHandler)
    {
        var telegramClient = new TelegramBotClient(
            new StaticHttpClientFactory(new HttpClient(telegramHandler)
            {
                BaseAddress = new Uri("https://api.telegram.org/bot123:abc/")
            }),
            new TelegramAgentChannelOptions
            {
                TelegramBotToken = "123:abc"
            },
            NullLogger<TelegramBotClient>.Instance);

        var apiClient = new PipelogiqApiClient("https://api.example.com", "sdk-key", apiHandler);

        return new TelegramAgentChannelHostedService(
            new TelegramAgentChannelOptions
            {
                TelegramBotToken = "123:abc"
            },
            runnerOptions,
            telegramClient,
            apiClient,
            NullLogger<TelegramAgentChannelHostedService>.Instance);
    }

    private static TelegramMessage CreateMessage(string text)
    {
        return new TelegramMessage
        {
            MessageId = 77,
            Text = text,
            Chat = new TelegramChat
            {
                Id = 123456
            },
            From = new TelegramUser
            {
                Id = 789,
                IsBot = false,
                Username = "tester"
            }
        };
    }

    private sealed class StaticHttpClientFactory(HttpClient httpClient) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => httpClient;
    }

    private sealed class RecordingTelegramHandler : HttpMessageHandler
    {
        public List<string> SentMessages { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var payload = await request.Content!.ReadAsStringAsync(cancellationToken);
            using var document = JsonDocument.Parse(payload);

            if (document.RootElement.TryGetProperty("text", out var text))
                SentMessages.Add(text.GetString() ?? string.Empty);

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """{"ok":true,"result":{"message_id":1}}""",
                    Encoding.UTF8,
                    "application/json")
            };
        }
    }

    private sealed class StaticResponseHandler(
        HttpStatusCode statusCode,
        string? responseBody = null,
        string mediaType = "application/json") : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = new HttpResponseMessage(statusCode);
            if (responseBody != null)
            {
                response.Content = new StringContent(responseBody, Encoding.UTF8, mediaType);
            }

            return Task.FromResult(response);
        }
    }
}
