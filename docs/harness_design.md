# Harness Design for MCP-Guided Long-Term-Memory Planning

## 1. Purpose

This design defines the **client-side harness** that sits in front of a planning model and a ready MCP server. Its job is not to execute the user task. Its job is to:

1. accept a raw user task description,
2. turn it into structured planning inputs,
3. retrieve relevant long-term memory from the MCP server,
4. synthesize a high-quality step-by-step execution plan,
5. produce a **worker packet** that a human can paste into another agent session for execution.

This aligns with the uploaded supervisor design in which the **harness is the control plane**, the **MCP server owns retrieval accuracy**, the harness must not query memory with raw long task text, and the harness must produce the execution plan while the worker stays execution-only. It also aligns with the MCP server design in which the server exposes `retrieve_memory_by_chunks`, `merge_retrieval_results`, and `build_memory_context_pack`, and where `build_memory_context_pack` explicitly does **not** create the execution plan. fileciteturn2file0 fileciteturn1file6turn1file9

---

## 2. Final Runtime Topology

```text
User task description
    ↓
Harness CLI / Harness library
    ├─ model pass 1: RequirementIntent extraction
    ├─ deterministic chunk compiler
    ├─ chunk quality gates
    ├─ MCP preflight + memory retrieval pipeline
    │    ├─ get_server_info
    │    ├─ retrieve_memory_by_chunks
    │    ├─ merge_retrieval_results
    │    ├─ build_memory_context_pack
    │    └─ optional targeted fallback inspection
    ├─ model pass 2: execution plan synthesis
    ├─ deterministic worker-packet builder
    └─ artifact writer
             ↓
Human copies worker packet
             ↓
Worker agent executes plan
```

### Hard architectural rules

1. The harness is a **separate client-side runtime**, not part of the MCP host.
2. The harness must talk to the **running MCP server**, not call `KnowledgeQueryTools` in-process.
3. The harness must create the execution plan; the MCP server must not.
4. The worker packet must be execution-oriented and must **forbid worker-side memory retrieval**.
5. The harness must preserve intermediate artifacts for traceability.

---

## 3. Budget-Saving V1 Scope

To keep implementation focused and cheap, v1 should make these explicit scope decisions:

1. **Transport**: support **HTTP MCP** only in the first implementation.
2. **Dispatch**: manual human copy/paste to the worker remains the only dispatch mode.
3. **Memory write-back**: not supported.
4. **Autonomous multi-agent loop**: not supported.
5. **Interactive clarification loop**: not supported in v1. The harness records ambiguities instead of entering a back-and-forth loop.
6. **Tool use by the planner model**: not supported. The harness itself calls MCP deterministically.
7. **Single planning model provider in v1**: use an **OpenAI-compatible HTTP chat endpoint** through a thin `HttpClient` adapter.
8. **Tests**: no live LLM calls and no live database calls. Use fakes and a local fake HTTP handler/server.

This preserves the core architecture from the uploaded designs while avoiding expensive agent-tool autonomy and avoiding a second supervisory runtime. fileciteturn2file0

---

## 4. Repository Placement

Implement the harness inside the existing repo using the client-side project slot already reserved by the MCP server design.

### Final solution layout after harness work

```text
src/
  HarnessMcp.Contracts/                  # existing, reused
  HarnessMcp.Core/                       # existing, reused by server only
  HarnessMcp.Infrastructure.Postgres/    # existing
  HarnessMcp.Transport.Mcp/              # existing
  HarnessMcp.Host.Aot/                   # existing MCP server host
  HarnessMcp.AgentClient/                # NEW harness implementation
tests/
  HarnessMcp.Tests.Unit/                 # existing
  HarnessMcp.Tests.Integration/          # existing
  HarnessMcp.Tests.Contracts/            # existing
  HarnessMcp.AgentClient.Tests/          # NEW harness tests
```

