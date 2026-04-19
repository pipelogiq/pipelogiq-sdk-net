# AI Agent

The SDK includes an optional AI agent layer built on top of Pipelogiq stages.
It uses regular stage handlers (`AgentOrchestratorHandler`, `AgentToolHandler`, `AgentConfirmationHandler`, `AgentThinkHandler`, `AgentResponderHandler`) and can run in:

- plan-and-execute mode
- ReAct mode (reason + act loop)

Built-in providers for the default planner are:

- `Anthropic` Messages API
- `OpenAI` Chat Completions API
- `Ollama` local chat API

## Setup

Register Pipelogiq first, then agent services and handlers:

```csharp
using PipelogiqSDK.Agent.Extensions;
using PipelogiqSDK.Agent.Models;

services.AddPipelogiq(options);

var agentBuilder = services.AddPipelogiqAgent(agent =>
{
    agent.LlmProvider = AgentLlmProvider.Anthropic;
    agent.LlmApiKey = Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY");
    agent.LlmModel = "claude-opus-4-6";
    agent.RequireConfirmationForMutations = true;
    agent.UseReActMode = true;
});

agentBuilder
    .AddTargetApi(new AgentTargetApiDefinition
    {
        Name = "orders-api",
        BaseUrl = "https://api.example.com",
        Authentication = new AgentAuthHeaderDefinition
        {
            HeaderName = "Authorization",
            Scheme = "Bearer",
            ValueTemplate = "{{context:userAccessToken}}"
        },
        HeaderTemplates = new Dictionary<string, string>
        {
            ["X-Tenant-Id"] = "{{context:tenantId}}"
        }
    })
    .AddTool(new AgentToolDefinition
    {
        Name = "getOrder",
        Description = "Gets order by id",
        HttpMethod = "GET",
        TargetApiName = "orders-api",
        UrlTemplate = "/api/orders/{id}"
    });
```

Register built-in agent handlers in the runner:

```csharp
runner.RegisterAgentHandlers();
```

## OpenAI Setup

Use OpenAI as the primary built-in provider when you want the full agent loop to run on Chat Completions:

```csharp
services.AddPipelogiqAgent(agent =>
{
    agent.LlmProvider = AgentLlmProvider.OpenAI;
    agent.LlmApiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
    agent.LlmModel = "gpt-4.1-mini";
    agent.UseReActMode = true;
    agent.RequireConfirmationForMutations = true;
});
```

The built-in OpenAI path currently:

- uses `POST /v1/chat/completions`
- supports tool calling
- supports image attachments
- does not yet support document attachments in the built-in planner

## Telegram Channel

The SDK also exposes a public Telegram integration that polls bot updates, starts AI agent pipelines, and sends final responses back to the same chat:

```csharp
using PipelogiqSDK.Agent.Configuration;
using PipelogiqSDK.Agent.Extensions;

services.AddPipelogiqAgent(agent =>
{
    agent.LlmProvider = AgentLlmProvider.Anthropic;
    agent.LlmApiKey = Environment.GetEnvironmentVariable("PIPELOGIQ_AGENT_LLM_API_KEY")
                     ?? Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY");
    agent.UseReActMode = true;
    agent.RequireConfirmationForMutations = true;
});

services.AddTelegramAgentChannel(new TelegramAgentChannelOptions
{
    TelegramBotToken = Environment.GetEnvironmentVariable("PIPELOGIQ_TELEGRAM_BOT_TOKEN")!,
});
```

`AddTelegramAgentChannel(...)` configures only the Telegram transport. Call `runner.RegisterAgentHandlers()` when the worker should execute AI agent stages.

Create and send an AI agent pipeline:

```csharp
var pipeline = AgentPipelineBuilderExtensions.CreateAiAgent(
    message: "Show order 42 status",
    replyTo: new AgentReplyTarget
    {
        Channel = "signalr",
        Address = "connection-123"
    },
    sessionId: "session-123",
    options: options);

pipeline
    .AddContextItem("tenantId", tenantId)
    .AddContextItem("userAccessToken", currentUserAccessToken);

await pipeline.SendAsync();
```

