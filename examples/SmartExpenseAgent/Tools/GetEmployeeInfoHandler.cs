using PipelogiqSDK.Abstractions;
using PipelogiqSDK.Agent.Models;
using PipelogiqSDK.Agent.Services;
using SmartExpenseAgent.Data;
using System.Text.Json;

namespace SmartExpenseAgent.Tools;

public record GetEmployeeInfoInput(string EmployeeId);

/// <summary>
/// Looks up the employee profile: name, department, role, and expense approval limits.
/// </summary>
public class GetEmployeeInfoHandler : AgentToolHandlerBase<GetEmployeeInfoInput>
{
    protected override Task<AgentToolOutput> ExecuteAsync(
        GetEmployeeInfoInput input, IStageContext? context = null, CancellationToken ct = default)
    {
        if (!MockCompanyDatabase.Employees.TryGetValue(input.EmployeeId, out var emp))
            return Task.FromResult(AgentToolOutput.Failure($"Employee '{input.EmployeeId}' not found."));

        var result = new
        {
            id             = emp.Id,
            fullName       = emp.FullName,
            department     = emp.Department,
            role           = emp.Role,
            singleExpenseLimit = emp.SingleExpenseLimit,
            monthlyExpenseLimit= emp.MonthlyExpenseLimit,
        };

        return Task.FromResult(AgentToolOutput.Success(JsonSerializer.Serialize(result)));
    }
}
