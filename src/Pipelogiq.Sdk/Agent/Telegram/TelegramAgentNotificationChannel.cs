using System.Globalization;
using PipelogiqSDK.Agent.Models;
using PipelogiqSDK.Agent.Services;

namespace PipelogiqSDK.Agent.Telegram;

internal sealed class TelegramAgentNotificationChannel(
    TelegramBotClient telegramBotClient) : IAgentNotificationChannel
{
    public string Name => "telegram";

    public bool CanHandle(AgentReplyTarget replyTarget)
    {
        if (replyTarget == null)
            return false;

        var channel = replyTarget.Channel?.Trim();
        return (string.Equals(channel, "telegram", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(channel, "tg", StringComparison.OrdinalIgnoreCase)) &&
               TryParseChatId(replyTarget.Address, out _);
    }

    public async Task NotifyAsync(AgentReplyTarget replyTarget, AgentNotification notification, CancellationToken ct = default)
    {
        if (!TryParseChatId(replyTarget.Address, out var chatId))
        {
            throw new InvalidOperationException($"Unsupported Telegram reply target '{replyTarget.Address}'.");
        }

        var message = BuildMessage(notification);
        await telegramBotClient.SendMessageAsync(chatId, message, ct);
    }

    private static bool TryParseChatId(string? address, out long chatId)
    {
        chatId = default;
        if (string.IsNullOrWhiteSpace(address))
            return false;

        return long.TryParse(address, NumberStyles.Integer, CultureInfo.InvariantCulture, out chatId);
    }

    private static string BuildMessage(AgentNotification notification)
    {
        if (string.Equals(notification.Type, "confirmation_required", StringComparison.OrdinalIgnoreCase) &&
            notification.ConfirmationStageId.HasValue)
        {
            return $"{notification.Message}\n\nApprove: /approve {notification.ConfirmationStageId.Value}\nReject: /reject {notification.ConfirmationStageId.Value} <reason>";
        }

        return notification.Message;
    }
}
