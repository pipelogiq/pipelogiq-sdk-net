using PipelogiqSDK.Configuration;

namespace PipelogiqSDK.Execution;

public static class GlobalRunnerContext
{
    public static PipelogiqRunnerOptions? Options { get; set; }
    public static string? Token { get; set; }
}
