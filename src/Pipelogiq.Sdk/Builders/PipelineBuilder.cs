using System.Text.Json;
using PipelogiqSDK.Configuration;
using PipelogiqSDK.Contracts;

namespace PipelogiqSDK.Builders;

/// <summary>
/// Builder for multi-stage pipeline payloads.
/// </summary>
public class PipelineBuilder : BaseBuilder<PipelineBuilder>
{
    private readonly string _name;
    private readonly List<StageInfo> _stages = new();
    private string? _idempotencyKey;

    private PipelineBuilder(string name, PipelogiqRunnerOptions? options = null) : base(options)
    {
        _name = name;
    }

    /// <summary>
    /// Creates a pipeline builder.
    /// </summary>
    /// <param name="name">Pipeline name.</param>
    /// <param name="options">Optional runner options.</param>
    /// <returns>Configured pipeline builder.</returns>
    public static PipelineBuilder Create(string name, PipelogiqRunnerOptions? options = null)
    {
        return new PipelineBuilder(name, options);
    }

    /// <summary>
    /// Configures fail-safe idempotent pipeline creation.
    /// Repeated sends with the same key return the same pipeline for the authenticated application.
    /// </summary>
    /// <param name="idempotencyKey">Non-empty client-provided idempotency key.</param>
    /// <returns>Current builder instance.</returns>
    public PipelineBuilder WithIdempotencyKey(string idempotencyKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(idempotencyKey);
        _idempotencyKey = idempotencyKey.Trim();
        return this;
    }

    /// <summary>
    /// Adds an action stage and infers handler name from action type.
    /// </summary>
    /// <typeparam name="TAction">Action handler type.</typeparam>
    /// <param name="stageName">Stage name.</param>
    /// <param name="input">Optional stage input object.</param>
    /// <param name="options">Optional stage options.</param>
    /// <returns>Current builder instance.</returns>
    public PipelineBuilder WithAction<TAction>(string stageName, object? input = null, StageOptions? options = null) where TAction : class
    {
        return WithAction(stageName, typeof(TAction).Name, input, options);
    }

    /// <summary>
    /// Adds an action stage with stage options and no input, inferring handler name from action type.
    /// </summary>
    /// <typeparam name="TAction">Action handler type.</typeparam>
    /// <param name="stageName">Stage name.</param>
    /// <param name="options">Stage options.</param>
    /// <returns>Current builder instance.</returns>
    public PipelineBuilder WithAction<TAction>(string stageName, StageOptions options) where TAction : class
    {
        return WithAction(stageName, typeof(TAction).Name, null, options);
    }

    /// <summary>
    /// Adds an action stage without input and infers handler name from action type.
    /// </summary>
    /// <typeparam name="TAction">Action handler type.</typeparam>
    /// <param name="stageName">Stage name.</param>
    /// <returns>Current builder instance.</returns>
    public PipelineBuilder WithAction<TAction>(string stageName) where TAction : class
    {
        return WithAction(stageName, typeof(TAction).Name, null, null);
    }

    /// <summary>
    /// Adds an action stage using type name as both stage and handler names.
    /// </summary>
    /// <typeparam name="TAction">Action handler type.</typeparam>
    /// <returns>Current builder instance.</returns>
    public PipelineBuilder WithAction<TAction>() where TAction : class
    {
        return WithAction(typeof(TAction).Name, typeof(TAction).Name);
    }

    /// <summary>
    /// Adds an action stage with stage options using type name as both stage and handler names.
    /// </summary>
    /// <typeparam name="TAction">Action handler type.</typeparam>
    /// <param name="options">Stage options.</param>
    /// <returns>Current builder instance.</returns>
    public PipelineBuilder WithAction<TAction>(StageOptions options) where TAction : class
    {
        return WithAction(typeof(TAction).Name, typeof(TAction).Name, null, options);
    }

    /// <summary>
    /// Adds an action stage with explicit stage and handler names, stage options, and no input.
    /// </summary>
    /// <param name="stageName">Stage name.</param>
    /// <param name="stageHandlerName">Registered stage handler name.</param>
    /// <param name="options">Stage options.</param>
    /// <returns>Current builder instance.</returns>
    public PipelineBuilder WithAction(string stageName, string stageHandlerName, StageOptions options)
    {
        return WithAction(stageName, stageHandlerName, null, options);
    }

    /// <summary>
    /// Adds an action stage with explicit stage and handler names.
    /// </summary>
    /// <param name="stageName">Stage name.</param>
    /// <param name="stageHandlerName">Registered stage handler name.</param>
    /// <param name="input">Optional stage input object.</param>
    /// <param name="options">Optional stage options.</param>
    /// <returns>Current builder instance.</returns>
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

    /// <summary>
    /// Adds event stage to current pipeline.
    /// </summary>
    /// <typeparam name="TEvent">Event handler type.</typeparam>
    /// <param name="eventName">Event name.</param>
    /// <returns>Current builder instance.</returns>
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

    /// <summary>
    /// Builds pipeline payload.
    /// </summary>
    /// <returns>Pipeline DTO payload.</returns>
    public PipelineDto Build()
    {
        var contextItems = BuildContextItemsWithCurrentActivityTraceContext();
        var key = RequireApiKey();
        return new PipelineDto
        {
            ApiKey = key,
            Name = _name,
            IdempotencyKey = _idempotencyKey,
            Stages = _stages,
            PipelineKeywords = Keywords,
            PipelineContextItems = contextItems,
        };
    }

    /// <summary>
    /// Sends pipeline payload to API and returns the created pipeline response.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Created pipeline response with id, status and stages.</returns>
    public Task<PipelineResponse> SendAsync(CancellationToken ct = default)
    {
        var pipeline = Build();
        return string.IsNullOrWhiteSpace(pipeline.IdempotencyKey)
            ? ApiClient.PostPipelineAsync(pipeline, ct)
            : ApiClient.PostIdempotentPipelineAsync(pipeline, ct);
    }
}
