# Getting Started

This guide covers the minimal setup to run a Pipelogiq worker with the .NET SDK.

## Installation

Add GitHub Packages source and install the package:

```bash
dotnet nuget add source "https://nuget.pkg.github.com/pipelogiq/index.json" \
  --name pipelogiq \
  --username <github-username> \
  --password <github-token> \
  --store-password-in-clear-text

dotnet add package PipelogiqSDK --source pipelogiq --prerelease
```

## Basic pipeline example

```csharp
using PipelogiqSDK.Builders;
using PipelogiqSDK.Configuration;

var options = new PipelogiqRunnerOptions
{
    ApiKey = Environment.GetEnvironmentVariable("PIPELOGIQ_API_KEY"),
    ApiUrl = Environment.GetEnvironmentVariable("PIPELOGIQ_API_URL") ?? "http://localhost:8081"
};

await PipelineBuilder.Create("sample-pipeline", options)
    .WithAction("hello", "HelloStageHandler", new { Name = "world" })
    .WithAction("receipt", "ReceiptHandler")
    .SendAsync();
```

`WithAction(...)` accepts an optional `input` argument. Omit it for handlers that do not require input payloads (`IStageHandler`).

## Register a stage handler

```csharp
using PipelogiqSDK.Abstractions;
using PipelogiqSDK.StageHelper;

public sealed record HelloInput(string Name);

public sealed class HelloStageHandler : IStageHandler<HelloInput>
{
    public Task<IStageResult> ExecuteAsync(HelloInput input, IStageContext? context = null)
    {
        context.AddItem("lastGreeting", $"Hello {input.Name}");
        return Task.FromResult<IStageResult>(StageResult.Success($"Hello {input.Name}"));
    }
}
```

## Run worker

```csharp
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using PipelogiqSDK.Api;
using PipelogiqSDK.Configuration;
using PipelogiqSDK.Runner;

var host = Host.CreateDefaultBuilder(args)
    .ConfigureServices(services =>
    {
        services.AddPipelogiq(new PipelogiqRunnerOptions
        {
            ApiKey = Environment.GetEnvironmentVariable("PIPELOGIQ_API_KEY"),
            ApiUrl = Environment.GetEnvironmentVariable("PIPELOGIQ_API_URL") ?? "http://localhost:8081",
            WorkerName = "minimal-worker"
        });

        services.AddTransient<HelloStageHandler>();
    })
    .Build();

var runner = host.Services.GetRequiredService<PipelineRunner>();
var helloStageHandler = host.Services.GetRequiredService<HelloStageHandler>();

runner.RegisterHandler("HelloStageHandler", helloStageHandler);
await runner.StartAsync(CancellationToken.None);
```

A runnable version of this setup is available in `examples/MinimalWorker`.
