using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using PipelogiqSDK.Abstractions;
using PipelogiqSDK.Contracts;
using PipelogiqSDK.StageHelper;

namespace PipelogiqSDK.Execution;

public class StageExecutor(IServiceProvider serviceProvider)
{
    private const string TraceparentKey = "traceparent";
    private const string TracestateKey = "tracestate";
    private static readonly ActivitySource StageActivitySource = new("PipelogiqSDK");

    public async Task<IStageResult> ExecuteStageHandlerAsync(StageExecutionData data)
    {
        var handler = ResolveHandler(data);
        var stageContext = BuildStageContext(data);
        using var activity = StartStageActivity(handler, data, stageContext);
        WriteTraceContextToPayload(stageContext, activity);

        try
        {
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

    private static Activity? StartStageActivity(object handler, StageExecutionData data, StageContext stageContext)
    {
        var traceparent = stageContext.TryGetValue<string>(TraceparentKey);
        var tracestate = stageContext.TryGetValue<string>(TracestateKey);

        Activity? activity;
        if (!string.IsNullOrWhiteSpace(traceparent) &&
            ActivityContext.TryParse(traceparent, tracestate, out var parentContext))
        {
            activity = StageActivitySource.StartActivity("pipelogiq.stage.execute", ActivityKind.Consumer, parentContext);
        }
        else
        {
            activity = StageActivitySource.StartActivity("pipelogiq.stage.execute", ActivityKind.Consumer);
        }

        if (activity == null)
            return null;

        activity.SetTag("pipelogiq.stage.id", data.StageId);
        activity.SetTag("pipelogiq.pipeline.id", data.PipelineId);
        activity.SetTag("pipelogiq.handler.type", handler.GetType().FullName ?? handler.GetType().Name);

        if (data.InputType != null)
            activity.SetTag("pipelogiq.handler.input_type", data.InputType.FullName ?? data.InputType.Name);

        return activity;
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

    private static async Task<IStageResult> InvokeGenericHandlerAsync(
        object handler,
        Type inputType,
        object? input,
        IStageContext stageContext)
    {
        var handlerInterface = typeof(IStageHandler<>).MakeGenericType(inputType);
        var executeMethod = handlerInterface.GetMethod(nameof(IStageHandler<object>.ExecuteAsync));

        if (executeMethod == null)
            throw new InvalidOperationException("IStageHandler.ExecuteAsync not found.");

        var parameters = new[] { input ?? Activator.CreateInstance(inputType), stageContext };
        var executeTask = (Task?)executeMethod.Invoke(handler, parameters);

        if (executeTask == null)
            throw new InvalidOperationException("Handler returned null task.");

        await executeTask.ConfigureAwait(false);

        var resultProperty = executeTask.GetType().GetProperty("Result");
        return (IStageResult?)resultProperty?.GetValue(executeTask)
               ?? throw new InvalidOperationException("Handler returned null result.");
    }
}
