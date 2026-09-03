namespace PipelogiqSDK.Abstractions;

/// <summary>
/// Optional stage-result contract that explicitly classifies a failure as retryable or terminal.
/// Existing <see cref="IStageResult"/> implementations remain valid and retain legacy server behavior.
/// </summary>
public interface IClassifiedStageResult
{
    /// <summary>
    /// Gets or sets whether a failed result is eligible for retry.
    /// <c>true</c> means retryable, <c>false</c> means terminal, and <c>null</c> delegates to legacy policy behavior.
    /// </summary>
    bool? Retryable { get; set; }
}
