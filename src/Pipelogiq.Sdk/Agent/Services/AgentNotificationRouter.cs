using System.Globalization;
using Microsoft.Extensions.Logging;
using PipelogiqSDK.Abstractions;
using PipelogiqSDK.Agent.Models;
using PipelogiqSDK.StageHelper;

namespace PipelogiqSDK.Agent.Services;

/// <summary>
/// Default router that resolves an agent reply route from context and dispatches through a matching channel.
/// </summary>
public class AgentNotificationRouter(
    IEnumerable<IAgentNotificationChannel> channels,
    IEnumerable<IAgentNotifier> legacyNotifiers,
    ILogger<AgentNotificationRouter> logger) : IAgentNotificationRouter
{
    private readonly List<IAgentNotificationChannel> _channels = channels.ToList();
    private readonly List<IAgentNotifier> _legacyNotifiers = legacyNotifiers.ToList();

    /// <inheritdoc />
    public async Task<AgentNotificationDispatchResult> NotifyAsync(
        IStageContext? context,
        AgentNotification notification,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(notification);

        var replyTarget = ResolveReplyTarget(context);
        if (replyTarget != null)
            return await DispatchToChannelAsync(replyTarget, notification, ct);

        var sessionId = context.TryGetValue<string>(AgentConstants.SessionId);
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            return new AgentNotificationDispatchResult
            {
                Delivered = false,
                FailureReason = "No session id or reply target is available."
            };
        }

        if (_legacyNotifiers.Count == 1)
        {
            try
            {
                await _legacyNotifiers[0].NotifyAsync(sessionId, notification, ct);
                return new AgentNotificationDispatchResult
                {
                    Delivered = true,
                    Channel = "legacy"
                };
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Legacy IAgentNotifier failed for session {SessionId}.", sessionId);
                return new AgentNotificationDispatchResult
                {
                    Delivered = false,
                    FailureReason = ex.Message
                };
            }
        }

        if (_legacyNotifiers.Count > 1)
        {
            return new AgentNotificationDispatchResult
            {
                Delivered = false,
                FailureReason = "Multiple legacy IAgentNotifier implementations are registered and the route is ambiguous."
            };
        }

        return new AgentNotificationDispatchResult
        {
            Delivered = false,
            FailureReason = "No notification channel matched the current route."
        };
    }

    private async Task<AgentNotificationDispatchResult> DispatchToChannelAsync(
        AgentReplyTarget replyTarget,
        AgentNotification notification,
        CancellationToken ct)
    {
        var matchingChannels = _channels
            .Where(channel => channel.CanHandle(replyTarget))
            .ToList();

        if (matchingChannels.Count == 0)
        {
            return new AgentNotificationDispatchResult
            {
                Delivered = false,
                FailureReason = $"No notification channel is registered for '{replyTarget.Channel}'."
            };
        }

        if (matchingChannels.Count > 1)
        {
            return new AgentNotificationDispatchResult
            {
                Delivered = false,
                FailureReason = $"Multiple notification channels match '{replyTarget.Channel}'."
            };
        }

        var channel = matchingChannels[0];

        try
        {
            await channel.NotifyAsync(replyTarget, notification, ct);
            return new AgentNotificationDispatchResult
            {
                Delivered = true,
                Channel = channel.Name
            };
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                ex,
                "Notification delivery through channel {Channel} failed for target {ReplyTarget}.",
                channel.Name,
                replyTarget.Address);

            return new AgentNotificationDispatchResult
            {
                Delivered = false,
                Channel = channel.Name,
                FailureReason = ex.Message
            };
        }
    }

    private AgentReplyTarget? ResolveReplyTarget(IStageContext? context)
    {
        var explicitTarget = context.TryGetValue<AgentReplyTarget>(AgentConstants.ReplyTarget);
        if (explicitTarget != null)
            return explicitTarget;

        var sessionId = context.TryGetValue<string>(AgentConstants.SessionId);
        if (TryParseLegacySessionRoute(sessionId, out var legacyTarget))
            return legacyTarget;

        return null;
    }

    private bool TryParseLegacySessionRoute(string? sessionId, out AgentReplyTarget? replyTarget)
    {
        replyTarget = null;

        if (string.IsNullOrWhiteSpace(sessionId))
            return false;

        var firstSeparator = sessionId.IndexOf(':');
        if (firstSeparator > 0 && firstSeparator < sessionId.Length - 1)
        {
            var channel = sessionId[..firstSeparator].Trim();
            var address = sessionId[(firstSeparator + 1)..].Trim();

            if (!string.IsNullOrWhiteSpace(channel) && !string.IsNullOrWhiteSpace(address))
            {
                replyTarget = new AgentReplyTarget
                {
                    Channel = CanonicalizeChannel(channel),
                    Address = address
                };
                return true;
            }
        }

        if (long.TryParse(sessionId, NumberStyles.Integer, CultureInfo.InvariantCulture, out var chatId))
        {
            replyTarget = AgentReplyTarget.Telegram(chatId);
            return true;
        }

        return false;
    }

    private static string CanonicalizeChannel(string channel)
    {
        return channel.Trim().ToLowerInvariant() switch
        {
            "tg" => "telegram",
            _ => channel.Trim().ToLowerInvariant()
        };
    }
}
