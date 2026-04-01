using PipelogiqSDK.Abstractions;
using PipelogiqSDK.Agent.Models;
using PipelogiqSDK.Agent.Services;
using SmartExpenseAgent.Data;
using System.Text.Json;

namespace SmartExpenseAgent.Tools;

public record ValidateExpensePolicyInput(
    string Category,
    decimal TotalAmount,
    int AttendeeCount,      // number of people (employee + guests)
    string EmployeeId,
    string ProjectCode
);

/// <summary>
/// Validates an expense against company policy and the employee's personal approval limits.
/// Returns a detailed breakdown of what is/isn't compliant.
/// </summary>
public class ValidateExpensePolicyHandler : AgentToolHandlerBase<ValidateExpensePolicyInput>
{
    protected override Task<AgentToolOutput> ExecuteAsync(
        ValidateExpensePolicyInput input, IStageContext? context = null, CancellationToken ct = default)
    {
        var violations = new List<string>();
        var warnings   = new List<string>();

        // --- Category policy ---
        if (!MockCompanyDatabase.Policies.TryGetValue(input.Category, out var policy))
            return Task.FromResult(AgentToolOutput.Failure($"Unknown expense category '{input.Category}'. Allowed: {string.Join(", ", MockCompanyDatabase.Policies.Keys)}."));

        if (input.TotalAmount > policy.MaxPerOccasion)
            violations.Add($"Amount €{input.TotalAmount} exceeds the per-occasion limit of €{policy.MaxPerOccasion} for category '{input.Category}'.");

        if (policy.MaxPerPersonPerDay > 0 && input.AttendeeCount > 0)
        {
            var perPerson = input.TotalAmount / input.AttendeeCount;
            if (perPerson > policy.MaxPerPersonPerDay)
                violations.Add($"Per-person amount €{perPerson:F2} exceeds the daily cap of €{policy.MaxPerPersonPerDay} for category '{input.Category}'.");
        }

        // --- Employee approval limit ---
        if (!MockCompanyDatabase.Employees.TryGetValue(input.EmployeeId, out var emp))
        {
            violations.Add($"Employee '{input.EmployeeId}' not found.");
        }
        else if (input.TotalAmount > emp.SingleExpenseLimit)
        {
            violations.Add($"Amount €{input.TotalAmount} exceeds {emp.FullName}'s single-expense approval limit of €{emp.SingleExpenseLimit}. Requires manager sign-off.");
        }

        // --- Project validity ---
        if (!MockCompanyDatabase.Projects.TryGetValue(input.ProjectCode, out var project))
        {
            violations.Add($"Project '{input.ProjectCode}' not found.");
        }
        else if (!project.IsActive)
        {
            violations.Add($"Project '{input.ProjectCode}' ({project.Name}) is no longer active.");
        }

        // --- Budget warning ---
        if (emp != null && MockCompanyDatabase.Budgets.TryGetValue(emp.Department, out var budget))
        {
            if (budget.Remaining < input.TotalAmount)
                violations.Add($"Insufficient department budget. Remaining: €{budget.Remaining}, requested: €{input.TotalAmount}.");
            else if (budget.UsedPercent >= 80)
                warnings.Add($"Department budget is {budget.UsedPercent:F0}% spent. Expense will be approved but finance should be notified.");
        }

        var isCompliant = violations.Count == 0;
        var result = new
        {
            isCompliant,
            category      = input.Category,
            policyNotes   = policy.Notes,
            requiresReceipt = policy.RequiresReceipt,
            violations,
            warnings,
            vatRecoveryRate = policy.VatRecoveryRate,
        };

        return Task.FromResult(AgentToolOutput.Success(JsonSerializer.Serialize(result)));
    }
}