The MCP server design already reserves `HarnessMcp.AgentClient` as the place for optional client-side orchestration utilities, so the harness should be implemented there rather than inventing a parallel unrelated project tree. fileciteturn2file2

---

## 5. Reuse Rules

### Reuse without duplication

Reuse these existing server contracts directly from `HarnessMcp.Contracts`:

- `RetrieveMemoryByChunksRequest`
- `RetrieveMemoryByChunksResponse`
- `MergeRetrievalResultsRequest`
- `MergeRetrievalResultsResponse`
- `BuildMemoryContextPackRequest`
- `BuildMemoryContextPackResponse`
- `SearchKnowledgeRequest`
- `SearchKnowledgeResponse`
- `GetKnowledgeItemRequest`
- `GetKnowledgeItemResponse`
- `GetRelatedKnowledgeRequest`
- `GetRelatedKnowledgeResponse`
- `ServerInfoResponse`
- supporting enums such as `ChunkType`, `QueryKind`, `AuthorityLevel`, `RetrievalClass`

The harness design from the uploaded document defines `RequirementIntent` and `RetrievalChunkSet` as mandatory intermediate artifacts before MCP retrieval. The harness should therefore implement its own richer planning-side versions of these artifacts, then map them into the existing MCP DTOs. fileciteturn2file0turn2file1

### Do not reuse server internals directly

The harness must not reference or call:

- `KnowledgeQueryTools` directly,
- `CompositionRoot` from the server,
- PostgreSQL repositories,
- core server services as a transport bypass.

The harness is a client of the MCP server, not an in-process shortcut.

---

## 6. Harness Subsystems

## 6.1 CLI / entrypoint subsystem

Project: `src/HarnessMcp.AgentClient`

Primary entrypoint:

- `Program.cs`

Command:

- `plan-task`

Arguments:

- `--task-file <path>` or `--task-text <text>` (exactly one required)
- `--output-dir <path>` required
- `--mcp-base-url <url>` required
- `--model-base-url <url>` required
- `--model-name <name>` required
- `--api-key-env <ENV_NAME>` optional, default `OPENAI_API_KEY`
- `--session-id <id>` optional
- `--project <text>` optional
- `--domain <text>` optional
- `--max-items-per-chunk <int>` optional, default `5`
- `--minimum-authority <Draft|Observed|Reviewed|Approved|Canonical>` optional, default `Reviewed`
- `--emit-intermediates true|false` optional, default `true`

Behavior:

1. load input task,
2. run the full planning session,
3. write artifacts to output dir,
4. print final artifact paths,
5. return non-zero exit code on validation or transport failure.

---

## 6.2 Planning model subsystem

Interfaces:

```csharp
public interface IPlanningModelClient
{
    Task<string> CompleteJsonAsync(
        string systemPrompt,
        string userPrompt,
        CancellationToken cancellationToken);
}
```

Implementation:

- `OpenAiCompatiblePlanningModelClient`

Use plain `HttpClient`.
Do not add SDK-heavy dependencies for the model provider.
Use a simple OpenAI-compatible `/v1/chat/completions` style request with:

- `model`
- `temperature = 0`
- `stream = false`
- `messages = [{system}, {user}]`

The harness expects the assistant content to be a single JSON object. Parse and validate it. Do not rely on vendor-specific tool calling or vendor-specific server-side schema enforcement.

### Why this choice

- cheap to implement,
- testable with a fake handler,
- no dependency on provider-specific tool semantics,
- enough for two JSON-only passes.

---

## 6.3 Requirement interpretation subsystem

Classes:

- `RequirementInterpretationService`
- `RequirementIntentPromptBuilder`
- `RequirementIntentParser`
- `RequirementIntentValidator`

Responsibilities:

1. send the raw task to the planning model,
2. require a strict JSON `RequirementIntent` response,
3. validate required fields,
4. normalize blanks and lists,
5. assign session/task ids if missing.

