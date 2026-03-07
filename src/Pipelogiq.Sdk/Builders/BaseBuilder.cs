using System.Diagnostics;
using System.Text.Json;
using PipelogiqSDK.Api;
using PipelogiqSDK.Configuration;
using PipelogiqSDK.Contracts;
using PipelogiqSDK.Execution;

namespace PipelogiqSDK.Builders;

/// <summary>
/// Base class for SDK builders with shared API key, keywords, and context handling.
/// </summary>
/// <typeparam name="TSelf">Concrete builder type.</typeparam>
public abstract class BaseBuilder<TSelf> where TSelf : BaseBuilder<TSelf>
{
    private const string TraceparentKey = "traceparent";
    private const string TracestateKey = "tracestate";

    /// <summary>
    /// API key used for request execution.
    /// </summary>
    protected string? ApiKey;

    /// <summary>
    /// Keyword collection attached to outbound payloads.
    /// </summary>
    protected readonly List<KeywordDto> Keywords = new();

    /// <summary>
    /// Context items attached to outbound payloads.
    /// </summary>
    protected readonly List<ContextItem> ContextItems = new();

    /// <summary>
    /// API client used by builders.
    /// </summary>
    protected PipelogiqApiClient ApiClient;

    /// <summary>
    /// Initializes builder with optional runner options.
    /// </summary>
    /// <param name="options">Runner options; falls back to global context when null.</param>
    protected BaseBuilder(PipelogiqRunnerOptions? options = null)
    {
        var runnerOptions = options ?? GlobalRunnerContext.Options ?? new PipelogiqRunnerOptions();
        ApiClient = new PipelogiqApiClient(runnerOptions);
    }

    /// <summary>
    /// Overrides API key for this builder instance.
    /// </summary>
    /// <param name="apiKey">API key value.</param>
    /// <returns>Current builder instance.</returns>
    public TSelf SetApiKey(string apiKey)
    {
        ApiKey = apiKey;
        return (TSelf)this;
    }

    /// <summary>
    /// Adds a keyword to outbound payload.
    /// </summary>
    /// <param name="key">Keyword key.</param>
    /// <param name="value">Keyword value.</param>
    /// <returns>Current builder instance.</returns>
    public TSelf AddKeyword(string key, string value)
    {
        Keywords.Add(new KeywordDto { Key = key, Value = value });
        return (TSelf)this;
    }

    /// <summary>
    /// Adds a context item to outbound payload.
    /// </summary>
    /// <param name="key">Context key.</param>
    /// <param name="value">Context value.</param>
    /// <returns>Current builder instance.</returns>
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

    /// <summary>
    /// Builds context items and injects current trace context when available.
    /// </summary>
    /// <returns>Context items list for outbound payload.</returns>
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

    /// <summary>
    /// Resolves API key from builder and global context.
    /// </summary>
    /// <returns>Non-empty API key.</returns>
    /// <exception cref="InvalidOperationException">Thrown when API key is not configured.</exception>
    protected string RequireApiKey()
    {
        ApiKey ??= GlobalRunnerContext.Token ?? GlobalRunnerContext.Options?.ApiKey;
        if (string.IsNullOrWhiteSpace(ApiKey))
            throw new InvalidOperationException("API key is required to call Pipelogiq API.");

        return ApiKey!;
    }
}
