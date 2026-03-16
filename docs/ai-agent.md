# AI Agent

The SDK includes an optional AI agent layer built on top of Pipelogiq stages.
It uses regular stage handlers (`AgentOrchestratorHandler`, `AgentToolHandler`, `AgentConfirmationHandler`, `AgentThinkHandler`, `AgentResponderHandler`) and can run in:

- plan-and-execute mode
- ReAct mode (reason + act loop)

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
