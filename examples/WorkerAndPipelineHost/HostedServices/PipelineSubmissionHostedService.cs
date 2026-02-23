using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Pipelogiq.Sdk.Examples.WorkerAndPipelineHost.Services;

namespace Pipelogiq.Sdk.Examples.WorkerAndPipelineHost.HostedServices;

internal sealed class PipelineSubmissionHostedService : BackgroundService
{
    private readonly DemoPipelineLauncher _launcher;
    private readonly IHostApplicationLifetime _lifetime;
    private readonly ILogger<PipelineSubmissionHostedService> _logger;

    public PipelineSubmissionHostedService(
        DemoPipelineLauncher launcher,
        IHostApplicationLifetime lifetime,
        ILogger<PipelineSubmissionHostedService> logger)
    {
        _launcher = launcher;
        _lifetime = lifetime;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await _launcher.SubmitCheckoutPipelineAsync(stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            _logger.LogInformation("Pipeline submission was cancelled.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to submit demo pipeline.");
        }
        finally
        {
            _lifetime.StopApplication();
        }
    }
}
