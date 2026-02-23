using PipelogiqSDK.Configuration;
using PipelogiqSDK.Contracts;

namespace PipelogiqSDK.Builders;

public class EventBuilder : BaseBuilder<EventBuilder>
{
    private readonly string _eventName;
    private readonly List<StageInfo> _stages = new();

    private EventBuilder(string eventName, string stageHandlerName, PipelogiqRunnerOptions? options = null) : base(options)
    {
        _eventName = eventName;
        AddStage(eventName, stageHandlerName);
    }

    public static EventBuilder Create<TEvent>(string eventName, PipelogiqRunnerOptions? options = null) where TEvent : class
    {
        return new EventBuilder(eventName, typeof(TEvent).Name, options);
    }

    public static EventBuilder Create(string eventName, string handlerName, PipelogiqRunnerOptions? options = null)
    {
        return new EventBuilder(eventName, handlerName, options);
    }

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

    public async Task<PipelineDto> SendAsync(CancellationToken ct = default)
    {
        return await ApiClient.PostEventAsync(Build(), ct);
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
