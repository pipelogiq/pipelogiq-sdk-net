using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using PipelogiqSDK.Abstractions;
using PipelogiqSDK.Contracts;
using PipelogiqSDK.StageHelper;

namespace PipelogiqSDK.Execution;

/// <summary>
/// Executes stage handlers and manages trace/context propagation.
/// </summary>
/// <param name="serviceProvider">Service provider for handler resolution.</param>
public class StageExecutor(IServiceProvider serviceProvider)
{
    private const string TraceparentKey = "traceparent";
    private const string TracestateKey = "tracestate";
    private const string StageActivityName = "pipelogiq.stage.execute";
    private static readonly ActivitySource StageActivitySource = new("PipelogiqSDK");

    // Cache compiled invoke delegates keyed by handler input type.
    // Avoids repeated MakeGenericType + GetMethod + Invoke on every stage execution.
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<Type, Func<object, object?, IStageContext, Task<IStageResult>>>
        InvokeCache = new();

    /// <summary>
    /// Executes a stage handler and returns stage result.
    /// </summary>
    /// <param name="data">Stage execution data.</param>
    /// <returns>Stage execution result.</returns>
    public async Task<IStageResult> ExecuteStageHandlerAsync(StageExecutionData data)
    {
        var handler = ResolveHandler(data);
        var stageContext = BuildStageContext(data);
        var previousActivity = Activity.Current;
        Activity? activity = null;

        try
        {
            activity = StartStageActivity(handler, data, stageContext);
            WriteTraceContextToPayload(stageContext, activity);

            var result = await InvokeHandlerAsync(handler, data, stageContext);
            activity?.SetStatus(ActivityStatusCode.Ok);
            WriteTraceContextToPayload(stageContext, activity);

            result.ContextItems = PayloadConverter.ToContextItems(stageContext.Payload);
            result.Logs = stageContext.Logger?.Logs;
            return result;
        }
        catch (Exception ex)
        {
            if (activity != null)
            {
                activity.SetStatus(ActivityStatusCode.Error, ex.Message);
                activity.SetTag("pipelogiq.error.type", ex.GetType().FullName ?? ex.GetType().Name);
            }

            WriteTraceContextToPayload(stageContext, activity);
            throw;
        }
        finally
        {
            activity?.Dispose();
            Activity.Current = previousActivity;
        }
    }

    private object ResolveHandler(StageExecutionData data)
    {
        if (data.HandlerInstance != null)
            return data.HandlerInstance;

        if (data.HandlerType != null)
            return serviceProvider.GetRequiredService(data.HandlerType);

        throw new InvalidOperationException("Stage handler is not provided.");
    }

    private static StageContext BuildStageContext(StageExecutionData data)
    {
        var payload = PayloadConverter.FromContextItems(data.ContextItems ?? new List<ContextItem>());

        return new StageContext
        {
            PipelineId = data.PipelineId,
            StageId = data.StageId,
            Payload = payload,
            CreatedTime = DateTime.UtcNow,
            Logger = data.Logger,
        };
    }

    private static Activity StartStageActivity(object handler, StageExecutionData data, StageContext stageContext)
    {
        var traceparent = stageContext.TryGetValue<string>(TraceparentKey);
        var tracestate = stageContext.TryGetValue<string>(TracestateKey);

        Activity activity;
        if (!string.IsNullOrWhiteSpace(traceparent) &&
            ActivityContext.TryParse(traceparent, tracestate, out var parentContext))
        {
            activity = StartStageActivityWithParent(parentContext, tracestate);
        }
        else
        {
            activity = StartRootStageActivity();
        }

        activity.SetTag("pipelogiq.stage.id", data.StageId);
        activity.SetTag("pipelogiq.pipeline.id", data.PipelineId);
        activity.SetTag("pipelogiq.handler.type", handler.GetType().FullName ?? handler.GetType().Name);

        if (data.InputType != null)
            activity.SetTag("pipelogiq.handler.input_type", data.InputType.FullName ?? data.InputType.Name);

        return activity;
    }

