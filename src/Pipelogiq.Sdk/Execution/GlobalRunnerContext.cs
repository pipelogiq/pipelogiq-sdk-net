using PipelogiqSDK.Configuration;

namespace PipelogiqSDK.Execution;

/// <summary>
/// Internal startup-time configuration carrier. Set once during AddPipelogiq() and never mutated again.
/// Not part of the public API — use DI-registered PipelogiqRunnerOptions instead.
/// </summary>
internal static class GlobalRunnerContext
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