`replyTo` is the explicit outbound route used for final responses and confirmations. For Telegram, the built-in transport sets this automatically. For API-driven flows, register your own `IAgentNotificationChannel` and pass a matching `replyTo`.

`CreateAiAgent(...)` returns a regular `PipelineBuilder`, so `AddContextItem(...)` values become available in the stage context payload and can be injected into outbound headers with templates such as `{{context:tenantId}}` and `{{context:userAccessToken}}`.

## Local Ollama

For local testing with Ollama and `gemma3`:

```csharp
services.AddPipelogiqAgent(agent =>
{
    agent.LlmProvider = AgentLlmProvider.Ollama;
    agent.LlmModel = "gemma3";
    agent.LlmApiBaseUrl = "http://localhost:11434";
    agent.UseReActMode = true;
    agent.RequireConfirmationForMutations = true;
});
```

No `LlmApiKey` is required for a default local Ollama instance.

## Step Routing

Use `AgentLlmStepRouter` when different logical steps should run on different providers or models:

```csharp
services.AddPipelogiqAgent(agent =>
{
    agent.LlmProvider = AgentLlmProvider.Anthropic;
    agent.LlmApiKey = Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY");
    agent.LlmModel = "claude-sonnet-4-6";

    agent.StepRouter = new AgentLlmStepRouter
    {
        Plan = new AgentLlmStepRoute
        {
            Provider = AgentLlmProvider.OpenAI,
            Model = "gpt-4.1-mini"
        },
        Synthesize = new AgentLlmStepRoute
        {
            Provider = AgentLlmProvider.OpenAI,
            Model = "gpt-4.1-mini"
        },
        Critic = new AgentLlmStepRoute
        {
            Provider = AgentLlmProvider.OpenAI,
            Model = "gpt-4.1"
        }
    };

    agent.Providers = new AgentProviderCatalog
    {
        OpenAI = new AgentProviderConnection
        {
            ApiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY"),
            ApiBaseUrl = "https://api.openai.com"
        }
    };
});
```

This keeps provider secrets worker-owned. Per-run overrides can change provider/model selection via `AgentRunOverrides.StepRouter`, but should not carry API keys.

## Native Tools

Register a C# method as a tool instead of an HTTP call using `AddNativeTool`. The LLM sees the same JSON Schema description; only the execution is local code.

### Untyped handler

Implement `IAgentToolHandler` directly when you need access to raw parameters:

```csharp
public class CalculateDiscountHandler : IAgentToolHandler
{
    public Task<AgentToolOutput> ExecuteAsync(
        IReadOnlyDictionary<string, object?> parameters,
        IStageContext? context = null,
        CancellationToken ct = default)
    {
        var price   = Convert.ToDecimal(parameters["price"]);
        var percent = Convert.ToDecimal(parameters["percent"]);
        var amount  = Math.Round(price * percent / 100, 2);
        return Task.FromResult(AgentToolOutput.Success(amount.ToString("F2")));
    }
}
```

### Typed handler (recommended)

Extend `AgentToolHandlerBase<TInput>` and let the SDK deserialize parameters into your model:

```csharp
public record DiscountInput(decimal Price, decimal Percent);

public class CalculateDiscountHandler : AgentToolHandlerBase<DiscountInput>
{
    protected override Task<AgentToolOutput> ExecuteAsync(
        DiscountInput input,
        IStageContext? context = null,
        CancellationToken ct = default)
    {
        var amount = Math.Round(input.Price * input.Percent / 100, 2);
        return Task.FromResult(AgentToolOutput.Success(amount.ToString("F2")));
    }
}
```

### Registration