### Planning-side `RequirementIntent`

```csharp
public sealed record RequirementIntent(
    string SessionId,
    string TaskId,
    string RawTask,
    string TaskType,
    string? Domain,
    string? Module,
    string? Feature,
    string Goal,
    IReadOnlyList<string> RequestedOperations,
    IReadOnlyList<string> HardConstraints,
    IReadOnlyList<string> SoftConstraints,
    IReadOnlyList<string> RiskSignals,
    IReadOnlyList<string> CandidateLayers,
    IReadOnlyList<string> RetrievalFocuses,
    IReadOnlyList<string> Ambiguities,
    string Complexity);
```

Validation rules:

- `TaskType`, `Goal`, `Complexity` required
- `Complexity` limited to `low|medium|high`
- `HardConstraints`, `RiskSignals`, `RequestedOperations` never null
- ambiguous or unknown fields become explicit ambiguity list entries instead of silent omission

---

## 6.4 Deterministic chunking subsystem

This is the most important control-plane subsystem. The uploaded harness design explicitly requires the harness to decompose requirements into purpose-specific chunks instead of querying memory with the raw requirement, and it defines the fixed chunk taxonomy and generation rules. fileciteturn2file0turn2file1

Classes:

- `RetrievalChunkCompiler`
- `ScopeInferenceService`
- `ChunkCoverageAnalyzer`
- `ChunkTextNormalizer`
- `ChunkIdFactory`

### Planning-side `RetrievalChunkSet`

```csharp
public sealed record RetrievalChunkSet(
    string SessionId,
    string TaskId,
    string Complexity,
    IReadOnlyList<RetrievalChunk> Chunks,
    ChunkCoverageReport CoverageReport);

public sealed record RetrievalChunk(
    string ChunkId,
    ChunkType ChunkType,
    string? Text,
    PlannedChunkScopes Scopes,
    SimilarCaseSignature? SimilarCase);

public sealed record PlannedChunkScopes(
    string? Domain,
    string? Module,
    IReadOnlyList<string> Features,
    IReadOnlyList<string> Layers,
    IReadOnlyList<string> Concerns,
    IReadOnlyList<string> Repos,
    IReadOnlyList<string> Services,
    IReadOnlyList<string> Symbols);

public sealed record SimilarCaseSignature(
    string TaskType,
    string FeatureShape,
    bool EngineChangeAllowed,
    IReadOnlyList<string> LikelyLayers,
    IReadOnlyList<string> RiskSignals,
    string? Complexity);
```

### Deterministic chunk algorithm

Implement exactly this order:

1. **core_task** from the main implementation target.
2. **constraint** chunks from every explicit hard boundary.
3. **risk** chunks from regression/failure cues.
4. **pattern** chunks from implementation-style wording.
5. **similar_case** chunk from structural signature when complexity is medium/high.

### Chunk rules

- one chunk = one retrieval purpose,
- compact text only,
- structured scope always present,
- no free-form chunk types,
- ambiguity preserved,
- no chunk may mix feature + constraint + risk + pattern in one text field.

### Example mapping

If user asks:

> Add year switching via ajax, do not change engine logic, and do not reintroduce placement issues.

Compiler must emit at least:

- `core_task`: `year switching for yearly weighted card`
- `constraint`: `engine logic must not change`
- `risk`: `avoid recurrence of previous placement inconsistency caused by ui inference`
- `pattern`: `ajax refresh with explicit loading state and no full reload`
- `similar_case`: structural signature object

---

## 6.5 Chunk quality-gate subsystem

Classes:

- `ChunkQualityGate`
- `ChunkQualityReportBuilder`

### `ChunkQualityReport`

```csharp
public sealed record ChunkQualityReport(
    bool IsValid,
    bool HasCoreTask,
    bool HasConstraint,
    bool HasRisk,
    bool HasPattern,
    bool HasSimilarCase,
    IReadOnlyList<string> Errors,
    IReadOnlyList<string> Warnings);
```

