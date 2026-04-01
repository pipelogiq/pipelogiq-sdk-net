using PipelogiqSDK.Abstractions;
using PipelogiqSDK.Agent.Models;
using PipelogiqSDK.Agent.Services;
using SmartExpenseAgent.Data;
using System.Text.Json;

namespace SmartExpenseAgent.Tools;

public record CheckDepartmentBudgetInput(string Department);

/// <summary>
/// Returns the remaining expense budget for a department and whether it can absorb the requested amount.
/// </summary>
public class CheckDepartmentBudgetHandler : AgentToolHandlerBase<CheckDepartmentBudgetInput>
{
    protected override Task<AgentToolOutput> ExecuteAsync(
        CheckDepartmentBudgetInput input, IStageContext? context = null, CancellationToken ct = default)
    {
        if (!MockCompanyDatabase.Budgets.TryGetValue(input.Department, out var budget))
            return Task.FromResult(AgentToolOutput.Failure($"No budget record found for department '{input.Department}'."));

        var result = new
        {
            department   = budget.Department,
            totalBudget  = budget.TotalBudget,
            spentBudget  = budget.SpentBudget,
            remaining    = budget.Remaining,
            usedPercent  = Math.Round(budget.UsedPercent, 1),
            isLow        = budget.UsedPercent >= 80,
        };

        return Task.FromResult(AgentToolOutput.Success(JsonSerializer.Serialize(result)));
    }
}