```csharp
agentBuilder.AddNativeTool(
    new AgentToolDefinition
    {
        Name        = "calculateDiscount",
        Description = "Calculates the discount amount given the price and the discount percentage",
        Params = new()
        {
            ["price"]   = new AgentToolParam { Type = "number", Description = "Item price",       Required = true },
            ["percent"] = new AgentToolParam { Type = "number", Description = "Discount percent", Required = true },
        }
    },
    new CalculateDiscountHandler());
```

Native tools:
- Have full access to `IStageContext` — read/write shared pipeline state and context items
- Can use injected services (pass them through the handler constructor)
- Return `AgentToolOutput.Success(outputString)` or `AgentToolOutput.Failure(errorMessage)`
- Are dispatched before HTTP tools — if a native handler is registered for a tool name, the HTTP fallback is never called

## Multiple APIs And Personalized Headers

- Register each external API once via `AddTargetApi(...)`.
- Point each tool to the correct API with `TargetApiName`.
- Use `AgentAuthHeaderDefinition` when the auth token must be sent in a custom header or without the `Bearer` scheme.
- Use `HeaderTemplates` for tenant/user-specific headers that come from pipeline `ContextItems`.

Legacy `agent.TargetApiBaseUrl`, `agent.TargetApiBearerToken`, and `agent.TargetApiHeaders` still work as a fallback for single-API setups, but named target APIs are the preferred model for new integrations.

## Confirmation Flow

When `RequireConfirmationForMutations=true`:

- mutating operations require explicit user approval
- `AgentConfirmationHandler` pauses the pipeline with `IsWaitingForApproval=true`
- resume via `ResumeStageApprovalAsync(stageId, approved, rejectionReason)`

In ReAct mode, mutating calls are guarded at runtime and cannot bypass confirmation policy.

## Tool Parameters

If a tool defines rich `Params` (`AgentToolParam`), `AgentToolHandler` validates and coerces input values before making HTTP requests:

- required parameter checks
- strict unknown-parameter rejection
- type coercion (`integer`, `number`, `boolean`, `array`, `object`, `string`)
- optional format checks (`date`, `date-time`, `time`, `uuid`, `email`, `url`)

## Handling Rate Limits and Cost

LLM APIs enforce request-per-minute and token-per-minute quotas. Rather than building retry logic inside each handler, report the failure with a structured `ErrorCode` and let the platform retry automatically with exponential backoff.

```csharp
// In any agent handler that calls an LLM API directly
catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.TooManyRequests)
{
    return StageResult.RateLimitExceeded($"Rate limit hit: {ex.Message}");
}
catch (TaskCanceledException)
{
    return StageResult.Timeout("LLM call timed out");
}
```

Then create a retry policy in the dashboard (or via API) targeting the handler:

```json
{
  "name": "llm-rate-limit-retry",
  "type": "retry",
  "targeting": {
    "handlers": ["AgentThinkHandler", "AgentToolHandler"]
  },
  "rule": {
    "maxAttempts": 6,
    "backoff": "exponential",
    "baseDelayMs": 2000,
    "maxDelayMs": 120000,
    "jitter": true,
    "retryOn": {
      "errorCodes": ["RATE_LIMIT_EXCEEDED", "TIMEOUT"]
    }
  }
}
```

This configuration retries up to 6 times with delays of ~2 s, ~4 s, ~8 s, ~16 s, ~32 s, capped at 120 s, with ±10% jitter. Stages that fail for other reasons (e.g. `UPSTREAM_ERROR` or validation failures) are failed immediately without consuming retries.

## Model Routing

`AgentModelRouter` remains a convenient model-only shortcut when all steps stay on the same provider:

Use `AgentModelRouter` to send different LLM operations to different models — cheaper models for planning and synthesis, full-power model for reasoning:

```csharp
services.AddPipelogiqAgent(agent =>
{
    agent.LlmProvider = AgentLlmProvider.Anthropic;
    agent.LlmApiKey = Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY");
    agent.LlmModel = "claude-opus-4-6";           // default for think steps
    agent.UseReActMode = true;

    agent.ModelRouter = new AgentModelRouter
    {
        PlanModel     = "claude-haiku-4-5-20251001", // cheap fast planning
        ThinkModel    = "claude-opus-4-6",            // full reasoning power
        SynthesizeModel = "claude-haiku-4-5-20251001" // cheap summarisation
    };
});
```

