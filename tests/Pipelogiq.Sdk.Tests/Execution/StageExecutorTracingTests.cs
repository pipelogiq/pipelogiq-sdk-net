using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using PipelogiqSDK.Abstractions;
using PipelogiqSDK.Contracts;
using PipelogiqSDK.Execution;
using PipelogiqSDK.StageHelper;

using Xunit;

namespace PipelogiqSDK.Tests.Execution;

public sealed class StageExecutorTracingTests
{
    private const string TraceparentKey = "traceparent";

    [Fact]
    public async Task ExecuteStageHandlerAsync_WithIncomingTraceparent_PropagatesSameTraceIdToHandler()
    {
        using var parentActivity = StartW3cActivity("incoming.parent");
        var expectedTraceId = parentActivity.TraceId;
        var incomingTraceparent = parentActivity.Id!;
        parentActivity.Stop();

        var handler = new TraceCapturingHandler();
        var executor = CreateExecutor();
        var stageResult = await executor.ExecuteStageHandlerAsync(new StageExecutionData
        {
            HandlerInstance = handler,
            StageId = 1,
            PipelineId = 10,
            ContextItems = CreateTraceContextItems(incomingTraceparent),
        });

        Assert.Equal(expectedTraceId.ToString(), handler.LastObservedTraceId);

        var outgoingTraceparent = GetContextItemValue(stageResult, TraceparentKey);
        Assert.True(ActivityContext.TryParse(outgoingTraceparent, null, out var outgoingContext));
        Assert.Equal(expectedTraceId, outgoingContext.TraceId);
    }

    [Fact]
    public async Task ExecuteStageHandlerAsync_WithoutIncomingTraceparent_CreatesNewTraceForHandler()
    {
        var previousActivity = Activity.Current;
        try
        {
            Activity.Current = null;

            var handler = new TraceCapturingHandler();
            var executor = CreateExecutor();
            var stageResult = await executor.ExecuteStageHandlerAsync(new StageExecutionData
            {
                HandlerInstance = handler,
                StageId = 2,
                PipelineId = 20,
                ContextItems = new List<ContextItem>(),
            });

            Assert.False(string.IsNullOrWhiteSpace(handler.LastObservedTraceId));

            var outgoingTraceparent = GetContextItemValue(stageResult, TraceparentKey);
            Assert.True(ActivityContext.TryParse(outgoingTraceparent, null, out var outgoingContext));
            Assert.Equal(handler.LastObservedTraceId, outgoingContext.TraceId.ToString());
        }
        finally
        {
            Activity.Current = previousActivity;
        }
    }

    [Fact]
    public async Task ExecuteStageHandlerAsync_SequentialExecutions_DoNotLeakTraceContext()
    {
        var firstHandler = new TraceCapturingHandler();
        var secondHandler = new TraceCapturingHandler();
        var executor = CreateExecutor();

        using var incomingParent = StartW3cActivity("incoming.first");
        var firstTraceId = incomingParent.TraceId.ToString();
        var firstTraceparent = incomingParent.Id!;
        incomingParent.Stop();

        var previousActivity = Activity.Current;

        await executor.ExecuteStageHandlerAsync(new StageExecutionData
        {
            HandlerInstance = firstHandler,
            StageId = 3,
            PipelineId = 30,
            ContextItems = CreateTraceContextItems(firstTraceparent),
        });

        Assert.Same(previousActivity, Activity.Current);
        Assert.Equal(firstTraceId, firstHandler.LastObservedTraceId);

        await executor.ExecuteStageHandlerAsync(new StageExecutionData
        {
            HandlerInstance = secondHandler,
            StageId = 4,
            PipelineId = 30,
            ContextItems = new List<ContextItem>(),
        });

        Assert.Same(previousActivity, Activity.Current);
        Assert.False(string.IsNullOrWhiteSpace(secondHandler.LastObservedTraceId));
        Assert.NotEqual(firstTraceId, secondHandler.LastObservedTraceId);
    }

    private static StageExecutor CreateExecutor()
    {
        var provider = new ServiceCollection().BuildServiceProvider();
        return new StageExecutor(provider);
    }

    private static List<ContextItem> CreateTraceContextItems(string traceparent)
    {
        return new List<ContextItem>
        {
            new()
            {
                Key = TraceparentKey,
                Value = traceparent,
                ValueType = typeof(string).AssemblyQualifiedName ?? typeof(string).FullName!,
            }
        };
    }

    private static string GetContextItemValue(IStageResult result, string key)
    {
        var item = Assert.Single(result.ContextItems!.Where(i =>
            string.Equals(i.Key, key, StringComparison.OrdinalIgnoreCase)));
        return item.Value;
    }

    private static Activity StartW3cActivity(string name)
    {
        var activity = new Activity(name);
        activity.SetIdFormat(ActivityIdFormat.W3C);
        activity.Start();
        return activity;
    }

    private sealed class TraceCapturingHandler : IStageHandler
    {
        public string? LastObservedTraceId { get; private set; }

        public Task<IStageResult> ExecuteAsync(IStageContext? context = null)
        {
            LastObservedTraceId = Activity.Current?.TraceId.ToString();
            return Task.FromResult<IStageResult>(StageResult.Success("ok"));
        }
    }
}
