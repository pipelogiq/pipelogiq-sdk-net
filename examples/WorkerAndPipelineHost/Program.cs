using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using PipelogiqSDK.Api;
using Pipelogiq.Sdk.Examples.WorkerAndPipelineHost.Configuration;
using Pipelogiq.Sdk.Examples.WorkerAndPipelineHost.Handlers;
using Pipelogiq.Sdk.Examples.WorkerAndPipelineHost.HostedServices;
using Pipelogiq.Sdk.Examples.WorkerAndPipelineHost.Services;

var settings = ExampleSettingsLoader.Load(args);

Console.WriteLine($"Pipelogiq example mode: {settings.Mode}");
Console.WriteLine($"API URL: {settings.ApiUrl}");
Console.WriteLine($"Worker name: {settings.WorkerName}");

using var host = Host.CreateDefaultBuilder(args)
    .ConfigureServices(services =>
    {
        services.AddSingleton(settings);
        services.AddPipelogiq(settings.ToRunnerOptions());

        services.AddTransient<FraudCheckHandler>();
        services.AddTransient<ChargeCustomerHandler>();
        services.AddTransient<ReceiptHandler>();

        services.AddScoped<WorkerHandlerRegistry>();
        services.AddSingleton<DemoPipelineLauncher>();

        switch (settings.Mode)
        {
            case ExampleMode.Worker:
                services.AddHostedService<PipelogiqWorkerHostedService>();
                break;
            case ExampleMode.Pipeline:
                services.AddHostedService<PipelineSubmissionHostedService>();
                break;
            default:
                throw new InvalidOperationException($"Unsupported mode: {settings.Mode}");
        }
    })
    .Build();

await host.RunAsync();
