using System.Diagnostics;

namespace Pipelogiq.Sdk.Examples.WorkerAndPipelineHost.Services;

internal static class ExampleTelemetry
{
    public static readonly ActivitySource ActivitySource = new("Pipelogiq.Sdk.Examples.WorkerAndPipelineHost");
}
