namespace PipelogiqSDK.Contracts;

/// <summary>
/// Well-known pipeline status values returned by Pipelogiq.
/// </summary>
public static class PipelineStatuses
{
    public const string NotStarted = "NotStarted";
    public const string Pending = "Pending";
    public const string Running = "Running";
    public const string Paused = "Paused";
    public const string Completed = "Completed";
    public const string Failed = "Failed";
    public const string Cancelled = "Cancelled";

    /// <summary>Returns whether a status represents a terminal pipeline state.</summary>
    public static bool IsTerminal(string? status)
    {
        return string.Equals(status, Completed, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(status, Failed, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(status, Cancelled, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(status, "Canceled", StringComparison.OrdinalIgnoreCase);
    }
}

/// <summary>
/// Well-known stage status values returned by Pipelogiq.
/// </summary>
public static class StageStatuses
{
    public const string NotStarted = "NotStarted";
    public const string Running = "Running";
    public const string Pending = "Pending";
    public const string RetryScheduled = "RetryScheduled";
    public const string Throttled = "Throttled";
    public const string WaitingForApproval = "WaitingForApproval";
    public const string Completed = "Completed";
    public const string Failed = "Failed";
    public const string Skipped = "Skipped";

    /// <summary>Returns whether a status represents a terminal stage state.</summary>
    public static bool IsTerminal(string? status)
    {
        return string.Equals(status, Completed, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(status, Failed, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(status, Skipped, StringComparison.OrdinalIgnoreCase);
    }
}
