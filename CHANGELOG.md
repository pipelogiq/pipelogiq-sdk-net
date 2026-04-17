# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

## [0.3.0-preview.5] - 2026-04-17

### Added

- **Second-model critic** — new `AgentCriticHandler` stage that reviews the think handler's proposed action with a separate LLM before execution. Supports three modes: `CriticOnFinal` (review only the terminal Done decision), `CriticOnMutating` (review mutating tool calls and Done), and `CriticOnEveryStep` (review every think decision). Configurable per-pipeline via `AgentRunOverrides`
- **OpenAI and Claude critic implementations** — `OpenAiCritic` and `ClaudeCritic` build structured review prompts with domain-specific rubrics and parse approve/reject verdicts with confidence scores and concerns
- **Critic resolver** — `AgentCriticResolver` selects the appropriate critic implementation based on `AgentLlmProvider` (OpenAI, Anthropic, or custom)
- **Per-pipeline critic overrides** — `AgentRunOverrides` allows configuring critic mode, provider, model, API key, rubric, and rejection cap without changing global `AgentOptions`
- **Critic rejection loop** — rejected proposals append structured feedback to conversation history and loop back to think, up to `MaxRejectionsPerStep` (default: 2) rejections per decision
- **Graceful worker shutdown** — `PipelineRunner` now cancels RabbitMQ consumers on SIGTERM without closing channels, then drains in-flight jobs with a configurable `DrainGracePeriod` (default: 30s) before disposing messaging resources
- **Drain-safe publish tokens** — critical publish operations (`SetStatusToRunning`, result publishing) now use independent `CancellationTokenSource` with 10s timeout instead of the parent `stoppingToken`, ensuring in-flight handlers can complete their work after shutdown signal

### Changed

- **Shutdown flow refactored** — shutdown is now phased: (1) cancel consumers to stop new message delivery, (2) drain in-flight jobs, (3) dispose channels. The `Draining` state is set in the outer `finally` block after the main loop exits

### Fixed

- **Unicode escapes in pipeline context** — `PayloadConverter` and `PipelineMessageSerializer` now use `JavaScriptEncoder.UnsafeRelaxedJsonEscaping` instead of the default encoder. Context values containing non-ASCII characters (Cyrillic, CJK, etc.) and nested JSON strings are now stored as readable UTF-8 text instead of `\uXXXX` escape sequences

## [0.3.0-preview.3] - 2026-04-12

### Added

- Worker diagnostic events now capture bootstrap failures, queue mismatches, session rejection, heartbeat failures, invalid stage payloads, and stage-processing failures with structured details.
- Dashboard worker activity now renders detailed event payloads so startup and degraded-state problems are visible without digging through server logs.

### Changed

- Worker state transitions now emit richer status updates that preserve `statusReason`, `lastError`, broker connectivity, and queue-lag context across the SDK and API boundary.
- Worker registry now shows status reason and last error inline, making degraded workers explain themselves directly in the UI.

## [0.3.0-preview.2] - 2026-04-12

### Added

- Detailed stage lifecycle logging, including execution start/finish records, input/output previews, and richer agent-stage diagnostics for think, tool, confirmation, orchestrator, and responder handlers.

### Changed

- Agent terminal flows now append `agent:responder` idempotently and mark responder stages with `RunNextIfFailed`, keeping the conversation recoverable after terminal tool-loop and budget conditions.

### Fixed

- ReAct-style agent pipelines no longer create duplicate responder stages in loop-protection paths.
- Responder follow-up stages now execute correctly after upstream failed agent stages when the runtime honors `run_next_if_failed`.

## [0.3.0-preview.1] - 2026-04-07

### Added

- Smart Expense Agent example with Telegram integration and Groq Whisper voice transcription, configured via environment variables.
- `PipelogiqSDK.Testing` helpers for agent unit tests, including `AgentTestHarness` and `MockLlmPlanner`.
- Structured stage error codes, retry-aware `StageResult` helpers, Anthropic prompt caching, model routing, token budget tracking, and long-term memory support for agent workflows.

### Fixed

- Worker connection and queue provisioning resilience improvements for retry-first startup and heartbeat reporting.

### Added

- **`ErrorCode` on stage results** — `IStageResult` and `StageResultDto` now expose an optional `ErrorCode` property. Report a structured error code alongside the failure message so that server-side retry policies can decide whether to retry based on the specific failure kind.
- **`StageResult` factory helpers for common error codes**:
  - `StageResult.RateLimitExceeded(message)` — sets `ErrorCode = "RATE_LIMIT_EXCEEDED"`
  - `StageResult.Timeout(message)` — sets `ErrorCode = "TIMEOUT"`
  - `StageResult.UpstreamError(message)` — sets `ErrorCode = "UPSTREAM_ERROR"`
  - `StageResult.Error(message, errorCode)` — general overload; `errorCode` is optional and defaults to `null` (preserves existing behaviour)

