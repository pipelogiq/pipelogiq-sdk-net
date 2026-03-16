using PipelogiqSDK.Agent.Models;
using PipelogiqSDK.Configuration;
using PipelogiqSDK.Contracts;
using PipelogiqSDK.Execution;

namespace PipelogiqSDK.Api;

/// <summary>
/// Pipelogiq HTTP client for pipelines, logs, and worker endpoints.
/// </summary>
public class PipelogiqApiClient : BaseApiClient
{
    private readonly string? _apiKey;

    /// <summary>
    /// Initializes a client from runner options.
    /// </summary>
    /// <param name="options">Runner options.</param>
    public PipelogiqApiClient(PipelogiqRunnerOptions options) : base(options.ApiUrl, options.ApiKey)
    {
        _apiKey = options.ApiKey;
    }

    /// <summary>
    /// Initializes a client from explicit base URL and API key.
    /// </summary>
    /// <param name="baseUrl">Base API URL.</param>
    /// <param name="apiKey">Optional API key.</param>
    public PipelogiqApiClient(string baseUrl, string? apiKey = null) : base(baseUrl, apiKey)
    {
        _apiKey = apiKey;
    }

    internal PipelogiqApiClient(string baseUrl, string? apiKey, HttpMessageHandler handler) : base(baseUrl, apiKey, handler)
    {
        _apiKey = apiKey;
    }

    /// <summary>
    /// Creates a pipeline via API.
    /// </summary>
    /// <param name="pipeline">Pipeline payload.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Created pipeline response.</returns>
    public Task<PipelineResponse> PostPipelineAsync(PipelineDto pipeline, CancellationToken ct = default)
    {
        return PostAsync<PipelineResponse>("pipelines", pipeline, ct);
    }

    /// <summary>
    /// Sends an event payload via pipeline endpoint.
    /// </summary>
    /// <param name="pipeline">Event payload.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Created event pipeline response.</returns>
    public Task<PipelineResponse> PostEventAsync(PipelineDto pipeline, CancellationToken ct = default)
    {
        return PostAsync<PipelineResponse>("pipelines", pipeline, ct);
    }

    /// <summary>
    /// Sends a log record.
    /// </summary>
    /// <param name="logDto">Log payload.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Created log payload.</returns>
    public Task<LogDto> PostLogAsync(LogDto logDto, CancellationToken ct = default)
    {
        return PostAsync<LogDto>("logs", logDto, ct);
    }

    /// <summary>
    /// Bootstraps worker session.
    /// </summary>
    /// <param name="request">Bootstrap request payload.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Bootstrap response.</returns>
    public Task<WorkerBootstrapResponse> PostWorkerBootstrapAsync(
        WorkerBootstrapRequest request,
        CancellationToken ct = default)
    {
        var apiKey = ResolveApiKey();
        var headers = new Dictionary<string, string>
        {
            ["Authorization"] = $"Bearer {apiKey}",
            ["X-API-Key"] = apiKey
        };

        return PostAsync<WorkerBootstrapResponse>("workers/bootstrap", request, ct, headers);
    }

    /// <summary>
    /// Sends worker heartbeat.
    /// </summary>
    /// <param name="workerSessionToken">Worker session token.</param>
    /// <param name="request">Heartbeat payload.</param>
    /// <param name="ct">Cancellation token.</param>
    public Task PostWorkerHeartbeatAsync(
        string workerSessionToken,
        WorkerHeartbeatRequest request,
        CancellationToken ct = default)
    {
        return PostAsync("workers/heartbeat", request, ct, BuildSessionHeaders(workerSessionToken));
    }

    /// <summary>
    /// Sends worker event.
    /// </summary>
    /// <param name="workerSessionToken">Worker session token.</param>
    /// <param name="request">Worker event payload.</param>
    /// <param name="ct">Cancellation token.</param>
    public Task PostWorkerEventAsync(
        string workerSessionToken,
        WorkerEventRequest request,
        CancellationToken ct = default)
    {
        return PostAsync("workers/events", request, ct, BuildSessionHeaders(workerSessionToken));
    }

    /// <summary>
    /// Sends worker shutdown event.
    /// </summary>
    /// <param name="workerSessionToken">Worker session token.</param>
    /// <param name="request">Shutdown payload.</param>
    /// <param name="ct">Cancellation token.</param>
    public Task PostWorkerShutdownAsync(
        string workerSessionToken,
        WorkerShutdownRequest request,
        CancellationToken ct = default)
    {
        return PostAsync("workers/shutdown", request, ct, BuildSessionHeaders(workerSessionToken));
    }

    /// <summary>
    /// Requests RabbitMQ connection details from API.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>RabbitMQ connection settings.</returns>
    public Task<RabbitConnectionResponse> GetRabbitMqConnectionAsync(CancellationToken ct = default)
    {
        return GetAsync<RabbitConnectionResponse>("rabbitmq/connection", ct);
    }

    /// <summary>
    /// Appends stages to a running pipeline (used by AI agent orchestrator).
    /// </summary>
    /// <param name="pipelineId">Pipeline identifier.</param>
    /// <param name="request">Stages to append.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Response containing appended stages with assigned IDs.</returns>
    public Task<AppendStagesResponse> AppendAgentStagesAsync(
        int pipelineId,
        AppendStagesRequest request,
        CancellationToken ct = default)
    {
        return PostAsync<AppendStagesResponse>($"pipelines/{pipelineId}/stages", request, ct);
    }

    /// <summary>
    /// Resumes a stage that is waiting for external approval.
    /// </summary>
    /// <param name="stageId">Stage identifier to resume.</param>
    /// <param name="approved">Whether the pending action was approved.</param>
    /// <param name="rejectionReason">Optional reason for rejection.</param>
    /// <param name="ct">Cancellation token.</param>
    public Task ResumeStageApprovalAsync(
        int stageId,
        bool approved,
        string? rejectionReason = null,
        CancellationToken ct = default)
    {
        var request = new ResumeStageRequest { Approved = approved, RejectionReason = rejectionReason };
        return PostAsync($"stages/{stageId}/resume", request, ct);
    }

    private string ResolveApiKey()
    {
        var value = _apiKey ?? GlobalRunnerContext.Token;
        if (string.IsNullOrWhiteSpace(value))
            throw new InvalidOperationException("API key is required for worker bootstrap.");

        return value;
    }

    private static Dictionary<string, string> BuildSessionHeaders(string workerSessionToken)
    {
        if (string.IsNullOrWhiteSpace(workerSessionToken))
            throw new InvalidOperationException("Worker session token is required.");

        return new Dictionary<string, string>
        {
            ["X-Worker-Session"] = workerSessionToken,
            ["Authorization"] = $"Bearer {workerSessionToken}"
        };
    }
}