### Mandatory gates

1. schema validity,
2. mandatory coverage,
3. compactness,
4. purity,
5. scope completeness,
6. ambiguity preservation.

### Enforcement

- If the report is invalid, the harness stops.
- Do not silently “fix” an invalid chunk set by guessing.
- Return the errors in `planning-session.json` and console output.

---

## 6.6 MCP transport subsystem

Interfaces:

```csharp
public interface IMcpToolClient
{
    Task<ServerInfoResponse> GetServerInfoAsync(CancellationToken cancellationToken);

    Task<RetrieveMemoryByChunksResponse> RetrieveMemoryByChunksAsync(
        RetrieveMemoryByChunksRequest request,
        CancellationToken cancellationToken);

    Task<MergeRetrievalResultsResponse> MergeRetrievalResultsAsync(
        MergeRetrievalResultsRequest request,
        CancellationToken cancellationToken);

    Task<BuildMemoryContextPackResponse> BuildMemoryContextPackAsync(
        BuildMemoryContextPackRequest request,
        CancellationToken cancellationToken);

    Task<SearchKnowledgeResponse> SearchKnowledgeAsync(
        SearchKnowledgeRequest request,
        CancellationToken cancellationToken);

    Task<GetKnowledgeItemResponse> GetKnowledgeItemAsync(
        GetKnowledgeItemRequest request,
        CancellationToken cancellationToken);

    Task<GetRelatedKnowledgeResponse> GetRelatedKnowledgeAsync(
        GetRelatedKnowledgeRequest request,
        CancellationToken cancellationToken);
}
```

Implementation:

- `HttpMcpToolClient`

### Implementation rule

The client must call the running MCP server over HTTP. It must not create an in-process server object graph.

### Preflight rules

Before planning starts, call `get_server_info` and verify:

- the server is reachable,
- the schema version is non-empty,
- `RetrieveMemoryByChunks`, `MergeRetrievalResults`, and `BuildMemoryContextPack` are enabled,
- `SearchKnowledge`, `GetKnowledgeItem`, and `GetRelatedKnowledge` are also enabled for fallback inspection.

The MCP server design already defines those as the public v1 tool surface and says the harness sequence must be the three primary memory tools in that order. fileciteturn1file7turn2file2

---

## 6.7 Memory retrieval pipeline subsystem

Classes:

- `MemoryRetrievalOrchestrator`
- `McpRequestMapper`
- `FallbackRetrievalPlanner`
- `MemoryEvidenceHydrator`
- `MemoryBundleAssembler`

### Primary path

1. map planning `RequirementIntent` + `RetrievalChunkSet` into `RetrieveMemoryByChunksRequest`
2. call `retrieve_memory_by_chunks`
3. call `merge_retrieval_results`
4. call `build_memory_context_pack`

That exact order is required by the uploaded designs. fileciteturn2file0turn2file2

### Search profile defaults

Use these defaults unless overridden by CLI options:

- `ActiveOnly = true`
- `MinimumAuthority = Reviewed`
- `MaxItemsPerChunk = 5`
- `RequireTypeSeparation = true`

### Fallback augmentation rules

The harness should remain deterministic and cheap. Only do targeted fallback retrieval when needed.

#### Rule A — missing similar cases
If task complexity is `medium` or `high` and `Merged.SimilarCases` is empty:

- call `search_knowledge` with `QueryKind = SimilarCase`
- query text built from the similar-case signature
- `TopK = 3`
- hydrate top 3 with `get_knowledge_item`

#### Rule B — missing constraints
If hard constraints exist in `RequirementIntent` but `Merged.Constraints` is empty:

- for each hard constraint, call `search_knowledge` with `QueryKind = Constraint`
- `TopK = 2`

#### Rule C — missing anti-patterns for risk-heavy tasks
If `RiskSignals.Count > 0` and `Merged.AntiPatterns` is empty:

