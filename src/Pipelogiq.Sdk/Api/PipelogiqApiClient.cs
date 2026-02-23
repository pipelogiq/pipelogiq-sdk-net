using PipelogiqSDK.Configuration;
using PipelogiqSDK.Contracts;
using PipelogiqSDK.Execution;

namespace PipelogiqSDK.Api;

public class PipelogiqApiClient : BaseApiClient
{
    private readonly string? _apiKey;

    public PipelogiqApiClient(PipelogiqRunnerOptions options) : base(options.ApiUrl, options.ApiKey)
    {
        _apiKey = options.ApiKey;
    }

    public PipelogiqApiClient(string baseUrl, string? apiKey = null) : base(baseUrl, apiKey)
    {
        _apiKey = apiKey;
    }

    public Task<PipelineDto> PostPipelineAsync(PipelineDto pipeline, CancellationToken ct = default)
    {
        return PostAsync<PipelineDto>("pipelines", pipeline, ct);
    }

    public Task<PipelineDto> PostEventAsync(PipelineDto pipeline, CancellationToken ct = default)
    {
        return PostAsync<PipelineDto>("pipelines", pipeline, ct);
    }

    public Task<LogDto> PostLogAsync(LogDto logDto, CancellationToken ct = default)
    {
        return PostAsync<LogDto>("logs", logDto, ct);
    }

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

    public Task PostWorkerHeartbeatAsync(
        string workerSessionToken,
        WorkerHeartbeatRequest request,
        CancellationToken ct = default)
    {
        return PostAsync("workers/heartbeat", request, ct, BuildSessionHeaders(workerSessionToken));
    }

    public Task PostWorkerEventAsync(
        string workerSessionToken,
        WorkerEventRequest request,
        CancellationToken ct = default)
    {
        return PostAsync("workers/events", request, ct, BuildSessionHeaders(workerSessionToken));
    }

    public Task PostWorkerShutdownAsync(
        string workerSessionToken,
        WorkerShutdownRequest request,
        CancellationToken ct = default)
    {
        return PostAsync("workers/shutdown", request, ct, BuildSessionHeaders(workerSessionToken));
    }

    public Task<RabbitConnectionResponse> GetRabbitMqConnectionAsync(CancellationToken ct = default)
    {
        return GetAsync<RabbitConnectionResponse>("rabbitmq/connection", ct);
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
