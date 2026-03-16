using System.Globalization;
using System.Net;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PipelogiqSDK.Agent.Configuration;
using PipelogiqSDK.Agent.Extensions;
using PipelogiqSDK.Agent.Models;
using PipelogiqSDK.Api;
using PipelogiqSDK.Configuration;

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
        if (message?.Chat == null || string.IsNullOrWhiteSpace(message.Text))
            return;

        if (message.From?.IsBot == true)
            return;

        var chatId = message.Chat.Id;
        if (!IsChatAllowed(chatId))
        {
            logger.LogInformation("Ignored Telegram message from chat {ChatId}: not in allow list.", chatId);
            return;
        }

        var text = message.Text.Trim();

        if (await TryHandleCommandAsync(message, text, ct))
            return;

        await StartAiPipelineAsync(message, text, ct);
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

    private async Task StartAiPipelineAsync(TelegramMessage message, string text, CancellationToken ct)
    {
        var chatId = message.Chat!.Id;
        var sessionId = $"tg:{chatId.ToString(CultureInfo.InvariantCulture)}";
        var userId = message.From?.Id.ToString(CultureInfo.InvariantCulture);

        var builder = AgentPipelineBuilderExtensions
            .CreateAiAgent(text, AgentReplyTarget.Telegram(chatId), sessionId, userId, runnerOptions)
            .AddKeyword("channel", "telegram")
            .AddKeyword("agent", "true")
            .AddContextItem("telegramChatId", chatId)
            .AddContextItem("telegramMessageId", message.MessageId);

        if (!string.IsNullOrWhiteSpace(message.From?.Username))
            builder.AddContextItem("telegramUsername", message.From.Username!);

        var response = await PipelineService.StartPipelineAsync(builder, ct);

        await SendMessageSafelyAsync(
            chatId,
            $"Request accepted. Pipeline #{response.Id} started.",
            ct);

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
