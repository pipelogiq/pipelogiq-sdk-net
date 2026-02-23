using PipelogiqSDK.Configuration;

namespace Pipelogiq.Sdk.Examples.WorkerAndPipelineHost.Configuration;

internal sealed class ExampleSettings
{
    public required string ApiKey { get; init; }
    public string ApiUrl { get; init; } = "http://localhost:8081";
    public string WorkerName { get; init; } = "checkout-worker-example";
    public string PipelineName { get; init; } = "checkout-demo";
    public ExampleMode Mode { get; init; } = ExampleMode.Worker;

    public PipelogiqRunnerOptions ToRunnerOptions()
    {
        return new PipelogiqRunnerOptions
        {
            ApiKey = ApiKey,
            ApiUrl = ApiUrl,
            WorkerName = WorkerName,
            Environment = Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT") ?? "Development",
            Metadata = new Dictionary<string, string>
            {
                ["exampleProject"] = "WorkerAndPipelineHost",
                ["exampleMode"] = Mode.ToString().ToLowerInvariant(),
            }
        };
    }
}
