using System.Diagnostics;
using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Json;
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
using JsonSerializer = System.Text.Json.JsonSerializer;

namespace PipelogiqSDK.Runner;

/// <summary>
/// Worker runtime that consumes stage messages, executes handlers, and publishes results.
/// </summary>
public class PipelineRunner
{
    private static readonly TimeSpan RetryDelay = TimeSpan.FromMinutes(1);
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly ILogger<PipelineRunner> _logger;
    private readonly PipelogiqApiClient _apiClient;
    private readonly PipelogiqRunnerOptions _runnerOptions;
    private readonly StageExecutor _stageExecutor;
    private readonly SemaphoreSlim _publishLock = new(1, 1);
    private readonly object _stateSync = new();

    private readonly Dictionary<string, Type> _handlerTypes = new();
    private readonly Dictionary<string, IStageHandler> _handlerInstances = new();
    private readonly List<IModel> _consumerChannels = new();

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
        var supportedHandlers = GetRegisteredHandlerNames();
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
                _logger.LogWarning(ex, "Worker bootstrap failed. Retrying in {RetryDelay}.", RetryDelay);
                await DelayBeforeRetry(stoppingToken);
                continue;
            }

            using var sessionCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
            var sessionToken = sessionCts.Token;
            var heartbeatTask = RunHeartbeatLoopAsync(sessionToken);

            try
            {
                ConnectAndSubscribe(sessionToken);
                SetState(WorkerStates.Ready, "Worker connected and listening.");
                await WaitForReconnectSignalAsync(sessionToken);
            }
            catch (OperationCanceledException) when (sessionToken.IsCancellationRequested)
            {
            }
            catch (WorkerConfigurationException ex)
            {
                SetState(WorkerStates.Error, "Worker configuration is invalid.", ex.Message);
                _logger.LogError(ex, "Worker stopped due to configuration error.");
                return;
            }
            catch (OperationInterruptedException ex) when (IsPreconditionFailed(ex))
            {
                var reason = ex.ShutdownReason?.ReplyText ?? ex.Message;
                SetState(WorkerStates.Error, "RabbitMQ queue configuration mismatch.", reason);
                _logger.LogError(ex, "Worker stopped due to RabbitMQ PRECONDITION_FAILED (406).");
                return;
            }
            catch (Exception ex)
            {
                SetState(WorkerStates.Degraded, "Broker connection failed.", ex.Message);
                _logger.LogWarning(ex, "RabbitMQ connection attempt failed.");
            }
            finally
            {
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

        if (stoppingToken.IsCancellationRequested)
            SetState(WorkerStates.Draining, "Worker draining.");

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

        SetState(WorkerStates.Starting, "Worker bootstrap completed.");
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

                if (_connection?.IsOpen == true)
                    SetState(WorkerStates.Ready, "Worker connected and listening.");
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (HttpRequestException ex) when (IsSessionAuthFailure(ex))
            {
                _sessionInvalid = true;
                SetState(WorkerStates.Degraded, "Worker session rejected by server.", ex.Message);
                _logger.LogWarning(
                    "Worker heartbeat rejected with {StatusCode}. Re-bootstrap is required.",
                    ex.StatusCode);
                return;
            }
            catch (Exception ex)
            {
                SetState(WorkerStates.Degraded, "Failed to send heartbeat.", ex.Message);
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

        return metadata;
    }

    private void ConnectAndSubscribe(CancellationToken stoppingToken)
    {
        if (_bootstrap is null)
            throw new InvalidOperationException("Worker is not bootstrapped.");

        var factory = new ConnectionFactory
        {
            Uri = new Uri(_bootstrap.MessageBroker.ConnectionString!),
            DispatchConsumersAsync = true,
            AutomaticRecoveryEnabled = false,
            ClientProvidedName = $"PipelogiqSDK.Runner/{ResolveWorkerName()}",
        };

        _connection = factory.CreateConnection();
        _publishChannel = _connection.CreateModel();
        EnsureQueueExists(_stageResultQueue);
        EnsureQueueExists(_stageSetStatusQueue);

        var prefetch = (ushort)Math.Clamp(_bootstrap.MessageBroker.Prefetch <= 0 ? 1 : _bootstrap.MessageBroker.Prefetch, 1, ushort.MaxValue);

        foreach (var queue in _stageNextQueues)
        {
            EnsureQueueExists(queue);

            var consumerChannel = _connection.CreateModel();
            consumerChannel.BasicQos(0, prefetch, false);

            var consumer = new AsyncEventingBasicConsumer(consumerChannel);
            consumer.Received += (_, ea) => HandleMessageAsync(consumerChannel, ea, stoppingToken);

            consumerChannel.BasicConsume(queue: queue, autoAck: false, consumer: consumer);
            _consumerChannels.Add(consumerChannel);
        }
    }

    private async Task WaitForReconnectSignalAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            if (_sessionInvalid)
                return;

            if (_connection is null || !_connection.IsOpen)
            {
                SetState(WorkerStates.Degraded, "Broker connection is closed.");
                return;
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
            parsedMessage = JsonSerializer.Deserialize<StageNextDto>(rawMessage, JsonOptions);
        }
        catch (Exception ex)
        {
            Interlocked.Increment(ref _jobsFailed);
            SetState(WorkerStates.Degraded, "Received malformed stage payload.", ex.Message);
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
            _logger.LogInformation("Received StageNext: {Payload}", JsonSerializer.Serialize(parsedMessage));
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
        var resultDto = new StageResultDto
        {
            PipelineId = stage.PipelineId,
            StageId = stage.StageId,
        };

        var logger = new PipelineLogger();

        try
        {
            var executionData = BuildExecutionData(stage);
            executionData.Logger = logger;
            await SetStatusToRunning(stage.StageId, stoppingToken);

            var stageResult = await _stageExecutor.ExecuteStageHandlerAsync(executionData);

            resultDto.Result = stageResult.Result;
            resultDto.IsSuccess = stageResult.IsSuccess;
            resultDto.ContextItems = stageResult.ContextItems;
            resultDto.NextStageId = stageResult.NextStageId;
            resultDto.RunNextIfCurrentFailed = stageResult.RunNextIfCurrentFailed;
        }
        catch (Exception ex)
        {
            resultDto.Result = $"{ex.Message}\n{ex.StackTrace}";
            resultDto.IsSuccess = false;
        }
        finally
        {
            resultDto.Logs = logger.Logs;
        }

        _logger.LogInformation("Publishing result for StageId {StageId}: {Payload}", stage.StageId, JsonSerializer.Serialize(resultDto));
        await PublishToQueueAsync(_stageResultQueue, JsonSerializer.Serialize(resultDto), stoppingToken);

        return resultDto.IsSuccess;
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

        await PublishToQueueAsync(_stageSetStatusQueue, JsonSerializer.Serialize(data), stoppingToken);
    }

    private void EnsureQueueExists(string queueName)
    {
        if (_connection is null)
            throw new InvalidOperationException("RabbitMQ connection is not available.");

        using var probeChannel = _connection.CreateModel();
        try
        {
            probeChannel.QueueDeclarePassive(queueName);
            return;
        }
        catch (OperationInterruptedException ex) when (IsQueueNotFound(ex))
        {
            if (_runnerOptions.QueueProvisioningMode == QueueProvisioningMode.AssertOnly)
            {
                throw new WorkerConfigurationException(
                    $"Queue '{queueName}' does not exist. QueueProvisioningMode is AssertOnly, so the SDK will not create it.");
            }
        }
        catch (OperationInterruptedException ex) when (IsPreconditionFailed(ex))
        {
            throw BuildQueueConfigurationException(queueName, "passive assert", ex);
        }

        if (_runnerOptions.QueueProvisioningMode != QueueProvisioningMode.Ensure)
            return;

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
        lock (_stateSync)
        {
            _workerState = state;

            if (!string.IsNullOrWhiteSpace(message))
                _statusMessage = message;

            if (string.IsNullOrWhiteSpace(error))
            {
                if (state is WorkerStates.Ready or WorkerStates.Starting)
                    _lastError = null;
            }
            else
            {
                _lastError = error;
            }
        }
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

    private void DisposeMessaging()
    {
        foreach (var channel in _consumerChannels)
            SafeDispose(channel);

        _consumerChannels.Clear();

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
