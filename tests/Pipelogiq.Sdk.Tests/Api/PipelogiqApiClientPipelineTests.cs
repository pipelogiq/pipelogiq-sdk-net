using System.Net;
using System.Text;
using System.Text.Json;
using PipelogiqSDK.Api;
using PipelogiqSDK.Contracts;

using Xunit;

namespace PipelogiqSDK.Tests.Api;

public sealed class PipelogiqApiClientPipelineTests
{
    [Fact]
    public async Task PostIdempotentPipelineAsync_UsesFailSafeEndpointAndUnwrapsExistingPipeline()
    {
        var handler = new RecordingHandler(_ => JsonResponse("""
        {
          "pipeline": {
            "id": 271,
            "name": "insurance.claim",
            "status": "Running",
            "createdAt": "2026-07-25T10:00:00Z",
            "idempotencyKey": "claim:tenant-7:42"
          },
          "created": false,
          "wasExisting": true
        }
        """));
        var client = new PipelogiqApiClient("https://pipelogiq.test", "api-key-value", handler);

        var pipeline = await client.PostIdempotentPipelineAsync(new PipelineDto
        {
            ApiKey = "api-key-value",
            Name = "insurance.claim",
            IdempotencyKey = "claim:tenant-7:42",
            Stages =
            [
                new StageInfo
                {
                    StageName = "ensure-claim",
                    StageHandlerName = "EnsureClaimHandler"
                }
            ]
        });

        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("/pipelines/idempotent", request.PathAndQuery);
        Assert.Contains("api-key-value", request.GetHeaderValues("X-API-Key"));

        using var body = JsonDocument.Parse(request.Body);
        Assert.Equal("claim:tenant-7:42", GetProperty(body.RootElement, "idempotencyKey").GetString());
        Assert.False(TryGetProperty(body.RootElement, "apiKey", out _));

        Assert.Equal(271, pipeline.Id);
        Assert.Equal("claim:tenant-7:42", pipeline.IdempotencyKey);
        Assert.True(pipeline.WasExisting);
    }

    [Fact]
    public async Task GetPipelineByIdempotencyKeyAsync_PostsKeyInBodyAndNeverPlacesItInUrl()
    {
        const string idempotencyKey = "claim/tenant 7/order?42";
        var handler = new RecordingHandler(_ => JsonResponse("""
        {
          "id": 272,
          "name": "insurance.claim",
          "status": "Completed",
          "createdAt": "2026-07-25T10:00:00Z",
          "idempotencyKey": "claim/tenant 7/order?42",
          "isTerminal": true
        }
        """));
        var client = new PipelogiqApiClient("https://pipelogiq.test", "api-key-value", handler);

        var pipeline = await client.GetPipelineByIdempotencyKeyAsync(idempotencyKey);

        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("/pipelines/by-idempotency-key", request.PathAndQuery);
        Assert.DoesNotContain("claim", request.PathAndQuery, StringComparison.OrdinalIgnoreCase);
        using var body = JsonDocument.Parse(request.Body);
        Assert.Equal(idempotencyKey, GetProperty(body.RootElement, "idempotencyKey").GetString());
        Assert.Equal(272, pipeline.Id);
        Assert.True(pipeline.IsTerminal);
    }

    [Fact]
    public async Task CancelPipelineAsync_PostsToApplicationScopedCancelEndpoint()
    {
        var handler = new RecordingHandler(_ => JsonResponse("""
        {
          "id": 273,
          "name": "insurance.claim",
          "status": "Cancelled",
          "createdAt": "2026-07-25T10:00:00Z",
          "isTerminal": true
        }
        """));
        var client = new PipelogiqApiClient("https://pipelogiq.test", "api-key-value", handler);

        await client.CancelPipelineAsync(273);

        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("/pipelines/273/cancel", request.PathAndQuery);
        Assert.Equal("{}", request.Body);
    }

