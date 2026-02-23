using PipelogiqSDK.Contracts;
using PipelogiqSDK.Execution;

namespace PipelogiqSDK.StageHelper;

public static class StageResult
{
    public static StageResultDto Success(string result)
    {
        return new StageResultDto
        {
            Result = result,
            IsSuccess = true
        };
    }

    public static StageResultDto Error(string result)
    {
        return new StageResultDto
        {
            Result = result,
            IsSuccess = false
        };
    }
}
