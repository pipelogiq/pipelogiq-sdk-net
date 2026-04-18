using System.Diagnostics;
using System.Net;
using System.Reflection;
using System.Text;
using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using PipelogiqSDK.Abstractions;
using PipelogiqSDK.Api;
using PipelogiqSDK.Configuration;
using PipelogiqSDK.Constants;
using PipelogiqSDK.Contracts;
using PipelogiqSDK.Execution;
using PipelogiqSDK.StageHelper;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using RabbitMQ.Client.Exceptions;
namespace PipelogiqSDK.Runner;

/// <summary>
/// Worker runtime that consumes stage messages, executes handlers, and publishes results.
/// </summary>
public class PipelineRunner
{
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(10);
    private readonly ILogger<PipelineRunner> _logger;
    private readonly PipelogiqApiClient _apiClient;
    private readonly PipelogiqRunnerOptions _runnerOptions;
    private readonly StageExecutor _stageExecutor;
    private readonly SemaphoreSlim _publishLock = new(1, 1);
    private readonly SemaphoreSlim _workerEventFlushLock = new(1, 1);
    private readonly object _stateSync = new();

    private readonly Dictionary<string, Type> _handlerTypes = new();
    private readonly Dictionary<string, IStageHandler> _handlerInstances = new();
    private readonly List<IModel> _consumerChannels = new();
    private readonly List<(IModel Channel, string ConsumerTag)> _activeConsumers = new();
    private readonly ConcurrentQueue<WorkerEventItem> _pendingWorkerEvents = new();
    private readonly object _queueStatusSync = new();
    private readonly HashSet<string> _activeStageNextQueues = new(StringComparer.Ordinal);
    private readonly HashSet<string> _missingStageNextQueues = new(StringComparer.Ordinal);

    private readonly DateTimeOffset _startedAt = DateTimeOffset.UtcNow;
    private readonly Process _process = Process.GetCurrentProcess();

    private IConnection? _connection;
    private IModel? _publishChannel;
    private WorkerBootstrapResponse? _bootstrap;
    private string? _workerId;
    private string? _workerSessionToken;
    private IReadOnlyList<string> _stageNextQueues = Array.Empty<string>();
    private string _stageResultQueue = PipelineChannels.StageResult;
    private string _stageSetStatusQueue = PipelineChannels.StageSetStatus;
    private DateTimeOffset _lastCpuSampleAt = DateTimeOffset.UtcNow;
    private TimeSpan _lastCpuTotal = TimeSpan.Zero;
    private string _workerState = WorkerStates.Starting;
    private string? _lastError;
    private string? _statusMessage;
    private volatile bool _sessionInvalid;

    private long _inFlightJobs;
    private long _jobsProcessed;
    private long _jobsFailed;

    /// <summary>
    /// Initializes runner instance.
    /// </summary>
    /// <param name="apiClient">Pipelogiq API client.</param>
    /// <param name="runnerOptions">Runner options.</param>
    /// <param name="logger">Logger instance.</param>
    /// <param name="stageExecutor">Stage executor.</param>
    public PipelineRunner(
        PipelogiqApiClient apiClient,
        PipelogiqRunnerOptions runnerOptions,
        ILogger<PipelineRunner> logger,
        StageExecutor stageExecutor)
    {
        _apiClient = apiClient;
        _runnerOptions = runnerOptions;
        _logger = logger;
        _stageExecutor = stageExecutor;
    }

    /// <summary>
    /// Registers handler instance by handler name.
    /// </summary>
    /// <param name="handlerName">Handler name from stage messages.</param>
    /// <param name="handler">Handler instance.</param>
    public void RegisterHandler(string handlerName, IStageHandler handler)
    {
        _handlerInstances[handlerName] = handler;
    }

    /// <summary>
    /// Registers handler type by handler name.
    /// </summary>
    /// <param name="handlerName">Handler name from stage messages.</param>
    /// <param name="handlerType">Handler type resolved from DI container.</param>
    public void RegisterHandler(string handlerName, Type handlerType)
    {
        _handlerTypes[handlerName] = handlerType;
    }

    /// <summary>
    /// Registers handler type by handler name.
    /// </summary>
    /// <typeparam name="THandler">Handler type resolved from DI container.</typeparam>
    /// <param name="handlerName">Handler name from stage messages.</param>
    public void RegisterHandler<THandler>(string handlerName) where THandler : class, IStageHandler
    {
        _handlerTypes[handlerName] = typeof(THandler);
    }

    /// <summary>
    /// Starts worker lifecycle loop until cancellation is requested.
    /// </summary>
    /// <param name="stoppingToken">Cancellation token.</param>
    public async Task StartAsync(CancellationToken stoppingToken)
    {
        IReadOnlyCollection<string> supportedHandlers;
        try
        {
            supportedHandlers = GetRegisteredHandlerNames();
        }
        catch (Exception ex)
        {
            SetState(WorkerStates.Error, "Worker startup failed before bootstrap.", ex.Message);
            _logger.LogError(ex, "Worker startup failed before bootstrap.");
            throw;
        }

        _process.Refresh();
        _lastCpuSampleAt = DateTimeOffset.UtcNow;
        _lastCpuTotal = _process.TotalProcessorTime;
        SetState(WorkerStates.Starting, "Worker starting.");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await BootstrapWorkerAsync(supportedHandlers, stoppingToken);
                _sessionInvalid = false;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                SetState(WorkerStates.Error, "Worker bootstrap failed.", ex.Message);
                EnqueueWorkerEvent(
                    "ERROR",
                    "worker.bootstrap_failed",
                    "Worker bootstrap failed.",
                    new Dictionary<string, object?>
                    {
                        ["error"] = ex.Message,
                        ["retryDelaySec"] = (int)RetryDelay.TotalSeconds,
                    });
                _logger.LogWarning(ex, "Worker bootstrap failed. Retrying in {RetryDelay}.", RetryDelay);
                await DelayBeforeRetry(stoppingToken);
                continue;
            }

            using var sessionCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
            var sessionToken = sessionCts.Token;
            var heartbeatTask = RunHeartbeatLoopAsync(sessionToken);

