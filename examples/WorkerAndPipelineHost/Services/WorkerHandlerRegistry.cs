using PipelogiqSDK.Agent.Configuration;
using PipelogiqSDK.Agent.Extensions;
using PipelogiqSDK.Runner;
using Pipelogiq.Sdk.Examples.WorkerAndPipelineHost.Handlers;

namespace Pipelogiq.Sdk.Examples.WorkerAndPipelineHost.Services;

internal sealed class WorkerHandlerRegistry(
    TelegramAgentChannelOptions? telegramOptions = null)
{
    public void RegisterHandlers(PipelineRunner runner)
    {
        runner.RegisterHandler(nameof(FraudCheckHandler), typeof(FraudCheckHandler));
        runner.RegisterHandler(nameof(ChargeCustomerHandler), typeof(ChargeCustomerHandler));
        runner.RegisterHandler(nameof(ReceiptHandler), typeof(ReceiptHandler));

        if (telegramOptions != null)
            runner.RegisterAgentHandlers();
    }
}
