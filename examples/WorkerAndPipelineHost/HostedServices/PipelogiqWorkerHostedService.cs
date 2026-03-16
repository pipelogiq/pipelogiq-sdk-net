using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PipelogiqSDK.Agent.Configuration;
using PipelogiqSDK.Runner;
using Pipelogiq.Sdk.Examples.WorkerAndPipelineHost.Configuration;
using Pipelogiq.Sdk.Examples.WorkerAndPipelineHost.Services;

namespace Pipelogiq.Sdk.Examples.WorkerAndPipelineHost.HostedServices;

internal sealed class PipelogiqWorkerHostedService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ExampleSettings _settings;
    private readonly TelegramAgentChannelOptions? _telegramOptions;
    private readonly ILogger<PipelogiqWorkerHostedService> _logger;

    public PipelogiqWorkerHostedService(
        IServiceScopeFactory scopeFactory,
        ExampleSettings settings,
        TelegramAgentChannelOptions? telegramOptions,
        ILogger<PipelogiqWorkerHostedService> logger)
    {
        _scopeFactory = scopeFactory;
        _settings = settings;
        _telegramOptions = telegramOptions;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var runner = scope.ServiceProvider.GetRequiredService<PipelineRunner>();
        var handlerRegistry = scope.ServiceProvider.GetRequiredService<WorkerHandlerRegistry>();

        handlerRegistry.RegisterHandlers(runner);

        _logger.LogInformation(
            "Starting Pipelogiq worker '{WorkerName}'. Registered handlers: {Handlers}. Telegram channel enabled: {TelegramEnabled}.",
            _settings.WorkerName,
            string.Join(", ", new[] { nameof(Handlers.FraudCheckHandler), nameof(Handlers.ChargeCustomerHandler), nameof(Handlers.ReceiptHandler) }),
            _telegramOptions != null);

        await runner.StartAsync(stoppingToken);
    }
}