- **Anthropic prompt caching** — `AgentTokenBudget.EnablePromptCaching` (default `true`) wraps the system prompt and tool list in `cache_control: ephemeral` blocks and sends `anthropic-beta: prompt-caching-2024-07-31`. Cached tokens cost ~10% of normal input tokens and cut multi-step agent costs significantly.

- **Model routing** — new `AgentModelRouter` class with `PlanModel`, `ThinkModel`, and `SynthesizeModel` properties. Set cheaper models for planning/synthesis steps while keeping a full-power model for think steps:
  ```csharp
  agent.ModelRouter = new AgentModelRouter
  {
      PlanModel       = "claude-haiku-4-5-20251001",
      ThinkModel      = "claude-opus-4-6",
      SynthesizeModel = "claude-haiku-4-5-20251001",
  };
  ```

- **Token budget and cost tracking** — `AgentTokenBudget` adds per-session limits (`MaxInputTokensPerSession`, `MaxCostUsdPerSession`). The `AgentThinkHandler` accumulates token usage in pipeline context after every LLM call (keys: `agent:session:inputTokens`, `agent:session:outputTokens`, `agent:session:cacheReadTokens`, `agent:session:cacheCreationTokens`, `agent:session:estimatedCostUsd`). When a limit is exceeded the agent appends the responder immediately and returns `ErrorCode = "BUDGET_EXCEEDED"` so retry policies can treat it as a terminal error.

- **LLM token usage on responses** — `AgentThinkDecision.TokenUsage` and `AgentPlan.TokenUsage` now carry an `AgentLlmUsage` object (`InputTokens`, `OutputTokens`, `CacheCreationTokens`, `CacheReadTokens`, `EstimatedCostUsd`) populated from the Anthropic `usage` field.

- **Long-term memory** — new `IAgentMemoryStore` interface with `RecallAsync`, `StoreAsync`, and `ClearSessionAsync`. The default `InMemoryAgentMemoryStore` is registered automatically; swap in your own implementation (PostgreSQL, Redis, etc.) for persistent cross-session memory. `AgentThinkHandler` recalls relevant memories before each think step and injects them into the system prompt.

- **Native tool handlers** — register C# code as an agent tool via `AddNativeTool(definition, handler)`. Implement `IAgentToolHandler` for raw parameter access or extend `AgentToolHandlerBase<TInput>` for typed deserialization (mirrors the `IStageHandler` / `IStageHandler<TInput>` pattern). Native handlers receive `IStageContext` and can read/write pipeline state. The `AgentToolHandler` stage handler dispatches to the native handler automatically when one is registered for the tool name, skipping the HTTP call.

- **Streaming progress notifications** — `AgentThinkHandler` now emits `"progress"` type `AgentNotification`s via `IAgentNotificationRouter` before each think step ("Thinking… (step N)") and before each tool call ("Calling tool: X…"). No configuration required; notifications flow through the same channel as final responses.

### Fixed

- Worker no longer stops on startup in `QueueProvisioningMode.AssertOnly` when required RabbitMQ queues are not created yet; it now enters retry loop and reconnects once queues appear.
- Worker runtime now keeps running after connection/configuration failures and retries reconnection every 10 seconds instead of stopping.
- Worker now reports `ready` when RabbitMQ connection is up even if only part of StageNext queues are subscribed, and heartbeat metadata now includes active/total/missing StageNext queue counts.

## [0.1.0] preview - 2026-02-21

### Added

- Initial public preview of Pipelogiq .NET SDK.
- Basic runner and stage execution support.
- OpenTelemetry-compatible tracing guidance.
- Logging integration.

> This is an early preview release. APIs may change.

[Unreleased]: https://github.com/pipelogiq/pipelogiq-sdk-net/compare/v0.3.0-preview.5...HEAD
[0.3.0-preview.5]: https://github.com/pipelogiq/pipelogiq-sdk-net/releases/tag/v0.3.0-preview.5
[0.3.0-preview.3]: https://github.com/pipelogiq/pipelogiq-sdk-net/releases/tag/v0.3.0-preview.3
[0.3.0-preview.2]: https://github.com/pipelogiq/pipelogiq-sdk-net/releases/tag/v0.3.0-preview.2
[0.3.0-preview.1]: https://github.com/pipelogiq/pipelogiq-sdk-net/releases/tag/v0.3.0-preview.1
[0.1.0]: https://github.com/pipelogiq/pipelogiq-sdk-net/releases/tag/v0.1.0
