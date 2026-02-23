using PipelogiqSDK.Runner;
using Pipelogiq.Sdk.Examples.WorkerAndPipelineHost.Handlers;

namespace Pipelogiq.Sdk.Examples.WorkerAndPipelineHost.Services;

internal sealed class WorkerHandlerRegistry(
    FraudCheckHandler fraudCheckHandler,
    ChargeCustomerHandler chargeCustomerHandler,
    ReceiptHandler receiptHandler)
{
    public void RegisterHandlers(PipelineRunner runner)
    {
        runner.RegisterHandler(nameof(FraudCheckHandler), fraudCheckHandler);
        runner.RegisterHandler(nameof(ChargeCustomerHandler), chargeCustomerHandler);
        runner.RegisterHandler(nameof(ReceiptHandler), receiptHandler);
    }
}
