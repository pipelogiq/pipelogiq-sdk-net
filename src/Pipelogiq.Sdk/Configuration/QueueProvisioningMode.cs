namespace PipelogiqSDK.Configuration;

/// <summary>
/// Defines queue provisioning strategy for worker startup.
/// </summary>
public enum QueueProvisioningMode
{
    /// <summary>
    /// Only asserts queues exist; does not create missing queues.
    /// Missing queues are treated as transient and worker retries until queues appear.
    /// </summary>
    AssertOnly = 0,

    /// <summary>
    /// Creates missing queues using bootstrap settings.
    /// </summary>
    Ensure = 1,
}