    private static Activity StartStageActivityWithParent(ActivityContext parentContext, string? tracestate)
    {
        var activity = StageActivitySource.StartActivity(StageActivityName, ActivityKind.Consumer, parentContext);
        if (activity != null)
            return activity;

        var fallbackActivity = new Activity(StageActivityName);
        fallbackActivity.SetIdFormat(ActivityIdFormat.W3C);
        fallbackActivity.SetParentId(parentContext.TraceId, parentContext.SpanId, parentContext.TraceFlags);

        if (!string.IsNullOrWhiteSpace(parentContext.TraceState))
            fallbackActivity.TraceStateString = parentContext.TraceState;
        else if (!string.IsNullOrWhiteSpace(tracestate))
            fallbackActivity.TraceStateString = tracestate;

        fallbackActivity.Start();
        return fallbackActivity;
    }

    private static Activity StartRootStageActivity()
    {
        var previousCurrent = Activity.Current;
        Activity.Current = null;

        try
        {
            var activity = StageActivitySource.StartActivity(StageActivityName, ActivityKind.Consumer);
            if (activity != null)
                return activity;

            var fallbackActivity = new Activity(StageActivityName);
            fallbackActivity.SetIdFormat(ActivityIdFormat.W3C);
            fallbackActivity.Start();
            return fallbackActivity;
        }
        finally
        {
            if (Activity.Current == null)
                Activity.Current = previousCurrent;
        }
    }

    private static void WriteTraceContextToPayload(StageContext stageContext, Activity? activity)
    {
        if (activity == null || string.IsNullOrWhiteSpace(activity.Id))
            return;

        stageContext.Payload ??= new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        stageContext.Payload[TraceparentKey] = activity.Id!;

        if (!string.IsNullOrWhiteSpace(activity.TraceStateString))
            stageContext.Payload[TracestateKey] = activity.TraceStateString!;
    }

    private static async Task<IStageResult> InvokeHandlerAsync(object handler, StageExecutionData data, StageContext stageContext)
    {
        var genericInterface = handler
            .GetType()
            .GetInterfaces()
            .FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IStageHandler<>));

        if (genericInterface != null)
        {
            var inputType = data.InputType ?? genericInterface.GenericTypeArguments.FirstOrDefault();
            var input = PrepareInput(data.JsonInput, inputType);
            return await InvokeGenericHandlerAsync(handler, inputType!, input, stageContext);
        }

        if (handler is IStageHandler nonGenericHandler)
            return await nonGenericHandler.ExecuteAsync(stageContext);

        throw new InvalidOperationException(
            $"Handler '{handler.GetType().FullName}' must implement {nameof(IStageHandler)} or {nameof(IStageHandler<object>)}.");
    }

    private static object? PrepareInput(string? serializedInput, Type? inputType)
    {
        if (inputType == null || string.IsNullOrWhiteSpace(serializedInput))
            return null;

        return PayloadConverter.DeserializeJson(serializedInput, inputType);
    }

    private static Task<IStageResult> InvokeGenericHandlerAsync(
        object handler,
        Type inputType,
        object? input,
        IStageContext stageContext)
    {
        var invoker = InvokeCache.GetOrAdd(inputType, BuildInvoker);
        return invoker(handler, input ?? Activator.CreateInstance(inputType), stageContext);
    }

    /// <summary>
    /// Builds a strongly-typed delegate that calls IStageHandler&lt;TInput&gt;.ExecuteAsync
    /// without reflection on the hot path. Compiled once per input type and cached.
    /// </summary>
    private static Func<object, object?, IStageContext, Task<IStageResult>> BuildInvoker(Type inputType)
    {
        var handlerInterface = typeof(IStageHandler<>).MakeGenericType(inputType);
        var executeMethod = handlerInterface.GetMethod(nameof(IStageHandler<object>.ExecuteAsync))
            ?? throw new InvalidOperationException($"IStageHandler<{inputType.Name}>.ExecuteAsync not found.");

        // Use expression trees to build a compiled delegate that avoids per-call reflection
        var handlerParam = System.Linq.Expressions.Expression.Parameter(typeof(object), "handler");
        var inputParam   = System.Linq.Expressions.Expression.Parameter(typeof(object), "input");
        var contextParam = System.Linq.Expressions.Expression.Parameter(typeof(IStageContext), "context");

        var castHandler = System.Linq.Expressions.Expression.Convert(handlerParam, handlerInterface);
        var castInput   = System.Linq.Expressions.Expression.Convert(inputParam, inputType);
        var call        = System.Linq.Expressions.Expression.Call(castHandler, executeMethod, castInput, contextParam);

        return System.Linq.Expressions.Expression.Lambda<Func<object, object?, IStageContext, Task<IStageResult>>>(
            call, handlerParam, inputParam, contextParam).Compile();
    }
}
