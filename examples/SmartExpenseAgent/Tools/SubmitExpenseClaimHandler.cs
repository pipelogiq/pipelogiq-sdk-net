using PipelogiqSDK.Abstractions;
using PipelogiqSDK.Agent.Models;
using PipelogiqSDK.Agent.Services;
using SmartExpenseAgent.Data;
using System.Text.Json;

namespace SmartExpenseAgent.Tools;

public record SubmitExpenseClaimInput(
    string EmployeeId,
    string ProjectCode,
    string Category,
    decimal TotalAmount,
    decimal VatRecoverable,
    int AttendeeCount,
    string Description
);

/// <summary>
/// MUTATING — creates the official expense claim record.
/// Requires user confirmation before executing (RequireConfirmationForMutations = true).
/// In production this would POST to your ERP/Concur/SAP.
/// </summary>
public class SubmitExpenseClaimHandler : AgentToolHandlerBase<SubmitExpenseClaimInput>
{
    protected override Task<AgentToolOutput> ExecuteAsync(
        SubmitExpenseClaimInput input, IStageContext? context = null, CancellationToken ct = default)
    {
        if (!MockCompanyDatabase.Employees.TryGetValue(input.EmployeeId, out var emp))
            return Task.FromResult(AgentToolOutput.Failure($"Employee '{input.EmployeeId}' not found."));

        if (!MockCompanyDatabase.Projects.TryGetValue(input.ProjectCode, out var project))
            return Task.FromResult(AgentToolOutput.Failure($"Project '{input.ProjectCode}' not found."));

        var claimId = MockCompanyDatabase.NextClaimId();
        var claim = new MockCompanyDatabase.ExpenseClaim(
            ClaimId      : claimId,
            EmployeeId   : emp.Id,
            EmployeeName : emp.FullName,
            Department   : emp.Department,
            ProjectCode  : input.ProjectCode,
            Category     : input.Category,
            TotalAmount  : input.TotalAmount,
            VatRecoverable: input.VatRecoverable,
            AttendeeCount: input.AttendeeCount,
            Description  : input.Description,
            SubmittedAt  : DateTimeOffset.UtcNow,
            Status       : "Pending Approval"
        );

        MockCompanyDatabase.SubmittedClaims.Add(claim);

        // Update the department's spent budget (optimistic — real ERP would do this)
        if (MockCompanyDatabase.Budgets.TryGetValue(emp.Department, out var budget))
        {
            MockCompanyDatabase.Budgets[emp.Department] = budget with
            {
                SpentBudget = budget.SpentBudget + input.TotalAmount
            };
        }

        var result = new
        {
            claimId,
            status          = "Pending Approval",
            employee        = emp.FullName,
            department      = emp.Department,
            project         = $"{input.ProjectCode} — {project.Name}",
            category        = input.Category,
            totalAmount     = input.TotalAmount,
            vatRecoverable  = input.VatRecoverable,
            effectiveCost   = input.TotalAmount - input.VatRecoverable,
            attendeeCount   = input.AttendeeCount,
            description     = input.Description,
            submittedAt     = claim.SubmittedAt.ToString("yyyy-MM-dd HH:mm UTC"),
            nextStep        = "Your manager will receive an approval request within 1 business hour.",
        };

        return Task.FromResult(AgentToolOutput.Success(JsonSerializer.Serialize(result)));
    }
}
