# MCP Server Design — Revised Single-Host Implementation Design

## 1. Purpose

This document replaces the earlier split-host MCP server design.

The previous design explicitly described:
- a separate HTTP AOT host
- a separate stdio AOT host
- a separate non-AOT monitoring server

That architecture is no longer correct for this project. The revised requirement is:
- **one single runtime host executable**
- **one single hosting project**
- the host must support **either HTTP MCP transport or stdio MCP transport**
- the host may optionally expose a **built-in monitoring web UI**
- **SignalR is used only for live tracking/monitoring of the log page and monitoring dashboard**, not as a second runtime server and not as part of the MCP protocol itself

This revised design keeps the original retrieval/database/tooling detail level, but updates the runtime topology so Codex does not incorrectly create multiple host executables or a separate monitor server. The earlier document explicitly mandated multiple hosts and a separate monitor host, which is the source of the current confusion. fileciteturn3file1

---

## 2. Final Architecture Decision

## 2.1 Single-host rule

There is exactly **one runtime host project**:

```text
HarnessMcp.Host.Aot
```

This project is the only server executable for the MCP system.

It supports two transport modes:
- `Http`
- `Stdio`

Transport mode is selected by configuration or command-line override at startup.

The host process is therefore a **single executable** that can run in one of two ways:
- as an HTTP MCP server
- as a stdio MCP server

There must not be separate runtime executables such as:
- `HarnessMcp.Host.HttpAot`
- `HarnessMcp.Host.StdioAot`
- `HarnessMcp.Monitor.Server`

Those names and that split topology are obsolete for this project.

## 2.2 Monitoring rule

The monitoring UI is **built into the same host**.

Monitoring is **optional** and controlled by configuration.

SignalR exists only to provide live updates to the monitoring web page. It is not used as a separate monitoring process, and it is not used as part of MCP transport.

When the host runs in `Http` mode and monitoring is enabled, the host exposes:
- MCP over HTTP
- health/version endpoints
- monitoring endpoints
- SignalR hub for live tracking
- static monitoring page

When the host runs in `Stdio` mode:
- MCP is served over stdio only
- there is no listening socket
- the monitoring web page is disabled
- SignalR is disabled
- the server may still collect monitor events internally for logging/debugging, but it must not expose the web UI

## 2.3 What remains unchanged from the earlier design

The following parts of the earlier design remain correct and are preserved:
- .NET 10 target framework
- official MCP C# SDK usage
- Native AOT as a primary requirement for the host
- manual composition instead of a DI-heavy runtime graph
- direct SQL through `Npgsql`
- PostgreSQL + pgvector + full-text search
- harness-primary MCP tool surface
- explicit mapping to the revised database tables
- read-only v1 data access model
- contract-driven, deterministic output design

What changes is the **runtime shape**, not the retrieval model. The earlier document’s split-host solution layout, split monitoring model, and “publish both AOT hosts” requirement are the parts being replaced. fileciteturn3file1turn3file13

---

## 3. Non-Negotiable Architecture Decisions

## 3.1 Framework and runtime decisions

Use these exact platform decisions:

- .NET target framework: `net10.0`
- official MCP SDK packages:
  - `ModelContextProtocol.Core` `1.2.0`
  - `ModelContextProtocol` `1.2.0`
  - `ModelContextProtocol.AspNetCore` `1.2.0`
- PostgreSQL data provider:
  - `Npgsql` `10.0.2`
- optional harness/client-side orchestration only:
  - `Microsoft.Agents.AI` `1.1.0`

Do **not** build the MCP server core on Microsoft Agent Framework.
Agent Framework is optional and allowed only in `HarnessMcp.AgentClient`.

## 3.2 AOT rules

The single host must support Native AOT.

Therefore:
- use Minimal APIs only for HTTP mode
- use `WebApplication.CreateSlimBuilder(args)` in HTTP mode
- use JSON source generation for all contracts used by the host and transport
- use configuration binding source generation
- do not use runtime assembly scanning for MCP tool/resource registration
- do not use MVC/controllers/Razor Pages/Blazor Server
- do not use EF Core
- do not use third-party DI containers
- do not use reflection-based plugin loading

## 3.3 Dependency injection policy

The application graph must use **manual composition**.

Allowed use of the built-in service collection is limited to host-edge integration points required by ASP.NET Core and the official MCP SDK.

Allowed:
- `builder.Services.AddMcpServer()`
- `builder.Services.AddHealthChecks()`
- `builder.Services.AddSignalR()` only inside the single host and only when monitoring UI is enabled
- registering already-created singletons for transport integration

Not allowed:
- assembly scanning registration
- making constructor injection the main composition mechanism of the application
- resolving domain services all over the codebase from `IServiceProvider`
- `WithToolsFromAssembly()`
- `WithResourcesFromAssembly()`

## 3.4 Database and retrieval architecture

### 3.4.1 Repository-pattern persistence boundary

The database part must use a **repository-pattern-based persistence abstraction** so the server can switch database/provider implementation with minimal impact on core logic.

This is a hard requirement.

The design therefore separates:
- **Core application services**, which depend only on repository interfaces / persistence API abstractions
- **Infrastructure provider implementations**, which implement those abstractions for PostgreSQL in v1

The host, transport layer, and core services must not depend directly on:
- `NpgsqlConnection`
- SQL text
- PostgreSQL-specific types
- pgvector-specific SQL expressions
- provider-specific row readers

All of those belong in infrastructure.

### 3.4.2 Persistence API rule

There must be a database-access abstraction layer API between core logic and the PostgreSQL implementation.

That API is expressed through repository interfaces in `HarnessMcp.Core`.

At minimum, v1 must include:

```csharp
public interface IKnowledgeRepository
{
    ValueTask<IReadOnlyList<KnowledgeCandidateDto>> SearchLexicalAsync(
        SearchKnowledgeRequest request,
        CancellationToken cancellationToken);

    ValueTask<IReadOnlyList<KnowledgeCandidateDto>> SearchSemanticAsync(
        SearchKnowledgeRequest request,
        ReadOnlyMemory<float> embedding,
        CancellationToken cancellationToken);

    ValueTask<GetKnowledgeItemResponse> GetKnowledgeItemAsync(
        GetKnowledgeItemRequest request,
        CancellationToken cancellationToken);

    ValueTask<GetRelatedKnowledgeResponse> GetRelatedKnowledgeAsync(
        GetRelatedKnowledgeRequest request,
        CancellationToken cancellationToken);
}
```

This repository API is the persistence boundary for v1.

Application services call the repository API.
The repository API is implemented by provider-specific infrastructure classes.

### 3.4.3 Provider-swappability rule

The PostgreSQL implementation is only one provider.

The design must allow a future provider such as:
- SQL Server
- SQLite
- Azure AI Search backed hybrid store
- document database + vector store
- test in-memory fake implementation

without changing:
- core retrieval orchestration
- merge logic
- context pack assembly
- MCP transport contracts
- host routing

Therefore:

- Core must depend only on repository interfaces and abstractions.
- `HarnessMcp.Infrastructure.Postgres` must contain PostgreSQL-specific implementation details.
- The composition root selects the concrete provider implementation.
- Switching providers must primarily be a composition-root and infrastructure concern, not a core rewrite.

### 3.4.4 Service-to-repository call rule

The call chain must be:

```text
MCP Tool / Resource
  -> Core Service
    -> Repository Interface (Core API boundary)
      -> PostgreSQL Repository Implementation
        -> SQL / Npgsql / pgvector
```

Not allowed:

```text
MCP Tool -> Npgsql
Core Service -> SQL text
Host -> repository SQL helpers
Transport -> provider-specific row mappers
```


Use PostgreSQL + pgvector + full-text search.

Use direct SQL through `Npgsql`.

Use a hybrid retrieval design:
- lexical candidate generation via PostgreSQL full-text search and controlled lexical expressions
- semantic candidate generation via pgvector similarity over route-specific or fallback embeddings
- merge/ranking in C#, not in one giant SQL query

### 3.4.5 Semantic-query compatibility rule

The embedding database is built by the Python builder, not by the MCP server. Therefore semantic query vectors must be generated in a way that is compatible with the vectors already persisted in `knowledge_embeddings`.

