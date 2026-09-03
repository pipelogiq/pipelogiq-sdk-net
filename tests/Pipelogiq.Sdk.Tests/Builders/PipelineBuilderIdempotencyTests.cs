using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Json;
using PipelogiqSDK.Api;
using PipelogiqSDK.Builders;
using PipelogiqSDK.Configuration;
using PipelogiqSDK.Contracts;

using Xunit;

namespace PipelogiqSDK.Tests.Builders;

public sealed class PipelineBuilderIdempotencyTests
{
    [Fact]
    public void Build_WithIdempotencyAndSensitiveContext_PreservesAdditiveMetadata()
    {
        var pipeline = PipelineBuilder
            .Create("insurance.claim", CreateOptions())
            .WithIdempotencyKey("claim:tenant-7:42")
            .AddContextItem("tenantId", "tenant-7")
            .AddSensitiveContextItem("accessToken", "must-not-be-logged")
            .WithAction("ensure-claim", "EnsureClaimHandler", options: new StageOptions
            {
                MaxRetries = 5,
                RetryInterval = 2,
                RetryOnErrorCodes =
                [
                    StageErrorCodes.Timeout,
                    StageErrorCodes.UpstreamError,
                    StageErrorCodes.RateLimitExceeded
                ],
                Backoff = "exponential",
                MaxRetryInterval = 60,
                Jitter = true
            })
            .Build();

        Assert.Equal("claim:tenant-7:42", pipeline.IdempotencyKey);
        var sensitive = Assert.Single(
            pipeline.PipelineContextItems!,
            item => item.Key == "accessToken");
        Assert.True(sensitive.IsSensitive);
        Assert.False(Assert.Single(
            pipeline.PipelineContextItems!,
            item => item.Key == "tenantId").IsSensitive);

        var options = Assert.Single(pipeline.Stages!).Options!;
        Assert.Equal(3, options.RetryOnErrorCodes?.Count);
        Assert.Equal("exponential", options.Backoff);
        Assert.Equal(60, options.MaxRetryInterval);
        Assert.True(options.Jitter);
    }

    [Fact]
    public async Task SendAsync_WithIdempotencyKey_ChoosesFailSafeEndpoint()
    {
        var handler = new RecordingHandler();
        var apiClient = new PipelogiqApiClient(
            "https://pipelogiq.test",
            "api-key-value",
            handler);
        var builder = PipelineBuilder
            .Create("insurance.claim", CreateOptions())
            .WithIdempotencyKey("claim:tenant-7:43")
            .WithAction("ensure-claim", "EnsureClaimHandler");
        ReplaceApiClient(builder, apiClient);

        var response = await builder.SendAsync();

        Assert.Equal(43, response.Id);
        Assert.Equal("/pipelines/idempotent", handler.PathAndQuery);
        using var body = JsonDocument.Parse(handler.Body);
        Assert.Equal(
            "claim:tenant-7:43",
            body.RootElement.GetProperty("idempotencyKey").GetString());
    }

    [Fact]
    public async Task SendAsync_WithoutIdempotencyKey_PreservesLegacyEndpoint()
    {
        var handler = new RecordingHandler();
        var apiClient = new PipelogiqApiClient(
            "https://pipelogiq.test",
            "api-key-value",
            handler);
        var builder = PipelineBuilder
            .Create("insurance.claim", CreateOptions())
            .WithAction("ensure-claim", "EnsureClaimHandler");
        ReplaceApiClient(builder, apiClient);

        await builder.SendAsync();

        Assert.Equal("/pipelines", handler.PathAndQuery);
    }

    private static PipelogiqRunnerOptions CreateOptions() => new()
    {
        ApiUrl = "https://pipelogiq.test",
        ApiKey = "api-key-value"
    };

    private static void ReplaceApiClient(PipelineBuilder builder, PipelogiqApiClient apiClient)
    {
        var field = typeof(BaseBuilder<PipelineBuilder>).GetField(
            "ApiClient",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(field);
        field.SetValue(builder, apiClient);
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        public string PathAndQuery { get; private set; } = string.Empty;
        public string Body { get; private set; } = string.Empty;

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            PathAndQuery = request.RequestUri?.PathAndQuery ?? string.Empty;
            Body = request.Content == null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken);

            var json = PathAndQuery == "/pipelines/idempotent"
                ? """
                  {
                    "pipeline": {
                      "id": 43,
                      "name": "insurance.claim",
                      "status": "NotStarted",
                      "createdAt": "2026-07-25T10:00:00Z"
                    },
                    "created": true,
                    "wasExisting": false
                  }
                  """
                : """
                  {
                    "id": 44,
                    "name": "insurance.claim",
                    "status": "NotStarted",
                    "createdAt": "2026-07-25T10:00:00Z"
                  }
                  """;

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
        }
    }
}
