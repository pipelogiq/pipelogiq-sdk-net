using PipelogiqSDK.Abstractions;
using PipelogiqSDK.Agent.Models;
using PipelogiqSDK.Agent.Services;
using SmartExpenseAgent.Data;
using System.Text.Json;

namespace SmartExpenseAgent.Tools;

public record CalculateVatInput(
    string Category,
    decimal TotalAmount,
    decimal VatRate = 0.20m   // default EU VAT 20%
);

/// <summary>
/// Calculates the VAT component and the recoverable portion based on expense category policy.
/// </summary>
public class CalculateVatHandler : AgentToolHandlerBase<CalculateVatInput>
{
    protected override Task<AgentToolOutput> ExecuteAsync(
        CalculateVatInput input, IStageContext? context = null, CancellationToken ct = default)
    {
        if (!MockCompanyDatabase.Policies.TryGetValue(input.Category, out var policy))
            return Task.FromResult(AgentToolOutput.Failure($"Unknown category '{input.Category}'."));

        // Amount is VAT-inclusive: net = total / (1 + vatRate)
        var net              = Math.Round(input.TotalAmount / (1 + input.VatRate), 2);
        var vatTotal         = Math.Round(input.TotalAmount - net, 2);
        var vatRecoverable   = Math.Round(vatTotal * policy.VatRecoveryRate, 2);
        var vatNonRecoverable= Math.Round(vatTotal - vatRecoverable, 2);

        var result = new
        {
            totalAmount      = input.TotalAmount,
            netAmount        = net,
            vatTotal,
            vatRate          = $"{input.VatRate * 100:F0}%",
            recoveryRate     = $"{policy.VatRecoveryRate * 100:F0}%",
            vatRecoverable,
            vatNonRecoverable,
            effectiveCost    = Math.Round(input.TotalAmount - vatRecoverable, 2),
        };

        return Task.FromResult(AgentToolOutput.Success(JsonSerializer.Serialize(result)));
    }
}