V1 design decision:
- MCP does **not** attempt to reimplement the Python builder's embedding pipeline in .NET as the primary path
- MCP uses a **builder-owned local query embedding API** to generate query vectors
- the API is query-only and returns vectors plus the metadata needed for compatibility checks in the same response

This is the practical and accuracy-first choice because the builder currently controls:
- sentence-transformers model loading
- local-path model resolution
- optional download-if-missing behavior
- normalization behavior
- hashing-fallback behavior when the normal model is unavailable

The MCP server must therefore treat the Python builder as the semantic-query embedding authority in v1.

### 3.4.6 Compatibility-before-semantic-search rule

The server must not silently run semantic search with incompatible query vectors.

Before semantic search is used for a request, MCP must verify that the query embedding response is compatible with the stored DB embeddings for the target role.

Minimum compatibility checks:
- response vector dimension equals the actual vector length returned by the API
- response metadata is compatible with the model/version metadata stored for the DB embeddings being queried
- response `normalize_embeddings` matches the normalization regime used to build the stored vectors
- response `fallback_mode` is acceptable for the stored DB embeddings

If compatibility fails, the server must either:
- fail fast with a clear operator-facing error, or
- explicitly degrade to lexical-only retrieval and report the reason in diagnostics

It must not silently treat arbitrary query vectors as compatible semantic vectors.

## 3.5 Monitoring in the single host

The earlier design said to keep SignalR only in a separate monitor host and not merge monitoring into the AOT host. That is no longer the design. fileciteturn3file1turn3file9

The new rule is:
- the single host owns monitoring
- SignalR is used only for tracking/log streaming and live monitoring updates
- the monitoring page is a support/debugging surface, not a second product surface
- the monitoring page must be configurable on/off
- the monitoring page exists only in `Http` mode

---

## 4. Required Alignment With the Other Designs

## 4.1 Harness alignment

The harness requires the sequence:
1. `retrieve_memory_by_chunks`
2. `merge_retrieval_results`
3. `build_memory_context_pack`

The server must therefore expose these tools explicitly and treat them as the **primary harness integration surface**.

The lower-level tools remain part of v1 and must be retained:
- `search_knowledge`
- `get_knowledge_item`
- `get_related_knowledge`
- `get_server_info`

Final public tool surface for v1:
1. `retrieve_memory_by_chunks`
2. `merge_retrieval_results`
3. `build_memory_context_pack`
4. `search_knowledge`
5. `get_knowledge_item`
6. `get_related_knowledge`
7. `get_server_info`

## 4.2 Database alignment

The server must read from the revised database design, not from the older rigid `memory_type` model.

The server’s read model must explicitly map to these tables:
- `knowledge_items`
- `knowledge_labels`
- `knowledge_scopes`
- `knowledge_tags`
- `knowledge_relations`
- `source_artifacts`
- `source_segments`
- `knowledge_item_segments`
- `case_shapes`
- `retrieval_profiles`
- `knowledge_embeddings`
- optional future:
  - `structure_entities`
  - `knowledge_item_entities`

The server does not write to these tables in v1.

## 4.3 Builder alignment for semantic search

The Python embedding builder is the authority for how corpus-compatible embeddings are created in v1.

That means MCP semantic search must align with the builder in these ways:
- query embeddings are produced by calling the builder-owned query API
- the API uses the same runtime embedding path as the builder's corpus embedding pipeline
- compatibility metadata returned by the API is checked against database embedding metadata before semantic search is used
- semantic incompatibility must not be hidden from the harness

This keeps the harness-controlled flow accurate because retrieval chunks are only useful if semantic search is both relevant and actually compatible with the built database.

---

## 5. Final Solution Layout

Use exactly this solution layout:

```text
src/
  HarnessMcp.Contracts/
  HarnessMcp.Core/
  HarnessMcp.Infrastructure.Postgres/
  HarnessMcp.Transport.Mcp/
  HarnessMcp.Host.Aot/
  HarnessMcp.AgentClient/                # optional
tests/
  HarnessMcp.Tests.Unit/
  HarnessMcp.Tests.Integration/
  HarnessMcp.Tests.Contracts/
```

## 5.1 Project responsibilities

### `HarnessMcp.Contracts`
Contains shared contracts and static metadata:
- request/response DTOs
- enums
- schema version constants
- JSON serializer source-generation context
- config DTOs
- monitor DTOs used by the built-in monitoring page
- validation helper methods that do not depend on ASP.NET Core
- contract mapping helper methods that are pure/static

No ASP.NET Core types.
No Npgsql types.
No MCP SDK attributes.
No SignalR hub types.

### `HarnessMcp.Core`
Contains application/domain logic:
- chunk retrieval orchestration
- retrieval result merge logic
- memory context pack assembly
- search orchestration
- ranking logic
- scope normalization
- authority filtering
- stale/superseded filtering policy
- request validation
- monitor event publication abstractions
- context pack cache abstractions
- app version/info abstractions
- monitoring snapshot abstractions
- monitoring projection abstractions
- repository interfaces / persistence API abstractions

No ASP.NET Core types.
No Npgsql types.
No MCP SDK types.
No SignalR hub types.
No provider-specific SQL or provider-specific row mapping code.

### `HarnessMcp.Infrastructure.Postgres`
Contains all database-facing and infrastructure code:
- repository implementations
- SQL text library
- row mappers
- evidence snippet reader
- health probe implementation
- query-embedding provider implementations
- context pack cache implementation if shared here
- Npgsql data source factory
- monitor ring buffer
- monitor event exporter
- monitor event broadcaster adapter interfaces/implementations if infrastructure-backed
- logging providers

This project is allowed to know PostgreSQL-specific details.
This project is not allowed to leak PostgreSQL-specific concerns into `HarnessMcp.Core` or public contracts.

### `HarnessMcp.Transport.Mcp`
Contains transport-facing MCP classes:
- MCP tool class(es)
- MCP resource class(es)
- transport exception to response mapping
- schema resource provider

This layer adapts MCP transport objects to core services.
It must call core services, which call repository abstractions.
It must not issue direct SQL or depend on provider-specific row mappers.

### `HarnessMcp.Host.Aot`
Contains the single host:
- `Program.cs`
- app config loader
- composition root
- transport mode runner
- HTTP MCP transport wiring
- stdio MCP transport wiring
- health endpoints
- version endpoint
- monitor event export endpoint
- monitoring snapshot endpoint
- SignalR hub
- static monitoring page endpoints
- monitoring UI configuration gates

The host composes concrete implementations.
The host does not query the database directly.

### `HarnessMcp.AgentClient` (optional)
Contains optional utilities only:
- MCP client smoke tests
- optional Microsoft Agent Framework integration
- harness proof-of-life console programs
- schema inspection utilities

---

## 6. Package Rules

## 6.1 Central package management

Use `Directory.Packages.props`.

Required versions:

```xml
<Project>
  <ItemGroup>
    <PackageVersion Include="ModelContextProtocol.Core" Version="1.2.0" />
    <PackageVersion Include="ModelContextProtocol" Version="1.2.0" />
    <PackageVersion Include="ModelContextProtocol.AspNetCore" Version="1.2.0" />
    <PackageVersion Include="Npgsql" Version="10.0.2" />
    <PackageVersion Include="Microsoft.Extensions.Configuration.Binder" Version="10.0.0" />
    <PackageVersion Include="Microsoft.Extensions.Hosting" Version="10.0.0" />
    <PackageVersion Include="Microsoft.Extensions.Logging.Abstractions" Version="10.0.0" />
    <PackageVersion Include="Microsoft.AspNetCore.SignalR.Client" Version="10.0.0" />
    <PackageVersion Include="Microsoft.Agents.AI" Version="1.1.0" />
  </ItemGroup>
</Project>
```

Notes:
- do **not** add `Microsoft.Agents.AI` to MCP server core projects
- do **not** add EF Core packages to the host
- the host may use the ASP.NET Core shared framework for SignalR server functionality; add extra SignalR packages only when the build requires them

## 6.2 Test packages

Use:
- `Microsoft.NET.Test.Sdk`
- `xunit`
- `xunit.runner.visualstudio`
- `FluentAssertions`

Use xUnit consistently.

---

## 7. Project File Rules

## 7.1 Host `.csproj` properties

For `HarnessMcp.Host.Aot`:

