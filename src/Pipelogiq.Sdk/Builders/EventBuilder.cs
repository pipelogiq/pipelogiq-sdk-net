using PipelogiqSDK.Configuration;
using PipelogiqSDK.Contracts;

namespace PipelogiqSDK.Builders;

/// <summary>
/// Builder for single-stage event payloads.
/// </summary>
public class EventBuilder : BaseBuilder<EventBuilder>
{
    private readonly string _eventName;
    private readonly List<StageInfo> _stages = new();

    private EventBuilder(string eventName, string stageHandlerName, PipelogiqRunnerOptions? options = null) : base(options)
    {
        _eventName = eventName;
        AddStage(eventName, stageHandlerName);
    }

    /// <summary>
    /// Creates an event builder where stage handler name is inferred from event type.
    /// </summary>
    /// <typeparam name="TEvent">Event type used to infer handler name.</typeparam>
    /// <param name="eventName">Event stage name.</param>
    /// <param name="options">Optional runner options.</param>
    /// <returns>Configured event builder.</returns>
    public static EventBuilder Create<TEvent>(string eventName, PipelogiqRunnerOptions? options = null) where TEvent : class
    {
        return new EventBuilder(eventName, typeof(TEvent).Name, options);
    }

    /// <summary>
    /// Creates an event builder with explicit handler name.
    /// </summary>
    /// <param name="eventName">Event stage name.</param>
    /// <param name="handlerName">Stage handler name.</param>
    /// <param name="options">Optional runner options.</param>
    /// <returns>Configured event builder.</returns>
    public static EventBuilder Create(string eventName, string handlerName, PipelogiqRunnerOptions? options = null)
    {
        return new EventBuilder(eventName, handlerName, options);
    }

    /// <summary>
    /// Builds event payload.
    /// </summary>
    /// <returns>Pipeline DTO with one event stage.</returns>
    public PipelineDto Build()
    {
        var contextItems = BuildContextItemsWithCurrentActivityTraceContext();
        var key = RequireApiKey();
        return new PipelineDto
        {
            ApiKey = key,
            Name = _eventName,
            Stages = _stages,
            PipelineKeywords = Keywords,
            PipelineContextItems = contextItems,
        };
    }

    /// <summary>
    /// Sends event payload to API and returns the created pipeline response.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Created pipeline response with id, status and stages.</returns>
    public Task<PipelineResponse> SendAsync(CancellationToken ct = default)
    {
        return ApiClient.PostEventAsync(Build(), ct);
    }

    private void AddStage(string stageName, string stageHandlerName)
    {
        _stages.Add(new StageInfo
        {
            StageName = stageName,
            StageHandlerName = stageHandlerName,
            IsEvent = true,
        });
    }
}
