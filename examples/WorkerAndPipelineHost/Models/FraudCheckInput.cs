namespace Pipelogiq.Sdk.Examples.WorkerAndPipelineHost.Models;

internal sealed record FraudCheckInput(
    string OrderId,
    string CustomerId,
    decimal Amount,
    string Currency);
