namespace Pipelogiq.Sdk.Examples.WorkerAndPipelineHost.Models;

internal sealed record ChargeCustomerInput(
    string OrderId,
    string PaymentToken,
    decimal Amount,
    string Currency);
