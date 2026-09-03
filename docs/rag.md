# RAG in Pipelogiq .NET SDK

> Status: design and implementation guide for a planned SDK extension.
>
> This document describes how Retrieval-Augmented Generation (RAG) should work in `pipelogiq-sdk-net` as a reusable SDK capability. It is intentionally written as an SDK-first architecture, not as an app-specific implementation for any one consumer product.
>
> As of `v0.3.2-preview.5`, the core AI agent runtime, native tools, session store, memory store, and pipeline orchestration are already available in the SDK. The RAG layer described here is the recommended extension model to build next.

## Strategic decision: adapter, not engine

The .NET ecosystem already has production-grade RAG internals:

- `Microsoft.Extensions.VectorData.Abstractions` — GA vector store abstraction, part of the .NET standard library surface
- `Microsoft.Extensions.AI` — GA embedding generation abstraction (`IEmbeddingGenerator`)
- `Microsoft.KernelMemory` — complete RAG runtime with ingestion pipeline, document extractors, chunking, embedding, 10+ storage backends, tenant tagging, content deduplication
- `Microsoft.SemanticKernel` connectors — PostgreSQL/pgvector, Qdrant, Azure AI Search, Redis, Elasticsearch, and more

Building a competing vector store abstraction, chunking engine, embedding client, or document extractor inside Pipelogiq would duplicate what Microsoft already ships and maintains with a 27K-star community.

**Pipelogiq should not reimplement RAG internals. Pipelogiq should own what it does better than anyone: pipeline orchestration, stage observability, scope resolution from pipeline context, and native agent tool integration.**

The architecture described here uses Microsoft Kernel Memory as the first adapter and reference implementation, wrapped with Pipelogiq-native orchestration, observability, and agent integration.

## What Pipelogiq adds on top of Kernel Memory

Kernel Memory handles: extraction, chunking, embedding, vector storage, search, content deduplication, document versioning.

Pipelogiq adds:

- **Pipeline-orchestrated ingestion** — ingestion runs as observable Pipelogiq stages with retry, logging, progress tracking, and failure handling through the standard pipeline dashboard
- **Agent tool integration** — RAG exposed as a native tool (`askKnowledgeBase`) inside the Pipelogiq AI Agent loop, visible in pipeline observability
- **Scope resolution from pipeline context** — `ApplicationId`, `TenantId`, `KnowledgeSpace` resolved automatically from `IStageContext` and `ContextItem`, not just from explicit request parameters
- **Access policy enforcement** — pluggable `IRagAccessPolicy` checked before every query, with Pipelogiq-aware defaults
- **Audit trail** — every query and answer logged with pipeline context (pipeline ID, stage ID, session ID, user ID) for compliance and debugging
- **Ingestion pipeline builder** — `PipelineBuilder.WithRagIngestion(...)` sends ingestion through Pipelogiq workers, making knowledge base updates observable and schedulable
- **Generated documentation ingestion** — SDK helpers to generate markdown from registered handlers, tools, and pipeline templates, then ingest as knowledge

These are the capabilities that Kernel Memory does not provide and that Pipelogiq is uniquely positioned to deliver.

## Why RAG belongs in the SDK

Pipelogiq already provides the right primitives to make RAG reusable across products:

- pipeline builders and stage handlers
- AI agent orchestration
- native tool execution inside agent workflows
- pipeline context items
- pluggable storage patterns
- worker-based background execution

Because of that, RAG should not be designed as a one-off feature inside a consumer app. It should be an optional SDK subsystem that any Pipelogiq-based application can adopt.

Good examples of consumer apps:

- internal operations assistants
- product-specific knowledge assistants
- support copilots
- pipeline troubleshooting agents
- documentation and runbook assistants

`VesselOps` is a valid consumer example, but it should stay a consumer. The RAG capability itself should live in the SDK.

## Design goals

The RAG extension should:

- plug into Pipelogiq AI Agent with minimal setup
- delegate RAG internals (chunking, embedding, vector search, extraction) to Kernel Memory
- support ingestion as background pipeline actions through Pipelogiq stages
- support querying as a native agent tool
- inherit storage provider choice from Kernel Memory configuration
- support tenant and application scoping via Pipelogiq pipeline context
- support source citations and auditability
- work in Docker and Kubernetes without special infrastructure assumptions

