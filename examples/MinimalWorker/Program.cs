using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using PipelogiqSDK.Abstractions;
using PipelogiqSDK.Api;
using PipelogiqSDK.Configuration;
using PipelogiqSDK.Runner;
using PipelogiqSDK.StageHelper;

var apiKey = Environment.GetEnvironmentVariable("PIPELOGIQ_API_KEY");
if (string.IsNullOrWhiteSpace(apiKey))
    throw new InvalidOperationException("Set PIPELOGIQ_API_KEY before running this example.");

var apiUrl = Environment.GetEnvironmentVariable("PIPELOGIQ_API_URL") ?? "http://localhost:8081";

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    cts.Cancel();
};

var host = Host.CreateDefaultBuilder(args)
    .ConfigureServices(services =>
    {
        services.AddPipelogiq(new PipelogiqRunnerOptions
        {
            ApiKey = apiKey,
            ApiUrl = apiUrl,
            WorkerName = "minimal-worker"
        });

        services.AddTransient<HelloStageHandler>();
    })
    .Build();

var runner = host.Services.GetRequiredService<PipelineRunner>();
var helloStageHandler = host.Services.GetRequiredService<HelloStageHandler>();

runner.RegisterHandler("HelloStageHandler", helloStageHandler);
await runner.StartAsync(cts.Token);

internal sealed record HelloInput(string Name);

internal sealed class HelloStageHandler : IStageHandler<HelloInput>
{
    public Task<IStageResult> ExecuteAsync(HelloInput input, IStageContext? context = null)
    {
        context.AddItem("lastGreeting", $"Hello {input.Name}");
        return Task.FromResult<IStageResult>(StageResult.Success($"Hello {input.Name}"));
    }
}