```xml
<PropertyGroup>
  <TargetFramework>net10.0</TargetFramework>
  <PublishAot>true</PublishAot>
  <InvariantGlobalization>true</InvariantGlobalization>
  <EnableConfigurationBindingGenerator>true</EnableConfigurationBindingGenerator>
  <ImplicitUsings>enable</ImplicitUsings>
  <Nullable>enable</Nullable>
</PropertyGroup>
```

## 7.2 Contract source generation

In `HarnessMcp.Contracts`, add one `JsonSerializerContext` containing all transport/config/monitor DTO types.

Name:

```csharp
AppJsonSerializerContext
```

It must be used by:
- MCP tool registration
- HTTP endpoint serialization where explicit options are needed
- config/document/schema serialization helpers
- monitoring snapshot/event endpoint serialization

---

## 8. Final Public MCP Tool Surface

## 8.1 Harness-primary tools

### `retrieve_memory_by_chunks`
Purpose:
- independently retrieve knowledge for each chunk from the harness
- preserve route separation by chunk type
- return type-separated results per chunk
- enforce active-only and minimum-authority filters
- suppress superseded records

### `merge_retrieval_results`
Purpose:
- merge chunk-level results into a task-level merged candidate set
- de-duplicate by `knowledge_item_id`
- preserve support provenance from which chunk(s) selected the item
- preserve authority and confidence cues
- retain bucket separation for downstream context pack assembly

### `build_memory_context_pack`
Purpose:
- build the final structured memory context pack used by the harness
- group merged items into stable output channels
- include warnings/diagnostics
- do **not** create an execution plan

## 8.2 Secondary tools retained from the earlier design

### `search_knowledge`
Purpose:
- focused route-specific inspection
- debugging retrieval behavior
- direct harness or developer search

### `get_knowledge_item`
Purpose:
- hydrate one item with provenance and optional relations/segments

### `get_related_knowledge`
Purpose:
- fetch related items by relation type

### `get_server_info`
Purpose:
- version, capability, transport mode, and schema metadata

## 8.3 Tools intentionally excluded from v1

Do not implement:
- write/update/delete knowledge
- embedding generation for the corpus
- SQL console tools
- shell tools
- filesystem browsing tools
- MCP sampling/elici­tation features
- roots tools

---

## 9. Contracts

All contracts must be in `HarnessMcp.Contracts` and must be source-generation-friendly.

## 9.1 Enums

Implement these enums:

```csharp
public enum RetrievalClass
{
    Decision,
    BestPractice,
    Antipattern,
    SimilarCase,
    Constraint,
    Reference,
    Structure
}

public enum QueryKind
{
    CoreTask,
    Constraint,
    Risk,
    Pattern,
    SimilarCase,
    Summary,
    Details
}

public enum AuthorityLevel
{
    Draft = 0,
    Observed = 1,
    Reviewed = 2,
    Approved = 3,
    Canonical = 4
}

public enum KnowledgeStatus
{
    Active,
    Deprecated,
    Superseded,
    Archived
}

public enum ChunkType
{
    CoreTask,
    Constraint,
    Risk,
    Pattern,
    SimilarCase
}

public enum RelationType
{
    Related,
    Supports,
    ConflictsWith,
    DependsOn,
    Exemplifies,
    DerivedFrom,
    Summarizes,
    SameSourceFamily
}

public enum TransportMode
{
    Http,
    Stdio
}

public enum MonitorEventKind
{
    Log,
    RequestStart,
    RequestSuccess,
    RequestFailure,
    SqlTiming,
    EmbeddingTiming,
    MergeTiming,
    ContextPackBuilt,
    Warning,
    HealthFailure
}
```

## 9.2 Scope DTO

```csharp
public sealed record ScopeFilterDto(
    IReadOnlyList<string> Domains,
    IReadOnlyList<string> Modules,
    IReadOnlyList<string> Features,
    IReadOnlyList<string> Layers,
    IReadOnlyList<string> Concerns,
    IReadOnlyList<string> Repos,
    IReadOnlyList<string> Services,
    IReadOnlyList<string> Symbols);
```

## 9.3 Search contracts

```csharp
public sealed record SearchKnowledgeRequest(
    string SchemaVersion,
    string RequestId,
    string QueryText,
    QueryKind QueryKind,
    ScopeFilterDto Scopes,
    IReadOnlyList<RetrievalClass> RetrievalClasses,
    AuthorityLevel MinimumAuthority,
    KnowledgeStatus Status,
    int TopK,
    bool IncludeEvidence,
    bool IncludeRawDetails);
```

```csharp
public sealed record EvidenceDto(
    Guid SourceArtifactId,
    string? SourcePath,
    IReadOnlyList<string> HeadingPath,
    string Snippet,
    int? StartLine,
    int? EndLine);
```

```csharp
public sealed record KnowledgeCandidateDto(
    Guid KnowledgeItemId,
    RetrievalClass RetrievalClass,
    string Title,
    string Summary,
    string? Details,
    double SemanticScore,
    double LexicalScore,
    double ScopeScore,
    double AuthorityScore,
    double CaseShapeScore,
    double FinalScore,
    AuthorityLevel Authority,
    KnowledgeStatus Status,
    ScopeFilterDto Scopes,
    IReadOnlyList<string> Labels,
    IReadOnlyList<string> Tags,
    IReadOnlyList<EvidenceDto> Evidence,
    IReadOnlyList<string> SupportedByChunks,
    IReadOnlyList<string> SupportedByQueryKinds);
```

```csharp
public sealed record SearchKnowledgeDiagnosticsDto(
    int LexicalCandidateCount,
    int VectorCandidateCount,
    int MergedCandidateCount,
    int FinalCandidateCount,
    long ElapsedMs,
    string QueryEmbeddingModel,
    string EmbeddingRoleUsed);
```

```csharp
public sealed record SearchKnowledgeResponse(
    string SchemaVersion,
    string Kind,
    string RequestId,
    IReadOnlyList<KnowledgeCandidateDto> Candidates,
    SearchKnowledgeDiagnosticsDto Diagnostics);
```

## 9.4 Harness-primary contracts

```csharp
public sealed record RetrievalChunkDto(
    string ChunkId,
    ChunkType ChunkType,
    string? Text,
    ScopeFilterDto? StructuredScopes,
    SimilarCaseShapeDto? TaskShape);
```

```csharp
public sealed record SimilarCaseShapeDto(
    string TaskType,
    string FeatureShape,
    bool EngineChangeAllowed,
    IReadOnlyList<string> LikelyLayers,
    IReadOnlyList<string> RiskSignals,
    string? Complexity);
```

```csharp
public sealed record RetrieveMemoryByChunksRequest(
    string SchemaVersion,
    string RequestId,
    string TaskId,
    RequirementIntentDto RequirementIntent,
    IReadOnlyList<RetrievalChunkDto> RetrievalChunks,
    ChunkSearchProfileDto SearchProfile);
```

```csharp
public sealed record RequirementIntentDto(
    string TaskType,
    string? Domain,
    string? Module,
    string? Feature,
    IReadOnlyList<string> HardConstraints,
    IReadOnlyList<string> RiskSignals);
```

```csharp
public sealed record ChunkSearchProfileDto(
    bool ActiveOnly,
    AuthorityLevel MinimumAuthority,
    int MaxItemsPerChunk,
    bool RequireTypeSeparation);
```

```csharp
public sealed record ChunkBucketDto(
    IReadOnlyList<KnowledgeCandidateDto> Decisions,
    IReadOnlyList<KnowledgeCandidateDto> BestPractices,
    IReadOnlyList<KnowledgeCandidateDto> Antipatterns,
    IReadOnlyList<KnowledgeCandidateDto> SimilarCases,
    IReadOnlyList<KnowledgeCandidateDto> Constraints,
    IReadOnlyList<KnowledgeCandidateDto> References,
    IReadOnlyList<KnowledgeCandidateDto> Structures);
```

```csharp
public sealed record ChunkRetrievalResultDto(
    string ChunkId,
    ChunkType ChunkType,
    ChunkBucketDto Results,
    SearchKnowledgeDiagnosticsDto Diagnostics);
```