Any unset property falls back to `agent.LlmModel`.

## Prompt Caching and Token Budget

Enable Anthropic prompt caching to reduce costs on repeated calls (same system prompt + tools):

```csharp
services.AddPipelogiqAgent(agent =>
{
    agent.LlmProvider = AgentLlmProvider.Anthropic;
    agent.LlmApiKey = Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY");

    agent.TokenBudget = new AgentTokenBudget
    {
        EnablePromptCaching     = true,    // default: true; adds cache_control breakpoints
        MaxInputTokensPerSession = 200_000, // stop before this total input token count
        MaxCostUsdPerSession     = 0.50m,  // stop if session cost exceeds $0.50
    };
});
```

When `EnablePromptCaching` is true, the SDK:
- Sends `anthropic-beta: prompt-caching-2024-07-31` on every Anthropic call
- Wraps the system prompt in a `cache_control: ephemeral` block
- Adds `cache_control` to the last tool in the tools list

Cached tokens cost ~10% of normal input tokens. Token and cost accumulators are tracked per session in pipeline context under:

| Context key | Description |
|-------------|-------------|
| `agent:session:inputTokens` | Total input tokens used this session |
| `agent:session:outputTokens` | Total output tokens generated |
| `agent:session:cacheReadTokens` | Tokens served from cache |
| `agent:session:cacheCreationTokens` | Tokens written to cache |
| `agent:session:llmCallCount` | Total number of LLM API calls made |
| `agent:session:estimatedCostUsd` | Running cost estimate in USD |
| `agent:session:usageSummary` | Structured JSON summary with per-model breakdown and total estimated cost |

## Long-Term Memory

Register an `IAgentMemoryStore` to recall facts across sessions:

```csharp
// Default: in-memory store (single process, non-persistent)
// No extra registration needed — InMemoryAgentMemoryStore is registered automatically.

// For persistent memory, replace the default:
services.AddSingleton<IAgentMemoryStore, MyPostgresAgentMemoryStore>();
```

The `AgentThinkHandler` automatically calls `RecallAsync` before each think step and injects relevant memories into the system prompt. Store new memories from your own handlers:

```csharp
public class AfterOrderHandler(IAgentMemoryStore memory) : IStageHandler
{
    public async Task<IStageResult> ExecuteAsync(IStageContext? context = null)
    {
        var sessionId = context.TryGetValue<string>("agent:sessionId") ?? string.Empty;
        await memory.StoreAsync(new AgentMemoryEntry
        {
            SessionId = sessionId,
            Category  = "preference",
            Content   = "User prefers metric units.",
        });
        return StageResult.Success("memory stored");
    }
}
```

## Progress Notifications

The `AgentThinkHandler` emits `"progress"` notifications before each think step and before each tool call. These flow through the same `IAgentNotificationRouter` used for final responses.

For Telegram, progress messages appear instantly in the chat as the agent works. For your own channel, receive them in your `IAgentNotificationChannel` implementation (filter on `notification.Type == "progress"`).

## Security Notes

- Tool outputs are treated as untrusted data before sending them back to the LLM.
- Prefer narrow tool scopes and explicit parameter schemas.
- Keep mutation confirmation enabled for production workflows that change state.

## Response Delivery

Responses and confirmations are routed through `IAgentNotificationRouter`, which resolves the destination from:

- explicit `replyTo`
- legacy route prefixes in `sessionId` such as `tg:123456`
- a single legacy `IAgentNotifier`, if you still register one

If the final response cannot be delivered, it is stored in stage context under `agent:finalResponse`.
If a confirmation request cannot be delivered, the stage now fails fast and stores the pending notification under `agent:pendingNotification` instead of waiting silently forever.