            try
            {
                while (!sessionToken.IsCancellationRequested && !_sessionInvalid)
                {
                    try
                    {
                        if (ConnectAndSubscribe(sessionToken))
                        {
                            SetReadyStateForCurrentConnection();
                            await WaitForReconnectSignalAsync(sessionToken);
                        }
                    }
                    catch (OperationCanceledException) when (sessionToken.IsCancellationRequested)
                    {
                        break;
                    }
                    catch (WorkerConfigurationException ex)
                    {
                        SetState(WorkerStates.Degraded, "Worker configuration is invalid.", ex.Message);
                        EnqueueWorkerEvent(
                            "WARN",
                            "worker.configuration_invalid",
                            "Worker configuration is invalid.",
                            new Dictionary<string, object?>
                            {
                                ["error"] = ex.Message,
                                ["retryDelaySec"] = (int)RetryDelay.TotalSeconds,
                            });
                        _logger.LogWarning(ex, "Worker configuration error. Retrying in {RetryDelay}.", RetryDelay);
                    }
                    catch (OperationInterruptedException ex) when (IsPreconditionFailed(ex))
                    {
                        var reason = ex.ShutdownReason?.ReplyText ?? ex.Message;
                        SetState(WorkerStates.Degraded, "RabbitMQ queue configuration mismatch.", reason);
                        EnqueueWorkerEvent(
                            "WARN",
                            "worker.queue_mismatch",
                            "RabbitMQ queue configuration mismatch.",
                            new Dictionary<string, object?>
                            {
                                ["error"] = reason,
                                ["replyCode"] = ex.ShutdownReason?.ReplyCode,
                                ["retryDelaySec"] = (int)RetryDelay.TotalSeconds,
                            });
                        _logger.LogWarning(ex, "RabbitMQ PRECONDITION_FAILED (406). Retrying in {RetryDelay}.", RetryDelay);
                    }
                    catch (Exception ex)
                    {
                        SetState(WorkerStates.Degraded, "Broker connection failed.", ex.Message);
                        EnqueueWorkerEvent(
                            "WARN",
                            "worker.broker_connection_failed",
                            "Broker connection failed.",
                            new Dictionary<string, object?>
                            {
                                ["error"] = ex.Message,
                                ["exceptionType"] = ex.GetType().Name,
                                ["retryDelaySec"] = (int)RetryDelay.TotalSeconds,
                            });
                        _logger.LogWarning(ex, "RabbitMQ connection attempt failed.");
                    }
                    finally
                    {
                        if (stoppingToken.IsCancellationRequested)
                        {
                            CancelConsumers();
                        }
                        else
                        {
                            DisposeMessaging();
                        }
                    }

                    if (sessionToken.IsCancellationRequested || _sessionInvalid)
                        break;

                    await DelayBeforeRetry(sessionToken);
                }
            }
            finally
            {
                if (stoppingToken.IsCancellationRequested)
                {
                    SetState(WorkerStates.Draining, "Worker draining.");
                    await DrainInFlightJobsAsync(_runnerOptions.DrainGracePeriod);
                }

                sessionCts.Cancel();
                await AwaitSilently(heartbeatTask);
                DisposeMessaging();
            }

            if (stoppingToken.IsCancellationRequested)
                break;

            if (_sessionInvalid)
            {
                _logger.LogInformation("Worker session expired. Re-bootstrapping.");
                continue;
            }

