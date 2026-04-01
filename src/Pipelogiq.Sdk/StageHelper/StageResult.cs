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
    public static StageResultDto Success(string result)
    {
        return new StageResultDto
        {
            Result = result,
            IsSuccess = true
        };
    }

    /// <summary>
    /// Creates failed stage result.
    /// </summary>
    /// <param name="result">Result message.</param>
    /// <param name="errorCode">Optional error code used by retry policies to decide whether to retry.</param>
    /// <returns>Failed stage result.</returns>
    public static StageResultDto Error(string result, string? errorCode = null)
    {
        return new StageResultDto
        {
            Result = result,
            IsSuccess = false,
            ErrorCode = errorCode
        };
    }

    /// <summary>
    /// Creates a failed result with error code "RATE_LIMIT_EXCEEDED".
    /// Retry policies configured with <c>retryOn.errorCodes: ["RATE_LIMIT_EXCEEDED"]</c> will pick this up automatically.
    /// </summary>
    /// <param name="result">Result message.</param>
    /// <returns>Failed stage result.</returns>
    public static StageResultDto RateLimitExceeded(string result)
        => Error(result, "RATE_LIMIT_EXCEEDED");

    /// <summary>
    /// Creates a failed result with error code "TIMEOUT".
    /// </summary>
    /// <param name="result">Result message.</param>
    /// <returns>Failed stage result.</returns>
    public static StageResultDto Timeout(string result)
        => Error(result, "TIMEOUT");

    /// <summary>
    /// Creates a failed result with error code "UPSTREAM_ERROR".
    /// </summary>
    /// <param name="result">Result message.</param>
    /// <returns>Failed stage result.</returns>
    public static StageResultDto UpstreamError(string result)
        => Error(result, "UPSTREAM_ERROR");
}
