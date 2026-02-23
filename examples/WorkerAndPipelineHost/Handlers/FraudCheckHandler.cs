using Microsoft.Extensions.Logging;
using PipelogiqSDK.Abstractions;
using PipelogiqSDK.StageHelper;
using Pipelogiq.Sdk.Examples.WorkerAndPipelineHost.Models;

namespace Pipelogiq.Sdk.Examples.WorkerAndPipelineHost.Handlers;

internal sealed class FraudCheckHandler : IStageHandler<FraudCheckInput>
{
    private readonly ILogger<FraudCheckHandler> _logger;

    public FraudCheckHandler(ILogger<FraudCheckHandler> logger)
    {
        _logger = logger;
    }

    public Task<IStageResult> ExecuteAsync(FraudCheckInput input, IStageContext? context = null)
    {
        var score = input.Amount >= 1000m ? 72 : 12;
        var decision = score >= 70 ? "reject" : "approve";

        _logger.LogInformation(
            "Fraud check for order {OrderId}: amount={Amount} {Currency}, score={Score}, decision={Decision}",
            input.OrderId,
            input.Amount,
            input.Currency,
            score,
            decision);

        context.AddItem("orderId", input.OrderId);
        context.AddItem("customerId", input.CustomerId);
        context.AddItem("fraudScore", score);
        context.AddItem("fraudDecision", decision);

        return Task.FromResult<IStageResult>(StageResult.Success($"Fraud decision: {decision} (score={score})"));
    }
}
