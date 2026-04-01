# Smart Expense Approval Agent

A realistic business demo showing how Pipelogiq's AI agent handles corporate expense claims through Telegram.

## The Scenario

An employee messages the Telegram bot in plain language:

> *"Business lunch with 2 Microsoft clients, €280 total, project MSFT-2024"*

The agent then:

1. **Verifies the employee** — looks up their name, department, and personal approval limits
2. **Validates policy** — checks category rules (€120/person/day for meals), per-occasion caps, and project status
3. **Checks department budget** — confirms Sales department has remaining budget
4. **Calculates VAT** — meals have 50% VAT recovery → €23.33 recoverable, effective cost €256.67
5. **Presents a summary** — shows all figures before doing anything
6. **Asks for confirmation** — "Submit this claim for €280?" (RequireConfirmationForMutations = true)
7. **Registers the claim** — creates claim EXP-2024-1001, status "Pending Approval"
8. **Confirms next steps** — "Your manager will receive an approval request within 1 business hour"

## What This Demonstrates

| Feature | How It's Used |
|---------|---------------|
| **Native tools** | All 5 tools are C# code, not HTTP calls |
| **Typed handlers** | `AgentToolHandlerBase<TInput>` auto-deserializes params |
| **Mutation confirmation** | `submitExpenseClaim` pauses for user approval |
| **ReAct mode** | Agent reasons step-by-step, one tool at a time |
| **Model routing** | Haiku for plan/synthesis, Sonnet for think steps |
| **Prompt caching** | System prompt + tools cached — cheaper on long conversations |
| **Token budget** | Hard stop at $0.30/conversation |
| **Telegram** | Full end-to-end user interaction |
| **Context access** | Tools read/write shared pipeline context |

## Mock Data

The example uses in-memory data from `Data/MockCompanyDatabase.cs`:

**Employees** (use these as employee IDs in the conversation):
| ID | Name | Department | Single Limit | Monthly Limit |
|----|------|------------|-------------|---------------|
| `anna.ivanova` | Anna Ivanova | Sales | €500 | €2,000 |
| `peter.kozlov` | Peter Kozlov | Sales | €1,500 | €6,000 |
| `olga.smirnova` | Olga Smirnova | Engineering | €300 | €1,200 |
| `ivan.petrov` | Ivan Petrov | Finance | €2,000 | €8,000 |

**Projects**: `MSFT-2024`, `INTERNAL-Q4`, `CLOUD-MIG` (active) · `LEGACY-EOL` (inactive — try it!)

**Categories**: `meals`, `travel`, `accommodation`, `software`, `gifts`, `training`

## Interesting Test Cases

```
# Happy path
"Business lunch with 2 Microsoft clients, €280, project MSFT-2024"
# → employee: anna.ivanova (set via context from Telegram session)

# Policy violation (€350 total / 3 people = €116.67/person — under €120 limit, barely OK)
"Team dinner with 2 colleagues, €350, project INTERNAL-Q4"

# Over personal limit (Olga's limit is €300)
"Conference ticket €450, project CLOUD-MIG, employee olga.smirnova"

# Inactive project
"Software license €200, project LEGACY-EOL"

# Budget warning (Engineering is at 92.7% spent)
"Training course €500, project CLOUD-MIG"
```

## Running

```bash
export PIPELOGIQ_API_KEY="your-key"
export PIPELOGIQ_API_URL="http://localhost:8081"
export ANTHROPIC_API_KEY="sk-ant-..."
export TELEGRAM_BOT_TOKEN="123456:ABC..."

dotnet run
```

The agent expects the Telegram user's ID to be stored in the pipeline context under `agent:userId`.
The Telegram transport sets this automatically from the Telegram `from.username` field.
The system prompt tells the agent to use `employeeId` from that value when calling tools.

## Architecture

```
Telegram User
     │
     ▼
TelegramAgentChannel (polls bot updates)
     │  creates pipeline with context: {userId, chatId}
     ▼
AgentThinkHandler (ReAct loop)
     │  calls LLM → decides next tool
     ├─► getEmployeeInfo        [native: GetEmployeeInfoHandler]
     ├─► validateExpensePolicy  [native: ValidateExpensePolicyHandler]
     ├─► checkDepartmentBudget  [native: CheckDepartmentBudgetHandler]
     ├─► calculateVat           [native: CalculateVatHandler]
     ├─► [CONFIRMATION PAUSE]   [AgentConfirmationHandler → Telegram]
     └─► submitExpenseClaim     [native: SubmitExpenseClaimHandler]
     │
     ▼
AgentResponderHandler
     │  synthesizes final response
     ▼
Telegram User ← "Claim EXP-2024-1001 submitted. Your manager will be notified."
```
