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
    /// <returns>Failed stage result.</returns>
    public static StageResultDto Error(string result)
    {
        return new StageResultDto
        {
            Result = result,
            IsSuccess = false
        };
    }
}
