using Microsoft.Extensions.DependencyInjection;
using PipelogiqSDK.Abstractions;
using PipelogiqSDK.Contracts;
using PipelogiqSDK.Execution;
using PipelogiqSDK.StageHelper;

using Xunit;

namespace PipelogiqSDK.Tests.Execution;

public sealed class StageExecutionMetadataTests
{
    [Fact]
    public async Task ExecuteStageHandlerAsync_ExposesOptionalMetadataAndPreservesSensitivity()
    {
        using var cancellation = new CancellationTokenSource();
        var handler = new CapturingHandler();
        var executor = new StageExecutor(new ServiceCollection().BuildServiceProvider());

        var result = await executor.ExecuteStageHandlerAsync(new StageExecutionData
        {
            HandlerInstance = handler,
            StageId = 42,
            PipelineId = 10,
            ExecutionId = "exec-42-4",
            Attempt = 4,
            IdempotencyKey = "claim:42",
            TimeoutSeconds = 120,
            CancellationToken = cancellation.Token,
            Traceparent = "00-0123456789abcdef0123456789abcdef-0123456789abcdef-01",
            Tracestate = "vendor=value",
            ContextItems =
            [
                new ContextItem
                {
                    Key = "accessToken",
                    Value = "\"secret\"",
                    ValueType = typeof(string).AssemblyQualifiedName!,
                    IsSensitive = true
                }
            ]
        });

        var metadata = Assert.IsAssignableFrom<IStageExecutionContext>(handler.Context);
        Assert.Equal("exec-42-4", metadata.ExecutionId);
        Assert.Equal(4, metadata.Attempt);
        Assert.Equal("claim:42", metadata.IdempotencyKey);
        Assert.Equal(120, metadata.TimeoutSeconds);
        Assert.Equal(cancellation.Token, metadata.CancellationToken);
        Assert.Equal("exec-42-4", handler.Context.GetExecutionId());
        Assert.Equal(4, handler.Context.GetAttempt());

        var sensitive = Assert.Single(result.ContextItems!, item => item.Key == "accessToken");
        Assert.True(sensitive.IsSensitive);
    }

    private sealed class CapturingHandler : IStageHandler
    {
        public IStageContext? Context { get; private set; }

        public Task<IStageResult> ExecuteAsync(IStageContext? context = null)
        {
            Context = context;
            return Task.FromResult<IStageResult>(StageResult.Success("ok"));
        }
    }
}
