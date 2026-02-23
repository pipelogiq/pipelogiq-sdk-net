namespace Pipelogiq.Sdk.Examples.WorkerAndPipelineHost.Configuration;

internal static class ExampleSettingsLoader
{
    public static ExampleSettings Load(string[] args)
    {
        var mode = ParseMode(args);
        var apiKey = Environment.GetEnvironmentVariable("PIPELOGIQ_API_KEY");

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new InvalidOperationException(
                "Set PIPELOGIQ_API_KEY before running this example. Supported modes: worker, pipeline.");
        }

        return new ExampleSettings
        {
            ApiKey = apiKey,
            ApiUrl = Environment.GetEnvironmentVariable("PIPELOGIQ_API_URL") ?? "http://localhost:8081",
            WorkerName = Environment.GetEnvironmentVariable("PIPELOGIQ_WORKER_NAME") ?? "checkout-worker-example",
            PipelineName = Environment.GetEnvironmentVariable("PIPELOGIQ_PIPELINE_NAME") ?? "checkout-demo",
            Mode = mode,
        };
    }

    private static ExampleMode ParseMode(string[] args)
    {
        if (args.Length == 0)
            return ExampleMode.Worker;

        var value = args[0].Trim().ToLowerInvariant();

        return value switch
        {
            "worker" or "--worker" => ExampleMode.Worker,
            "pipeline" or "submit" or "--pipeline" => ExampleMode.Pipeline,
            _ => throw new InvalidOperationException(
                $"Unknown mode '{args[0]}'. Use 'worker' or 'pipeline'.")
        };
    }
}