- call `search_knowledge` with `QueryKind = Risk`
- build query from joined risk cues
- `TopK = 3`

#### Rule D — evidence hydration
For the top items finally passed into plan synthesis, hydrate details with `get_knowledge_item`:

- top 3 decisions
- top 3 constraints
- top 3 best practices
- top 3 anti-patterns
- top 3 similar cases

Hydration is for better rationale and for better worker packet references.

### `PlanningMemoryBundle`

```csharp
public sealed record PlanningMemoryBundle(
    RetrieveMemoryByChunksResponse Retrieved,
    MergeRetrievalResultsResponse Merged,
    BuildMemoryContextPackResponse ContextPack,
    IReadOnlyList<GetKnowledgeItemResponse> HydratedItems,
    IReadOnlyList<SearchKnowledgeResponse> FallbackSearches,
    IReadOnlyList<string> Diagnostics,
    bool UsedFallbackSearches);
```

---

## 6.8 Planning-context summarization subsystem

Classes:

- `PlanningContextSummarizer`
- `MemorySectionRenderer`

This subsystem converts `PlanningMemoryBundle` into a deterministic compact text summary for the plan-generation prompt.

### Summary rules

1. preserve section buckets:
   - decisions
   - constraints
   - best practices
   - anti-patterns
   - similar cases
   - warnings
2. include `knowledge_item_id` references when available,
3. keep snippets short,
4. include only final selected items,
5. explicitly state when a section is empty,
6. include whether fallbacks were used.

The summary must not dump raw MCP JSON into the model prompt.

---

## 6.9 Plan synthesis subsystem

Classes:

- `ExecutionPlanService`
- `ExecutionPlanPromptBuilder`
- `ExecutionPlanParser`
- `ExecutionPlanValidator`

### Planning-side `ExecutionPlan`

```csharp
public sealed record ExecutionPlan(
    string SessionId,
    string TaskId,
    string Objective,
    IReadOnlyList<string> Assumptions,
    IReadOnlyList<string> HardConstraints,
    IReadOnlyList<string> AntiPatternsToAvoid,
    IReadOnlyList<ExecutionStep> Steps,
    IReadOnlyList<string> ValidationChecks,
    IReadOnlyList<string> Deliverables,
    IReadOnlyList<string> OpenQuestions);

public sealed record ExecutionStep(
    int StepNumber,
    string Title,
    string Purpose,
    IReadOnlyList<string> Inputs,
    IReadOnlyList<string> Actions,
    IReadOnlyList<string> Outputs,
    IReadOnlyList<string> AcceptanceChecks,
    IReadOnlyList<string> SupportingMemoryIds,
    IReadOnlyList<string> Notes);
```

### Plan-generation rules

The model is allowed to synthesize the plan, but the harness controls the shape.

The prompt must require:

1. plan generation only from:
   - raw task
   - requirement intent
   - retrieval chunk set
   - planning memory summary
2. no new requirements invented,
3. constraints preserved verbatim,
4. anti-patterns preserved explicitly,
5. steps ordered for actual execution,
6. every step must include outputs and acceptance checks,
7. steps must cite memory ids where memory influenced the step,
8. the plan must be execution-ready for another agent,
9. the plan must not ask the worker to retrieve memory again.

### Plan validator rules

Reject if:

- no steps,
- any step missing actions or acceptance checks,
- plan drops any hard constraint,
- plan omits anti-patterns while memory bundle contains them,
- step numbers not consecutive starting from 1,
- output sections null,
- worker-side retrieval is suggested.

---

## 6.10 Worker packet subsystem

Classes:

- `WorkerPacketBuilder`
- `WorkerPacketMarkdownRenderer`

### `WorkerExecutionPacket`