## Non-goals for v1

The first version should not try to solve everything:

- no hidden auto-retrieval inside the planner
- no custom vector store implementation — use Kernel Memory backends
- no custom chunking engine — use Kernel Memory handlers with configuration
- no custom embedding client — use Kernel Memory or `Microsoft.Extensions.AI`
- no custom document extractors — use Kernel Memory extractors
- no graph-RAG
- no mandatory reranker
- no raw indexing of all pipeline logs
- no product-specific domain logic in the SDK

## Current SDK extension points that RAG should use

The RAG design should build on top of the existing SDK surface:

- `AddPipelogiqAgent(...)`
- `AgentBuilder.AddNativeTool(...)`
- `PipelineRunner.RegisterAgentHandlers()`
- `PipelineBuilder`
- `ContextItem` and `AddContextItem(...)`
- `IAgentToolHandler`

That means RAG should feel like a natural SDK extension rather than a separate unrelated subsystem.

## Package layout

Two packages. Not five.

### `Pipelogiq.Sdk.Rag`

The main package. Contains:

- `IRagService` — thin facade over Kernel Memory query
- `IRagIndexer` — thin facade over Kernel Memory ingestion
- `IRagScopeResolver` — resolves scope from `IStageContext` and pipeline context
- `IRagAccessPolicy` — pluggable authorization before query
- `RagScope`, `RagQueryRequest`, `RagQueryResponse`, `RagCitation` — Pipelogiq-native models
- `RagAuditRecord` — audit trail model
- `AskKnowledgeBaseHandler` — native agent tool handler
- `IngestKnowledgeDocumentHandler` — Pipelogiq stage handler for ingestion
- `ReindexKnowledgeBaseHandler` — Pipelogiq stage handler for reindex
- `GenerateHandlerDocumentationHandler` — generates docs from registered handlers
- DI extension methods (`AddPipelogiqRag`, `ExposeAsAgentTool`)
- `PipelineBuilder` extensions (`WithRagIngestion`, `WithRagAsk`)
- Kernel Memory adapter wiring

Dependencies:

- `PipelogiqSDK` (project reference)
- `Microsoft.KernelMemory.Abstractions`

### `Pipelogiq.Sdk.Rag.KernelMemory`

The initial Kernel Memory adapter. Contains:

- `KernelMemoryRagService` — implements `IRagService` by delegating to `IKernelMemory.AskAsync`
- `KernelMemoryRagIndexer` — implements `IRagIndexer` by delegating to `IKernelMemory.ImportDocumentAsync`
- Scope-to-tag mapping (translates `RagScope` to Kernel Memory `TagCollection` filters)
- Audit logging decorator
- DI extension (`UseKernelMemory`)

Dependencies:

- `Pipelogiq.Sdk.Rag` (project reference)
- `Microsoft.KernelMemory.Core`

The consumer chooses the Kernel Memory storage backend through standard KM configuration. PostgreSQL/pgvector, Qdrant, Azure AI Search, Redis — all available through existing KM packages without any Pipelogiq-specific connector code.

### No custom storage packages

There is no `Pipelogiq.Sdk.Rag.Postgres`, no `Pipelogiq.Sdk.Rag.Qdrant`, no `Pipelogiq.Sdk.Rag.AzureSearch`. Storage is configured through Kernel Memory. Pipelogiq does not own that layer.

### Optional future packages

- `Pipelogiq.Sdk.Rag.DirectVectorData` — lightweight adapter using `Microsoft.Extensions.VectorData` directly, without the full Kernel Memory runtime, for consumers who only need vector search without the ingestion pipeline

Only add this when there is a concrete production need where Kernel Memory is too heavy or not the right dependency for a given consumer.

## High-level architecture