    [Fact]
    public async Task GetPipelineAsync_DeserializesRetryAndTerminalStatusMetadata()
    {
        var handler = new RecordingHandler(_ => JsonResponse("""
        {
          "id": 274,
          "name": "insurance.claim",
          "status": "Failed",
          "createdAt": "2026-07-25T10:00:00Z",
          "finishedAt": "2026-07-25T10:05:00Z",
          "idempotencyKey": "claim:274",
          "isTerminal": true,
          "stages": [
            {
              "id": 801,
              "pipelineId": 274,
              "name": "ensure-claim",
              "status": "Failed",
              "createdAt": "2026-07-25T10:00:00Z",
              "startedAt": "2026-07-25T10:04:00Z",
              "finishedAt": "2026-07-25T10:05:00Z",
              "nextRetryAt": "2026-07-25T10:06:00Z",
              "attempt": 3,
              "retryAttempt": 2,
              "lastErrorCode": "BUSINESS_REJECTED",
              "failureDisposition": "terminal",
              "isTerminal": true,
              "failureCount": 1,
              "lastFailedAt": "2026-07-25T10:05:00Z",
              "hasFailureHistory": true,
              "options": {
                "maxRetries": 5,
                "retryOnErrorCodes": ["TIMEOUT", "UPSTREAM_ERROR"],
                "backoff": "exponential",
                "maxRetryInterval": 60,
                "jitter": true
              },
              "logs": [
                {
                  "id": 11,
                  "stageId": 801,
                  "message": "redacted",
                  "logLevel": "error",
                  "created": "2026-07-25T10:05:00Z"
                }
              ]
            }
          ]
        }
        """));
        var client = new PipelogiqApiClient("https://pipelogiq.test", "api-key-value", handler);

        var pipeline = await client.GetPipelineAsync(274);
        var stage = Assert.Single(pipeline.Stages!);

        Assert.True(pipeline.IsTerminal);
        Assert.Equal("claim:274", pipeline.IdempotencyKey);
        Assert.Equal(3, stage.Attempts);
        Assert.Equal(2, stage.RetryAttempt);
        Assert.Equal("BUSINESS_REJECTED", stage.LastErrorCode);
        Assert.Equal("terminal", stage.FailureDisposition);
        Assert.True(stage.IsTerminal);
        Assert.Equal("exponential", stage.Options?.Backoff);
        Assert.Equal(60, stage.Options?.MaxRetryInterval);
        Assert.True(stage.Options?.Jitter);
        Assert.Equal("redacted", Assert.Single(stage.Logs!).Message);
    }

    [Theory]
    [InlineData("Completed", true)]
    [InlineData("Failed", true)]
    [InlineData("Cancelled", true)]
    [InlineData("Canceled", true)]
    [InlineData("Running", false)]
    public void PipelineStatuses_IsTerminal_RecognizesKnownValues(string status, bool expected)
    {
        Assert.Equal(expected, PipelineStatuses.IsTerminal(status));
    }

    private static HttpResponseMessage JsonResponse(string json)
    {
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
    }

    private static JsonElement GetProperty(JsonElement element, string name)
    {
        Assert.True(TryGetProperty(element, name, out var value), $"Property '{name}' was not found.");
        return value;
    }

    private static bool TryGetProperty(JsonElement element, string name, out JsonElement value)
    {
        foreach (var property in element.EnumerateObject())
        {
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }

        value = default;
        return false;
    }

    private sealed class RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFactory)
        : HttpMessageHandler
    {
        public List<RecordedRequest> Requests { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var body = request.Content == null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken);
            Requests.Add(new RecordedRequest(
                request.Method,
                request.RequestUri?.PathAndQuery ?? string.Empty,
                body,
                request.Headers.ToDictionary(
                    header => header.Key,
                    header => header.Value.ToArray(),
                    StringComparer.OrdinalIgnoreCase)));
            return responseFactory(request);
        }
    }

    private sealed record RecordedRequest(
        HttpMethod Method,
        string PathAndQuery,
        string Body,
        IReadOnlyDictionary<string, string[]> Headers)
    {
        public IReadOnlyList<string> GetHeaderValues(string name)
            => Headers.TryGetValue(name, out var values) ? values : [];
    }
}
