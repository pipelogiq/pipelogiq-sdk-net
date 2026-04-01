using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using PipelogiqSDK.Agent.Configuration;
using PipelogiqSDK.Agent.Extensions;
using PipelogiqSDK.Agent.Models;
using PipelogiqSDK.Api;
using PipelogiqSDK.Configuration;
using PipelogiqSDK.Runner;
using SmartExpenseAgent.Tools;

// ─────────────────────────────────────────────────────────────────────────────
//  Smart Expense Approval Agent — Pipelogiq SDK Demo
//
//  Scenario: employees submit expense claims via Telegram.
//  The agent validates policy, checks budgets, calculates VAT, asks for
//  confirmation, then registers the claim — all in a single conversation.
//
//  Required environment variables:
//    PIPELOGIQ_API_KEY       — your Pipelogiq workspace API key
//    PIPELOGIQ_API_URL       — Pipelogiq server URL (default: http://localhost:8081)
//    ANTHROPIC_API_KEY       — Anthropic API key
//    TELEGRAM_BOT_TOKEN      — BotFather token for the expense bot
// ─────────────────────────────────────────────────────────────────────────────

var host = Host.CreateDefaultBuilder(args)
    .ConfigureServices(services =>
    {
        // ── 1. Pipelogiq core ────────────────────────────────────────────────
        services.AddPipelogiq(new PipelogiqRunnerOptions
        {
            ApiKey     = Env("PIPELOGIQ_API_KEY"),
            ApiUrl     = Env("PIPELOGIQ_API_URL", "http://localhost:8081"),
            WorkerName = "smart-expense-agent",
        });

        // ── 2. AI agent configuration ────────────────────────────────────────
        var agentBuilder = services.AddPipelogiqAgent(agent =>
        {
            agent.LlmProvider = AgentLlmProvider.Anthropic;
            agent.LlmApiKey   = Env("ANTHROPIC_API_KEY");

            // Use Sonnet for thinking (good balance of cost vs quality),
            // Haiku for the cheaper plan/synthesize steps.
            agent.LlmModel    = "claude-sonnet-4-6";
            agent.ModelRouter = new AgentModelRouter
            {
                PlanModel       = "claude-haiku-4-5-20251001",
                ThinkModel      = "claude-sonnet-4-6",
                SynthesizeModel = "claude-haiku-4-5-20251001",
            };

            agent.TokenBudget = new AgentTokenBudget
            {
                EnablePromptCaching    = true,
                MaxCostUsdPerSession   = 0.30m,   // hard stop at $0.30/conversation
            };

            agent.UseReActMode                  = true;
            agent.RequireConfirmationForMutations = true;
            agent.MaxThinkSteps                 = 12;

            // Inject the Telegram username as the employee ID so the agent
            // never has to ask for it — it's always known from the session.
            agent.ContextInjectionKeys["agent:userId"] = "Employee ID (use this for all tool calls, never ask the user for it)";

            // Human-friendly progress messages — visible in the chat
            agent.Progress = new PipelogiqSDK.Agent.Configuration.AgentProgressOptions
            {
                ThinkingMessages = [
                    "Looking into that…",
                    "Running the numbers…",
                    "Almost there…",
                    "Double-checking the details…",
                    "Just a moment…",
                ],
                ShowStepNumber  = false,
            };

            agent.SystemPrompt = """
                You are a friendly expense assistant for Acme Corp.
                Help employees submit expense claims quickly and correctly.

                Workflow for every expense request — follow this order exactly:
                1. Call getEmployeeInfo with the Employee ID from context.
                   If getEmployeeInfo fails (employee not found), politely ask the user for their
                   employee ID and retry. Known IDs for this demo: anna.ivanova, peter.kozlov,
                   olga.smirnova, ivan.petrov.
                2. Call validateExpensePolicy to check company rules.
                3. Call checkDepartmentBudget to confirm budget availability.
                4. Call calculateVat to calculate the recoverable VAT.
                5. Present a clear, friendly summary of all figures.
                6. Call submitExpenseClaim (requires user confirmation).

                Rules:
                - The Employee ID from context is the user's Telegram username — try it first.
                - If a tool returns an error, handle it gracefully — never give up without explaining.
                - Be warm and conversational, not robotic or technical.
                - Be precise about amounts and limits.
                - If validation fails, explain clearly and suggest what the employee can do.
                - After submission, give the claim ID and mention next steps naturally.
                - Reply in the same language the employee uses (Russian, English, etc.).
                """;
        });

        // ── 3. Register tools ────────────────────────────────────────────────

        agentBuilder

            // Native tool: employee lookup
            .AddNativeTool(
                new AgentToolDefinition
                {
                    Name            = "getEmployeeInfo",
                    Description     = "Retrieves employee profile: full name, department, role, and expense approval limits",
                    ProgressMessage = "Looking up your profile…",
                    Params = new()
                    {
                        ["employeeId"] = new AgentToolParam
                        {
                            Type        = "string",
                            Description = "Employee ID (e.g. anna.ivanova)",
                            Required    = true,
                        },
                    },
                },
                new GetEmployeeInfoHandler())

            // Native tool: budget check
            .AddNativeTool(
                new AgentToolDefinition
                {
                    Name            = "checkDepartmentBudget",
                    Description     = "Returns the remaining expense budget for a department",
                    ProgressMessage = "Checking department budget…",
                    Params = new()
                    {
                        ["department"] = new AgentToolParam
                        {
                            Type        = "string",
                            Description = "Department name (e.g. Sales, Engineering, Finance)",
                            Required    = true,
                        },
                    },
                },
                new CheckDepartmentBudgetHandler())

            // Native tool: policy validation
            .AddNativeTool(
                new AgentToolDefinition
                {
                    Name            = "validateExpensePolicy",
                    Description     = "Validates the expense against company policy rules and employee approval limits. Returns compliance status and any violations.",
                    ProgressMessage = "Checking company policy…",
                    Params = new()
                    {
                        ["category"] = new AgentToolParam
                        {
                            Type        = "string",
                            Description = "Expense category",
                            Required    = true,
                            EnumValues  = ["meals", "travel", "accommodation", "software", "gifts", "training"],
                        },
                        ["totalAmount"] = new AgentToolParam
                        {
                            Type        = "number",
                            Description = "Total amount in EUR (VAT inclusive)",
                            Required    = true,
                        },
                        ["attendeeCount"] = new AgentToolParam
                        {
                            Type        = "integer",
                            Description = "Total number of people (employee + guests)",
                            Required    = true,
                        },
                        ["employeeId"] = new AgentToolParam
                        {
                            Type        = "string",
                            Description = "Employee ID submitting the expense",
                            Required    = true,
                        },
                        ["projectCode"] = new AgentToolParam
                        {
                            Type        = "string",
                            Description = "Project code to charge the expense to",
                            Required    = true,
                        },
                    },
                },
                new ValidateExpensePolicyHandler())

            // Native tool: VAT calculation
            .AddNativeTool(
                new AgentToolDefinition
                {
                    Name            = "calculateVat",
                    Description     = "Calculates the VAT component and recoverable amount based on the expense category",
                    ProgressMessage = "Calculating VAT…",
                    Params = new()
                    {
                        ["category"] = new AgentToolParam
                        {
                            Type        = "string",
                            Description = "Expense category (determines VAT recovery rate)",
                            Required    = true,
                        },
                        ["totalAmount"] = new AgentToolParam
                        {
                            Type        = "number",
                            Description = "VAT-inclusive total amount in EUR",
                            Required    = true,
                        },
                        ["vatRate"] = new AgentToolParam
                        {
                            Type        = "number",
                            Description = "VAT rate as decimal (default 0.20 for 20%)",
                            Required    = false,
                            Example     = "0.20",
                        },
                    },
                },
                new CalculateVatHandler())

            // Native tool: claim submission (MUTATING — requires confirmation)
            .AddNativeTool(
                new AgentToolDefinition
                {
                    Name            = "submitExpenseClaim",
                    Description     = "Creates the official expense claim record. MUTATING — sends the claim for manager approval.",
                    ProgressMessage = "Submitting the claim…",
                    IsMutating      = true,
                    Params = new()
                    {
                        ["employeeId"] = new AgentToolParam
                        {
                            Type        = "string",
                            Description = "Employee ID",
                            Required    = true,
                        },
                        ["projectCode"] = new AgentToolParam
                        {
                            Type        = "string",
                            Description = "Project code",
                            Required    = true,
                        },
                        ["category"] = new AgentToolParam
                        {
                            Type        = "string",
                            Description = "Expense category",
                            Required    = true,
                        },
                        ["totalAmount"] = new AgentToolParam
                        {
                            Type        = "number",
                            Description = "Total amount in EUR",
                            Required    = true,
                        },
                        ["vatRecoverable"] = new AgentToolParam
                        {
                            Type        = "number",
                            Description = "Recoverable VAT amount in EUR",
                            Required    = true,
                        },
                        ["attendeeCount"] = new AgentToolParam
                        {
                            Type        = "integer",
                            Description = "Number of attendees",
                            Required    = true,
                        },
                        ["description"] = new AgentToolParam
                        {
                            Type        = "string",
                            Description = "Short description of the expense",
                            Required    = true,
                        },
                    },
                },
                new SubmitExpenseClaimHandler());

        // ── 4. Telegram transport ────────────────────────────────────────────
        services.AddTelegramAgentChannel(Env("TELEGRAM_BOT_TOKEN"), tg =>
        {
            // Voice transcription via Groq Whisper (free, fast).
            // Configure GROQ_API_KEY with your key from console.groq.com
            tg.VoiceTranscriber = async (bytes, mimeType, ct) =>
            {
                Console.Error.WriteLine($"[VoiceTranscriber] Starting transcription: {bytes.Length} bytes, mime={mimeType}");

                using var http = new HttpClient();
                http.DefaultRequestHeaders.Authorization =
                    new AuthenticationHeaderValue("Bearer", Env("GROQ_API_KEY"));

                using var form = new MultipartFormDataContent();
                form.Add(new ByteArrayContent(bytes), "file", "audio.ogg");
                form.Add(new StringContent("whisper-large-v3-turbo"), "model");
                form.Add(new StringContent("json"), "response_format");

                Console.Error.WriteLine("[VoiceTranscriber] Sending request to Groq Whisper API...");
                using var res = await http.PostAsync(
                    "https://api.groq.com/openai/v1/audio/transcriptions", form, ct);

                var responseBody = await res.Content.ReadAsStringAsync(ct);
                Console.Error.WriteLine($"[VoiceTranscriber] Response: HTTP {(int)res.StatusCode} {res.StatusCode}");

                if (!res.IsSuccessStatusCode)
                {
                    Console.Error.WriteLine($"[VoiceTranscriber] ERROR - Response body: {responseBody}");
                    res.EnsureSuccessStatusCode();
                }

                Console.Error.WriteLine($"[VoiceTranscriber] Raw response: {responseBody}");

                using var doc = JsonDocument.Parse(responseBody);
                var text = doc.RootElement.GetProperty("text").GetString() ?? string.Empty;
                Console.Error.WriteLine($"[VoiceTranscriber] Transcription result: \"{text}\"");
                return text;
            };
        });
    })
    .Build();

// ── 5. Register agent stage handlers and start ───────────────────────────────
// Start the .NET generic host (Telegram IHostedService, etc.)
await host.StartAsync();

// Resolve and start the Pipelogiq worker loop — this is the client-side
// RabbitMQ consumer that executes agent stage handlers. It runs concurrently
// with the host until cancellation is requested.
var runner = host.Services.GetRequiredService<PipelineRunner>();
runner.RegisterAgentHandlers();

Console.WriteLine("Smart Expense Agent is running. Send a message to your Telegram bot.");
Console.WriteLine("Example: \"Business lunch with 2 Microsoft clients, €280 total, project MSFT-2024\"");

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

await Task.WhenAny(
    runner.StartAsync(cts.Token),
    host.WaitForShutdownAsync(cts.Token));

await host.StopAsync();

// ─────────────────────────────────────────────────────────────────────────────
static string Env(string name, string? fallback = null) =>
    Environment.GetEnvironmentVariable(name)
    ?? fallback
    ?? throw new InvalidOperationException($"Environment variable '{name}' is required.");