```csharp
public sealed record WorkerExecutionPacket(
    string SessionId,
    string TaskId,
    string Objective,
    IReadOnlyList<string> AllowedScope,
    IReadOnlyList<string> ForbiddenActions,
    IReadOnlyList<string> HardConstraints,
    IReadOnlyList<string> KeyMemory,
    IReadOnlyList<ExecutionStep> Steps,
    IReadOnlyList<string> RequiredOutputSections);
```

### Packet rules

The packet must be concise enough to paste into another agent, but complete enough that the worker does not need upstream context.

It must contain:

1. objective,
2. exact constraints,
3. explicit forbidden actions,
4. distilled key memory bullets,
5. ordered steps,
6. required result format.

### Required worker prohibitions

Always include these lines in the worker packet:

- do not retrieve long-term memory independently,
- do not reinterpret the task at architecture level,
- do not expand scope beyond the listed steps,
- do not change forbidden layers/components,
- if blocked by ambiguity, state the block instead of inventing behavior.

---

## 6.11 Artifact-writing subsystem

Classes:

- `PlanningArtifactWriter`
- `ArtifactPathBuilder`

Artifacts written per run:

```text
<output-dir>/
  00-session.json
  01-raw-task.txt
  02-requirement-intent.json
  03-retrieval-chunks.json
  04-chunk-quality-report.json
  05-retrieve-memory-by-chunks.json
  06-merge-retrieval-results.json
  07-build-memory-context-pack.json
  08-planning-memory-summary.md
  09-execution-plan.json
  10-execution-plan.md
  11-worker-packet.md
```

If `--emit-intermediates false`, still always emit:

- `00-session.json`
- `10-execution-plan.md`
- `11-worker-packet.md`

---

## 7. End-to-End Harness Algorithm

1. Read task text.
2. Create `sessionId` and `taskId`.
3. Preflight MCP with `get_server_info`.
4. Call planning model pass 1 to get `RequirementIntent`.
5. Validate `RequirementIntent`.
6. Compile deterministic `RetrievalChunkSet`.
7. Run `ChunkQualityGate`.
8. Map to `RetrieveMemoryByChunksRequest`.
9. Call `retrieve_memory_by_chunks`.
10. Call `merge_retrieval_results`.
11. Call `build_memory_context_pack`.
12. Run targeted fallback searches only when rules say so.
13. Hydrate final selected items with `get_knowledge_item`.
14. Build `PlanningMemoryBundle`.
15. Render deterministic memory summary.
16. Call planning model pass 2 to get `ExecutionPlan`.
17. Validate `ExecutionPlan`.
18. Build `WorkerExecutionPacket` deterministically.
19. Render markdown plan and worker packet.
20. Write artifacts.
21. Print final output paths.

---

## 8. Prompt Design

The harness uses exactly **two** model prompts in v1.

### Prompt 1 — Requirement intent extraction

Input:

- raw task text
- optional project/domain CLI metadata

Output:

- strict `RequirementIntent` JSON only

### Prompt 2 — Execution plan synthesis

Input:

- raw task text
- validated `RequirementIntent`
- validated `RetrievalChunkSet`
- compact memory summary
- explicit schema for `ExecutionPlan`

Output:

- strict `ExecutionPlan` JSON only

### Explicitly not allowed in v1

- model tool calling,
- model-driven memory retrieval,
- conversational clarification loop,
- chain of arbitrary model steps.

This keeps the harness in control and matches the uploaded supervisor design where the skill/harness defines the mandatory procedure. fileciteturn2file0turn2file1

---

## 9. Failure Handling

### Hard failure cases

Fail the run if:

- MCP preflight fails,
- the server does not advertise required tools,
- requirement intent JSON is invalid,
- chunk quality report is invalid,
- primary MCP pipeline fails,
- execution plan JSON is invalid,
- plan validator fails.

### Soft warnings

Continue with warnings if:

- fallback searches were required,
- some memory sections remained empty,
- similarities were weak,
- hydrated evidence was unavailable for some items.