```text
Consumer app
|
+-- AddPipelogiq(...)
|
+-- AddPipelogiqAgent(...)
|
+-- AddPipelogiqRag(rag => rag
|       .UseKernelMemory(km => km          ← Kernel Memory handles internals
|           .WithOpenAIDefaults(apiKey)
|           .WithPostgresMemoryDb(conn))
|       .WithScopeResolver<MyResolver>()   ← Pipelogiq resolves scope
|       .WithAccessPolicy<MyPolicy>()      ← Pipelogiq enforces access
|       .WithAuditStore<PostgresAudit>()   ← Pipelogiq logs audit trail
|       .ExposeAsAgentTool(agentBuilder))  ← Pipelogiq wires agent tool
|
+-- RegisterAgentHandlers()
+-- RegisterRagHandlers()                  ← Pipelogiq stage handlers
|
+-- Pipelines / workers / agent runtime
```

Responsibility boundary:

- **Kernel Memory** owns: text extraction, chunking, embedding generation, vector storage, vector search, content deduplication, document versioning
- **Pipelogiq** owns: pipeline-based ingestion orchestration, agent tool exposure, scope resolution from pipeline context, access policy, audit trail, observability metrics, generated documentation ingestion

Kernel Memory is the first supported adapter, not a permanent hard dependency of the architecture. Pipelogiq keeps `IRagService` and `IRagIndexer` as its own contracts so that other adapters can be introduced later without changing consumer-facing code.

## Pipelogiq-owned contracts

These are the interfaces that Pipelogiq defines. They do not duplicate Kernel Memory — they add Pipelogiq-specific orchestration on top.

```csharp
/// <summary>
/// Pipelogiq facade for RAG queries. Resolves scope, checks access policy,
/// delegates to the underlying RAG engine, and writes audit records.
/// </summary>
public interface IRagService
{
    Task<RagQueryResponse> QueryAsync(RagQueryRequest request, CancellationToken ct = default);
}

/// <summary>
/// Pipelogiq facade for RAG ingestion. Maps Pipelogiq scope to engine tags,
/// delegates to the underlying RAG engine, and logs ingestion events.
/// </summary>
public interface IRagIndexer
{
    Task<RagIndexResult> IngestAsync(RagIngestionRequest request, CancellationToken ct = default);
    Task ReindexAsync(RagReindexRequest request, CancellationToken ct = default);
    Task DeleteAsync(string logicalKey, RagScope scope, CancellationToken ct = default);
}

/// <summary>
/// Resolves RAG scope from pipeline context, stage context, and explicit request values.
/// Default implementation reads from IStageContext and ContextItems.
/// </summary>
public interface IRagScopeResolver
{
    RagScope Resolve(IStageContext? context, RagQueryRequest? request = null);
}

/// <summary>
/// Checks whether a query is authorized for the resolved scope.
/// Default implementation is permissive within the same ApplicationId.
/// </summary>
public interface IRagAccessPolicy
{
    Task<RagAccessDecision> AuthorizeAsync(
        RagScope scope,
        RagQueryRequest request,
        CancellationToken ct = default);
}
```

Note what is **not** here: no `IVectorStore`, no `IEmbeddingService`, no `IDocumentTextExtractor`, no `IChunkingStrategy`. Those are Kernel Memory's job.

## Pipelogiq-owned models

```csharp
public sealed class RagScope
{
    public int? ApplicationId { get; set; }
    public string? TenantId { get; set; }
    public int? PipelineId { get; set; }
    public int? StageId { get; set; }
    public string? StageHandlerName { get; set; }
    public string? UserId { get; set; }
    public string? KnowledgeSpace { get; set; }
}

public sealed class RagQueryRequest
{
    public string Question { get; set; } = string.Empty;
    public int? ApplicationId { get; set; }
    public string? TenantId { get; set; }
    public string? KnowledgeSpace { get; set; }
    public int TopK { get; set; } = 8;
    public double MinRelevance { get; set; } = 0.5;
}

public sealed class RagQueryResponse
{
    public string Answer { get; set; } = string.Empty;
    public IReadOnlyList<RagCitation> Citations { get; set; } = [];
    public bool NoResult { get; set; }
}

public sealed class RagCitation
{
    public string DocumentId { get; set; } = string.Empty;
    public string DocumentTitle { get; set; } = string.Empty;
    public int? PageNo { get; set; }
    public string Snippet { get; set; } = string.Empty;
    public double Score { get; set; }
}

public sealed class RagIngestionRequest
{
    public int? ApplicationId { get; set; }
    public string? TenantId { get; set; }
    public string LogicalKey { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string? KnowledgeSpace { get; set; }
    public string ContentType { get; set; } = "text/plain";
    public string? Content { get; set; }
    public Stream? FileStream { get; set; }
    public string? FileName { get; set; }
}

public sealed class RagAuditRecord
{
    public Guid Id { get; set; }
    public string? SessionId { get; set; }
    public int? PipelineId { get; set; }
    public int? StageId { get; set; }
    public int? ApplicationId { get; set; }
    public string? TenantId { get; set; }
    public string? UserId { get; set; }
    public string Question { get; set; } = string.Empty;
    public string Answer { get; set; } = string.Empty;
    public IReadOnlyList<RagCitation> Citations { get; set; } = [];
    public DateTimeOffset CreatedAt { get; set; }
}
```