```csharp
public sealed record RetrieveMemoryByChunksResponse(
    string SchemaVersion,
    string Kind,
    string RequestId,
    string TaskId,
    IReadOnlyList<ChunkRetrievalResultDto> ChunkResults,
    IReadOnlyList<string> Notes,
    long ElapsedMs);
```

```csharp
public sealed record MergedKnowledgeItemDto(
    KnowledgeCandidateDto Item,
    IReadOnlyList<string> SupportedByChunkIds,
    IReadOnlyList<ChunkType> SupportedByChunkTypes,
    IReadOnlyList<string> MergeRationales);
```

```csharp
public sealed record MergeRetrievalResultsRequest(
    string SchemaVersion,
    string RequestId,
    string TaskId,
    RetrieveMemoryByChunksResponse Retrieved);
```

```csharp
public sealed record MergeRetrievalResultsResponse(
    string SchemaVersion,
    string Kind,
    string RequestId,
    string TaskId,
    IReadOnlyList<MergedKnowledgeItemDto> Decisions,
    IReadOnlyList<MergedKnowledgeItemDto> Constraints,
    IReadOnlyList<MergedKnowledgeItemDto> BestPractices,
    IReadOnlyList<MergedKnowledgeItemDto> AntiPatterns,
    IReadOnlyList<MergedKnowledgeItemDto> SimilarCases,
    IReadOnlyList<MergedKnowledgeItemDto> References,
    IReadOnlyList<MergedKnowledgeItemDto> Structures,
    IReadOnlyList<string> Warnings,
    long ElapsedMs);
```

```csharp
public sealed record BuildMemoryContextPackRequest(
    string SchemaVersion,
    string RequestId,
    string TaskId,
    RequirementIntentDto RequirementIntent,
    RetrieveMemoryByChunksResponse Retrieved,
    MergeRetrievalResultsResponse Merged);
```

```csharp
public sealed record ContextPackSectionDto(
    IReadOnlyList<MergedKnowledgeItemDto> Decisions,
    IReadOnlyList<MergedKnowledgeItemDto> Constraints,
    IReadOnlyList<MergedKnowledgeItemDto> BestPractices,
    IReadOnlyList<MergedKnowledgeItemDto> AntiPatterns,
    IReadOnlyList<MergedKnowledgeItemDto> SimilarCases,
    IReadOnlyList<MergedKnowledgeItemDto> References,
    IReadOnlyList<MergedKnowledgeItemDto> Structures);
```

```csharp
public sealed record ContextPackDiagnosticsDto(
    int ChunksProcessed,
    int DistinctKnowledgeItems,
    long RetrievalElapsedMs,
    long MergeElapsedMs,
    long AssemblyElapsedMs,
    IReadOnlyList<string> Warnings);
```

```csharp
public sealed record BuildMemoryContextPackResponse(
    string SchemaVersion,
    string Kind,
    string RequestId,
    string TaskId,
    ContextPackSectionDto ContextPack,
    ContextPackDiagnosticsDto Diagnostics);
```

## 9.5 Read contracts

```csharp
public sealed record GetKnowledgeItemRequest(
    string SchemaVersion,
    string RequestId,
    Guid KnowledgeItemId,
    bool IncludeRelations,
    bool IncludeSegments,
    bool IncludeLabels,
    bool IncludeTags,
    bool IncludeScopes);
```

```csharp
public sealed record KnowledgeSegmentDto(
    Guid SourceSegmentId,
    string SpanLevel,
    IReadOnlyList<string> HeadingPath,
    int? StartLine,
    int? EndLine,
    int? StartOffset,
    int? EndOffset,
    string Role,
    string? SourcePath);
```

```csharp
public sealed record RelatedKnowledgeDto(
    Guid KnowledgeItemId,
    RelationType RelationType,
    string Title,
    string Summary,
    RetrievalClass RetrievalClass,
    AuthorityLevel Authority,
    double RelationStrength);
```

```csharp
public sealed record GetKnowledgeItemResponse(
    string SchemaVersion,
    string Kind,
    string RequestId,
    KnowledgeCandidateDto Item,
    IReadOnlyList<KnowledgeSegmentDto> Segments,
    IReadOnlyList<RelatedKnowledgeDto> Relations);
```

```csharp
public sealed record GetRelatedKnowledgeRequest(
    string SchemaVersion,
    string RequestId,
    Guid KnowledgeItemId,
    IReadOnlyList<RelationType> RelationTypes,
    int TopK);
```

```csharp
public sealed record GetRelatedKnowledgeResponse(
    string SchemaVersion,
    string Kind,
    string RequestId,
    Guid KnowledgeItemId,
    IReadOnlyList<RelatedKnowledgeDto> Items);
```

## 9.6 Server info contract

```csharp
public sealed record ServerInfoResponse(
    string SchemaVersion,
    string Kind,
    string ServerName,
    string ServerVersion,
    string ProtocolMode,
    FeatureFlagsDto Features,
    SchemaSetDto SchemaSet);
```

```csharp
public sealed record FeatureFlagsDto(
    bool RetrieveMemoryByChunks,
    bool MergeRetrievalResults,
    bool BuildMemoryContextPack,
    bool SearchKnowledge,
    bool GetKnowledgeItem,
    bool GetRelatedKnowledge,
    bool HttpTransport,
    bool StdioTransport,
    bool WriteOperations,
    bool MonitoringUi,
    bool RealtimeTracking);
```

```csharp
public sealed record SchemaSetDto(
    string RetrieveMemoryByChunks,
    string MergeRetrievalResults,
    string BuildMemoryContextPack,
    string SearchKnowledge,
    string GetKnowledgeItem,
    string GetRelatedKnowledge,
    string GetServerInfo);
```

## 9.7 Monitoring contracts

```csharp
public sealed record MonitorEventDto(
    long Sequence,
    DateTimeOffset TimestampUtc,
    MonitorEventKind EventKind,
    string? RequestId,
    string? ToolName,
    string? TaskId,
    string? Level,
    string Summary,
    string? PayloadPreviewJson);
```

```csharp
public sealed record MonitorBatchDto(
    long FromExclusiveSequence,
    long ToInclusiveSequence,
    IReadOnlyList<MonitorEventDto> Events);
```

```csharp
public sealed record MonitorServerSummaryDto(
    string ServerName,
    string ServerVersion,
    string ProtocolMode,
    bool MonitoringEnabled,
    bool RealtimeEnabled,
    DateTimeOffset StartedUtc,
    string Environment,
    bool DatabaseConfigured,
    string EmbeddingProviderSummary);
```

```csharp
public sealed record MonitorSnapshotDto(
    MonitorServerSummaryDto Server,
    IReadOnlyList<MonitorEventDto> RecentLogs,
    IReadOnlyList<MonitorEventDto> RecentOperations,
    IReadOnlyList<MonitorEventDto> RecentTimings,
    IReadOnlyList<MonitorEventDto> RecentWarnings,
    IReadOnlyList<MonitorEventDto> RecentOutputs,
    long LastSequence);
```

---

## 10. Core Interfaces

Implement these exact interfaces in `HarnessMcp.Core`.

## 10.1 Application services

```csharp
public interface IChunkRetrievalService
{
    ValueTask<RetrieveMemoryByChunksResponse> RetrieveMemoryByChunksAsync(
        RetrieveMemoryByChunksRequest request,
        CancellationToken cancellationToken);
}

public interface IRetrievalMergeService
{
    ValueTask<MergeRetrievalResultsResponse> MergeRetrievalResultsAsync(
        MergeRetrievalResultsRequest request,
        CancellationToken cancellationToken);
}

public interface IMemoryContextPackService
{
    ValueTask<BuildMemoryContextPackResponse> BuildMemoryContextPackAsync(
        BuildMemoryContextPackRequest request,
        CancellationToken cancellationToken);
}

public interface IKnowledgeSearchService
{
    ValueTask<SearchKnowledgeResponse> SearchKnowledgeAsync(
        SearchKnowledgeRequest request,
        CancellationToken cancellationToken);
}

public interface IKnowledgeReadService
{
    ValueTask<GetKnowledgeItemResponse> GetKnowledgeItemAsync(
        GetKnowledgeItemRequest request,
        CancellationToken cancellationToken);
}

public interface IRelatedKnowledgeService
{
    ValueTask<GetRelatedKnowledgeResponse> GetRelatedKnowledgeAsync(
        GetRelatedKnowledgeRequest request,
        CancellationToken cancellationToken);
}
```

