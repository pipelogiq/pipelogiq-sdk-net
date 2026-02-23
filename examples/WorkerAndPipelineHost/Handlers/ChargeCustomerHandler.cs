using Microsoft.Extensions.Logging;
using PipelogiqSDK.Abstractions;
using PipelogiqSDK.StageHelper;
using Pipelogiq.Sdk.Examples.WorkerAndPipelineHost.Models;

namespace Pipelogiq.Sdk.Examples.WorkerAndPipelineHost.Handlers;

internal sealed class ChargeCustomerHandler : IStageHandler<ChargeCustomerInput>
{
    private readonly ILogger<ChargeCustomerHandler> _logger;

    public ChargeCustomerHandler(ILogger<ChargeCustomerHandler> logger)
    {
        _logger = logger;
    }

    public Task<IStageResult> ExecuteAsync(ChargeCustomerInput input, IStageContext? context = null)
    {
        var fraudDecision = context.TryGetValue<string>("fraudDecision") ?? "unknown";
        if (!string.Equals(fraudDecision, "approve", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning(
                "Charge skipped for order {OrderId}. Fraud decision was '{FraudDecision}'.",
                input.OrderId,
                fraudDecision);

            context.AddItem("chargeStatus", "skipped");
            return Task.FromResult<IStageResult>(StageResult.Error($"Charge skipped because fraudDecision={fraudDecision}."));
        }

        var chargeId = $"ch_{Guid.NewGuid():N}";

        _logger.LogInformation(
            "Charge authorized for order {OrderId}. ChargeId={ChargeId}, amount={Amount} {Currency}.",
            input.OrderId,
            chargeId,
            input.Amount,
            input.Currency);

        context.AddItem("chargeId", chargeId);
        context.AddItem("chargeStatus", "authorized");

        return Task.FromResult<IStageResult>(StageResult.Success($"Charge authorized: {chargeId}"));
    }
}