## Scoping and multi-tenant isolation

This is where Pipelogiq adds real value over raw Kernel Memory.

Kernel Memory supports tagging but has no concept of pipeline context, stage context, or application-level scope resolution.

### Scope resolution chain

The default `PipelogiqRagScopeResolver` resolves scope in this order:

1. Explicit values from `RagQueryRequest` (highest priority)
2. Values from `IStageContext` payload (`applicationId`, `tenantId`, `userId`)
3. Values from pipeline `ContextItem`s
4. SDK-level defaults from `RagOptions`

### Scope-to-tag mapping

The Kernel Memory adapter maps `RagScope` to KM `TagCollection`:

```csharp
// Inside KernelMemoryRagService:
var filters = new MemoryFilters();

if (scope.ApplicationId.HasValue)
    filters.ByTag("applicationId", scope.ApplicationId.Value.ToString());
if (!string.IsNullOrEmpty(scope.TenantId))
    filters.ByTag("tenantId", scope.TenantId);
if (!string.IsNullOrEmpty(scope.KnowledgeSpace))
    filters.ByTag("knowledgeSpace", scope.KnowledgeSpace);
```

This ensures tenant isolation is enforced at the storage layer, not at the LLM layer.

### Security principles

- Filter before prompt construction — always
- Do not rely on the LLM to respect permissions
- `ApplicationId` is the primary boundary
- `TenantId` is the secondary boundary
- `KnowledgeSpace` is the optional third boundary
- Log every query and answer for audit

## Agent tool integration

RAG should be exposed as a native agent tool, not hidden inside the planner.

Reasons:

- tool calls are visible in pipeline observability
- retrieval usage becomes explicit
- agent behavior is easier to debug
- citations can be logged per tool invocation
- token cost is easier to reason about

### `AskKnowledgeBaseHandler`

```csharp
public sealed class AskKnowledgeBaseInput
{
    public string Question { get; set; } = string.Empty;
    public string? KnowledgeSpace { get; set; }
    public int TopK { get; set; } = 5;
}

public sealed class AskKnowledgeBaseHandler(
    IRagService ragService,
    IRagScopeResolver scopeResolver)
    : AgentToolHandlerBase<AskKnowledgeBaseInput>
{
    protected override async Task<AgentToolOutput> ExecuteAsync(
        AskKnowledgeBaseInput input,
        IStageContext? context = null,
        CancellationToken ct = default)
    {
        var scope = scopeResolver.Resolve(context);
        if (!string.IsNullOrEmpty(input.KnowledgeSpace))
            scope.KnowledgeSpace = input.KnowledgeSpace;

        var response = await ragService.QueryAsync(new RagQueryRequest
        {
            Question = input.Question,
            ApplicationId = scope.ApplicationId,
            TenantId = scope.TenantId,
            KnowledgeSpace = scope.KnowledgeSpace,
            TopK = input.TopK,
        }, ct);

        if (response.NoResult)
            return AgentToolOutput.Success(
                "No relevant knowledge found. Proceed using other available tools and context.");

        var result = new
        {
            answer = response.Answer,
            sources = response.Citations.Select(c => new
            {
                title = c.DocumentTitle,
                page = c.PageNo,
                snippet = c.Snippet,
                relevance = c.Score,
            }),
        };

        return AgentToolOutput.Success(JsonSerializer.Serialize(result));
    }
}
```

### Agent query flow

