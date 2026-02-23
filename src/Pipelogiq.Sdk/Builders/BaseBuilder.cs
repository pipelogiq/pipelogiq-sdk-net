using System.Diagnostics;
using System.Text.Json;
using PipelogiqSDK.Api;
using PipelogiqSDK.Configuration;
using PipelogiqSDK.Contracts;
using PipelogiqSDK.Execution;

namespace PipelogiqSDK.Builders;

public abstract class BaseBuilder<TSelf> where TSelf : BaseBuilder<TSelf>
{
    private const string TraceparentKey = "traceparent";
    private const string TracestateKey = "tracestate";

    protected string? ApiKey;
    protected readonly List<KeywordDto> Keywords = new();
    protected readonly List<ContextItem> ContextItems = new();
    protected PipelogiqApiClient ApiClient;

    protected BaseBuilder(PipelogiqRunnerOptions? options = null)
    {
        var runnerOptions = options ?? GlobalRunnerContext.Options ?? new PipelogiqRunnerOptions();
        ApiClient = new PipelogiqApiClient(runnerOptions);
    }

    public TSelf SetApiKey(string apiKey)
    {
        ApiKey = apiKey;
        return (TSelf)this;
    }

    public TSelf AddKeyword(string key, string value)
    {
        Keywords.Add(new KeywordDto { Key = key, Value = value });
        return (TSelf)this;
    }

    public TSelf AddContextItem(string key, object value)
    {
        ContextItems.Add(new ContextItem
        {
            Key = key,
            Value = JsonSerializer.Serialize(value),
            ValueType = value.GetType().AssemblyQualifiedName ?? value.GetType().FullName ?? string.Empty,
        });
        return (TSelf)this;
    }

    protected List<ContextItem> BuildContextItemsWithCurrentActivityTraceContext()
    {
        var contextItems = ContextItems
            .Select(item => new ContextItem
            {
                Key = item.Key,
                Value = item.Value,
                ValueType = item.ValueType,
            })
            .ToList();

        var activity = Activity.Current;
        if (activity == null)
            return contextItems;

        if (!HasContextItem(contextItems, TraceparentKey) && !string.IsNullOrWhiteSpace(activity.Id))
            contextItems.Add(CreateContextItem(TraceparentKey, activity.Id!));

        if (!HasContextItem(contextItems, TracestateKey) && !string.IsNullOrWhiteSpace(activity.TraceStateString))
            contextItems.Add(CreateContextItem(TracestateKey, activity.TraceStateString!));

        return contextItems;
    }

    private static bool HasContextItem(IEnumerable<ContextItem> items, string key)
    {
        return items.Any(item => string.Equals(item.Key, key, StringComparison.OrdinalIgnoreCase));
    }

    private static ContextItem CreateContextItem(string key, object value)
    {
        var valueType = value.GetType();

        return new ContextItem
        {
            Key = key,
            Value = JsonSerializer.Serialize(value),
            ValueType = valueType.AssemblyQualifiedName ?? valueType.FullName ?? string.Empty,
        };
    }

    protected string RequireApiKey()
    {
        ApiKey ??= GlobalRunnerContext.Token ?? GlobalRunnerContext.Options?.ApiKey;
        if (string.IsNullOrWhiteSpace(ApiKey))
            throw new InvalidOperationException("API key is required to call Pipelogiq API.");

        return ApiKey!;
    }
}
