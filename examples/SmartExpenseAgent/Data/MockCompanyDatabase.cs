namespace SmartExpenseAgent.Data;

/// <summary>
/// In-memory company data. In production this would be your ERP / HR system.
/// </summary>
public static class MockCompanyDatabase
{
    // ── Employees ────────────────────────────────────────────────────────────

    public record Employee(
        string Id,
        string FullName,
        string Department,
        string Role,
        decimal SingleExpenseLimit,  // max per single claim
        decimal MonthlyExpenseLimit  // max per month
    );

    public static readonly IReadOnlyDictionary<string, Employee> Employees =
        new Dictionary<string, Employee>(StringComparer.OrdinalIgnoreCase)
        {
            ["anna.ivanova"] = new("anna.ivanova", "Anna Ivanova",  "Sales",      "Account Executive", 500m,  2000m),
            ["peter.kozlov"] = new("peter.kozlov", "Peter Kozlov",  "Sales",      "Sales Manager",     1500m, 6000m),
            ["fedulov_sergei"]= new("fedulov_sergei","Olga Smirnova", "Engineering","Team Lead",         300m,  1200m),
            ["ivan.petrov"]  = new("ivan.petrov",  "Ivan Petrov",   "Finance",    "Controller",        2000m, 8000m),
        };

    // ── Department budgets ───────────────────────────────────────────────────

    public record DepartmentBudget(
        string Department,
        decimal TotalBudget,
        decimal SpentBudget
    )
    {
        public decimal Remaining => TotalBudget - SpentBudget;
        public decimal UsedPercent => TotalBudget > 0 ? SpentBudget / TotalBudget * 100 : 0;
    }

    public static readonly Dictionary<string, DepartmentBudget> Budgets =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["Sales"]       = new("Sales",       5_000m, 1_840m),
            ["Engineering"] = new("Engineering", 3_000m, 2_780m),
            ["Finance"]     = new("Finance",     2_000m,   350m),
        };

    // ── Expense categories and policies ─────────────────────────────────────

    public record ExpensePolicy(
        string Category,
        decimal MaxPerOccasion,    // max total for single expense
        decimal MaxPerPersonPerDay,// per-person daily cap
        decimal VatRecoveryRate,   // fraction of VAT recoverable (0..1)
        bool RequiresReceipt,
        string Notes
    );

    public static readonly IReadOnlyDictionary<string, ExpensePolicy> Policies =
        new Dictionary<string, ExpensePolicy>(StringComparer.OrdinalIgnoreCase)
        {
            ["meals"]        = new("meals",        300m, 120m, 0.50m, true,  "Business meals with external clients. Max €120/person/day."),
            ["travel"]       = new("travel",      2000m,   0m, 1.00m, true,  "Flights, trains, taxis. Full VAT recovery."),
            ["accommodation"]= new("accommodation",250m,   0m, 1.00m, true,  "Hotels. Max €250/night. Full VAT recovery."),
            ["software"]     = new("software",    500m,    0m, 1.00m, true,  "SaaS tools and licenses. Full VAT recovery."),
            ["gifts"]        = new("gifts",        50m,    0m, 0.00m, true,  "Client gifts. No VAT recovery. Max €50/recipient."),
            ["training"]     = new("training",    800m,    0m, 1.00m, true,  "Conferences, courses. Full VAT recovery."),
        };

    // ── Projects ─────────────────────────────────────────────────────────────

    public record Project(string Code, string Name, string Owner, bool IsActive);

    public static readonly IReadOnlyDictionary<string, Project> Projects =
        new Dictionary<string, Project>(StringComparer.OrdinalIgnoreCase)
        {
            ["MSFT-2024"]   = new("MSFT-2024",   "Microsoft Partnership",   "Sales",      true),
            ["INTERNAL-Q4"] = new("INTERNAL-Q4", "Q4 Internal Initiatives", "Finance",    true),
            ["CLOUD-MIG"]   = new("CLOUD-MIG",   "Cloud Migration Phase 2", "Engineering",true),
            ["LEGACY-EOL"]  = new("LEGACY-EOL",  "Legacy System EOL",       "Engineering",false),
        };

    // ── Submitted claims (grows during session) ───────────────────────────────

    public static readonly List<ExpenseClaim> SubmittedClaims = [];

    public record ExpenseClaim(
        string ClaimId,
        string EmployeeId,
        string EmployeeName,
        string Department,
        string ProjectCode,
        string Category,
        decimal TotalAmount,
        decimal VatRecoverable,
        int AttendeeCount,
        string Description,
        DateTimeOffset SubmittedAt,
        string Status
    );

    public static string NextClaimId() =>
        $"EXP-{DateTimeOffset.UtcNow:yyyy}-{SubmittedClaims.Count + 1001:D4}";
}
