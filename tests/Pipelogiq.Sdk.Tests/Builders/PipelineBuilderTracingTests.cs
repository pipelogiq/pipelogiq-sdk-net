using System.Diagnostics;
using PipelogiqSDK.Builders;
using PipelogiqSDK.Configuration;
using PipelogiqSDK.Contracts;

using Xunit;

namespace PipelogiqSDK.Tests.Builders;

public sealed class PipelineBuilderTracingTests
{
    private const string TraceparentKey = "traceparent";
    private const string TracestateKey = "tracestate";
    private const string LegacyTraceIdKey = "X-Trace-Id";
    private const string LegacyCorrelationIdKey = "X-Correlation-Id";
    private const string LegacyLegacyTraceIdKey = "X-Legacy-Trace-Id";

    [Fact]
    public void Build_WithCurrentActivity_UsesCurrentTraceIdInTraceparent()
    {
        using var current = StartW3cActivity("pipeline.send.current");

        var builder = PipelineBuilder.Create("checkout", CreateOptions())
            .WithAction("fraud-check", "FraudCheckHandler");

        var pipeline = builder.Build();

        var traceparent = GetContextItemValue(pipeline.PipelineContextItems, TraceparentKey);
        Assert.True(ActivityContext.TryParse(traceparent, null, out var parsed));
        Assert.Equal(current.TraceId, parsed.TraceId);
        AssertNoLegacyTraceKeys(pipeline.PipelineContextItems);
    }

    [Fact]
    public void Build_WithoutCurrentActivity_CreatesNewW3cTraceContext()
    {
        var previousActivity = Activity.Current;
        try
        {
            Activity.Current = null;

            var builder = PipelineBuilder.Create("checkout", CreateOptions())
                .WithAction("fraud-check", "FraudCheckHandler");

            var pipeline = builder.Build();
            var traceparent = GetContextItemValue(pipeline.PipelineContextItems, TraceparentKey);

            Assert.True(ActivityContext.TryParse(traceparent, null, out var parsed));
            Assert.NotEqual(default, parsed.TraceId);

            var tracestate = GetOptionalContextItemValue(pipeline.PipelineContextItems, TracestateKey);
            if (!string.IsNullOrWhiteSpace(tracestate))
                Assert.False(string.IsNullOrWhiteSpace(tracestate));

            AssertNoLegacyTraceKeys(pipeline.PipelineContextItems);
        }
        finally
        {
            Activity.Current = previousActivity;
        }
    }

    private static PipelogiqRunnerOptions CreateOptions()
    {
        return new PipelogiqRunnerOptions
        {
            ApiUrl = "http://localhost",
            ApiKey = "test-api-key",
        };
    }

    private static string GetContextItemValue(IEnumerable<ContextItem>? contextItems, string key)
    {
        var item = Assert.Single((contextItems ?? Array.Empty<ContextItem>()).Where(i =>
            string.Equals(i.Key, key, StringComparison.OrdinalIgnoreCase)));
        return item.Value;
    }

    private static string? GetOptionalContextItemValue(IEnumerable<ContextItem>? contextItems, string key)
    {
        return (contextItems ?? Array.Empty<ContextItem>())
            .FirstOrDefault(i => string.Equals(i.Key, key, StringComparison.OrdinalIgnoreCase))
            ?.Value;
    }

    private static void AssertNoLegacyTraceKeys(IEnumerable<ContextItem>? contextItems)
    {
        var keys = (contextItems ?? Array.Empty<ContextItem>()).Select(i => i.Key).ToList();
        Assert.DoesNotContain(keys, key => string.Equals(key, LegacyTraceIdKey, StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(keys, key => string.Equals(key, LegacyCorrelationIdKey, StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(keys, key => string.Equals(key, LegacyLegacyTraceIdKey, StringComparison.OrdinalIgnoreCase));
    }

    private static Activity StartW3cActivity(string name)
    {
        var activity = new Activity(name);
        activity.SetIdFormat(ActivityIdFormat.W3C);
        activity.Start();
        return activity;
    }
}