## 10.2 Retrieval/ranking policy interfaces

```csharp
public interface IQueryEmbeddingService
{
    ValueTask<ReadOnlyMemory<float>> EmbedAsync(
        string text,
        CancellationToken cancellationToken);
}

public interface IHybridRankingService
{
    IReadOnlyList<KnowledgeCandidateDto> Rank(
        IReadOnlyList<KnowledgeCandidateDto> lexical,
        IReadOnlyList<KnowledgeCandidateDto> semantic,
        SearchKnowledgeRequest request);
}

public interface IChunkQueryPlanner
{
    SearchKnowledgeRequest BuildSearchRequest(
        RetrieveMemoryByChunksRequest request,
        RetrievalChunkDto chunk,
        string requestIdSuffix);
}

public interface IRequestValidator
{
    void Validate(SearchKnowledgeRequest request);
    void Validate(RetrieveMemoryByChunksRequest request);
    void Validate(MergeRetrievalResultsRequest request);
    void Validate(BuildMemoryContextPackRequest request);
    void Validate(GetKnowledgeItemRequest request);
    void Validate(GetRelatedKnowledgeRequest request);
}

public interface IScopeNormalizer
{
    ScopeFilterDto Normalize(ScopeFilterDto scopes);
}

public interface IAuthorityPolicy
{
    bool IsAllowed(AuthorityLevel actual, AuthorityLevel required);
}

public interface ISupersessionPolicy
{
    bool IsVisible(KnowledgeStatus status, Guid? supersededBy);
}

public interface IContextPackAssembler
{
    BuildMemoryContextPackResponse Assemble(
        BuildMemoryContextPackRequest request,
        MergeRetrievalResultsResponse merged,
        long assemblyElapsedMs);
}
```

## 10.3 Monitoring/cache/info interfaces

```csharp
public interface IMonitorEventSink
{
    void Publish(MonitorEventDto evt);
}

public interface IMonitorEventExporter
{
    ValueTask<MonitorBatchDto> GetSinceAsync(
        long lastSequence,
        int maxCount,
        CancellationToken cancellationToken);
}

public interface IMonitoringSnapshotService
{
    ValueTask<MonitorSnapshotDto> GetSnapshotAsync(CancellationToken cancellationToken);
}

public interface IMonitorEventBroadcaster
{
    ValueTask BroadcastAsync(MonitorEventDto evt, CancellationToken cancellationToken);
}

public interface IContextPackCache
{
    void Put(BuildMemoryContextPackResponse response);
    bool TryGet(string taskId, out BuildMemoryContextPackResponse? response);
}

public interface IAppInfoProvider
{
    ServerInfoResponse GetServerInfo();
}

public interface IHealthProbe
{
    ValueTask<HealthProbeResult> CheckAsync(CancellationToken cancellationToken);
}
```

## 10.4 Repository interfaces

### 10.4.1 Repository-boundary rule

`IKnowledgeRepository` is the persistence API boundary for v1.

Core services must use this interface rather than direct DB code.

The repository implementation may use PostgreSQL-specific SQL and helpers internally, but that provider-specific logic must remain inside `HarnessMcp.Infrastructure.Postgres`.

### 10.4.2 Future provider extensibility

If a future provider is added, it should be able to implement the same repository interface contracts without changing:
- core service orchestration
- merge logic
- context pack assembly
- MCP transport surface
- host routing

That is the primary reason for the repository-pattern requirement.


```csharp
public interface IKnowledgeRepository
{
    ValueTask<IReadOnlyList<KnowledgeCandidateDto>> SearchLexicalAsync(
        SearchKnowledgeRequest request,
        CancellationToken cancellationToken);

    ValueTask<IReadOnlyList<KnowledgeCandidateDto>> SearchSemanticAsync(
        SearchKnowledgeRequest request,
        ReadOnlyMemory<float> embedding,
        CancellationToken cancellationToken);

    ValueTask<GetKnowledgeItemResponse> GetKnowledgeItemAsync(
        GetKnowledgeItemRequest request,
        CancellationToken cancellationToken);

    ValueTask<GetRelatedKnowledgeResponse> GetRelatedKnowledgeAsync(
        GetRelatedKnowledgeRequest request,
        CancellationToken cancellationToken);
}
```

---

## 11. Core Classes

Implement these classes exactly.

- `ChunkRetrievalService`
- `RetrievalMergeService`
- `MemoryContextPackService`
- `KnowledgeSearchService`
- `KnowledgeReadService`
- `RelatedKnowledgeService`
- `ChunkQueryPlanner`
- `HybridRankingService`
- `AuthorityPolicy`
- `SupersessionPolicy`
- `ScopeNormalizer`
- `RequestValidator`
- `ContextPackAssembler`
- `DiagnosticsBuilder`
- `QueryTextNormalizer`
- `CaseShapeMatcher`
- `AppInfoProvider`
- `MonitoringSnapshotService`
- `UiEventProjector`
- `UiTrimPolicy`

### 11.1 Responsibilities identical to the earlier retrieval design

The responsibilities for:
- `ChunkRetrievalService`
- `RetrievalMergeService`
- `MemoryContextPackService`
- `KnowledgeSearchService`
- `KnowledgeReadService`
- `RelatedKnowledgeService`
- `ChunkQueryPlanner`
- `HybridRankingService`
- `CaseShapeMatcher`
- `SupersessionPolicy`
- `RequestValidator`

remain the same as in the earlier detailed design, except that monitoring publication targets the built-in monitoring pipeline inside the same host rather than an external monitor server.

### 11.2 Additional monitoring responsibilities

#### `MonitoringSnapshotService`
Responsibilities:
1. read the recent monitor event buffer
2. group recent events into UI sections
3. return a deterministic snapshot for `/monitor/snapshot`
4. include host/server summary metadata

#### `UiEventProjector`
Responsibilities:
1. map internal monitor events into UI-appropriate summaries
2. trim payloads to a configured preview size
3. classify events into logs, operations, timings, warnings, outputs

#### `UiTrimPolicy`
Responsibilities:
1. enforce `MaxPayloadPreviewChars`
2. cap overly large strings
3. preserve valid JSON previews when trimming is possible

---

## 12. Infrastructure Classes

Implement these classes in `HarnessMcp.Infrastructure.Postgres`.

- `PostgresKnowledgeRepository`
- `NpgsqlDataSourceFactory`
- `PostgresRowMappers`
- `SqlTextLibrary`
- `ConnectionHealthProbe`
- `VectorParameterFormatter`
- `TsvectorQueryBuilder`
- `EvidenceSnippetReader`
- `LocalHttpQueryEmbeddingService`
- `BuilderApiEmbedQueryRequest`
- `BuilderApiEmbedQueryResponse`
- `NoOpQueryEmbeddingService`
- `InMemoryContextPackCache`
- `MonitorRingBuffer`
- `MonitorEventExporter`
- `MonitorEventBroadcaster`
- `RotatingFileLoggerProvider`
- `RotatingFileLogger`
- `LogFileRoller`
- `LogEventFormatter`
- `MonitorAwareLoggerProvider`
- `MonitorAwareLogger`

## 12.1 `NpgsqlDataSourceFactory`

Use `NpgsqlSlimDataSourceBuilder`.

Factory rules:
- set connection string from config
- set command timeout
- call `EnableJsonTypes()`
- do not use dynamic JSON mapping
- do not use dependency-injection helpers from `Npgsql`
- return a singleton `NpgsqlDataSource`

## 12.2 SQL families

Maintain exactly four SQL families:
1. lexical search
2. semantic search
3. item hydration
4. relation traversal

## 12.3 Lexical search strategy

Search text in this priority order:
1. `retrieval_profiles.profile_text` where `profile_type = request.QueryKind`
2. `knowledge_items.normalized_retrieval_text`
3. `knowledge_items.title`
4. `knowledge_items.summary`

Rules:
- use a CTE to precompute filtered items
- join `knowledge_scopes` only when requested scopes are present
- do not explode rows with evidence joins in the lexical candidate query
- cap lexical candidates by config (`LexicalCandidateCount`)

## 12.4 Semantic search strategy

