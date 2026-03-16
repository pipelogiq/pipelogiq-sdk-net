using Microsoft.Extensions.Logging.Abstractions;
using PipelogiqSDK.Agent.Models;
using PipelogiqSDK.Agent.Services;
using PipelogiqSDK.Contracts;

using Xunit;

namespace PipelogiqSDK.Tests.Agent;

public sealed class AgentNotificationRouterTests
{
    [Fact]
    public async Task NotifyAsync_WithExplicitReplyTarget_UsesMatchingChannel()
    {
        var channel = new CaptureChannel("webhook");
        var router = new AgentNotificationRouter([channel], Array.Empty<IAgentNotifier>(), NullLogger<AgentNotificationRouter>.Instance);
        var context = new StageContext
        {
            Payload = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
            {
                ["agent:replyTarget"] = new AgentReplyTarget
                {
                    Channel = "webhook",
                    Address = "client-7"
                }
            }
        };

        var result = await router.NotifyAsync(context, new AgentNotification
        {
            Type = "response",
            Message = "done"
        });

        Assert.True(result.Delivered);
        Assert.Equal("webhook", result.Channel);
        Assert.Equal("client-7", channel.LastTarget!.Address);
    }

    [Fact]
    public async Task NotifyAsync_WithLegacyTelegramSession_RoutesToTelegramChannel()
    {
        var channel = new CaptureChannel("telegram");
        var router = new AgentNotificationRouter([channel], Array.Empty<IAgentNotifier>(), NullLogger<AgentNotificationRouter>.Instance);
        var context = new StageContext
        {
            Payload = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
            {
                ["agent:sessionId"] = "tg:123456"
            }
        };

        var result = await router.NotifyAsync(context, new AgentNotification
        {
            Type = "response",
            Message = "done"
        });

        Assert.True(result.Delivered);
        Assert.Equal("telegram", channel.LastTarget!.Channel);
        Assert.Equal("123456", channel.LastTarget.Address);
    }

    [Fact]
    public async Task NotifyAsync_WithSingleLegacyNotifier_FallsBackToLegacyImplementation()
    {
        var notifier = new CaptureLegacyNotifier();
        var router = new AgentNotificationRouter(
            Array.Empty<IAgentNotificationChannel>(),
            [notifier],
            NullLogger<AgentNotificationRouter>.Instance);
        var context = new StageContext
        {
            Payload = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
            {
                ["agent:sessionId"] = "plain-session"
            }
        };

        var result = await router.NotifyAsync(context, new AgentNotification
        {
            Type = "response",
            Message = "done"
        });

        Assert.True(result.Delivered);
        Assert.Equal("legacy", result.Channel);
        Assert.Equal("plain-session", notifier.LastSessionId);
    }

    private sealed class CaptureChannel(string channelName) : IAgentNotificationChannel
    {
        public AgentReplyTarget? LastTarget { get; private set; }

        public string Name => channelName;

        public bool CanHandle(AgentReplyTarget replyTarget) =>
            string.Equals(replyTarget.Channel, channelName, StringComparison.OrdinalIgnoreCase);

        public Task NotifyAsync(AgentReplyTarget replyTarget, AgentNotification notification, CancellationToken ct = default)
        {
            LastTarget = replyTarget;
            return Task.CompletedTask;
        }
    }

    private sealed class CaptureLegacyNotifier : IAgentNotifier
    {
        public string? LastSessionId { get; private set; }

        public Task NotifyAsync(string sessionId, AgentNotification notification, CancellationToken ct = default)
        {
            LastSessionId = sessionId;
            return Task.CompletedTask;
        }
    }
}