            await DelayBeforeRetry(stoppingToken);
        }

        using var shutdownCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await TrySendShutdownAsync(shutdownCts.Token);
        SetState(WorkerStates.Stopped, "Worker stopped.");
        DisposeMessaging();
    }

    private async Task BootstrapWorkerAsync(IReadOnlyCollection<string> supportedHandlers, CancellationToken stoppingToken)
    {
        SetState(WorkerStates.Starting, "Bootstrapping worker.");

        var bootstrapRequest = BuildBootstrapRequest(supportedHandlers);
        var bootstrapResponse = await _apiClient.PostWorkerBootstrapAsync(bootstrapRequest, stoppingToken);
        ValidateBootstrapResponse(bootstrapResponse);

        _bootstrap = bootstrapResponse;
        _workerId = bootstrapResponse.WorkerId!;
        _workerSessionToken = bootstrapResponse.WorkerSessionToken!;
        _stageResultQueue = string.IsNullOrWhiteSpace(bootstrapResponse.Queues.StageResult)
            ? PipelineChannels.StageResult
            : bootstrapResponse.Queues.StageResult;
        _stageSetStatusQueue = string.IsNullOrWhiteSpace(bootstrapResponse.Queues.StageSetStatus)
            ? PipelineChannels.StageSetStatus
            : bootstrapResponse.Queues.StageSetStatus;
        _stageNextQueues = BuildStageNextQueueNames(supportedHandlers, bootstrapResponse);
        ResetStageNextQueueCoverage();

        SetState(WorkerStates.Starting, "Worker bootstrap completed.");
        await FlushPendingWorkerEventsAsync(stoppingToken);
    }

    private WorkerBootstrapRequest BuildBootstrapRequest(IReadOnlyCollection<string> supportedHandlers)
    {
        var capabilities = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase)
        {
            ["batchAck"] = false,
            ["otel"] = false,
        };

        if (_runnerOptions.Capabilities is not null)
        {
            foreach (var capability in _runnerOptions.Capabilities)
                capabilities[capability.Key] = capability.Value;
        }

        Dictionary<string, string>? metadata = null;
        if (_runnerOptions.Metadata is not null && _runnerOptions.Metadata.Count > 0)
            metadata = new Dictionary<string, string>(_runnerOptions.Metadata, StringComparer.OrdinalIgnoreCase);

        metadata ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        metadata["sdk"] = "dotnet";

        return new WorkerBootstrapRequest
        {
            WorkerName = ResolveWorkerName(),
            InstanceId = ResolveInstanceId(),
            WorkerVersion = ResolveWorkerVersion(),
            SdkVersion = ResolveSdkVersion(),
            Environment = ResolveEnvironment(),
            HostName = Environment.MachineName,
            Pid = Environment.ProcessId,
            SupportedHandlers = supportedHandlers.ToList(),
            Capabilities = capabilities,
            Metadata = metadata
        };
    }

    private static void ValidateBootstrapResponse(WorkerBootstrapResponse bootstrapResponse)
    {
        if (string.IsNullOrWhiteSpace(bootstrapResponse.WorkerId))
            throw new InvalidOperationException("Bootstrap response is missing workerId.");

        if (string.IsNullOrWhiteSpace(bootstrapResponse.WorkerSessionToken))
            throw new InvalidOperationException("Bootstrap response is missing workerSessionToken.");

        if (!string.Equals(bootstrapResponse.MessageBroker.Type, "rabbitmq", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Unsupported message broker type: '{bootstrapResponse.MessageBroker.Type}'.");

        if (string.IsNullOrWhiteSpace(bootstrapResponse.MessageBroker.ConnectionString))
            throw new InvalidOperationException("Bootstrap response is missing messageBroker.connectionString.");

        if (string.IsNullOrWhiteSpace(bootstrapResponse.Application.AppId))
            throw new InvalidOperationException("Bootstrap response is missing application.appId.");
    }

    private static IReadOnlyList<string> BuildStageNextQueueNames(
        IReadOnlyCollection<string> supportedHandlers,
        WorkerBootstrapResponse bootstrapResponse)
    {
        var appId = bootstrapResponse.Application.AppId!;
        var pattern = string.IsNullOrWhiteSpace(bootstrapResponse.Queues.StageNextPattern)
            ? "{appId}_{handler}_StageNext"
            : bootstrapResponse.Queues.StageNextPattern;

        return supportedHandlers
            .Select(handler =>
                pattern
                    .Replace("{appId}", appId, StringComparison.OrdinalIgnoreCase)
                    .Replace("{handler}", handler, StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }

    private async Task RunHeartbeatLoopAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            if (string.IsNullOrWhiteSpace(_workerId) || string.IsNullOrWhiteSpace(_workerSessionToken))
                return;

            try
            {
                await _apiClient.PostWorkerHeartbeatAsync(_workerSessionToken, BuildHeartbeatRequest(), stoppingToken);
                await FlushPendingWorkerEventsAsync(stoppingToken);

                if (_connection?.IsOpen == true)
                    SetReadyStateForCurrentConnection();
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (HttpRequestException ex) when (IsSessionAuthFailure(ex))
            {
                _sessionInvalid = true;
                SetState(WorkerStates.Degraded, "Worker session rejected by server.", ex.Message);
                EnqueueWorkerEvent(
                    "ERROR",
                    "worker.session_rejected",
                    "Worker session rejected by server.",
                    new Dictionary<string, object?>
                    {
                        ["statusCode"] = ex.StatusCode?.ToString(),
                        ["error"] = ex.Message,
                    });
                _logger.LogWarning(
                    "Worker heartbeat rejected with {StatusCode}. Re-bootstrap is required.",
                    ex.StatusCode);
                return;
            }
            catch (Exception ex)
            {
                SetState(WorkerStates.Degraded, "Failed to send heartbeat.", ex.Message);
                EnqueueWorkerEvent(
                    "WARN",
                    "worker.heartbeat_failed",
                    "Failed to send heartbeat.",
                    new Dictionary<string, object?>
                    {
                        ["error"] = ex.Message,
                        ["exceptionType"] = ex.GetType().Name,
                    });
                _logger.LogWarning(ex, "Failed to send worker heartbeat.");
            }

            try
            {
                await Task.Delay(GetHeartbeatInterval(), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
        }
    }

    private WorkerHeartbeatRequest BuildHeartbeatRequest()
    {
        var (state, message, lastError) = SnapshotState();

        return new WorkerHeartbeatRequest
        {
            WorkerId = _workerId!,
            State = state,
            UptimeSec = Math.Max(0, (long)(DateTimeOffset.UtcNow - _startedAt).TotalSeconds),
            BrokerConnected = _connection?.IsOpen == true,
            InFlightJobs = Interlocked.Read(ref _inFlightJobs),
            JobsProcessed = Interlocked.Read(ref _jobsProcessed),
            JobsFailed = Interlocked.Read(ref _jobsFailed),
            QueueLag = GetQueueLag(),
            CpuPercent = GetCpuPercent(),
            MemoryMb = Math.Round(GC.GetTotalMemory(false) / (1024d * 1024d), 2),
            LastError = lastError,
            Message = message,
            Metadata = BuildHeartbeatMetadata(),
        };
    }

    private Dictionary<string, object?> BuildHeartbeatMetadata()
    {
        var metadata = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["workerName"] = ResolveWorkerName(),
        };

        if (!string.IsNullOrWhiteSpace(_bootstrap?.ConfigVersion))
            metadata["configVersion"] = _bootstrap.ConfigVersion;

        if (!string.IsNullOrWhiteSpace(_bootstrap?.Application.AppId))
            metadata["appId"] = _bootstrap.Application.AppId;

        if (!string.IsNullOrWhiteSpace(_bootstrap?.MessageBroker.Type))
            metadata["brokerType"] = _bootstrap.MessageBroker.Type;

        var (activeStageNext, totalStageNext, missingStageNextNames) = SnapshotStageNextQueueCoverage();
        metadata["stageNextQueuesActive"] = activeStageNext;
        metadata["stageNextQueuesTotal"] = totalStageNext;
        metadata["stageNextQueuesMissing"] = Math.Max(0, totalStageNext - activeStageNext);
        if (missingStageNextNames.Length > 0)
            metadata["stageNextQueuesMissingNames"] = missingStageNextNames;

        return metadata;
    }

    private bool ConnectAndSubscribe(CancellationToken stoppingToken)
    {
        if (_bootstrap is null)
            throw new InvalidOperationException("Worker is not bootstrapped.");

        var factory = new ConnectionFactory
        {
            Uri = new Uri(_bootstrap.MessageBroker.ConnectionString!),
            DispatchConsumersAsync = true,
            AutomaticRecoveryEnabled = true,
            TopologyRecoveryEnabled = true,
            NetworkRecoveryInterval = TimeSpan.FromSeconds(5),
            RequestedHeartbeat = TimeSpan.FromSeconds(30),
            ClientProvidedName = $"PipelogiqSDK.Runner/{ResolveWorkerName()}",
        };

        _connection = factory.CreateConnection();
        _publishChannel = _connection.CreateModel();
        if (!EnsureQueueExists(_stageResultQueue))
            return false;

        if (!EnsureQueueExists(_stageSetStatusQueue))
            return false;

        ResetStageNextQueueCoverage();

        var prefetch = (ushort)Math.Clamp(_bootstrap.MessageBroker.Prefetch <= 0 ? 1 : _bootstrap.MessageBroker.Prefetch, 1, ushort.MaxValue);

        foreach (var queue in _stageNextQueues)
        {
            if (!EnsureQueueExists(queue, markWorkerDegradedOnMissing: false))
            {
                MarkStageNextQueueMissing(queue);
                continue;
            }

            SubscribeToStageNextQueue(queue, prefetch, stoppingToken);
        }

        return true;
    }

    private async Task WaitForReconnectSignalAsync(CancellationToken stoppingToken)
    {
        var consecutiveClosedChecks = 0;
        const int maxClosedChecksBeforeReconnect = 4;

        while (!stoppingToken.IsCancellationRequested)
        {
            if (_sessionInvalid)
                return;

            if (_connection is null || !_connection.IsOpen)
            {
                consecutiveClosedChecks++;
                if (consecutiveClosedChecks >= maxClosedChecksBeforeReconnect)
                {
                    SetState(WorkerStates.Degraded, "Broker connection is closed.");
                    return;
                }

                _logger.LogWarning(
                    "Broker connection not open (check {Check}/{Max}). Waiting for auto-recovery…",
                    consecutiveClosedChecks, maxClosedChecksBeforeReconnect);
            }
            else
            {
                consecutiveClosedChecks = 0;
                TrySubscribeMissingStageNextQueues(stoppingToken);
            }

            await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
        }
    }

    private async Task HandleMessageAsync(IModel channel, BasicDeliverEventArgs delivery, CancellationToken stoppingToken)
    {
        if (stoppingToken.IsCancellationRequested || _sessionInvalid)
        {
            SafeNack(channel, delivery.DeliveryTag, requeue: true);
            return;
        }

        var rawMessage = string.Empty;
        StageNextDto? parsedMessage;
        try
        {
            rawMessage = Encoding.UTF8.GetString(delivery.Body.ToArray());
            parsedMessage = PipelineMessageSerializer.Deserialize<StageNextDto>(rawMessage);
        }
        catch (Exception ex)
        {
            Interlocked.Increment(ref _jobsFailed);
            SetState(WorkerStates.Degraded, "Received malformed stage payload.", ex.Message);
            EnqueueWorkerEvent(
                "WARN",
                "worker.stage_payload_invalid",
                "Received malformed stage payload.",
                new Dictionary<string, object?>
                {
                    ["error"] = ex.Message,
                });
            _logger.LogWarning(ex, "Received malformed StageNext payload.");
            SafeAck(channel, delivery.DeliveryTag);
            return;
        }

        if (parsedMessage is null || string.IsNullOrWhiteSpace(parsedMessage.StageHandlerName))
        {
            Interlocked.Increment(ref _jobsFailed);
            _logger.LogWarning("Dropping invalid StageNext payload: {Payload}", rawMessage);
            SafeAck(channel, delivery.DeliveryTag);
            return;
        }

        Interlocked.Increment(ref _inFlightJobs);
        try
        {
            _logger.LogInformation("Received StageNext: {Payload}", PipelineMessageSerializer.Serialize(parsedMessage));

            if (delivery.Redelivered && await IsStageAlreadyTerminalAsync(parsedMessage))
            {
                _logger.LogWarning(
                    "Skipping redelivered message for stage {StageId} — stage is already in a terminal state.",
                    parsedMessage.StageId);
                Interlocked.Increment(ref _jobsProcessed);
                SafeAck(channel, delivery.DeliveryTag);
                return;
            }

            var isSuccess = await ExecuteAndPublishResult(parsedMessage, stoppingToken);

            if (isSuccess)
            {
                Interlocked.Increment(ref _jobsProcessed);
            }
            else
            {
                Interlocked.Increment(ref _jobsFailed);
            }

            SafeAck(channel, delivery.DeliveryTag);
        }
        catch (Exception ex)
        {
            Interlocked.Increment(ref _jobsFailed);
            SetState(WorkerStates.Degraded, "Stage processing failed.", ex.Message);
            EnqueueWorkerEvent(
                "ERROR",
                "worker.stage_processing_failed",
                $"Stage processing failed for stage {parsedMessage.StageId}.",
                new Dictionary<string, object?>
                {
                    ["stageId"] = parsedMessage.StageId,
                    ["pipelineId"] = parsedMessage.PipelineId,
                    ["handler"] = parsedMessage.StageHandlerName,
                    ["error"] = ex.Message,
                });
            _logger.LogError(
                ex,
                "Processing failed for stage {StageId}. Message disposition follows DLQ settings.",
                parsedMessage.StageId);
            SafeNack(channel, delivery.DeliveryTag, requeue: ShouldRequeueAfterFailure());
        }
        finally
        {
            Interlocked.Decrement(ref _inFlightJobs);
        }
    }

    private async Task<bool> ExecuteAndPublishResult(StageNextDto stage, CancellationToken stoppingToken)
    {
        var resultDto = new WorkerStageResultMessage
        {
            PipelineId = stage.PipelineId,
            StageId = stage.StageId,
        };

        var logger = new PipelineLogger();

        try
        {
            var executionData = BuildExecutionData(stage);
            executionData.Logger = logger;
            logger.Info(
                $"Stage execution starting [stageId={stage.StageId}, handler={stage.StageHandlerName}, pipelineId={stage.PipelineId}, inputPreview={stage.Input.ToLogPreview(800)}]");

            using var publishCts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await SetStatusToRunning(stage.StageId, publishCts.Token);

            var stageResult = await _stageExecutor.ExecuteStageHandlerAsync(executionData);

            resultDto.Result = stageResult.Result;
            resultDto.IsSuccess = stageResult.IsSuccess;
            resultDto.ErrorCode = stageResult.ErrorCode;
            resultDto.ContextItems = stageResult.ContextItems;
            resultDto.NextStageId = stageResult.NextStageId;
            resultDto.RunNextIfCurrentFailed = stageResult.RunNextIfCurrentFailed;
            resultDto.IsWaitingForApproval = stageResult.IsWaitingForApproval;
            resultDto.AppendedStages = stageResult.AppendedStages?
                .Select(MapAppendedStage)
                .ToList();
            logger.Info(
                $"Stage execution finished [stageId={stage.StageId}, success={stageResult.IsSuccess}, waitingForApproval={stageResult.IsWaitingForApproval}, errorCode={stageResult.ErrorCode ?? "-"}, nextStageId={(stageResult.NextStageId?.ToString() ?? "-")}, contextItems={stageResult.ContextItems?.Count ?? 0}, appendedStages={stageResult.AppendedStages?.Count ?? 0}, resultPreview={stageResult.Result.ToLogPreview(800)}]");
        }
        catch (Exception ex)
        {
            resultDto.Result = $"{ex.Message}\n{ex.StackTrace}";
            resultDto.IsSuccess = false;
            logger.Error(
                $"Stage execution crashed [stageId={stage.StageId}, handler={stage.StageHandlerName}, error={ex.Message.ToLogPreview(800)}]");
        }
        finally
        {
            resultDto.Logs = logger.Logs;
        }

        using var resultPublishCts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var serializedResult = PipelineMessageSerializer.Serialize(resultDto);
        _logger.LogInformation("Publishing result for StageId {StageId}: {Payload}", stage.StageId, serializedResult);
        await PublishToQueueAsync(_stageResultQueue, serializedResult, resultPublishCts.Token);

        return resultDto.IsSuccess;
    }

    private static WorkerAppendedStageMessage MapAppendedStage(StageInfo stage)
    {
        return new WorkerAppendedStageMessage
        {
            StageId = stage.StageId,
            PipelineId = stage.PipelineId,
            StageName = stage.StageName,
            StageHandlerName = stage.StageHandlerName,
            Description = null,
            Input = NormalizeStageInput(stage.Input),
            Options = stage.Options,
            IsEvent = stage.IsEvent,
        };
    }

    private static string? NormalizeStageInput(object? input)
    {
        return input switch
        {
            null => null,
            string text when string.IsNullOrWhiteSpace(text) => null,
            string text => text,
            _ => PipelineMessageSerializer.Serialize(input)
        };
    }

    private StageExecutionData BuildExecutionData(StageNextDto stage)
    {
        var handlerName = stage.StageHandlerName ?? throw new InvalidOperationException("StageHandlerName is required.");

        var data = new StageExecutionData
        {
            JsonInput = stage.Input,
            StageId = stage.StageId,
            PipelineId = stage.PipelineId,
            ContextItems = stage.ContextItems,
        };

        if (_handlerInstances.TryGetValue(handlerName, out var instance))
        {
            data.HandlerInstance = instance;
            data.HandlerType = instance.GetType();
            data.InputType = ResolveInputType(instance.GetType());
            return data;
        }

        if (_handlerTypes.TryGetValue(handlerName, out var handlerType))
        {
            data.HandlerType = handlerType;
            data.InputType = ResolveInputType(handlerType);
            return data;
        }

        throw new InvalidOperationException($"Handler '{handlerName}' is not registered.");
    }

    private static Type? ResolveInputType(Type handlerType)
    {
        var interfaceType = handlerType
            .GetInterfaces()
            .FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IStageHandler<>));

        return interfaceType?.GetGenericArguments().FirstOrDefault();
    }

    private List<string> GetRegisteredHandlerNames()
    {
        var handlerNames = _handlerInstances.Keys.Union(_handlerTypes.Keys).ToList();

        if (handlerNames.Count == 0)
            throw new InvalidOperationException("No handlers registered for PipelineRunner.");

        return handlerNames;
    }

    private async Task SetStatusToRunning(int stageId, CancellationToken stoppingToken)
    {
        var data = new
        {
            StageId = stageId,
            Status = "Running",
        };

        await PublishToQueueAsync(_stageSetStatusQueue, PipelineMessageSerializer.Serialize(data), stoppingToken);
    }

    private bool EnsureQueueExists(string queueName, bool markWorkerDegradedOnMissing = true)
    {
        if (_connection is null)
            throw new InvalidOperationException("RabbitMQ connection is not available.");

        using var probeChannel = _connection.CreateModel();
        try
        {
            probeChannel.QueueDeclarePassive(queueName);
            return true;
        }
        catch (OperationInterruptedException ex) when (IsQueueNotFound(ex))
        {
            if (_runnerOptions.QueueProvisioningMode == QueueProvisioningMode.AssertOnly)
            {
                if (markWorkerDegradedOnMissing)
                {
                    SetState(
                        WorkerStates.Degraded,
                        "Waiting for RabbitMQ queues to become available.",
                        $"Queue '{queueName}' does not exist yet.");
                }

                _logger.LogWarning(
                    "Queue {QueueName} does not exist yet and QueueProvisioningMode is AssertOnly. Retrying in {RetryDelay}.",
                    queueName,
                    RetryDelay);
                return false;
            }
        }
        catch (OperationInterruptedException ex) when (IsPreconditionFailed(ex))
        {
            throw BuildQueueConfigurationException(queueName, "passive assert", ex);
        }

        if (_runnerOptions.QueueProvisioningMode != QueueProvisioningMode.Ensure)
            return false;

        using var createChannel = _connection.CreateModel();
        try
        {
            createChannel.QueueDeclare(
                queue: queueName,
                durable: true,
                exclusive: false,
                autoDelete: false,
                arguments: BuildQueueArguments());
        }
        catch (OperationInterruptedException ex) when (IsPreconditionFailed(ex))
        {
            throw BuildQueueConfigurationException(queueName, "ensure declare", ex);
        }

        _logger.LogInformation("Created missing queue {QueueName} using bootstrap DLQ arguments.", queueName);
        return true;
    }

    private void SubscribeToStageNextQueue(string queueName, ushort prefetch, CancellationToken stoppingToken)
    {
        if (_connection is null || !_connection.IsOpen)
            throw new InvalidOperationException("RabbitMQ connection is not available.");

        var consumerChannel = _connection.CreateModel();
        consumerChannel.BasicQos(0, prefetch, false);

        var consumer = new AsyncEventingBasicConsumer(consumerChannel);
        consumer.Received += (_, ea) => HandleMessageAsync(consumerChannel, ea, stoppingToken);

        var consumerTag = consumerChannel.BasicConsume(queue: queueName, autoAck: false, consumer: consumer);
        _consumerChannels.Add(consumerChannel);
        _activeConsumers.Add((consumerChannel, consumerTag));
        MarkStageNextQueueSubscribed(queueName);
    }

    private void TrySubscribeMissingStageNextQueues(CancellationToken stoppingToken)
    {
        if (_connection is null || !_connection.IsOpen)
            return;

        var missingQueues = SnapshotMissingStageNextQueueNames();
        if (missingQueues.Length == 0)
            return;

        var prefetch = (ushort)Math.Clamp(_bootstrap?.MessageBroker.Prefetch <= 0 ? 1 : _bootstrap?.MessageBroker.Prefetch ?? 1, 1, ushort.MaxValue);
        foreach (var queue in missingQueues)
        {
            if (stoppingToken.IsCancellationRequested || _connection is null || !_connection.IsOpen)
                return;

            if (!EnsureQueueExists(queue, markWorkerDegradedOnMissing: false))
                continue;

            SubscribeToStageNextQueue(queue, prefetch, stoppingToken);
            _logger.LogInformation("Subscribed to newly available StageNext queue {QueueName}.", queue);
            SetReadyStateForCurrentConnection();
        }
    }

    private void ResetStageNextQueueCoverage()
    {
        lock (_queueStatusSync)
        {
            _activeStageNextQueues.Clear();
            _missingStageNextQueues.Clear();
            foreach (var queue in _stageNextQueues)
                _missingStageNextQueues.Add(queue);
        }
    }

    private void MarkStageNextQueueSubscribed(string queueName)
    {
        lock (_queueStatusSync)
        {
            _missingStageNextQueues.Remove(queueName);
            _activeStageNextQueues.Add(queueName);
        }
    }

    private void MarkStageNextQueueMissing(string queueName)
    {
        lock (_queueStatusSync)
        {
            if (_activeStageNextQueues.Contains(queueName))
                return;

            _missingStageNextQueues.Add(queueName);
        }
    }

    private (int Active, int Total, string[] MissingNames) SnapshotStageNextQueueCoverage()
    {
        lock (_queueStatusSync)
        {
            return (
                _activeStageNextQueues.Count,
                _stageNextQueues.Count,
                _missingStageNextQueues.OrderBy(queue => queue, StringComparer.Ordinal).ToArray());
        }
    }

    private string[] SnapshotMissingStageNextQueueNames()
    {
        lock (_queueStatusSync)
        {
            return _missingStageNextQueues.OrderBy(queue => queue, StringComparer.Ordinal).ToArray();
        }
    }

    private void SetReadyStateForCurrentConnection()
    {
        if (_connection is null || !_connection.IsOpen)
            return;

        var (activeStageNext, totalStageNext, missingStageNextNames) = SnapshotStageNextQueueCoverage();
        if (totalStageNext == 0)
        {
            SetState(WorkerStates.Ready, "Worker connected and listening.");
            return;
        }

        if (missingStageNextNames.Length == 0)
        {
            SetState(
                WorkerStates.Ready,
                $"Worker connected. StageNext subscriptions active: {activeStageNext}/{totalStageNext}.");
            return;
        }

        SetState(
            WorkerStates.Ready,
            $"Worker connected. StageNext subscriptions active: {activeStageNext}/{totalStageNext}. Waiting for {missingStageNextNames.Length} queue(s).");
    }

    private IDictionary<string, object> BuildQueueArguments()
    {
        var dlqEnabled = _bootstrap?.MessageBroker.DlqEnabled ?? false;
        var dlqTtlSec = _bootstrap?.MessageBroker.DlqTtlSec ?? 0;

        return new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
        {
            ["dlqEnabled"] = dlqEnabled,
            ["dlqTtlSec"] = dlqTtlSec,
        };
    }

    private WorkerConfigurationException BuildQueueConfigurationException(
        string queueName,
        string operation,
        OperationInterruptedException ex)
    {
        var dlqEnabled = _bootstrap?.MessageBroker.DlqEnabled ?? false;
        var dlqTtlSec = _bootstrap?.MessageBroker.DlqTtlSec ?? 0;
        var brokerReason = ex.ShutdownReason?.ReplyText ?? ex.Message;

        return new WorkerConfigurationException(
            $"Queue '{queueName}' failed {operation} with PRECONDITION_FAILED (406). " +
            $"Expected arguments: dlqEnabled={dlqEnabled}, dlqTtlSec={dlqTtlSec}. " +
            $"Broker reason: {brokerReason}",
            ex);
    }

    private static bool IsQueueNotFound(OperationInterruptedException ex)
    {
        return ex.ShutdownReason?.ReplyCode == 404;
    }

    private static bool IsPreconditionFailed(OperationInterruptedException ex)
    {
        return ex.ShutdownReason?.ReplyCode == 406;
    }

    private async Task PublishToQueueAsync(string queueName, string payload, CancellationToken stoppingToken)
    {
        await _publishLock.WaitAsync(stoppingToken);
        try
        {
            if (_publishChannel is null || !_publishChannel.IsOpen)
                throw new InvalidOperationException("RabbitMQ publish channel is not available.");

            var properties = _publishChannel.CreateBasicProperties();
            properties.Persistent = true;

            var body = Encoding.UTF8.GetBytes(payload);
            _publishChannel.BasicPublish(exchange: "", routingKey: queueName, basicProperties: properties, body: body);
        }
        finally
        {
            _publishLock.Release();
        }
    }

    private long GetQueueLag()
    {
        if (_connection is null || !_connection.IsOpen || _stageNextQueues.Count == 0)
            return 0;

        try
        {
            using var channel = _connection.CreateModel();
            long lag = 0;

            foreach (var queueName in _stageNextQueues)
            {
                var queue = channel.QueueDeclarePassive(queueName);
                lag += (long)queue.MessageCount;
            }

            return lag;
        }
        catch
        {
            return 0;
        }
    }

    private double GetCpuPercent()
    {
        try
        {
            _process.Refresh();
            var now = DateTimeOffset.UtcNow;
            var elapsedMs = (now - _lastCpuSampleAt).TotalMilliseconds;
            if (elapsedMs <= 0)
                return 0;

            var currentCpu = _process.TotalProcessorTime;
            var cpuMs = (currentCpu - _lastCpuTotal).TotalMilliseconds;

            _lastCpuSampleAt = now;
            _lastCpuTotal = currentCpu;

            var cpuPercent = cpuMs / (elapsedMs * Environment.ProcessorCount) * 100d;
            if (double.IsNaN(cpuPercent) || double.IsInfinity(cpuPercent) || cpuPercent < 0)
                return 0;

            return Math.Round(cpuPercent, 2);
        }
        catch
        {
            return 0;
        }
    }

    private TimeSpan GetHeartbeatInterval()
    {
        var interval = _bootstrap?.Heartbeat.IntervalSec ?? 15;
        if (interval <= 0)
            interval = 15;

        return TimeSpan.FromSeconds(interval);
    }

    private string ResolveWorkerName()
    {
        if (!string.IsNullOrWhiteSpace(_runnerOptions.WorkerName))
            return _runnerOptions.WorkerName;

        var fromEnv = Environment.GetEnvironmentVariable("PIPELOGIQ_WORKER_NAME");
        if (!string.IsNullOrWhiteSpace(fromEnv))
            return fromEnv;

        return "pipelogiq-dotnet-worker";
    }

    private string ResolveInstanceId()
    {
        if (!string.IsNullOrWhiteSpace(_runnerOptions.InstanceId))
            return _runnerOptions.InstanceId;

        var fromEnv = Environment.GetEnvironmentVariable("PIPELOGIQ_INSTANCE_ID");
        if (!string.IsNullOrWhiteSpace(fromEnv))
            return fromEnv;

        return $"{Environment.MachineName}-{Environment.ProcessId}";
    }

    private string ResolveWorkerVersion()
    {
        if (!string.IsNullOrWhiteSpace(_runnerOptions.WorkerVersion))
            return _runnerOptions.WorkerVersion;

        return Assembly.GetEntryAssembly()?.GetName().Version?.ToString()
               ?? Assembly.GetExecutingAssembly().GetName().Version?.ToString()
               ?? "unknown";
    }

    private static string ResolveSdkVersion()
    {
        return Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "unknown";
    }

    private string ResolveEnvironment()
    {
        if (!string.IsNullOrWhiteSpace(_runnerOptions.Environment))
            return _runnerOptions.Environment;

        return Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT")
               ?? Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT")
               ?? "prod";
    }

    private void SetState(string state, string? message = null, string? error = null)
    {
        string previousState;
        string? previousMessage;
        string? previousError;
        string? nextMessage;
        string? nextError;

        lock (_stateSync)
        {
            previousState = _workerState;
            previousMessage = _statusMessage;
            previousError = _lastError;

            nextMessage = string.IsNullOrWhiteSpace(message) ? _statusMessage : message.Trim();
            if (string.IsNullOrWhiteSpace(error))
            {
                nextError = state is WorkerStates.Ready or WorkerStates.Starting ? null : _lastError;
            }
            else
            {
                nextError = error.Trim();
            }

            if (previousState == state &&
                string.Equals(previousMessage, nextMessage, StringComparison.Ordinal) &&
                string.Equals(previousError, nextError, StringComparison.Ordinal))
            {
                return;
            }

            _workerState = state;
            _statusMessage = nextMessage;
            _lastError = nextError;
        }

        LogStateTransition(previousState, state, nextMessage, nextError);
        EnqueueStateTransitionEvent(previousState, state, nextMessage, nextError);
    }

    private (string State, string? Message, string? LastError) SnapshotState()
    {
        lock (_stateSync)
        {
            return (_workerState, _statusMessage, _lastError);
        }
    }

    private async Task TrySendShutdownAsync(CancellationToken stoppingToken)
    {
        if (string.IsNullOrWhiteSpace(_workerId) || string.IsNullOrWhiteSpace(_workerSessionToken))
            return;

        try
        {
            await FlushPendingWorkerEventsAsync(stoppingToken);

            var shutdownRequest = new WorkerShutdownRequest
            {
                WorkerId = _workerId,
                State = WorkerStates.Stopped,
                Message = "Worker shutdown.",
                Metadata = new Dictionary<string, object?>
                {
                    ["jobsProcessed"] = Interlocked.Read(ref _jobsProcessed),
                    ["jobsFailed"] = Interlocked.Read(ref _jobsFailed),
                }
            };

            await _apiClient.PostWorkerShutdownAsync(_workerSessionToken, shutdownRequest, stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to post worker shutdown.");
        }
    }

    private static bool IsSessionAuthFailure(HttpRequestException ex)
    {
        return ex.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden;
    }

    private void LogStateTransition(string previousState, string nextState, string? message, string? error)
    {
        var logMessage = BuildStateLogMessage(previousState, nextState, message, error);
        switch (nextState)
        {
            case WorkerStates.Error:
                _logger.LogError("{Message}", logMessage);
                break;
            case WorkerStates.Degraded:
                _logger.LogWarning("{Message}", logMessage);
                break;
            default:
                _logger.LogInformation("{Message}", logMessage);
                break;
        }
    }

    private void EnqueueStateTransitionEvent(string previousState, string nextState, string? message, string? error)
    {
        var details = new Dictionary<string, object?>
        {
            ["from"] = previousState,
            ["to"] = nextState,
        };

        if (!string.IsNullOrWhiteSpace(message))
            details["statusReason"] = message;

        if (!string.IsNullOrWhiteSpace(error))
            details["lastError"] = error;

        var eventType = previousState == nextState ? "worker.status_updated" : "worker.state_changed";
        EnqueueWorkerEvent(
            nextState is WorkerStates.Error ? "ERROR" : nextState is WorkerStates.Degraded ? "WARN" : "INFO",
            eventType,
            BuildStateLogMessage(previousState, nextState, message, error),
            details);
    }

    private void EnqueueWorkerEvent(
        string level,
        string eventType,
        string message,
        Dictionary<string, object?>? details = null)
    {
        if (string.IsNullOrWhiteSpace(eventType) || string.IsNullOrWhiteSpace(message))
            return;

        _pendingWorkerEvents.Enqueue(new WorkerEventItem
        {
            Timestamp = DateTime.UtcNow,
            Level = string.IsNullOrWhiteSpace(level) ? "INFO" : level.Trim().ToUpperInvariant(),
            EventType = eventType.Trim(),
            Message = message.Trim(),
            Details = details is { Count: > 0 } ? details : null,
        });

        if (!string.IsNullOrWhiteSpace(_workerId) && !string.IsNullOrWhiteSpace(_workerSessionToken))
            _ = Task.Run(() => FlushPendingWorkerEventsAsync(CancellationToken.None));
    }

    private async Task FlushPendingWorkerEventsAsync(CancellationToken stoppingToken)
    {
        if (string.IsNullOrWhiteSpace(_workerId) || string.IsNullOrWhiteSpace(_workerSessionToken))
            return;

        if (_pendingWorkerEvents.IsEmpty)
            return;

        if (!await _workerEventFlushLock.WaitAsync(0, stoppingToken))
            return;

        try
        {
            while (!stoppingToken.IsCancellationRequested && !_pendingWorkerEvents.IsEmpty)
            {
                var batch = new List<WorkerEventItem>();
                while (batch.Count < 50 && _pendingWorkerEvents.TryDequeue(out var item))
                    batch.Add(item);

                if (batch.Count == 0)
                    return;

                try
                {
                    await _apiClient.PostWorkerEventAsync(_workerSessionToken, new WorkerEventRequest
                    {
                        WorkerId = _workerId!,
                        Events = batch,
                    }, stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    RequeueWorkerEvents(batch);
                    throw;
                }
                catch (Exception ex)
                {
                    RequeueWorkerEvents(batch);
                    _logger.LogDebug(ex, "Failed to flush worker diagnostic events.");
                    return;
                }
            }
        }
        finally
        {
            _workerEventFlushLock.Release();
        }
    }

    private void RequeueWorkerEvents(IEnumerable<WorkerEventItem> events)
    {
        foreach (var item in events)
            _pendingWorkerEvents.Enqueue(item);
    }

    private static string BuildStateLogMessage(string previousState, string nextState, string? message, string? error)
    {
        var builder = new StringBuilder();
        if (previousState == nextState)
        {
            builder.Append("Worker status updated");
        }
        else
        {
            builder.Append("Worker state changed from ")
                .Append(previousState)
                .Append(" to ")
                .Append(nextState);
        }

        if (!string.IsNullOrWhiteSpace(message))
            builder.Append(": ").Append(message.Trim());

        if (!string.IsNullOrWhiteSpace(error))
            builder.Append(" [error=").Append(error.Trim()).Append(']');

        return builder.ToString();
    }

    private async Task<bool> IsStageAlreadyTerminalAsync(StageNextDto stage)
    {
        if (!stage.PipelineId.HasValue)
            return false;

        try
        {
            var pipeline = await _apiClient.GetPipelineAsync(stage.PipelineId.Value);
            var current = pipeline?.Stages?.FirstOrDefault(s => s.Id == stage.StageId);
            if (current is null)
                return false;

            return current.Status is "Completed" or "Failed" or "Skipped";
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not verify stage {StageId} status before execution — proceeding.", stage.StageId);
            return false;
        }
    }

    private bool ShouldRequeueAfterFailure()
    {
        if (_bootstrap is null)
            return true;

        return !_bootstrap.MessageBroker.DlqEnabled;
    }

    private static async Task AwaitSilently(Task task)
    {
        try
        {
            await task;
        }
        catch
        {
        }
    }

    private static async Task DelayBeforeRetry(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(RetryDelay, stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }

    private void CancelConsumers()
    {
        foreach (var (channel, consumerTag) in _activeConsumers)
        {
            try
            {
                if (channel.IsOpen)
                    channel.BasicCancel(consumerTag);
            }
            catch
            {
            }
        }

        _activeConsumers.Clear();
    }

    private async Task DrainInFlightJobsAsync(TimeSpan timeout)
    {
        var inFlight = Interlocked.Read(ref _inFlightJobs);
        if (inFlight == 0)
            return;

        _logger.LogInformation("Draining {InFlight} in-flight job(s), timeout {Timeout}s.", inFlight, (int)timeout.TotalSeconds);

        var deadline = DateTimeOffset.UtcNow + timeout;
        while (Interlocked.Read(ref _inFlightJobs) > 0 && DateTimeOffset.UtcNow < deadline)
        {
            await Task.Delay(250);
        }

        var remaining = Interlocked.Read(ref _inFlightJobs);
        if (remaining > 0)
            _logger.LogWarning("Drain timeout reached. {Remaining} job(s) still in-flight.", remaining);
        else
            _logger.LogInformation("All in-flight jobs drained successfully.");
    }

    private void DisposeMessaging()
    {
        _activeConsumers.Clear();

        foreach (var channel in _consumerChannels)
            SafeDispose(channel);

        _consumerChannels.Clear();
        ResetStageNextQueueCoverage();

        if (_publishChannel is not null)
        {
            SafeDispose(_publishChannel);
            _publishChannel = null;
        }

        if (_connection is not null)
        {
            SafeDispose(_connection);
            _connection = null;
        }
    }

    private static void SafeAck(IModel channel, ulong deliveryTag)
    {
        try
        {
            if (channel.IsOpen)
                channel.BasicAck(deliveryTag, multiple: false);
        }
        catch
        {
        }
    }

    private static void SafeNack(IModel channel, ulong deliveryTag, bool requeue)
    {
        try
        {
            if (channel.IsOpen)
                channel.BasicNack(deliveryTag, multiple: false, requeue: requeue);
        }
        catch
        {
        }
    }

    private static void SafeDispose(IModel channel)
    {
        try
        {
            if (channel.IsOpen)
                channel.Close();
        }
        catch
        {
        }

        try
        {
            channel.Dispose();
        }
        catch
        {
        }
    }

    private static void SafeDispose(IConnection connection)
    {
        try
        {
            if (connection.IsOpen)
                connection.Close();
        }
        catch
        {
        }

        try
        {
            connection.Dispose();
        }
        catch
        {
        }
    }

    private sealed class WorkerStageResultMessage
    {
        public int? PipelineId { get; set; }
        public int StageId { get; set; }
        public string Result { get; set; } = string.Empty;
        public bool IsSuccess { get; set; }
        public string? ErrorCode { get; set; }
        public int? NextStageId { get; set; }
        public bool RunNextIfCurrentFailed { get; set; }
        public bool IsWaitingForApproval { get; set; }
        public List<StageLogDto>? Logs { get; set; }
        public List<ContextItem>? ContextItems { get; set; }
        public List<WorkerAppendedStageMessage>? AppendedStages { get; set; }
    }

    private sealed class WorkerAppendedStageMessage
    {
        public int? StageId { get; set; }
        public int? PipelineId { get; set; }
        public string? StageName { get; set; }
        public string? StageHandlerName { get; set; }
        public string? Description { get; set; }
        public string? Input { get; set; }
        public StageOptions? Options { get; set; }
        public bool? IsEvent { get; set; }
    }

    private sealed class WorkerConfigurationException(string message, Exception? innerException = null)
        : InvalidOperationException(message, innerException);

    private static class WorkerStates
    {
        public const string Starting = "starting";
        public const string Ready = "ready";
        public const string Degraded = "degraded";
        public const string Draining = "draining";
        public const string Stopped = "stopped";
        public const string Error = "error";
    }
}