Rules:
- search `knowledge_embeddings`
- prefer `embedding_role = request.QueryKind` when available
- fallback to `embedding_role = normalized_retrieval_text`
- use pgvector distance order in SQL
- cap semantic candidates by config (`SemanticCandidateCount`)

## 12.5 Item hydration strategy

Use dedicated projections, not one mega-join.

Hydration sequence:
1. load item base row
2. load scopes
3. load labels
4. load tags
5. load evidence/segments
6. load relations if requested

## 12.6 Relation traversal strategy

Use `knowledge_relations` joined back to `knowledge_items`.

Sort by:
1. relation strength descending if present
2. authority descending
3. updated_at descending

## 12.7 Evidence hydration strategy

Evidence must come from:
- `source_artifacts`
- `source_segments`
- `knowledge_item_segments`

Never infer snippets by re-reading raw files.

## 12.8 Monitoring infrastructure

### `MonitorRingBuffer`
Responsibilities:
- bounded thread-safe in-memory event storage
- monotonically increasing event sequence numbers
- retrieval by `after` sequence
- storage for latest event collections used by `/monitor/snapshot`

### `MonitorEventExporter`
Responsibilities:
- implement `/monitor/events`
- return batches after a sequence number
- obey default and max take limits

### `MonitorEventBroadcaster`
Responsibilities:
- broadcast monitor events to SignalR clients when realtime monitoring is enabled
- no-op when realtime monitoring is disabled

### `MonitorAwareLoggerProvider` / `MonitorAwareLogger`
Responsibilities:
- forward logs to ordinary sinks
- also publish monitor events when monitoring is enabled
- project log category/level/message into `MonitorEventDto`

---

## 13. MCP Tool and Resource Classes

Implement exactly one MCP tool class and one MCP resource class.

## 13.1 `KnowledgeQueryTools`

```csharp
[McpServerToolType]
public sealed class KnowledgeQueryTools
{
    public KnowledgeQueryTools(
        IChunkRetrievalService chunkRetrievalService,
        IRetrievalMergeService mergeService,
        IMemoryContextPackService contextPackService,
        IKnowledgeSearchService searchService,
        IKnowledgeReadService readService,
        IRelatedKnowledgeService relatedService,
        IAppInfoProvider appInfoProvider,
        IMonitorEventSink monitorEventSink)
    {
        ...
    }

    [McpServerTool]
    public ValueTask<RetrieveMemoryByChunksResponse> RetrieveMemoryByChunks(
        RetrieveMemoryByChunksRequest request,
        CancellationToken cancellationToken)
        => ...;

    [McpServerTool]
    public ValueTask<MergeRetrievalResultsResponse> MergeRetrievalResults(
        MergeRetrievalResultsRequest request,
        CancellationToken cancellationToken)
        => ...;

    [McpServerTool]
    public ValueTask<BuildMemoryContextPackResponse> BuildMemoryContextPack(
        BuildMemoryContextPackRequest request,
        CancellationToken cancellationToken)
        => ...;

    [McpServerTool]
    public ValueTask<SearchKnowledgeResponse> SearchKnowledge(
        SearchKnowledgeRequest request,
        CancellationToken cancellationToken)
        => ...;

    [McpServerTool]
    public ValueTask<GetKnowledgeItemResponse> GetKnowledgeItem(
        GetKnowledgeItemRequest request,
        CancellationToken cancellationToken)
        => ...;

    [McpServerTool]
    public ValueTask<GetRelatedKnowledgeResponse> GetRelatedKnowledge(
        GetRelatedKnowledgeRequest request,
        CancellationToken cancellationToken)
        => ...;

    [McpServerTool]
    public ServerInfoResponse GetServerInfo()
        => ...;
}
```

## 13.2 `KnowledgeResources`

Implement these resources:
- `kb://items/{id}`
- `kb://contextpacks/{taskId}`
- `kb://schemas/retrieve_memory_by_chunks_response/1.0`
- `kb://schemas/merge_retrieval_results_response/1.0`
- `kb://schemas/build_memory_context_pack_response/1.0`
- `kb://schemas/search_knowledge_response/1.0`
- `kb://schemas/get_knowledge_item_response/1.0`
- `kb://schemas/get_related_knowledge_response/1.0`
- `kb://schemas/server_info_response/1.0`

## 13.3 Registration rule

Register tools/resources using explicit generic or target-instance methods only.

Allowed:
- `WithTools<KnowledgeQueryTools>(serializerOptions)`
- `WithTools(composed.KnowledgeQueryTools, serializerOptions)`
- `WithResources<KnowledgeResources>()`
- `WithResources(composed.KnowledgeResources)`

Not allowed:
- `WithToolsFromAssembly()`
- `WithResourcesFromAssembly()`

---

## 14. Composition Root

## 14.1 `ComposedApplication`

```csharp
public sealed record ComposedApplication(
    AppConfig Config,
    NpgsqlDataSource DataSource,
    IHealthProbe HealthProbe,
    KnowledgeQueryTools KnowledgeQueryTools,
    KnowledgeResources KnowledgeResources,
    IMonitorEventSink MonitorEventSink,
    IMonitorEventExporter MonitorEventExporter,
    IMonitoringSnapshotService MonitoringSnapshotService,
    IMonitorEventBroadcaster MonitorEventBroadcaster,
    IAppInfoProvider AppInfoProvider,
    ILoggerFactory LoggerFactory);
```

## 14.2 `CompositionRoot`

```csharp
public static class CompositionRoot
{
    public static ComposedApplication Build(AppConfig config)
    {
        ...
    }
}
```

## 14.3 Required build order inside `CompositionRoot.Build`

1. validate config
2. create logger factory
3. create monitor ring buffer and event sink/exporter
4. create `NpgsqlDataSource`
5. create repository and health probe
6. create query embedding service
7. create policies/validators/normalizers
8. create ranking and matching services
9. create application services
10. create monitoring snapshot/projector/trim services
11. create cache and schema providers
12. create MCP tool/resource objects
13. create monitor broadcaster wrapper
14. return `ComposedApplication`

No other project may construct the graph.

This is also where the concrete persistence provider is selected.

For PostgreSQL v1, the composition root selects the PostgreSQL repository implementation, for example:

```csharp
IKnowledgeRepository repository = new PostgresKnowledgeRepository(...);
```

If a future provider is introduced, the composition-root selection changes there, not throughout the core.

---

## 15. Configuration Design

Use one config file:

```text
appsettings.mcp.json
```

## 15.1 Config DTOs

Implement:
- `AppConfig`
- `ServerConfig`
- `DatabaseConfig`
- `RetrievalConfig`
- `EmbeddingConfig`
- `LoggingConfig`
- `MonitoringConfig`
- `FeatureConfig`

## 15.2 Sections

Use exactly these top-level sections:
- `Server`
- `Database`
- `Retrieval`
- `Embedding`
- `Logging`
- `Monitoring`
- `Features`

## 15.3 Config fields

### `Server`
- `TransportMode`
- `HttpListenUrl`
- `Environment`
- `EnableMonitoringUi`

### `Database`
- `Host`
- `Port`
- `Database`
- `Username`
- `Password`
- `SearchSchema`
- `CommandTimeoutSeconds`

### `Retrieval`
- `DefaultTopK`
- `MaxTopK`
- `MinimumAuthority`
- `LexicalCandidateCount`
- `SemanticCandidateCount`
- `EmbeddingRole`
- `MaxQueryTextLength`
- `MaxChunkTextLength`

### `Embedding`
- `QueryEmbeddingProvider`
- `Endpoint`
- `Model`
- `TimeoutSeconds`
- `RequireCompatibilityCheck`
- `AllowLexicalFallbackOnSemanticIncompatibility`
- `AllowHashingFallback`

### `Logging`
- `Level`
- `Directory`
- `FileNamePrefix`
- `MaxFileSizeBytes`
- `MaxRetainedFiles`
- `ForwardToMonitor`

### `Monitoring`
- `EnableEventExport`
- `EnableRealtimeUi`
- `RingBufferSize`
- `EventExportDefaultTake`
- `MaxRenderedRows`
- `MaxPayloadPreviewChars`

### `Features`
- `EnableContextPackCache`
- `EnableRawDetails`
- `EnableStructureChannel`

## 15.4 Binding rules

Use configuration binding source generation.

Do not use reflection-heavy binding helpers outside what the generated binder supports.

