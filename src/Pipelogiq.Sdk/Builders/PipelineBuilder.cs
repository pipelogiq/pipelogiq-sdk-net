using System.Text.Json;
using PipelogiqSDK.Configuration;
using PipelogiqSDK.Contracts;

namespace PipelogiqSDK.Builders;

public class PipelineBuilder : BaseBuilder<PipelineBuilder>
{
    private readonly string _name;
    private readonly List<StageInfo> _stages = new();

    private PipelineBuilder(string name, PipelogiqRunnerOptions? options = null) : base(options)
    {
        _name = name;
    }

    public static PipelineBuilder Create(string name, PipelogiqRunnerOptions? options = null)
    {
        return new PipelineBuilder(name, options);
    }

    public PipelineBuilder WithAction<TAction>(string stageName, object? input = null, StageOptions? options = null) where TAction : class
    {
        return WithAction(stageName, typeof(TAction).Name, input, options);
    }

    public PipelineBuilder WithAction(string stageName, string stageHandlerName, object? input = null, StageOptions? options = null)
    {
        _stages.Add(new StageInfo
        {
            StageName = stageName,
            StageHandlerName = stageHandlerName,
            Input = input != null ? JsonSerializer.Serialize(input) : null,
            Options = options,
            IsEvent = false,
        });
        return this;
    }

    public PipelineBuilder AsEvent<TEvent>(string eventName) where TEvent : class
    {
        _stages.Add(new StageInfo
        {
            StageName = eventName,
            StageHandlerName = typeof(TEvent).Name,
            IsEvent = true
        });

        return this;
    }

    public PipelineDto Build()
    {
        var contextItems = BuildContextItemsWithCurrentActivityTraceContext();
        var key = RequireApiKey();
        return new PipelineDto
        {
            ApiKey = key,
            Name = _name,
            Stages = _stages,
            PipelineKeywords = Keywords,
            PipelineContextItems = contextItems,
        };
    }

    public async Task SendAsync(CancellationToken ct = default)
    {
        await ApiClient.PostPipelineAsync(Build(), ct);
    }
}