1. User message enters AI agent
2. `AgentThinkHandler` selects `askKnowledgeBase`
3. `AskKnowledgeBaseHandler` receives question
4. `IRagScopeResolver` resolves scope from `IStageContext`
5. `IRagAccessPolicy` checks authorization
6. `IRagService.QueryAsync(...)` delegates to Kernel Memory with scope-based tag filters
7. Result returned to agent loop with citations
8. Audit record written with pipeline context
9. Agent cites or summarizes based on tool output

## Ingestion architecture

Ingestion runs through Pipelogiq stages, making it observable and retryable.

### `IngestKnowledgeDocumentHandler`

A Pipelogiq stage handler that delegates the actual extraction/chunking/embedding to Kernel Memory but wraps it in pipeline observability:

```csharp
public sealed class IngestKnowledgeDocumentHandler(
    IRagIndexer ragIndexer,
    ILogger<IngestKnowledgeDocumentHandler> logger)
    : IStageHandler<RagIngestionStageInput>
{
    public async Task<IStageResult> ExecuteAsync(
        RagIngestionStageInput input,
        IStageContext? context = null)
    {
        context?.LogInfo($"Ingesting document [{input.Title}] into knowledge space [{input.KnowledgeSpace ?? "default"}]");

        try
        {
            var result = await ragIndexer.IngestAsync(new RagIngestionRequest
            {
                ApplicationId = input.ApplicationId,
                TenantId = input.TenantId,
                LogicalKey = input.LogicalKey,
                Title = input.Title,
                KnowledgeSpace = input.KnowledgeSpace,
                ContentType = input.ContentType,
                Content = input.Content,
                FileName = input.FileName,
            });

            context?.LogInfo($"Document ingested [{input.Title}] — {result.ChunkCount} chunks indexed");
            return StageResult.Success($"Ingested [{input.Title}]: {result.ChunkCount} chunks.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "RAG ingestion failed for [{Title}]", input.Title);
            context?.LogError($"Ingestion failed: {ex.Message}");
            return StageResult.Error($"Ingestion failed: {ex.Message}");
        }
    }
}
```

### What happens inside Kernel Memory (not Pipelogiq's concern)

When `IngestAsync` delegates to Kernel Memory, KM internally runs:

1. Detect file format, extract text
2. Partition text into chunks
3. Generate embeddings
4. Upsert chunks with tags into vector store
5. Deduplicate by content hash

Pipelogiq does not reimplement or customize these steps. If a consumer needs custom chunking or extraction, they configure it through Kernel Memory's handler pipeline.

### Ingestion via PipelineBuilder

```csharp
await PipelineBuilder.Create("kb-ingestion", options)
    .WithRagIngestion(new RagIngestionRequest
    {
        ApplicationId = 42,
        TenantId = "acme",
        Title = "Payments Runbook",
        KnowledgeSpace = "ops",
        ContentType = "text/markdown",
        Content = markdownText,
    })
    .SendAsync();
```

### Ingestion via direct service call

For cases where pipeline overhead is unnecessary (e.g., inline ingestion after saving a budget):

```csharp
await ragIndexer.IngestAsync(new RagIngestionRequest
{
    ApplicationId = applicationId,
    LogicalKey = $"budget:{projectId}:{budgetId}",
    Title = $"Budget — {projectTitle}",
    KnowledgeSpace = "budgets",
    Content = budgetSummaryText,
});
```

## Direct query (non-agent)

For application features such as a knowledge chat page:

```csharp
var response = await ragService.QueryAsync(new RagQueryRequest
{
    ApplicationId = 42,
    TenantId = "acme",
    Question = "How does the retry policy work for payment sync?",
    KnowledgeSpace = "ops",
});

// response.Answer — grounded answer
// response.Citations — source references
// response.NoResult — true if nothing relevant found
```

## How knowledge should be populated in practice

The knowledge base should be populated through explicit ingestion flows. It should not be treated as something the SDK magically derives from runtime behavior.

### Source types for MVP

#### 1. Uploaded documents

Markdown, PDF, DOCX, HTML, plain text. Kernel Memory handles extraction out of the box.

Typical use: product documentation, internal policies, runbooks, SOPs, integration guides.

#### 2. Generated technical documentation

Handler docs, tool docs, pipeline template docs, OpenAPI-derived endpoint docs.

