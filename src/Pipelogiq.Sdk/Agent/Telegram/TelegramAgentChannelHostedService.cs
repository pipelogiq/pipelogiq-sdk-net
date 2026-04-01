using System.Globalization;
using System.Net;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PipelogiqSDK.Agent.Configuration;
using PipelogiqSDK.Agent.Extensions;
using PipelogiqSDK.Agent.Models;
using PipelogiqSDK.Api;
using PipelogiqSDK.Configuration;
using System.Text;

namespace PipelogiqSDK.Agent.Telegram;

internal sealed class TelegramAgentChannelHostedService(
    TelegramAgentChannelOptions options,
    PipelogiqRunnerOptions runnerOptions,
    TelegramBotClient telegramBotClient,
    PipelogiqApiClient apiClient,
    ILogger<TelegramAgentChannelHostedService> logger) : BackgroundService
{
    private readonly HashSet<long> _allowedChatIds = options.TelegramAllowedChatIds.ToHashSet();

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation(
            "Telegram channel listener started. Allowed chats configured: {HasAllowList}.",
            _allowedChatIds.Count > 0);

        long? offset = null;

        while (!stoppingToken.IsCancellationRequested)
        {
            IReadOnlyList<TelegramUpdate> updates;
            try
            {
                updates = await telegramBotClient.GetUpdatesAsync(offset, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to poll Telegram updates.");
                await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
                continue;
            }

            foreach (var update in updates)
            {
                offset = update.UpdateId + 1;
                await HandleUpdateSafelyAsync(update, stoppingToken);
            }
        }
    }

    internal async Task HandleUpdateSafelyAsync(TelegramUpdate update, CancellationToken ct)
    {
        try
        {
            await HandleMessageAsync(update.Message, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to handle Telegram update {UpdateId}.", update.UpdateId);

            var chatId = update.Message?.Chat?.Id;
            if (chatId.HasValue)
            {
                await SendMessageSafelyAsync(
                    chatId.Value,
                    "Request failed while processing the message. Check worker logs and try again.",
                    ct);
            }
        }
    }

    private async Task HandleMessageAsync(TelegramMessage? message, CancellationToken ct)
    {
        if (message?.Chat == null)
            return;

        if (message.From?.IsBot == true)
            return;

        var chatId = message.Chat.Id;
        if (!IsChatAllowed(chatId))
        {
            logger.LogInformation("Ignored Telegram message from chat {ChatId}: not in allow list.", chatId);
            return;
        }

        // Handle slash commands (text only)
        var rawText = message.Text?.Trim();
        if (!string.IsNullOrEmpty(rawText) && rawText.StartsWith('/'))
        {
            if (await TryHandleCommandAsync(message, rawText, ct))
                return;
        }

        // Build message text + optional attachments from different Telegram content types
        var (agentText, attachments) = await BuildAgentInputAsync(message, ct);

        if (string.IsNullOrWhiteSpace(agentText) && attachments.Count == 0)
            return;

        await StartAiPipelineAsync(message, agentText, attachments, ct);
    }

    /// <summary>
    /// Extracts the agent text and any file attachments from a Telegram message.
    /// </summary>
    private async Task<(string Text, List<AgentAttachment> Attachments)> BuildAgentInputAsync(
        TelegramMessage message, CancellationToken ct)
    {
        var attachments = new List<AgentAttachment>();

        // Plain text message
        if (!string.IsNullOrWhiteSpace(message.Text) && message.Photo == null
            && message.Document == null && message.Voice == null && message.Audio == null)
        {
            return (message.Text.Trim(), attachments);
        }

        var caption = message.Caption?.Trim() ?? string.Empty;

        // Photo(s) — Telegram provides multiple resolutions; use the last (largest)
        if (message.Photo is { Length: > 0 })
        {
            var photo = message.Photo[^1];
            if (photo.FileSize == null || photo.FileSize <= options.MaxFileSizeBytes)
            {
                var att = await DownloadAttachmentAsync(photo.FileId, "image/jpeg", null, ct);
                if (att != null) attachments.Add(att);
            }
            else
            {
                logger.LogInformation("Skipped photo from chat {ChatId}: size {Size} exceeds limit.", message.Chat!.Id, photo.FileSize);
            }

            var text = string.IsNullOrEmpty(caption) ? "What is in this image?" : caption;
            return (text, attachments);
        }

        // Document (PDF, etc.)
        if (message.Document != null)
        {
            var doc = message.Document;
            if (doc.FileSize <= options.MaxFileSizeBytes)
            {
                var mime = doc.MimeType ?? "application/octet-stream";
                var att = await DownloadAttachmentAsync(doc.FileId, mime, doc.FileName, ct);
                if (att != null) attachments.Add(att);
            }
            else
            {
                logger.LogInformation("Skipped document '{File}' from chat {ChatId}: size {Size} exceeds limit.", doc.FileName, message.Chat!.Id, doc.FileSize);
            }

            var fileName = doc.FileName ?? "document";
            var text = string.IsNullOrEmpty(caption)
                ? $"Process the attached file: {fileName}"
                : caption;
            return (text, attachments);
        }

        // Voice message
        if (message.Voice != null)
        {
            var voice = message.Voice;
            var mime = voice.MimeType ?? "audio/ogg";
            var durationSec = voice.Duration;

            if (options.VoiceTranscriber != null && (voice.FileSize == null || voice.FileSize <= options.MaxFileSizeBytes))
            {
                try
                {
                    var filePath = await telegramBotClient.GetFilePathAsync(voice.FileId, ct);
                    if (!string.IsNullOrEmpty(filePath))
                    {
                        var bytes = await telegramBotClient.DownloadFileAsync(filePath, ct);
                        var transcribed = await options.VoiceTranscriber(bytes, mime, ct);
                        return (transcribed, attachments);
                    }
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Voice transcription failed for chat {ChatId}.", message.Chat!.Id);
                }
            }

            // No transcriber or transcription failed — pass descriptive text
            return ($"[Voice message, {durationSec} sec]", attachments);
        }

        // Audio file
        if (message.Audio != null)
        {
            var audio = message.Audio;
            var mime = audio.MimeType ?? "audio/mpeg";
            var fileName = audio.FileName ?? "audio";

            if (options.VoiceTranscriber != null && (audio.FileSize == null || audio.FileSize <= options.MaxFileSizeBytes))
            {
                try
                {
                    var filePath = await telegramBotClient.GetFilePathAsync(audio.FileId, ct);
                    if (!string.IsNullOrEmpty(filePath))
                    {
                        var bytes = await telegramBotClient.DownloadFileAsync(filePath, ct);
                        var transcribed = await options.VoiceTranscriber(bytes, mime, ct);
                        return (string.IsNullOrEmpty(caption) ? transcribed : $"{caption}\n{transcribed}", attachments);
                    }
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Audio transcription failed for chat {ChatId}.", message.Chat!.Id);
                }
            }

            return ($"[Audio file: {fileName}, {audio.Duration} sec]", attachments);
        }

        return (caption, attachments);
    }

    private async Task<AgentAttachment?> DownloadAttachmentAsync(
        string fileId, string mimeType, string? fileName, CancellationToken ct)
    {
        try
        {
            var filePath = await telegramBotClient.GetFilePathAsync(fileId, ct);
            if (string.IsNullOrEmpty(filePath)) return null;

            var bytes = await telegramBotClient.DownloadFileAsync(filePath, ct);
            return new AgentAttachment
            {
                Type = AgentAttachment.TypeFromMediaType(mimeType),
                MediaType = mimeType,
                Base64Data = Convert.ToBase64String(bytes),
                FileName = fileName,
            };
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to download file {FileId} from Telegram.", fileId);
            return null;
        }
    }

    private bool IsChatAllowed(long chatId) =>
        _allowedChatIds.Count == 0 || _allowedChatIds.Contains(chatId);

    private async Task<bool> TryHandleCommandAsync(TelegramMessage message, string text, CancellationToken ct)
    {
        if (!text.StartsWith('/'))
            return false;

        var chatId = message.Chat!.Id;
        ParseCommand(text, out var command, out var args);

        switch (command)
        {
            case "/start":
            case "/help":
                await SendMessageSafelyAsync(chatId, BuildHelpMessage(), ct);
                return true;

            case "/approve":
            {
                if (!TryParseStageId(args, out var stageId))
                {
                    await SendMessageSafelyAsync(chatId, "Usage: /approve <stageId>", ct);
                    return true;
                }

                await HandleResumeStageCommandAsync(chatId, stageId, approved: true, rejectionReason: null, ct);
                return true;
            }

            case "/reject":
            {
                if (!TryParseRejectArgs(args, out var stageId, out var reason))
                {
                    await SendMessageSafelyAsync(chatId, "Usage: /reject <stageId> <reason>", ct);
                    return true;
                }

                await HandleResumeStageCommandAsync(chatId, stageId, approved: false, rejectionReason: reason, ct);
                return true;
            }

            default:
                await SendMessageSafelyAsync(chatId, "Unknown command. Use /help.", ct);
                return true;
        }
    }

    private async Task StartAiPipelineAsync(
        TelegramMessage message,
        string text,
        List<AgentAttachment> attachments,
        CancellationToken ct)
    {
        var chatId = message.Chat!.Id;
        var sessionId = $"tg:{chatId.ToString(CultureInfo.InvariantCulture)}";
        var userId = !string.IsNullOrWhiteSpace(message.From?.Username)
            ? message.From!.Username
            : message.From?.Id.ToString(CultureInfo.InvariantCulture);

        var input = new AgentOrchestratorInput
        {
            Message = text,
            ReplyTo = AgentReplyTarget.Telegram(chatId),
            SessionId = sessionId,
            UserId = userId,
            Attachments = attachments.Count > 0 ? attachments : null,
        };

        var builder = AgentPipelineBuilderExtensions
            .CreateAiAgent(input, runnerOptions)
            .AddKeyword("channel", "telegram")
            .AddKeyword("agent", "true")
            .AddContextItem("telegramChatId", chatId)
            .AddContextItem("telegramMessageId", message.MessageId);

        if (!string.IsNullOrWhiteSpace(message.From?.Username))
            builder.AddContextItem("telegramUsername", message.From.Username!);

        var response = await PipelineService.StartPipelineAsync(builder, ct);

        logger.LogInformation(
            "Started AI pipeline {PipelineId} from Telegram chat {ChatId}, message {MessageId}.",
            response.Id,
            chatId,
            message.MessageId);
    }

    private async Task HandleResumeStageCommandAsync(
        long chatId,
        int stageId,
        bool approved,
        string? rejectionReason,
        CancellationToken ct)
    {
        try
        {
            await apiClient.ResumeStageApprovalAsync(stageId, approved, rejectionReason, ct);
            var confirmation = approved
                ? $"Approved stage {stageId}."
                : $"Rejected stage {stageId}.";
            await SendMessageSafelyAsync(chatId, confirmation, ct);
        }
        catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.Conflict)
        {
            logger.LogInformation(
                ex,
                "Stage resume command conflicted for stage {StageId} in chat {ChatId}.",
                stageId,
                chatId);

            await SendMessageSafelyAsync(
                chatId,
                $"Stage {stageId} is no longer waiting for approval, or it was already resumed with a different decision.",
                ct);
        }
    }

    private async Task SendMessageSafelyAsync(long chatId, string text, CancellationToken ct)
    {
        try
        {
            await telegramBotClient.SendMessageAsync(chatId, text, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to send Telegram message to chat {ChatId}.", chatId);
        }
    }

    private static void ParseCommand(string text, out string command, out string args)
    {
        var firstSpace = text.IndexOf(' ');
        var token = firstSpace < 0 ? text : text[..firstSpace];
        var atIndex = token.IndexOf('@');

        var normalized = atIndex >= 0 ? token[..atIndex] : token;
        command = normalized.ToLowerInvariant();
        args = firstSpace < 0 ? string.Empty : text[(firstSpace + 1)..].Trim();
    }

    private static bool TryParseStageId(string args, out int stageId)
    {
        stageId = default;
        if (string.IsNullOrWhiteSpace(args))
            return false;

        var token = args.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault();
        return int.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out stageId);
    }

    private static bool TryParseRejectArgs(string args, out int stageId, out string reason)
    {
        stageId = default;
        reason = string.Empty;

        if (string.IsNullOrWhiteSpace(args))
            return false;

        var firstSpace = args.IndexOf(' ');
        var idToken = firstSpace < 0 ? args : args[..firstSpace];
        if (!int.TryParse(idToken, NumberStyles.Integer, CultureInfo.InvariantCulture, out stageId))
            return false;

        reason = firstSpace < 0 ? "Rejected from Telegram." : args[(firstSpace + 1)..].Trim();
        if (string.IsNullOrWhiteSpace(reason))
            reason = "Rejected from Telegram.";

        return true;
    }

    private static string BuildHelpMessage()
    {
        return """
Send any text message to start an AI agent pipeline.

Commands:
/approve <stageId>
/reject <stageId> <reason>
/help
""";
    }
}
