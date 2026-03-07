using PipelogiqSDK.Configuration;

namespace PipelogiqSDK.Execution;

/// <summary>
/// Global mutable context shared between runner and builders.
/// </summary>
public static class GlobalRunnerContext
{
    /// <summary>
    /// Gets or sets globally configured runner options.
    /// </summary>
    public static PipelogiqRunnerOptions? Options { get; set; }

    /// <summary>
    /// Gets or sets globally configured bearer token.
    /// </summary>
    public static string? Token { get; set; }
}