This is one of the strongest Pipelogiq-native use cases. The SDK should make it easy for a consumer app to generate markdown from its own registered handlers and tools, then ingest it as knowledge.

#### 3. External synchronized sources

Git, Confluence, SharePoint, Notion, blob storage.

For v1, the SDK only needs to support ingestion via `IRagIndexer.IngestAsync`. Source connectors are the consumer's responsibility. Kernel Memory also has its own connector ecosystem for common sources.

#### 4. Curated execution summaries

Not raw logs. Summarized incident reports, recurring failure explanations, recovery playbooks, known integration issues.

This is execution-aware knowledge, which is much safer and more useful than indexing raw stage logs directly.

### Phased rollout

**Phase 1** — Manual upload: product docs, SOPs, policies, runbooks.

**Phase 2** — Generated docs: handler docs, tool docs, pipeline docs, OpenAPI docs.

**Phase 3** — External sync: Git, Confluence, SharePoint (via consumer code or KM connectors).

**Phase 4** — Execution-derived summaries: failure explanations, known issue summaries, operational lessons.

This phased approach keeps quality high and prevents the knowledge base from turning into a noisy dump.

## DI surface

```csharp
services.AddPipelogiq(options => { ... });

var agentBuilder = services.AddPipelogiqAgent(opts =>
{
    opts.LlmProvider = AgentLlmProvider.OpenAI;
    opts.LlmApiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
    opts.LlmModel = "gpt-5-mini";
    opts.UseReActMode = true;
});

services.AddPipelogiqRag(rag =>
{
    rag.DefaultTopK = 8;
    rag.MinRelevance = 0.5;
    rag.RequireSources = true;
})
.UseKernelMemory(km => km
    .WithOpenAIDefaults(Environment.GetEnvironmentVariable("OPENAI_API_KEY")!)
    .WithPostgresMemoryDb(connectionString))
.ExposeAsAgentTool(agentBuilder, tool =>
{
    tool.Name = "askKnowledgeBase";
    tool.Description = "Search indexed internal knowledge for documentation, policies, runbooks, and operational lessons.";
});
```

```csharp
runner.RegisterAgentHandlers()
      .RegisterRagHandlers();
```

## Observability

RAG should behave like a first-class Pipelogiq capability.

That means:

- ingestion runs through Pipelogiq stages — visible in pipeline dashboard with progress, duration, errors
- query from agent appears as a tool call — visible in agent execution trace
- scope and access policy decisions are logged
- citations are attached to audit records
- failures return structured error codes

Useful metrics (exposed via standard Pipelogiq stage logging):

- ingestion stage duration
- ingestion failures per document type
- query latency (Pipelogiq overhead + KM search time)
- zero-hit queries (no relevant chunks found)
- low-confidence answers
- queries per knowledge space
- audit record count

Kernel Memory internal metrics (embedding latency, chunk counts, vector search latency) are available through KM's own logging and can be surfaced in Pipelogiq observability if the consumer configures KM logging to the same sink.

## Audit trail

Every answer should be auditable.

Minimum audit record:

- request id
- session id
- application id
- tenant id
- user id
- pipeline id
- stage id
- question
- scope used (resolved, not just requested)
- answer
- citation document IDs and scores
- created time

The SDK should expose an `IRagAuditSink` extension point. A default implementation can be `NoOp`, structured logging, or a simple local store depending on the host application. Consumers that want durable audit persistence can plug in a custom database-backed implementation.

## Risks and mitigations

### Hallucinations

Mitigation: Pipelogiq should enforce grounded-answer behavior at the adapter boundary by requiring citations, applying scope filters before prompt construction, and returning "no relevant knowledge found" when retrieval does not produce usable evidence. Kernel Memory helps with retrieval and citations, but Pipelogiq should not treat any external engine as a hard guarantee against hallucination.

### Stale documents

Mitigation: Kernel Memory handles document versioning and content hash deduplication internally. Pipelogiq provides `ReindexKnowledgeBaseHandler` as a schedulable pipeline stage.

### Permission leakage

Mitigation: `IRagAccessPolicy` checked before every query. Scope-based tag filters applied at the storage layer before prompt construction. Audit trail records every query.

