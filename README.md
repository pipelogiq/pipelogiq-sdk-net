# Pipelogiq .NET SDK

![Status: Preview v0.3.1-preview.4](https://img.shields.io/badge/status-Preview%20v0.3.1--preview.4-orange)

Pipelogiq .NET SDK provides the .NET worker runtime and API client helpers for Pipelogiq pipelines.
This repository is currently shipping **v0.3.1-preview.4** packages focused on execution, transport, integration primitives, and AI agent workflows.
APIs may change while the SDK is still stabilizing.

## Installation

Packages are published on NuGet.org.

```bash
dotnet add package PipelogiqSDK --prerelease
```

## Quickstart

```csharp
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using PipelogiqSDK.Abstractions;
using PipelogiqSDK.Api;
using PipelogiqSDK.Configuration;
using PipelogiqSDK.Runner;
using PipelogiqSDK.StageHelper;

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

internal sealed record HelloInput(string Name);

internal sealed class HelloStageHandler : IStageHandler<HelloInput>
{
    public Task<IStageResult> ExecuteAsync(HelloInput input, IStageContext? context = null)
    {
        context.AddItem("lastGreeting", $"Hello {input.Name}");
        return Task.FromResult<IStageResult>(StageResult.Success($"Hello {input.Name}"));
    }
}
```

For handlers without input (`IStageHandler`), `PipelineBuilder.WithAction(...)` can be called without an input payload:

```csharp
var options = new PipelogiqRunnerOptions
{
    ApiKey = Environment.GetEnvironmentVariable("PIPELOGIQ_API_KEY"),
    ApiUrl = Environment.GetEnvironmentVariable("PIPELOGIQ_API_URL") ?? "http://localhost:8081"
};

await PipelineBuilder.Create("sample-pipeline", options)
    .WithAction("receipt", "ReceiptHandler")
    .SendAsync();
```

## Tracing

The SDK is compatible with OpenTelemetry conventions and W3C trace context.
The SDK automatically captures `traceparent` / `tracestate` from `Activity.Current` when building pipelines/events and creates a consumer `Activity` around stage handler execution (when an `ActivityListener`/OpenTelemetry pipeline is configured).
Manual `traceparent` / `tracestate` values are still respected and not overwritten.
See `docs/tracing-opentelemetry.md` for examples.

## AI Agent (Preview)

The SDK includes optional AI agent handlers that can orchestrate tool calls as pipeline stages.
See `docs/ai-agent.md` for setup, confirmation flow, and security notes.

## Compatibility

- Target framework: `net8.0`
- Current maturity level: `v0.x preview` (breaking changes are possible)

Details: `docs/compatibility.md`

## Documentation

- `docs/getting-started.md`
- `docs/tracing-opentelemetry.md`
- `docs/compatibility.md`
- `docs/ai-agent.md`
- `examples/README.md`
- `examples/MinimalWorker/README.md`
- `examples/WorkerAndPipelineHost/README.md`

## Contributing

See `CONTRIBUTING.md` for build, test, branch naming, and pull request expectations.

## License

Licensed under the MIT License. See `LICENSE`.
