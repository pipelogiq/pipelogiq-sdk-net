using PipelogiqSDK.Api;
using PipelogiqSDK.Builders;
using PipelogiqSDK.Configuration;
using PipelogiqSDK.Contracts;

var options = new PipelogiqRunnerOptions
{
    ApiUrl = Environment.GetEnvironmentVariable("PIPELOGIQ_API_URL") ?? "http://localhost:8081",
    ApiKey = Environment.GetEnvironmentVariable("PIPELOGIQ_API_KEY")
        ?? throw new InvalidOperationException("PIPELOGIQ_API_KEY is required."),
};

var tenantId = "tenant-42";
var processId = "private-insurance-process-7301";
var orderServiceId = "order-service-9281";
// Opaque and persisted before the first create request; do not put a token,
// claim payload, PII, or raw business identifiers in this key.
var correlationKey = "corr-01J3INSURANCE7WQ8K5Y9M2";

var transientRetry = new StageOptions
{
    MaxRetries = 5,
    RetryInterval = 2,
    RetryOnErrorCodes =
    [
        StageErrorCodes.Timeout,
        StageErrorCodes.UpstreamError,
        StageErrorCodes.RateLimitExceeded,
        StageErrorCodes.TransportUnavailable,
    ],
    Backoff = "exponential",
    MaxRetryInterval = 60,
    Jitter = true,
    TimeOut = 60,
};

// A payment event creates this normal (non-isEvent) pipeline. Pipelogiq does
// not wait for payment and context is not the consumer's business state.
var claimPipeline = await PipelineBuilder
    .Create("private-insurance.claim", options)
    .WithIdempotencyKey($"{correlationKey}:claim:v1")
    .AddKeyword("correlation", correlationKey)
    .AddContextItem("tenantId", tenantId)
    .AddContextItem("privateInsuranceProcessId", processId)
    .AddContextItem("orderServiceId", orderServiceId)
    .AddContextItem("correlationKey", correlationKey)
    .WithAction<ValidateClaimPrerequisitesHandler>("validate-claim-prerequisites")
    .WithAction<EnsureClaimHandler>(
        "ensure-claim",
        new EnsureClaimInput(processId),
        transientRetry)
    .WithAction<PublishMedicalCaseHandler>("publish-insurance-to-medical-case", transientRetry)
    .WithAction<MarkArrivalAllowedHandler>("mark-arrival-allowed", transientRetry)
    .SendAsync();

// Persist this ID in the consumer database in the same outbox/intent flow that
// owns processId. Repeating SendAsync after an HTTP timeout returns this ID.
await ConsumerPipelineIndex.SaveAsync(processId, claimPipeline.Id);

var api = new PipelogiqApiClient(options);
var status = await api.GetPipelineAsync(claimPipeline.Id);
Console.WriteLine(
    $"claim pipeline={status.Id}, status={status.Status}, terminal={status.IsTerminal ?? PipelineStatuses.IsTerminal(status.Status)}");

// Cancellation of an external reservation is a separate short workflow.
var cancellationPipeline = await PipelineBuilder
    .Create("private-insurance.cancel-reservation", options)
    .WithIdempotencyKey($"{correlationKey}:cancel:v1")
    .AddKeyword("correlation", correlationKey)
    .AddContextItem("tenantId", tenantId)
    .AddContextItem("privateInsuranceProcessId", processId)
    .AddContextItem("correlationKey", correlationKey)
    .WithAction<EnsureReservationCancelledHandler>(
        "ensure-external-reservation-cancelled",
        options: transientRetry)
    .WithAction<PersistCancellationResultHandler>("persist-cancellation-result")
    .SendAsync();

await ConsumerPipelineIndex.SaveCancellationAsync(processId, cancellationPipeline.Id);
