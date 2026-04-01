# Getting Started

This guide covers the minimal setup to run a Pipelogiq worker with the .NET SDK.

## Installation

Install the package from NuGet.org:

```bash
dotnet add package PipelogiqSDK --prerelease
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

## Reporting structured errors

When a stage fails, return a result with an `ErrorCode` so that server-side retry policies can decide whether to retry based on the specific failure kind:

```csharp
public Task<IStageResult> ExecuteAsync(MyInput input, IStageContext? context = null)
{
    try
    {
        var response = await _httpClient.PostAsync(...);

        if ((int)response.StatusCode == 429)
            return Task.FromResult<IStageResult>(
                StageResult.RateLimitExceeded("API rate limit exceeded"));

        if (!response.IsSuccessStatusCode)
            return Task.FromResult<IStageResult>(
                StageResult.UpstreamError($"Upstream returned {response.StatusCode}"));

        return Task.FromResult<IStageResult>(StageResult.Success("ok"));
    }
    catch (TaskCanceledException)
    {
        return Task.FromResult<IStageResult>(StageResult.Timeout("Request timed out"));
    }
}
```

Built-in factory helpers and their error codes:

| Helper | `ErrorCode` |
|--------|-------------|
| `StageResult.RateLimitExceeded(msg)` | `RATE_LIMIT_EXCEEDED` |
| `StageResult.Timeout(msg)` | `TIMEOUT` |
| `StageResult.UpstreamError(msg)` | `UPSTREAM_ERROR` |
| `StageResult.Error(msg, code)` | any string you define |

On the server side, configure a retry policy with `retryOn.errorCodes` to target specific codes:

```json
{
  "type": "retry",
  "rule": {
    "maxAttempts": 5,
    "backoff": "exponential",
    "baseDelayMs": 1000,
    "maxDelayMs": 60000,
    "jitter": true,
    "retryOn": {
      "errorCodes": ["RATE_LIMIT_EXCEEDED", "TIMEOUT"]
    }
  }
}
```

Stages that fail with an error code not in the list (e.g. a validation error) are marked `Failed` immediately without retrying.
