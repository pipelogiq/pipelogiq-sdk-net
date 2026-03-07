namespace PipelogiqSDK.Contracts;

/// <summary>
/// Represents a typed context value.
/// </summary>
public class ContextItem
{
    /// <summary>
    /// Gets or sets context key.
    /// </summary>
    public string Key { get; set; } = null!;

    /// <summary>
    /// Gets or sets serialized value.
    /// </summary>
    public string Value { get; set; } = null!;

    /// <summary>
    /// Gets or sets CLR type name of the value.
    /// </summary>
    public string ValueType { get; set; } = null!;
}