### Kernel Memory as dependency

Mitigation: `IRagService` and `IRagIndexer` are Pipelogiq-owned interfaces. The Kernel Memory adapter is the first implementation. If KM becomes unsuitable, a `Pipelogiq.Sdk.Rag.DirectVectorData` adapter using `Microsoft.Extensions.VectorData` directly can replace it without changing consumer code.

### Kernel Memory version drift

Mitigation: Pin `Microsoft.KernelMemory.Abstractions` as the dependency in `Pipelogiq.Sdk.Rag`, and `Microsoft.KernelMemory.Core` only in `Pipelogiq.Sdk.Rag.KernelMemory`. This isolates the concrete dependency.

## Implementation phases

### Phase 1: contracts and Kernel Memory adapter

- Create `Pipelogiq.Sdk.Rag` with `IRagService`, `IRagIndexer`, `IRagScopeResolver`, `IRagAccessPolicy`, models
- Create `Pipelogiq.Sdk.Rag.KernelMemory` with adapter implementations
- Add `AddPipelogiqRag()` and `UseKernelMemory()` DI extensions
- Default scope resolver reading from `IStageContext`
- Default permissive access policy within same `ApplicationId`

### Phase 2: agent tool and stage handlers

- Add `AskKnowledgeBaseHandler` — native agent tool
- Add `IngestKnowledgeDocumentHandler` — Pipelogiq stage handler
- Add `ReindexKnowledgeBaseHandler` — Pipelogiq stage handler
- Add `ExposeAsAgentTool()` and `RegisterRagHandlers()` extensions
- Add `PipelineBuilder.WithRagIngestion()` and `WithRagAsk()` helpers

### Phase 3: observability and audit

- Add `RagAuditRecord` and default audit store
- Add stage-level logging for ingestion (document title, chunk count, duration)
- Add tool-level logging for queries (scope, hit count, latency)
- Add metrics integration with Pipelogiq observability

### Phase 4: generated documentation helpers

- Add `GenerateHandlerDocumentationHandler` — generates markdown from registered stage handlers
- Add `GeneratePipelineDocumentationHandler` — generates markdown from pipeline templates
- Add `GenerateToolDocumentationHandler` — generates markdown from registered agent tools
- Wire as schedulable pipeline stages for periodic knowledge refresh

## What Pipelogiq does NOT build

- Vector store implementations — use Kernel Memory backends
- Embedding clients — use Kernel Memory or `Microsoft.Extensions.AI`
- Document text extractors — use Kernel Memory handlers
- Chunking engines — use Kernel Memory handlers
- Storage connectors for Qdrant, Azure AI Search, Redis, etc. — use Kernel Memory packages

If a consumer needs PostgreSQL/pgvector: `Microsoft.KernelMemory.Postgres`.
If a consumer needs Qdrant: `Microsoft.KernelMemory.Qdrant`.
If a consumer needs Azure AI Search: `Microsoft.KernelMemory.AzureAISearch`.

Pipelogiq does not wrap, repackage, or re-export these. The consumer configures them directly through `UseKernelMemory(km => km.With...)`.

## Final recommendation

RAG is a strong fit for Pipelogiq AI Agent, but it should be implemented as:

- an orchestration and observability layer on top of Kernel Memory,
- not as a competing RAG engine,
- not as hidden planner behavior,
- and not as a one-off app service.

The right architecture is:

- `Pipelogiq.Sdk.Rag` — Pipelogiq-owned contracts, scope resolution, access policy, audit, stage handlers, agent tool
- `Pipelogiq.Sdk.Rag.KernelMemory` — first adapter delegating RAG internals to Microsoft Kernel Memory
- Retrieval exposed as a native agent tool
- Ingestion exposed as Pipelogiq stage handlers
- Storage configured through Kernel Memory (PostgreSQL/pgvector, Qdrant, Azure AI Search, or any other KM backend)
- Strict scope-based filtering by application and tenant

This approach delivers RAG in weeks instead of months, avoids maintaining infrastructure that Microsoft already maintains, and focuses Pipelogiq's effort on what it does better than anyone: pipeline orchestration, agent integration, and observability, while keeping the door open for future adapters if Kernel Memory is not the right long-term fit for every consumer.
