using Microsoft.Extensions.Logging;
using PipelogiqSDK.Abstractions;
using PipelogiqSDK.StageHelper;

namespace Pipelogiq.Sdk.Examples.WorkerAndPipelineHost.Handlers;

internal sealed class ReceiptHandler : IStageHandler
{
    private readonly ILogger<ReceiptHandler> _logger;

    public ReceiptHandler(ILogger<ReceiptHandler> logger)
    {
        _logger = logger;
    }

    public Task<IStageResult> ExecuteAsync(IStageContext? context = null)
    {
        var orderId = context.TryGetValue<string>("orderId") ?? "unknown-order";
        var email = context.TryGetValue<string>("customerEmail") ?? "unknown@example.com";
        var chargeId = context.TryGetValue<string>("chargeId") ?? "n/a";
        var chargeStatus = context.TryGetValue<string>("chargeStatus") ?? "unknown";

        _logger.LogInformation(
            "Receipt stage for order {OrderId}: email={Email}, chargeStatus={ChargeStatus}, chargeId={ChargeId}.",
            orderId,
            email,
            chargeStatus,
            chargeId);

        context.AddItem("receiptStatus", "queued");

        return Task.FromResult<IStageResult>(
            StageResult.Success($"Receipt queued for {email} (order={orderId}, chargeStatus={chargeStatus})."));
    }
}
