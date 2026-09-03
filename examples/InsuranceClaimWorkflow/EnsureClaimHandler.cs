using PipelogiqSDK.Abstractions;
using PipelogiqSDK.Contracts;
using PipelogiqSDK.StageHelper;

public sealed record EnsureClaimInput(string PrivateInsuranceProcessId);

// Consumer-owned state. A production implementation persists this in the
// consumer database; pipeline context is never the source of truth.
public interface IClaimStateStore
{
    Task<ClaimState> GetAsync(string processId, CancellationToken cancellationToken);
    // Atomically performs NotSubmitted -> Submitting, allocates and persists an
    // opaque permanent external request key, and grants exactly one caller the
    // right to issue the first POST. Concurrent callers receive ShouldPost=false.
    Task<ClaimSubmissionStart> BeginSubmissionAsync(
        string processId,
        CancellationToken cancellationToken);
    // Called only after the external status API definitively says that the
    // stable request key was never received. A compare-and-set again grants
    // at most one caller permission to POST with that same key.
    Task<ClaimSubmissionStart> ResumeAfterDefinitiveNotFoundAsync(
        string processId,
        string stableIdempotencyKey,
        CancellationToken cancellationToken);
    Task MarkOutcomeUnknownAsync(string processId, CancellationToken cancellationToken);
    Task MarkConfirmedAsync(string processId, string externalClaimId, CancellationToken cancellationToken);
    Task MarkRejectedAsync(string processId, string reasonCode, CancellationToken cancellationToken);
}

public enum ClaimSubmissionState
{
    NotSubmitted,
    Submitting,
    OutcomeUnknown,
    Confirmed,
    Rejected,
}

public sealed record ClaimState(
    ClaimSubmissionState State,
    string StableIdempotencyKey,
    string? ExternalClaimId = null);

public sealed record ClaimSubmissionStart(ClaimState State, bool ShouldPost);

public interface IClaimGateway
{
    Task<ClaimPostResult> PostOnceAsync(
        string stableIdempotencyKey,
        string processId,
        CancellationToken cancellationToken);
    Task<ClaimStatusResult> GetStatusAsync(
        string stableIdempotencyKey,
        CancellationToken cancellationToken);
}

public sealed record ClaimPostResult(bool Confirmed, bool BusinessRejected, string? ExternalClaimId);
public sealed record ClaimStatusResult(
    bool Confirmed,
    bool BusinessRejected,
    bool Pending,
    bool DefinitelyNotReceived,
    string? ExternalClaimId);