## 15.5 Transport and monitoring behavior rules

### When `TransportMode = Http`
- create ASP.NET Core slim web app
- expose MCP over HTTP
- map health/version endpoints
- map `/monitor/events`
- if `Server.EnableMonitoringUi = true`, map `/monitor`, `/monitor/snapshot`, `/monitor/hub`
- if `Monitoring.EnableRealtimeUi = true`, broadcast via SignalR

### When `TransportMode = Stdio`
- run MCP over stdio only
- do not start a listening web server
- monitoring UI is disabled regardless of configuration
- log a warning if `EnableMonitoringUi = true` in stdio mode

---

## 16. Query Embedding Strategy

The server generates **query embeddings only**.

Corpus embeddings are assumed to already exist in `knowledge_embeddings` and to have been built by the Python embedding builder.

Implement:
- `LocalHttpQueryEmbeddingService`
- `NoOpQueryEmbeddingService`

## 16.1 Primary v1 strategy: builder-owned query embedding API

V1 semantic compatibility must be achieved by calling the Python builder's local query embedding API, not by making .NET the primary embedding implementation.

This is the required v1 path because it maximizes correctness and minimizes compatibility drift.

The server must call a **single local HTTP JSON endpoint** exposed by the builder:

```text
POST /embed-query
```

The request body is:

```json
{
  "texts": ["some query text", "another query text"]
}
```

The response body is:

```json
{
  "vectors": [[0.1, 0.2], [0.3, 0.4]],
  "provider": "sentence_transformers",
  "model_name": "...",
  "model_version": null,
  "normalize_embeddings": true,
  "dimension": 384,
  "fallback_mode": false
}
```

The response metadata is part of the compatibility contract and is intentionally returned by the same endpoint. No separate health or model-info endpoint is required for MCP task execution.

## 16.2 `LocalHttpQueryEmbeddingService` rules

`LocalHttpQueryEmbeddingService` must:
- use `HttpClient`
- call the configured `Embedding.Endpoint` which points to the builder's `POST /embed-query` endpoint
- send one or more query texts in JSON using `System.Text.Json`
- parse vectors and compatibility metadata using `System.Text.Json`
- validate response shape before returning vectors
- treat the builder API as the semantic-query embedding authority in v1
- not depend on any Python SDK, model SDK, or Agent Framework component

The service may batch multiple chunk texts in one HTTP call for efficiency, but batching must not change the semantic independence of chunk retrieval. The harness flow still treats each chunk as a separate retrieval purpose.

## 16.3 Builder API response contract used by MCP

The server must define internal response/request models matching the builder API contract, for example:
- `BuilderApiEmbedQueryRequest`
- `BuilderApiEmbedQueryResponse`

Minimum response fields required by MCP:
- `vectors`
- `provider`
- `model_name`
- `model_version`
- `normalize_embeddings`
- `dimension`
- `fallback_mode`

Validation rules:
- vector count must match request text count
- every vector must have the same length
- `dimension` must match actual vector length

## 16.4 Compatibility checks before semantic search

For semantic search, MCP must compare the builder API response metadata against the stored DB embedding metadata for the role being queried.

Required checks:
1. response vector dimension matches the actual vector length returned by the API
2. response vector dimension matches the stored DB vector dimension for the selected embedding role
3. response `model_name` / `model_version` are compatible with the builder metadata used to create the stored DB embeddings
4. response `normalize_embeddings` matches the normalization regime used for the stored DB embeddings
5. response `fallback_mode` is acceptable for the stored DB embeddings

If `Embedding.RequireCompatibilityCheck = true`, these checks are mandatory and semantic search must not proceed when they fail.

If the builder API reports hashing fallback mode and `Embedding.AllowHashingFallback = false`, semantic search must be disabled for that request.

## 16.5 Behavior when semantic compatibility fails

If semantic compatibility fails, the server must follow `Embedding.AllowLexicalFallbackOnSemanticIncompatibility`:

- when `true`, degrade to lexical-only retrieval and record the reason in diagnostics
- when `false`, fail fast with a clear operator-facing error

The harness-controlled flow must not silently use incompatible semantic vectors because that would undermine retrieval accuracy.

## 16.6 Behavior in harness-controlled flow

For the harness-primary flow:
1. `retrieve_memory_by_chunks` builds a `SearchKnowledgeRequest` per chunk
2. each chunk may use the builder-owned query embedding API to obtain a semantic query vector
3. semantic search and lexical search remain route-aware and chunk-specific
4. if semantic search is unavailable or incompatible for a chunk, that chunk must explicitly fall back or fail according to config
5. merge and context-pack assembly must preserve diagnostics so the harness can tell whether semantic search was active or degraded

This is required for correctness and accuracy because the harness relies on MCP to own retrieval quality, not just tool naming.

## 16.7 Similar-case route rule

`QueryKind.SimilarCase` must not rely on semantic vector similarity alone.

For similar-case retrieval:
- the builder API may still provide the semantic query vector
- but final retrieval quality must also use structured case-shape scoring over `case_shapes`
- semantic search must remain a supporting signal, not the only signal

This preserves the dedicated similar-case semantics required by the harness design.

## 16.8 Config semantics for builder API path

When `Embedding.QueryEmbeddingProvider = LocalHttp`, the following fields are used:
- `Embedding.Endpoint`: full builder API URL for `POST /embed-query`
- `Embedding.Model`: optional operator-facing expected model hint used for validation/diagnostics
- `Embedding.TimeoutSeconds`: HTTP timeout
- `Embedding.RequireCompatibilityCheck`: require metadata compatibility before semantic search
- `Embedding.AllowLexicalFallbackOnSemanticIncompatibility`: allow lexical-only fallback when semantic search is unavailable/incompatible
- `Embedding.AllowHashingFallback`: whether builder hashing fallback vectors are allowed for semantic search

When `Embedding.QueryEmbeddingProvider = NoOp`, `NoOpQueryEmbeddingService` returns an empty vector sentinel and the semantic repository path must be skipped.

## 16.9 Accuracy-first rule

For v1, correctness and compatibility are more important than implementing a second .NET-native embedding path.

Therefore:
- the primary semantic-query embedding path is the builder-owned API
- the design does not require .NET to independently reproduce SentenceTransformer behavior in v1
- if a future .NET-native path is added, it must not replace the builder-API path until parity is explicitly proven

## 17. Host Design

## 17.1 Single host responsibilities

`HarnessMcp.Host.Aot` is responsible for:
- loading config
- calling `CompositionRoot.Build`
- choosing runtime mode
- exposing HTTP MCP transport in HTTP mode
- exposing stdio MCP transport in stdio mode
- mapping health/version endpoints in HTTP mode
- exposing monitor event export endpoint in HTTP mode
- optionally serving the monitoring page in HTTP mode
- optionally broadcasting monitor events via SignalR in HTTP mode

## 17.2 HTTP mode endpoints

When `TransportMode = Http`, map:
- `MapMcp()` transport routes
- `GET /healthz`
- `GET /readyz`
- `GET /version`
- `GET /monitor/events?after={n}&take={m}`
- `GET /monitor/snapshot` when monitoring UI is enabled
- `GET /monitor` when monitoring UI is enabled
- `MapHub<MonitoringHub>("/monitor/hub")` when monitoring UI and realtime are enabled

## 17.3 Monitoring page requirements

The page must be a compact debugging/inspection surface.

Use a static HTML page with plain JavaScript and CSS.
Do not use React/Vue/Angular.
Do not introduce Node-based frontend tooling.

The page must show:
1. server summary
2. live log stream
3. recent MCP operations
4. retrieval operations
5. SQL/embedding timings
6. generated output previews
7. warnings/errors

### Page load flow
1. call `/monitor/snapshot`
2. render initial state
3. connect SignalR if enabled
4. append incoming events live
5. pause auto-scroll when user scrolls away from bottom

### Browser limits
- render at most `Monitoring.MaxRenderedRows`
- trim large payloads using `UiTrimPolicy`

## 17.4 Stdio mode responsibilities

When `TransportMode = Stdio`:
- run MCP over stdio
- ensure logs go to stderr only
- do not start the web server
- do not map monitor routes
- do not start SignalR hub

---

## 18. Logging and Monitoring Design

## 18.1 Logging abstraction

