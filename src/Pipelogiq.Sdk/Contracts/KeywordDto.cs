namespace PipelogiqSDK.Contracts;

/// <summary>
/// Represents a key-value keyword pair.
/// </summary>
public class KeywordDto
{
    /// <summary>
    /// Gets or sets keyword key.
    /// </summary>
    public string Key { get; set; } = null!;

    /// <summary>
    /// Gets or sets keyword value.
    /// </summary>
    public string Value { get; set; } = null!;
}
