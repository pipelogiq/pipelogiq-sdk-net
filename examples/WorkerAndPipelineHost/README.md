# Worker And Pipeline Host Example

A structured example project that shows both:

- running a Pipelogiq worker (`worker` mode)
- submitting a demo pipeline (`pipeline` mode)

It is organized into separate folders for handlers, services, hosted services, models, and configuration.

## Project layout

- `Configuration/` - mode + environment settings parsing
- `Handlers/` - sample stage handlers
- `HostedServices/` - host entry points for worker/pipeline modes
- `Models/` - stage input DTOs
- `Services/` - pipeline launcher, handler registry, trace propagation helpers

## Environment variables

- `PIPELOGIQ_API_KEY` (required)
- `PIPELOGIQ_API_URL` (optional, default `http://localhost:8081`)
- `PIPELOGIQ_WORKER_NAME` (optional, default `checkout-worker-example`)
- `PIPELOGIQ_PIPELINE_NAME` (optional, default `checkout-demo`)
- `PIPELOGIQ_TELEGRAM_BOT_TOKEN` (optional; enables Telegram channel listener in `worker` mode)
- `PIPELOGIQ_TELEGRAM_ALLOWED_CHAT_IDS` (optional CSV of chat ids, e.g. `123456789,-1001122334455`)
- `PIPELOGIQ_TELEGRAM_POLL_TIMEOUT_SECONDS` (optional, default `25`)
- `PIPELOGIQ_AGENT_LLM_PROVIDER` (optional, `Anthropic` or `Ollama`; default `Anthropic`)
- `PIPELOGIQ_AGENT_LLM_API_KEY` (required for Anthropic when Telegram channel is enabled; fallback: `ANTHROPIC_API_KEY`)
- `PIPELOGIQ_AGENT_LLM_MODEL` (optional, default `claude-opus-4-6` for Anthropic, `gemma3` for Ollama)
- `PIPELOGIQ_AGENT_LLM_API_BASE_URL` (optional, default `https://api.anthropic.com` for Anthropic, `http://localhost:11434` for Ollama)
- `PIPELOGIQ_AGENT_USE_REACT_MODE` (optional, default `true`)
- `PIPELOGIQ_AGENT_REQUIRE_CONFIRMATION` (optional, default `false`)
- `PIPELOGIQ_AGENT_SYSTEM_PROMPT` (optional)
- `PIPELOGIQ_AGENT_TARGET_API_BASE_URL` (optional; for external tool calls)
- `PIPELOGIQ_AGENT_TARGET_API_BEARER_TOKEN` (optional; for external tool calls)

## Run worker mode

```bash
export PIPELOGIQ_API_KEY="<your-api-key>"
export PIPELOGIQ_API_URL="http://localhost:8081"

dotnet run --project examples/WorkerAndPipelineHost/Pipelogiq.Sdk.Examples.WorkerAndPipelineHost.csproj -- worker
```

## Run worker mode with Telegram AI channel

When `PIPELOGIQ_TELEGRAM_BOT_TOKEN` is set, the worker host also starts a Telegram listener:

- each incoming Telegram text message creates an AI agent pipeline
- final agent response is routed back to the same chat via the notification router + Telegram channel transport
- approval commands are supported:
  - `/approve <stageId>`
  - `/reject <stageId> <reason>`

Program wiring now uses the public SDK extension:

```csharp
services.AddHostedService<PipelogiqWorkerHostedService>();

services.AddPipelogiqAgent(agent =>
{
    agent.LlmProvider = settings.AgentLlmProvider;
    agent.LlmApiKey = settings.AgentLlmApiKey;
    agent.UseReActMode = settings.AgentUseReActMode;
});

if (settings.TelegramChannelEnabled)
    services.AddTelegramAgentChannel(settings.ToTelegramAgentChannelOptions());
```

Minimal registration:

```csharp
services.AddPipelogiqAgent(agent =>
{
    agent.LlmProvider = AgentLlmProvider.Anthropic;
    agent.LlmApiKey = Environment.GetEnvironmentVariable("PIPELOGIQ_AGENT_LLM_API_KEY")
                     ?? Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY");
});

services.AddTelegramAgentChannel("<telegram-bot-token>");
```

Extended registration with explicit options:

```csharp
services.AddPipelogiqAgent(agent =>
{
    agent.LlmProvider = AgentLlmProvider.Anthropic;
    agent.LlmApiKey = "<llm-api-key>";
    agent.RequireConfirmationForMutations = true;
});

services.AddTelegramAgentChannel(new TelegramAgentChannelOptions
{
    TelegramBotToken = "<telegram-bot-token>",
    TelegramAllowedChatIds = new long[] { 123456789 }
});
```

```bash
export PIPELOGIQ_API_KEY="<your-api-key>"
export PIPELOGIQ_API_URL="http://localhost:8081"
export PIPELOGIQ_TELEGRAM_BOT_TOKEN="<telegram-bot-token>"
export PIPELOGIQ_AGENT_LLM_API_KEY="<llm-api-key>"
export PIPELOGIQ_TELEGRAM_ALLOWED_CHAT_IDS="<chat-id-1>,<chat-id-2>"

dotnet run --project examples/WorkerAndPipelineHost/Pipelogiq.Sdk.Examples.WorkerAndPipelineHost.csproj -- worker
```

Local Ollama example:

```bash
export PIPELOGIQ_API_KEY="<your-api-key>"
export PIPELOGIQ_API_URL="http://localhost:8081"
export PIPELOGIQ_TELEGRAM_BOT_TOKEN="<telegram-bot-token>"
export PIPELOGIQ_AGENT_LLM_PROVIDER="Ollama"
export PIPELOGIQ_AGENT_LLM_MODEL="gemma3"
export PIPELOGIQ_AGENT_LLM_API_BASE_URL="http://localhost:11434"

dotnet run --project examples/WorkerAndPipelineHost/Pipelogiq.Sdk.Examples.WorkerAndPipelineHost.csproj -- worker
```

## Run pipeline submission mode

```bash
export PIPELOGIQ_API_KEY="<your-api-key>"
export PIPELOGIQ_API_URL="http://localhost:8081"

dotnet run --project examples/WorkerAndPipelineHost/Pipelogiq.Sdk.Examples.WorkerAndPipelineHost.csproj -- pipeline
```

## Handlers included

- `FraudCheckHandler` (`IStageHandler<FraudCheckInput>`)
- `ChargeCustomerHandler` (`IStageHandler<ChargeCustomerInput>`)
- `ReceiptHandler` (`IStageHandler` without typed input)

The pipeline submission example builds a simple checkout flow using these handlers.
