using System.Text.Json;
using PipelogiqSDK.Abstractions;
using PipelogiqSDK.Contracts;
using PipelogiqSDK.Runner;
using PipelogiqSDK.StageHelper;

using Xunit;

namespace PipelogiqSDK.Tests.Contracts;

public sealed class StageReliabilityContractTests
{
    [Theory]
    [InlineData("TIMEOUT")]
    [InlineData("UPSTREAM_ERROR")]
    [InlineData("RATE_LIMIT_EXCEEDED")]
    public void TransientFactories_ReturnExplicitlyRetryableResults(string errorCode)
    {
        var result = errorCode switch
        {
            StageErrorCodes.Timeout => StageResult.Timeout("timeout"),
            StageErrorCodes.UpstreamError => StageResult.UpstreamError("upstream"),
            StageErrorCodes.RateLimitExceeded => StageResult.RateLimitExceeded("rate limit"),
            _ => throw new InvalidOperationException()
        };

        Assert.Equal(errorCode, result.ErrorCode);
        Assert.True(result.Retryable);
        Assert.IsAssignableFrom<IClassifiedStageResult>(result);
    }

    [Theory]
    [InlineData("BUSINESS_REJECTED")]
    [InlineData("VALIDATION_ERROR")]
    [InlineData("INVALID_STATE")]
    [InlineData("MISSING_REQUIRED_DATA")]
    public void TerminalFactories_ReturnExplicitlyTerminalResults(string errorCode)
    {
        var result = errorCode switch
        {
            StageErrorCodes.BusinessRejected => StageResult.BusinessRejected("rejected"),
            StageErrorCodes.ValidationError => StageResult.ValidationError("invalid"),
            StageErrorCodes.InvalidState => StageResult.InvalidState("state"),
            StageErrorCodes.MissingRequiredData => StageResult.MissingRequiredData("missing"),
            _ => throw new InvalidOperationException()
        };

        Assert.Equal(errorCode, result.ErrorCode);
        Assert.False(result.Retryable);
    }

    [Fact]
    public void LegacyError_RetainsUnclassifiedRetryBehavior()
    {
        var result = StageResult.Error("legacy", "LEGACY_ERROR");

        Assert.Null(result.Retryable);
    }

    [Fact]
    public void Serialize_StageResult_UsesStableReliabilityMetadataNames()
    {
        var payload = PipelineMessageSerializer.Serialize(new StageResultDto
        {
            PipelineId = 10,
            StageId = 42,
            Result = "retry later",
            IsSuccess = false,
            ErrorCode = StageErrorCodes.Timeout,
            Retryable = true,
            ExecutionId = "exec-42-2",
            Attempt = 2,
            IdempotencyKey = "claim:42",
            TimeoutSeconds = 30,
            Traceparent = "00-0123456789abcdef0123456789abcdef-0123456789abcdef-01",
            Tracestate = "vendor=value"
        });

        using var document = JsonDocument.Parse(payload);
        var root = document.RootElement;

        Assert.True(root.GetProperty("retryable").GetBoolean());
        Assert.Equal("exec-42-2", root.GetProperty("executionId").GetString());
        Assert.Equal(2, root.GetProperty("attempt").GetInt32());
        Assert.Equal("claim:42", root.GetProperty("idempotencyKey").GetString());
        Assert.Equal(30, root.GetProperty("timeoutSeconds").GetInt32());
        Assert.True(root.TryGetProperty("traceparent", out _));
        Assert.True(root.TryGetProperty("tracestate", out _));
    }

    [Fact]
    public void Deserialize_StageNext_PreservesExecutionAndSensitiveContextMetadata()
    {
        var dto = PipelineMessageSerializer.Deserialize<StageNextDto>("""
        {
          "pipelineId": 10,
          "stageId": 42,
          "stageHandlerName": "EnsureClaimHandler",
          "executionId": "exec-42-3",
          "attempt": 3,
          "idempotencyKey": "claim:42",
          "timeoutSeconds": 90,
          "traceparent": "00-0123456789abcdef0123456789abcdef-0123456789abcdef-01",
          "tracestate": "vendor=value",
          "contextItems": [
            {
              "key": "accessToken",
              "value": "\"redacted\"",
              "valueType": "System.String",
              "isSensitive": true
            }
          ]
        }
        """);

        Assert.NotNull(dto);
        Assert.Equal("exec-42-3", dto.ExecutionId);
        Assert.Equal(3, dto.Attempt);
        Assert.Equal("claim:42", dto.IdempotencyKey);
        Assert.Equal(90, dto.TimeoutSeconds);
        Assert.True(Assert.Single(dto.ContextItems!).IsSensitive);
    }
}