public sealed class EnsureClaimHandler(
    IClaimStateStore stateStore,
    IClaimGateway gateway) : IStageHandler<EnsureClaimInput>
{
    public async Task<IStageResult> ExecuteAsync(
        EnsureClaimInput input,
        IStageContext? context = null)
    {
        var cancellationToken =
            (context as IStageExecutionContext)?.CancellationToken ?? CancellationToken.None;
        ClaimState state;
        try
        {
            state = await stateStore.GetAsync(
                input.PrivateInsuranceProcessId,
                cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return StageResult.UpstreamError("Consumer claim state is temporarily unavailable.");
        }

        if (state.State == ClaimSubmissionState.Confirmed)
            return StageResult.Success("Claim was already confirmed.");
        if (state.State == ClaimSubmissionState.Rejected)
            return StageResult.BusinessRejected("Claim was already rejected.");

        if (state.State == ClaimSubmissionState.NotSubmitted)
        {
            ClaimSubmissionStart submission;
            try
            {
                submission = await stateStore.BeginSubmissionAsync(
                    input.PrivateInsuranceProcessId,
                    cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                return StageResult.UpstreamError(
                    "Could not durably reserve the claim submission.");
            }

            if (submission.ShouldPost)
                return await PostClaimAsync(input, submission.State, cancellationToken);

            state = submission.State;
        }

        // Concurrent executions and every execution after OutcomeUnknown query
        // the original key before any possible POST.
        return await ReconcileAsync(
            input.PrivateInsuranceProcessId,
            input,
            state,
            cancellationToken);
    }

    private async Task<IStageResult> PostClaimAsync(
        EnsureClaimInput input,
        ClaimState state,
        CancellationToken cancellationToken)
    {
        try
        {
            var posted = await gateway.PostOnceAsync(
                state.StableIdempotencyKey,
                input.PrivateInsuranceProcessId,
                cancellationToken);
            if (posted.BusinessRejected)
            {
                if (!await TryPersistRejectedAsync(input.PrivateInsuranceProcessId))
                    return StageResult.UpstreamError(
                        "Claim was rejected, but local rejection persistence must be retried.");
                return StageResult.BusinessRejected("Claim rejected by upstream.");
            }

            if (!posted.Confirmed || string.IsNullOrWhiteSpace(posted.ExternalClaimId))
                return await MarkOutcomeUnknownAsync(
                    input.PrivateInsuranceProcessId,
                    "Claim POST returned no terminal outcome; reconcile on retry.");

            if (!await TryPersistConfirmedAsync(
                    input.PrivateInsuranceProcessId,
                    posted.ExternalClaimId))
            {
                return StageResult.UpstreamError(
                    "Claim was confirmed, but local confirmation persistence must be reconciled.");
            }
            return StageResult.Success("Claim confirmed.");
        }
        catch (TimeoutException)
        {
            return await MarkOutcomeUnknownAsync(
                input.PrivateInsuranceProcessId,
                "Claim POST outcome is unknown; reconcile on retry.",
                timeout: true);
        }
        catch (HttpRequestException)
        {
            return await MarkOutcomeUnknownAsync(
                input.PrivateInsuranceProcessId,
                "Claim transport outcome is unknown; reconcile on retry.");
        }
    }

    private async Task<IStageResult> ReconcileAsync(
        string processId,
        EnsureClaimInput input,
        ClaimState state,
        CancellationToken cancellationToken)
    {
        ClaimStatusResult reconciled;
        try
        {
            reconciled = await gateway.GetStatusAsync(
                state.StableIdempotencyKey,
                cancellationToken);
        }
        catch (TimeoutException)
        {
            return StageResult.Timeout("Claim status query timed out.");
        }
        catch (HttpRequestException)
        {
            return StageResult.TransportUnavailable("Claim status query is unavailable.");
        }

        if (reconciled.Confirmed)
        {
            if (string.IsNullOrWhiteSpace(reconciled.ExternalClaimId) ||
                !await TryPersistConfirmedAsync(processId, reconciled.ExternalClaimId))
            {
                return StageResult.UpstreamError(
                    "Claim is confirmed, but local confirmation persistence must be retried.");
            }
            return StageResult.Success("Claim confirmed by reconciliation.");
        }
        if (reconciled.BusinessRejected)
        {
            if (!await TryPersistRejectedAsync(processId))
                return StageResult.UpstreamError(
                    "Claim rejection persistence must be retried.");
            return StageResult.BusinessRejected("Claim rejected by upstream.");
        }
        if (reconciled.DefinitelyNotReceived)
        {
            ClaimSubmissionStart resumed;
            try
            {
                resumed = await stateStore.ResumeAfterDefinitiveNotFoundAsync(
                    processId,
                    state.StableIdempotencyKey,
                    cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                return StageResult.UpstreamError(
                    "Could not durably reserve a reconciled claim submission.");
            }

            if (resumed.ShouldPost)
                return await PostClaimAsync(input, resumed.State, cancellationToken);
            return StageResult.UpstreamError(
                "Another execution is resubmitting the same claim key.");
        }
        return StageResult.UpstreamError("Claim outcome is still pending.");
    }

    private async Task<IStageResult> MarkOutcomeUnknownAsync(
        string processId,
        string message,
        bool timeout = false)
    {
        try
        {
            await stateStore.MarkOutcomeUnknownAsync(processId, CancellationToken.None);
        }
        catch
        {
            return StageResult.UpstreamError(
                "Claim outcome and local claim journal state both require reconciliation.");
        }
        return timeout
            ? StageResult.Timeout(message)
            : StageResult.TransportUnavailable(message);
    }

    private async Task<bool> TryPersistConfirmedAsync(string processId, string externalClaimId)
    {
        try
        {
            await stateStore.MarkConfirmedAsync(
                processId,
                externalClaimId,
                CancellationToken.None);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private async Task<bool> TryPersistRejectedAsync(string processId)
    {
        try
        {
            await stateStore.MarkRejectedAsync(
                processId,
                StageErrorCodes.BusinessRejected,
                CancellationToken.None);
            return true;
        }
        catch
        {
            return false;
        }
    }
}

// Other stages are intentionally omitted: their business state and external
// adapters belong to the consumer, not Pipelogiq core.
public sealed class ValidateClaimPrerequisitesHandler;
public sealed class PublishMedicalCaseHandler;
public sealed class MarkArrivalAllowedHandler;
public sealed class EnsureReservationCancelledHandler;
public sealed class PersistCancellationResultHandler;

public static class ConsumerPipelineIndex
{
    public static Task SaveAsync(string processId, int pipelineId) => Task.CompletedTask;
    public static Task SaveCancellationAsync(string processId, int pipelineId) => Task.CompletedTask;
}
