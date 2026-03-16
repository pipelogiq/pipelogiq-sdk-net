using System.Globalization;

namespace PipelogiqSDK.Agent.Models;

/// <summary>
/// Explicit notification destination used to send agent responses and confirmations.
/// </summary>
public class AgentReplyTarget
{
    /// <summary>
    /// Transport/channel name, for example "telegram", "signalr", or "webhook".
    /// </summary>
    public string Channel { get; set; } = null!;

    /// <summary>
    /// Transport-specific address or recipient identifier.
    /// </summary>
    public string Address { get; set; } = null!;

    /// <summary>
    /// Optional transport-specific metadata.
    /// </summary>
    public Dictionary<string, string>? Metadata { get; set; }

    /// <summary>
    /// Creates a Telegram reply target for the specified chat.
    /// </summary>
    public static AgentReplyTarget Telegram(long chatId)
    {
        return new AgentReplyTarget
        {
            Channel = "telegram",
            Address = chatId.ToString(CultureInfo.InvariantCulture)
        };
    }
}
