using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpenTelemetry.Metrics;
using PipelogiqSDK.Agent.Configuration;
using PipelogiqSDK.Agent.Extensions;
using PipelogiqSDK.Agent.Models;
using PipelogiqSDK.Agent.Services;
using PipelogiqSDK.Api;
using PipelogiqSDK.Configuration;
using PipelogiqSDK.Postgres;
using PipelogiqSDK.Redis;
using PipelogiqSDK.Runner;

// ─────────────────────────────────────────────────────────────────────────────
//  Production-Ready Agent Worker
//
//  Demonstrates a horizontally-scalable Pipelogiq agent worker with:
//    - PostgreSQL-backed session & memory stores (durable, multi-instance)
//    - Redis-backed session & memory stores (fast, multi-instance)
//    - Prometheus-compatible metrics via System.Diagnostics.Metrics
//    - Multiple lifecycle observers (metrics + audit log)
//    - Tool policy for role-based access control
//    - Telegram transport
//
//  Required environment variables:
//    PIPELOGIQ_API_KEY       — Pipelogiq workspace API key
//    PIPELOGIQ_API_URL       — Pipelogiq server URL (default: http://localhost:8081)
//    ANTHROPIC_API_KEY       — Anthropic API key for Claude
//    TELEGRAM_BOT_TOKEN      — Telegram bot token
//    STORE_PROVIDER          — "redis" or "postgres" (default: redis)
//    REDIS_CONNECTION         — Redis connection string (default: localhost:6379)
//    POSTGRES_CONNECTION      — PostgreSQL connection string
// ─────────────────────────────────────────────────────────────────────────────

var host = Host.CreateDefaultBuilder(args)
    .ConfigureServices(services =>
    {
        // ── 1. Pipelogiq core ────────────────────────────────────────────────
        services.AddPipelogiq(new PipelogiqRunnerOptions
        {
            ApiKey     = Env("PIPELOGIQ_API_KEY"),
            ApiUrl     = Env("PIPELOGIQ_API_URL", "http://localhost:8081"),
            WorkerName = Env("WORKER_NAME", "production-worker"),
        });

        // ── 2. AI agent ─────────────────────────────────────────────────────
        var agentBuilder = services.AddPipelogiqAgent(agent =>
        {
            agent.LlmProvider = AgentLlmProvider.Anthropic;
            agent.LlmApiKey   = Env("ANTHROPIC_API_KEY");
            agent.LlmModel    = "claude-sonnet-4-6";

            agent.ModelRouter = new AgentModelRouter
            {
                PlanModel       = "claude-haiku-4-5-20251001",
                ThinkModel      = "claude-sonnet-4-6",
                SynthesizeModel = "claude-haiku-4-5-20251001",
            };

            agent.TokenBudget = new AgentTokenBudget
            {
                EnablePromptCaching  = true,
                MaxCostUsdPerSession = 0.50m,
            };

            agent.UseReActMode                    = true;
            agent.RequireConfirmationForMutations  = true;
            agent.MaxThinkSteps                   = 15;

            agent.SystemPrompt = """
                You are a helpful assistant. Follow the user's instructions carefully.
                If a tool fails, explain the error clearly and suggest alternatives.
                Reply in the same language the user writes in.
                """;
        });

        // ── 3. Durable stores (Redis or Postgres) ───────────────────────────
        var storeProvider = Env("STORE_PROVIDER", "redis").ToLowerInvariant();
        switch (storeProvider)
        {
            case "redis":
                agentBuilder.UseRedisStores(services, Env("REDIS_CONNECTION", "localhost:6379"));
                break;
            case "postgres":
                agentBuilder.UsePostgresStores(services, Env("POSTGRES_CONNECTION"));
                break;
            default:
                throw new InvalidOperationException(
                    $"Unknown STORE_PROVIDER '{storeProvider}'. Use 'redis' or 'postgres'.");
        }

        // ── 4. Lifecycle observers ──────────────────────────────────────────
        // Multiple observers are supported — CompositeLifecycleObserver wraps them.

        // a) Built-in metrics observer: emits System.Diagnostics.Metrics counters
        //    scraped by Prometheus via OpenTelemetry.
        agentBuilder.AddLifecycleObserver<MetricsLifecycleObserver>(services);

        // b) Custom audit log observer (defined below)
        agentBuilder.AddLifecycleObserver(services, new AuditLogObserver());

        // ── 5. OpenTelemetry Prometheus exporter ────────────────────────────
        services.AddOpenTelemetry()
            .WithMetrics(metrics =>
            {
                metrics.AddMeter("PipelogiqSDK.Agent");
                metrics.AddPrometheusExporter();
            });

        // ── 6. Telegram transport ───────────────────────────────────────────
        var telegramToken = Environment.GetEnvironmentVariable("TELEGRAM_BOT_TOKEN");
        if (!string.IsNullOrWhiteSpace(telegramToken))
            services.AddTelegramAgentChannel(telegramToken);
    })
    .Build();

// ── 7. Start ────────────────────────────────────────────────────────────────
await host.StartAsync();

var runner = host.Services.GetRequiredService<PipelineRunner>();
runner.RegisterAgentHandlers();

Console.WriteLine($"Production worker started (store: {Env("STORE_PROVIDER", "redis")}).");
Console.WriteLine("Prometheus metrics at /metrics (port 9464 by default).");

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

await Task.WhenAny(
    runner.StartAsync(cts.Token),
    host.WaitForShutdownAsync(cts.Token));

await host.StopAsync();

// ─────────────────────────────────────────────────────────────────────────────
static string Env(string name, string? fallback = null) =>
    Environment.GetEnvironmentVariable(name)
    ?? fallback
    ?? throw new InvalidOperationException($"Environment variable '{name}' is required.");

// ─────────────────────────────────────────────────────────────────────────────
//  Custom lifecycle observer — writes to console as a simple audit log.
//  In production, replace Console.WriteLine with your logging framework,
//  Datadog, Sentry, or any external audit system.
// ─────────────────────────────────────────────────────────────────────────────
sealed class AuditLogObserver : IAgentLifecycleObserver
{
    public Task OnToolExecutedAsync(AgentToolLifecycleEvent e, CancellationToken ct = default)
    {
        var status = e.IsSuccess ? "OK" : "FAIL";
        Console.WriteLine($"[AUDIT] Tool={e.ToolName} Status={status} Mutating={e.IsMutating} Session={e.SessionId}");
        return Task.CompletedTask;
    }

    public Task OnSessionCompletedAsync(AgentSessionLifecycleEvent e, CancellationToken ct = default)
    {
        Console.WriteLine($"[AUDIT] SessionComplete Tools={e.ToolCallCount} Cost=${e.EstimatedCostUsd:F4} Session={e.SessionId}");
        return Task.CompletedTask;
    }

    public Task OnBudgetWarningAsync(AgentBudgetLifecycleEvent e, CancellationToken ct = default)
    {
        Console.WriteLine($"[AUDIT] BudgetWarning Spent=${e.SpentUsd:F4} Limit=${e.LimitUsd:F4} Session={e.SessionId}");
        return Task.CompletedTask;
    }
}
