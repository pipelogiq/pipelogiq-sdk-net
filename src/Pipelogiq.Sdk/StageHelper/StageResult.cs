using PipelogiqSDK.Contracts;
using PipelogiqSDK.Execution;

namespace PipelogiqSDK.StageHelper;

/// <summary>
/// Factory helpers for common stage results.
/// </summary>
public static class StageResult
{
    /// <summary>
    /// Creates successful stage result.
    /// </summary>
    /// <param name="result">Result message.</param>
    /// <returns>Successful stage result.</returns>
    public static StageResultDto Success(string result, IEnumerable<StageInfo>? appendedStages = null)
    {
        return new StageResultDto
        {
            Result = result,
            IsSuccess = true,
            AppendedStages = appendedStages?.ToList()
        };
    }

    /// <summary>
    /// Creates failed stage result.
    /// </summary>
    /// <param name="result">Result message.</param>
    /// <param name="errorCode">Optional error code used by retry policies to decide whether to retry.</param>
    /// <returns>Failed stage result.</returns>
    public static StageResultDto Error(
        string result,
        string? errorCode = null,
        IEnumerable<StageInfo>? appendedStages = null)
    {
        return new StageResultDto
        {
            Result = result,
            IsSuccess = false,
            ErrorCode = errorCode,
            AppendedStages = appendedStages?.ToList()
        };
    }

    /// <summary>
    /// Creates an explicitly retryable failed stage result.
    /// </summary>
    public static StageResultDto RetryableError(
        string result,
        string errorCode,
        IEnumerable<StageInfo>? appendedStages = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(errorCode);
        return new StageResultDto
        {
            Result = result,
            IsSuccess = false,
            ErrorCode = errorCode,
            Retryable = true,
            AppendedStages = appendedStages?.ToList()
        };
    }

    /// <summary>
    /// Creates an explicitly terminal failed stage result that must not be retried automatically.
    /// </summary>
    public static StageResultDto TerminalError(
        string result,
        string errorCode,
        IEnumerable<StageInfo>? appendedStages = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(errorCode);
        return new StageResultDto
        {
            Result = result,
            IsSuccess = false,
            ErrorCode = errorCode,
            Retryable = false,
            AppendedStages = appendedStages?.ToList()
        };
    }

    /// <summary>
    /// Creates a failed result with error code "RATE_LIMIT_EXCEEDED".
    /// Retry policies configured with <c>retryOn.errorCodes: ["RATE_LIMIT_EXCEEDED"]</c> will pick this up automatically.
    /// </summary>
    /// <param name="result">Result message.</param>
    /// <returns>Failed stage result.</returns>
    public static StageResultDto RateLimitExceeded(string result)
        => RetryableError(result, StageErrorCodes.RateLimitExceeded);

    /// <summary>
    /// Creates a failed result with error code "TIMEOUT".
    /// </summary>
    /// <param name="result">Result message.</param>
    /// <returns>Failed stage result.</returns>
    public static StageResultDto Timeout(string result)
        => RetryableError(result, StageErrorCodes.Timeout);

    /// <summary>
    /// Creates a failed result with error code "UPSTREAM_ERROR".
    /// </summary>
    /// <param name="result">Result message.</param>
    /// <returns>Failed stage result.</returns>
    public static StageResultDto UpstreamError(string result)
        => RetryableError(result, StageErrorCodes.UpstreamError);

    /// <summary>
    /// Creates a retryable transport-unavailable failure.
    /// </summary>
    public static StageResultDto TransportUnavailable(string result)
        => RetryableError(result, StageErrorCodes.TransportUnavailable);

    /// <summary>
    /// Creates a terminal business-rejection failure.
    /// </summary>
    public static StageResultDto BusinessRejected(string result)
        => TerminalError(result, StageErrorCodes.BusinessRejected);

    /// <summary>
    /// Creates a terminal validation failure.
    /// </summary>
    public static StageResultDto ValidationError(string result)
        => TerminalError(result, StageErrorCodes.ValidationError);

    /// <summary>
    /// Creates a terminal invalid-state failure.
    /// </summary>
    public static StageResultDto InvalidState(string result)
        => TerminalError(result, StageErrorCodes.InvalidState);

    /// <summary>
    /// Creates a terminal failure for missing mandatory business data.
    /// </summary>
    public static StageResultDto MissingRequiredData(string result)
        => TerminalError(result, StageErrorCodes.MissingRequiredData);
}