Warnings must be written into:

- `00-session.json`
- `10-execution-plan.md`
- `11-worker-packet.md` only if relevant to execution.

---

## 10. Observability

`00-session.json` must include:

```json
{
  "sessionId": "...",
  "taskId": "...",
  "startedUtc": "...",
  "endedUtc": "...",
  "mcpBaseUrl": "...",
  "modelBaseUrl": "...",
  "modelName": "...",
  "usedFallbackSearches": true,
  "warnings": [],
  "errors": [],
  "artifactPaths": {}
}
```

Do not invent a database or telemetry pipeline for the harness in v1.
Use file artifacts only.

---

## 11. Test Design

Add `tests/HarnessMcp.AgentClient.Tests`.
Use xUnit and FluentAssertions, consistent with the server repo. The server design explicitly standardizes xUnit and FluentAssertions. fileciteturn1file6turn2file2

### Mandatory tests

#### Unit tests

1. `RequirementIntentValidatorTests`
   - rejects missing goal
   - rejects invalid complexity
   - preserves ambiguities

2. `RetrievalChunkCompilerTests`
   - generates expected chunk types for mixed requirements
   - generates similar-case chunk for medium/high complexity
   - never mixes multiple purposes into one chunk

3. `ChunkQualityGateTests`
   - fails when hard constraint exists but no constraint chunk was emitted
   - fails when chunk text is too long
   - fails when chunk purpose mixing is detected

4. `McpRequestMapperTests`
   - maps planning chunks into MCP DTOs correctly

5. `ExecutionPlanValidatorTests`
   - rejects plan missing constraints
   - rejects plan missing acceptance checks
   - rejects plan that asks worker to retrieve memory

6. `WorkerPacketBuilderTests`
   - includes forbidden actions
   - includes required output sections
   - includes key memory bullets

#### Integration-style tests with fakes

7. `PlanningSessionRunnerTests`
   - happy path with fake model + fake MCP client
   - fallback similar-case search path
   - missing constraints fallback path
   - artifact emission path

8. `OpenAiCompatiblePlanningModelClientTests`
   - parses JSON content correctly using a fake HTTP handler
   - fails cleanly on non-JSON assistant output

9. `HttpMcpToolClientTests`
   - verifies tool-call request/response mapping against a fake HTTP server/handler

### Test rule

No live LLM, no live MCP server, no live DB in automated tests.

---

## 12. Implementation Order

### Phase 1

- add `HarnessMcp.AgentClient`
- add CLI options/config
- add planning-side contracts
- add requirement intent extraction
- add deterministic chunk compiler and quality gate

### Phase 2

- add `IMcpToolClient` and `HttpMcpToolClient`
- add MCP preflight
- add primary memory retrieval pipeline
- add fallback retrieval/hydration

### Phase 3

- add plan synthesis prompt + validator
- add worker packet builder
- add artifact rendering/writing

### Phase 4

- add tests
- wire project into solution/build scripts

---

## 13. Final Implementation Decisions

1. Put the harness in `HarnessMcp.AgentClient`.
2. Reuse `HarnessMcp.Contracts` for MCP DTOs.
3. Keep planning-only contracts inside the new client project.
4. Use **two model passes only**.
5. Make chunking **deterministic**, not free-form.
6. Use **HTTP MCP only** in v1.
7. Require `get_server_info` preflight.
8. Run `retrieve_memory_by_chunks -> merge_retrieval_results -> build_memory_context_pack` exactly in that order.
9. Use secondary MCP tools only for targeted fallback inspection and hydration.
10. Create the execution plan in the harness, not in the MCP server.
11. Build the worker packet deterministically.
12. Persist all artifacts to files.
13. Keep the worker execution-only.

That is the concrete harness design that fits the uploaded supervisor design, fits the MCP server surface already defined, and is tight enough to drive a one-round Codex implementation. fileciteturn2file0turn2file2
