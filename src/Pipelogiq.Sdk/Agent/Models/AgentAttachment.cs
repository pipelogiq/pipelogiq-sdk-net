namespace PipelogiqSDK.Agent.Models;

/// <summary>
/// A binary attachment (image, document, audio) sent by the user alongside their message.
/// Images and PDFs are passed directly to the LLM as vision/document content blocks.
/// Audio files require a transcription handler configured in TelegramAgentChannelOptions.
/// </summary>
public class AgentAttachment
{
    /// <summary>
    /// Attachment category: "image", "document", or "audio".
    /// </summary>
    public string Type { get; set; } = "image";

    /// <summary>
    /// MIME type, e.g. "image/jpeg", "image/png", "application/pdf", "audio/ogg".
    /// </summary>
    public string MediaType { get; set; } = "image/jpeg";

    /// <summary>
    /// Base64-encoded file content.
    /// </summary>
    public string Base64Data { get; set; } = string.Empty;

    /// <summary>
    /// Optional original filename (e.g. "receipt.pdf", "photo.jpg").
    /// </summary>
    public string? FileName { get; set; }

    /// <summary>
    /// Derives the attachment type from a MIME type string.
    /// </summary>
    public static string TypeFromMediaType(string mediaType) => mediaType switch
    {
        var m when m.StartsWith("image/", StringComparison.OrdinalIgnoreCase) => "image",
        var m when m.StartsWith("audio/", StringComparison.OrdinalIgnoreCase) => "audio",
        "application/pdf" => "document",
        _ => "document",
    };
}