Use `Microsoft.Extensions.Logging` abstractions only.

Implement:
- `RotatingFileLoggerProvider`
- `RotatingFileLogger`
- `LogFileRoller`
- `LogEventFormatter`
- `MonitorAwareLoggerProvider`
- `MonitorAwareLogger`
- `UiLogProjector`

## 18.2 Rotation rules

Rotate by file size only.

Defaults:
- `MaxFileSizeBytes = 10 * 1024 * 1024`
- `MaxRetainedFiles = 10`

## 18.3 Monitor event types

Implement monitor events for:
- request start
- request success
- request failure
- SQL query timing
- embedding timing
- merge timing
- context pack build completion
- health probe failures
- general log messages
- warnings/errors

## 18.4 SignalR usage rule

SignalR exists only for:
- realtime monitoring of the web dashboard
- realtime log and operation tracking

SignalR is not used:
- as a separate server
- as a transport for MCP
- as a second application host

---

## 19. Algorithm Details

## 19.1 Chunk-to-query mapping algorithm

For every `RetrieveMemoryByChunksRequest`:
1. preserve chunk input order
2. normalize chunk text and scopes
3. convert chunk to `SearchKnowledgeRequest`
4. keep type-specific retrieval class filters
5. query per chunk independently
6. bucket search results by `RetrievalClass`

Never merge chunk retrieval before per-chunk search completes.

### 19.1.1 Semantic-compatibility behavior during chunk retrieval

For each chunk retrieval:
- build the chunk-specific search request
- obtain semantic query vectors through the builder-owned query embedding API when semantic search is enabled
- validate compatibility before semantic search is used
- if semantic search is disabled or incompatible, degrade or fail according to embedding config
- record the outcome in chunk diagnostics/notes so the harness can see whether semantic retrieval was active

## 19.2 Merge algorithm

For each retrieval class bucket:
1. flatten all chunk candidates in that bucket
2. group by `KnowledgeItemId`
3. choose canonical representative by:
   - highest authority
   - highest final score
   - newest `updated_at`
4. union support provenance (`SupportedByChunkIds`, `SupportedByChunkTypes`, `SupportedByQueryKinds`)
5. create merge rationale strings
6. output stable class bucket ordering

## 19.3 Context pack assembly algorithm

1. accept retrieved + merged responses
2. ensure task IDs match
3. preserve final section order:
   - decisions
   - constraints
   - best practices
   - anti-patterns
   - similar cases
   - references
   - structures
4. generate warnings for:
   - empty decisions section
   - empty constraints section when request had hard constraints
   - low-support similar cases
   - no results for a chunk
5. include timing diagnostics

## 19.4 Scope scoring algorithm

Use additive normalized scope scoring:
- domain match = `+0.25`
- module match = `+0.20`
- feature match = `+0.15`
- layer overlap = up to `+0.15`
- concern overlap = up to `+0.10`
- repo/service/symbol overlap = up to `+0.15`

Clamp to `1.0`.

## 19.5 Authority scoring algorithm

Normalize authority levels:
- Draft = `0.20`
- Observed = `0.40`
- Reviewed = `0.60`
- Approved = `0.80`
- Canonical = `1.00`

## 19.6 Similar-case scoring algorithm

For `QueryKind.SimilarCase`:
- task type exact = `0.30`
- feature shape exact = `0.30`
- engine change flag exact = `0.15`
- likely layer overlap = `0.15`
- risk signal overlap = `0.10`

Clamp to `1.0`.

---

## 20. Testing Strategy

## 20.1 Unit tests

Create unit tests for:
- `HybridRankingService`
- `AuthorityPolicy`
- `SupersessionPolicy`
- `ScopeNormalizer`
- `RequestValidator`
- `ChunkQueryPlanner`
- `RetrievalMergeService`
- `ContextPackAssembler`
- `CaseShapeMatcher`
- `UiTrimPolicy`
- `MonitoringSnapshotService`
- `MonitorRingBuffer`

## 20.2 Integration tests

Create integration tests for:
- `PostgresKnowledgeRepository.SearchLexicalAsync`
- `PostgresKnowledgeRepository.SearchSemanticAsync`
- `PostgresKnowledgeRepository.GetKnowledgeItemAsync`
- `PostgresKnowledgeRepository.GetRelatedKnowledgeAsync`
- HTTP MCP host end-to-end against a seeded test PostgreSQL database
- stdio MCP host end-to-end tool invocation using the same single host project in stdio mode
- monitor event export endpoint
- monitoring snapshot endpoint
- monitor page returns HTML when enabled
- builder query-embedding API integration path for semantic search
- compatibility failure path degrades to lexical-only or fails according to config
- harness-primary retrieval flow reports semantic degradation accurately in diagnostics

## 20.3 Contract/schema tests

Add tests that ensure:
- JSON serialization uses source-generated context
- enums serialize deterministically
- schema version fields are populated
- MCP tool names match the required names exactly
- monitor DTOs serialize deterministically

## 20.4 AOT build gate

CI must publish the single host:

```text
dotnet publish src/HarnessMcp.Host.Aot/HarnessMcp.Host.Aot.csproj -c Release -r win-x64
```

Treat any unresolved AOT warning as a blocker until understood.

---

## 21. Implementation Order

Implement in this exact order:
1. `HarnessMcp.Contracts`
2. `HarnessMcp.Core`
3. `HarnessMcp.Infrastructure.Postgres`
4. `HarnessMcp.Transport.Mcp`
5. `HarnessMcp.Host.Aot`
6. optional `HarnessMcp.AgentClient`
7. tests

This order validates the core retrieval stack before transport and monitoring finish-out.

---

## 22. Final Do/Don’t Rules

### Do
- use official MCP C# SDK
- use explicit MCP tool/resource registration
- use .NET 10
- use Native AOT for the single host
- use Minimal APIs and `CreateSlimBuilder` in HTTP mode
- use JSON/config source generation
- use `Npgsql` direct SQL and `NpgsqlSlimDataSourceBuilder`
- use the harness-primary tool trio
- retain `search_knowledge` and read tools as secondary tools
- map explicitly to the revised database tables
- keep SignalR only for live monitoring/tracking inside the single host
- keep monitoring UI configurable on/off
- keep all database access behind repository interfaces / persistence abstractions
- let services call repository abstractions rather than provider-specific APIs
- use the builder-owned query embedding API as the primary semantic-query path in v1

### Don’t
- do not implement the MCP protocol manually
- do not use Agent Framework inside the MCP server core
- do not use `WithToolsFromAssembly` or `WithResourcesFromAssembly`
- do not use EF Core in the host
- do not use third-party DI containers
- do not collapse chunk retrieval, merge, and context pack assembly into one opaque method internally
- do not replace `retrieval_class` with dynamic labels
- do not infer evidence snippets outside the database provenance tables
- do not add write/shell/database-console tools in v1
- do not create separate runtime host executables for HTTP, stdio, or monitoring
- do not let core services call `Npgsql` directly
- do not let MCP tools/resources query the database directly
- do not let host code depend on SQL details
- do not leak PostgreSQL-specific details into public contracts
- do not add extra builder-management APIs to satisfy semantic-query embedding in v1
- do not silently use incompatible semantic vectors

---

## 23. Final Decision Summary

The final revised implementation design is:
- **one single runtime host executable**: `HarnessMcp.Host.Aot`
- transport mode is selected at startup: `Http` or `Stdio`
- the host keeps Native AOT as a first-class requirement
- the monitoring web UI is built into the same host and can be turned on/off in config
- SignalR is used only for live tracking and realtime monitoring of the log/dashboard page
- the retrieval/database/core tool design remains aligned with the earlier detailed design
- database access is explicitly behind a repository-pattern API boundary
- PostgreSQL-specific implementation remains in infrastructure and is selected in the composition root
- semantic query compatibility with the Python-built embedding database is achieved through the builder-owned query embedding API
- switching database/provider should primarily be an infrastructure + composition change, not a core rewrite
- v1 remains read-only and deterministic

This is the design that should now drive implementation.


## 24. Anti-Refusal Execution Rules
- Large scope is not a valid reason to stop.
- Implement best complete migration in one round.
- If external infra unavailable, still finish code and tests.
- Internal projection models may contain fields like updated_at; public DTOs must remain exact.
