using System.Text.Json;
using PipelogiqSDK.Contracts;
using PipelogiqSDK.Runner;

using Xunit;

namespace PipelogiqSDK.Tests.Runner;

/// <summary>
/// Covers JSON contracts used for worker queue messages.
/// </summary>
public sealed class PipelineMessageSerializerTests
{
    /// <summary>
    /// Stage results should emit camelCase field names so waiting state survives queue transport.
    /// </summary>
    [Fact]
    public void Serialize_StageResult_UsesCamelCaseWaitingFlag()
    {
        var payload = PipelineMessageSerializer.Serialize(new StageResultDto
        {
            PipelineId = 10,
            StageId = 42,
            Result = "Waiting for user confirmation.",
            IsSuccess = true,
            IsWaitingForApproval = true,
        });

        using var document = JsonDocument.Parse(payload);
        var root = document.RootElement;

        Assert.Equal(42, root.GetProperty("stageId").GetInt32());
        Assert.True(root.GetProperty("isSuccess").GetBoolean());
        Assert.True(root.GetProperty("isWaitingForApproval").GetBoolean());
        Assert.False(root.TryGetProperty("IsWaitingForApproval", out _));
    }

    /// <summary>
    /// Status update payloads should use the same camelCase queue contract.
    /// </summary>
    [Fact]
    public void Serialize_StatusUpdate_UsesCamelCaseFieldNames()
    {
        var payload = PipelineMessageSerializer.Serialize(new
        {
            StageId = 77,
            Status = "Running",
        });

        using var document = JsonDocument.Parse(payload);
        var root = document.RootElement;

        Assert.Equal(77, root.GetProperty("stageId").GetInt32());
        Assert.Equal("Running", root.GetProperty("status").GetString());
        Assert.False(root.TryGetProperty("StageId", out _));
        Assert.False(root.TryGetProperty("Status", out _));
    }
}
