# Harness-Controlled Supervisor Design with MCP-Based Long-Term Memory Retrieval
## Comprehensive Design with Dedicated Harness Chunking Subsystem

## 1. Purpose

This document defines a **comprehensive harness-controlled supervisor design** for AI-assisted software engineering.

It combines:

- the original **harness-as-control-plane** architecture
- the **MCP-based memory retrieval model**
- a dedicated **harness chunking subsystem** for high-accuracy long-term memory querying

This version is intended to be the full design, not a partial supplement.

The design goal is to ensure that:

- the **control plane stays in the harness**
- **MCP tools own retrieval accuracy**
- a long user requirement is **never used directly as a raw memory query**
- the harness is **specially designed** to decompose requirements into correct retrieval chunks
- long-term memory retrieval achieves both:
  - **high precision**
  - **strong recall**
- the harness assembles a trusted memory context pack for downstream planning and execution

---

## 2. Scope

This design includes:

1. harness as control plane
2. role of skill in defining the flow
3. role of MCP in accurate memory retrieval
4. requirement interpretation
5. dedicated harness chunking subsystem
6. chunk schema and chunk quality rules
7. structured retrieval input contracts
8. detailed harness ↔ MCP contracts
9. retrieval merging and context pack assembly
10. worker-facing execution handoff

This design does **not** define:

- validation design
- how source knowledge is decomposed into database elements
- memory write-back design
- worker automation design
- database internals beyond what retrieval contracts require

Those belong to separate designs.

---

## 3. Target Architecture

```text
Requirement
  ↓
Harness
  ├─ interpret requirement
  ├─ decompose requirement into retrieval chunks
  ├─ validate chunk quality
  ├─ build structured retrieval input
  ├─ call MCP memory tools
  ├─ merge retrieved contexts
  ├─ assemble trusted memory pack
  ├─ generate execution plan
  └─ dispatch to worker (by human copy and paste)
```

### Responsibility split

| Layer | Responsibility |
|---|---|
| Harness | Control plane, workflow sequencing, requirement interpretation, chunking, retrieval input construction, memory pack assembly, plan generation |
| Skill | Defines mandatory supervisor flow and required artifacts |
| MCP Memory Tools | Accurate retrieval, ranking, filtering, stale suppression, conflict resolution, merging, memory context synthesis |
| Worker Agent | Executes structured plan |
| Human | Copies worker packet into worker session |

---

## 4. Why This Architecture

This design is based on the following conclusions:

1. A **strong harness can act as the supervisor control plane**.
2. The **skill should define the process**, not hold project memory.
3. **MCP tools should provide accurate memory retrieval** and return decision-ready outputs.
4. The harness should **not** send a long raw requirement directly into memory search.
5. The harness should first **interpret the requirement**, then **decompose it into chunked retrieval units**.
6. The worker should **not** retrieve long-term memory independently.
7. The worker should only execute a **structured plan** generated from trusted memory context.

This preserves the benefits of a supervisor architecture without requiring a fully custom supervisor runtime.

---

## 5. Core Design Principles

### 5.1 Harness owns the control plane
The harness decides:

- when requirement interpretation happens
- how requirement decomposition happens
- what retrieval chunks are valid
- what structured input is sent to MCP
- which tools are called
- what order is mandatory
- what artifacts are required
- what final plan format must be used

### 5.2 MCP owns memory retrieval accuracy
The harness does **not** perform:

- raw semantic ranking
- stale suppression
- conflict resolution across memory records
- authority weighting
- merging heuristics for raw retrieval candidates

These belong to MCP tools.

### 5.3 The harness must specially control chunking
Long user requirements cannot be used directly as memory queries.
Therefore, chunk decomposition must be:

- explicitly designed
- schema-driven
- validated
- part of the harness flow

### 5.4 One retrieval chunk must serve one retrieval purpose
A retrieval chunk must not mix:
- core feature target
- hard constraints
- risk cues
- implementation patterns
- similar-case shape

### 5.5 Precision and recall must both be protected
The system must avoid two opposite failures:

- low precision from broad queries
- low recall from overly narrow queries

The design must use multiple focused retrieval routes and controlled MCP-side merging.

### 5.6 Worker is execution-only
The worker receives:

- a structured execution plan
- hard constraints
- allowed scope
- required output format

The worker should not independently retrieve project memory.

---

## 6. End-to-End Flow

### Stage 0 — Requirement input
Input can be:

- product requirement
- engineering request
- bugfix requirement
- refactor objective
- implementation task

Example:

> Revise the yearly weighted card to support year switching via ajax without changing engine logic, and ensure the UI does not reintroduce previous placement issues.

The raw requirement is too long and semantically mixed to be used directly as a memory query.

---

### Stage 1 — Requirement interpretation
The harness converts the raw requirement into a normalized `RequirementIntent`.

Purpose:

- extract structure from natural language
- identify scope, constraints, and risk
- prepare the requirement for chunk decomposition

---

### Stage 2 — Dedicated harness chunking
The harness converts `RequirementIntent` into `RetrievalChunkSet`.

Purpose:

- break the task into several small, focused retrieval units
- ensure each retrieval unit has one primary semantic purpose
- preserve all important dimensions of the requirement

This is the main accuracy-critical subsystem.

---

### Stage 3 — Chunk quality validation
The harness validates the `RetrievalChunkSet` before retrieval.

Purpose:

- ensure chunking is correct
- ensure coverage is complete
- ensure chunk text is compact
- ensure no chunk mixes multiple retrieval purposes

---

### Stage 4 — Structured retrieval request generation
The harness converts the validated chunk set into `MemoryRetrievalRequest`.

Purpose:

- produce MCP-ready retrieval input
- carry structured metadata alongside chunk text
- ensure retrieval is compact and deterministic

---

### Stage 5 — MCP retrieval
The harness calls MCP tools in sequence:

1. `retrieve_memory_by_chunks`
2. `merge_retrieval_results`
3. `build_memory_context_pack`

Optional MCP calls may be added later, but these are the core retrieval path.

---

### Stage 6 — Memory pack assembly
The harness receives planning-ready memory context from MCP and assembles a trusted memory pack.

---

### Stage 7 — Execution plan generation
The harness uses:

- `RequirementIntent`
- `RetrievalChunkSet`
- `MemoryContextPack`

to generate a structured execution plan.

---

### Stage 8 — Human dispatch to worker
Human copies the worker packet into the worker session.

This design intentionally keeps dispatch manual.

---

## 7. Skill Design

The skill defines the mandatory harness procedure.

### 7.1 Skill responsibilities

The skill must require the harness to:

1. interpret raw requirement into `RequirementIntent`
2. decompose `RequirementIntent` into `RetrievalChunkSet`
3. validate the chunk set
4. build `MemoryRetrievalRequest`
5. call MCP memory tools in the required sequence
6. assemble a trusted memory pack
7. generate a structured execution plan
8. generate a human-copyable worker packet

### 7.2 Skill should not do
The skill should **not** attempt to:

- manually search raw memory
- rank raw memory in prompt
- replace chunking rules with free-form instructions
- infer active architectural decisions from unstructured context
- skip requirement chunking and query memory directly with long text

### 7.3 Example skill outline

```text
You are the harness control plane for supervisor-style planning.

You must follow this procedure exactly:
1. Interpret the raw requirement into a RequirementIntent JSON object.
2. Convert RequirementIntent into a RetrievalChunkSet JSON object.
3. Validate the chunk set using the required chunk-quality rules.
4. Build a MemoryRetrievalRequest JSON object.
5. Call the required MCP memory tools.
6. Assemble a trusted memory context pack.
7. Generate an ExecutionPlan JSON object.
8. Generate a human-copyable WorkerExecutionPacket.

You must not use raw requirement text directly as memory query input.
You must not skip chunk decomposition.
You must use only validated structured intermediate artifacts.
```

---

## 8. Requirement Interpretation Model

## 8.1 Output artifact: `RequirementIntent`

```json
{
  "task_id": "task-2026-04-11-001",
  "task_type": "ui-change",
  "domain": "mingpan",
  "module": "yearly-weighted-card",
  "feature": "year-switching",
  "goal": "support year switching via ajax",
  "requested_operations": [
    "add year selector",
    "refresh card asynchronously"
  ],
  "hard_constraints": [
    "engine must not change"
  ],
  "soft_constraints": [
    "prefer minimal change"
  ],
  "risk_signals": [
    "placement consistency",
    "ui refresh state",
    "previous defect recurrence"
  ],
  "candidate_layers": [
    "ui",
    "api"
  ],
  "retrieval_focuses": [
    "architecture decisions",
    "anti-patterns",
    "best practices",
    "similar cases"
  ],
  "ambiguities": [],
  "complexity": "medium"
}
```

## 8.2 Interpretation rules

The harness must extract:

- task type
- domain / module / feature
- explicit hard constraints
- soft preferences
- likely risk signals
- likely affected layers
- relevant memory focus types
- ambiguity indicators
- estimated task complexity

## 8.3 Why this matters
This prevents the harness from treating the requirement as one search phrase.
Instead, the requirement becomes structured input for chunking and retrieval.

---

## 9. Dedicated Harness Chunking Subsystem

This is the new mandatory subsystem that must be included in the design.

## 9.1 Purpose
The chunking subsystem ensures that a long user requirement is decomposed into small retrieval units that are:

- compact
- retrieval-specific
- scope-aware
- complete
- validated

Its job is to maximize retrieval relevance without losing essential context.

---

## 9.2 Why chunking must be a first-class subsystem

Without special design, the agent may:

- generate inconsistent retrieval queries
- mix multiple retrieval purposes in one query
- produce overlong semantic queries
- miss architectural constraints
- miss anti-patterns tied to risk signals
- miss similar cases because the feature query dominates everything

Therefore, chunking cannot be left to ad hoc model behavior.

---

## 9.3 Chunk taxonomy

The harness must use this fixed taxonomy.

### A. `core_task`
Purpose:
- retrieve memory directly about the feature target

Example:
- year switching for yearly weighted card

### B. `constraint`
Purpose:
- retrieve hard boundaries and do-not-change rules

Example:
- engine logic must not change

### C. `risk`
Purpose:
- retrieve anti-patterns, historical defect cues, fragile areas

Example:
- avoid recurrence of previous placement inconsistency caused by UI inference

### D. `pattern`
Purpose:
- retrieve preferred implementation patterns and style guidance

Example:
- ajax refresh with explicit loading state and no full reload

### E. `similar_case`
Purpose:
- retrieve structurally similar historical cases

Example:
- ui change, card refresh, no engine change, ui/api layers only

No free-form chunk type should be used unless the design is explicitly extended.

---

## 9.4 Chunking algorithm

The harness must break the requirement into chunks in this order.

### Step 1 — Identify implementation target
Find:
- what is being changed
- which module/feature is the main target

Produces:
- `core_task` chunk

### Step 2 — Extract hard constraints
Find:
- what must not change
- architectural boundaries
- explicit negative restrictions

Produces:
- one or more `constraint` chunks

### Step 3 — Infer failure risks
Find:
- prior problem references
- likely fragile areas
- risk-sensitive terms such as consistency, regression, placement, async state, authorization, etc.

Produces:
- one or more `risk` chunks

### Step 4 — Infer implementation style
Find:
- phrases like “via ajax”, “minimal change”, “UI only”, “without engine changes”, “loading state”, “no full reload”

Produces:
- one or more `pattern` chunks

### Step 5 — Build structural similarity signature
Convert task into:
- task_type
- feature_shape
- engine_change_allowed
- likely_layers
- relevant risk signals

Produces:
- `similar_case` chunk

---

## 9.5 Chunk generation rules

### Rule 1 — one chunk per primary retrieval purpose
Do not mix:
- feature target
- constraints
- risks
- patterns
- task shape

### Rule 2 — keep chunk text compact
Chunk text must be short enough to act as a precise retrieval key.

Preferred form:
- one short clause or sentence
- not a paragraph

### Rule 3 — every chunk must carry structured scope
Each chunk must include:
- domain
- module if known
- layers if known
- concern tags if relevant

### Rule 4 — explicit constraints must become explicit chunks
If the requirement includes:
- without changing X
- do not modify Y
- UI only
- keep engine unchanged

then at least one `constraint` chunk is mandatory.

### Rule 5 — risk wording must become risk chunks
If the requirement includes:
- avoid previous issue
- do not reintroduce
- consistency issue
- regression
- previous defect

then at least one `risk` chunk is mandatory.

### Rule 6 — implementation-style wording must become pattern chunks
If the requirement includes:
- via ajax
- minimal change
- loading state
- async refresh
- no full reload

then at least one `pattern` chunk is mandatory.

### Rule 7 — similar-case chunk is mandatory for medium/high complexity tasks
If task complexity is medium or high, a `similar_case` chunk must be generated.

### Rule 8 — ambiguous tasks must preserve ambiguity
If ambiguity exists, the harness must preserve it explicitly instead of hiding it.

---

## 9.6 Chunk compactness and purity rules

### Good chunk
- “engine logic must not change”

### Bad chunk
- “add year switching via ajax but do not change engine and avoid placement regression and keep API minimal”

The bad chunk mixes:
- feature target
- constraint
- risk
- style

It must be split into multiple chunks.

---

## 9.7 Required intermediate artifact: `RetrievalChunkSet`

```json
{
  "task_id": "task-2026-04-11-001",
  "complexity": "medium",
  "chunks": [
    {
      "chunk_id": "c1",
      "chunk_type": "core_task",
      "text": "year switching for yearly weighted card",
      "scopes": {
        "domain": "mingpan",
        "module": "yearly-weighted-card",
        "layers": ["ui"]
      }
    },
    {
      "chunk_id": "c2",
      "chunk_type": "constraint",
      "text": "engine logic must not change",
      "scopes": {
        "domain": "mingpan",
        "layers": ["ui", "api", "engine"]
      }
    },
    {
      "chunk_id": "c3",
      "chunk_type": "risk",
      "text": "avoid recurrence of previous placement inconsistency caused by ui inference",
      "scopes": {
        "domain": "mingpan",
        "module": "yearly-weighted-card",
        "concerns": ["placement", "frontend-inference"]
      }
    },
    {
      "chunk_id": "c4",
      "chunk_type": "pattern",
      "text": "ajax refresh with explicit loading state and no full reload",
      "scopes": {
        "layers": ["ui", "api"],
        "concerns": ["async-refresh", "loading-state"]
      }
    },
    {
      "chunk_id": "c5",
      "chunk_type": "similar_case",
      "task_shape": {
        "task_type": "ui-change",
        "feature_shape": "card-refresh",
        "engine_change_allowed": false,
        "likely_layers": ["ui", "api"],
        "risk_signals": ["placement consistency", "async refresh"]
      }
    }
  ],
  "coverage_report": {
    "has_core_task": true,
    "has_constraint": true,
    "has_risk": true,
    "has_pattern": true,
    "has_similar_case": true
  }
}
```

---

## 9.8 Why this artifact is required

This artifact ensures that:

- the decomposition is explicit
- chunk purposes are separated
- query inputs stay short
- all important retrieval dimensions are covered
- MCP gets retrieval-ready inputs instead of long requirement text

---

## 10. Harness Quality Gates Before MCP Retrieval

The harness must validate the chunk set before retrieval.

### Gate 1 — schema validity
`RequirementIntent` and `RetrievalChunkSet` must be valid structured artifacts.

### Gate 2 — mandatory coverage
Required chunk types must exist when implied by the task.

### Gate 3 — compactness
Chunk text must stay short and query-friendly.

### Gate 4 — purity
Each chunk must represent one primary retrieval purpose.

### Gate 5 — scope completeness
Chunks must carry enough scope metadata for precise retrieval.

### Gate 6 — ambiguity preservation
Ambiguity must remain visible rather than being silently flattened.

If any gate fails, the harness must regenerate the chunk set before calling MCP.

---

## 11. Structured Retrieval Input Contract

After the chunk set passes validation, the harness builds the retrieval request.

## 11.1 Output artifact: `MemoryRetrievalRequest`

```json
{
  "request_id": "mrr-2026-04-11-001",
  "task_id": "task-2026-04-11-001",
  "requirement_intent": {
    "task_type": "ui-change",
    "domain": "mingpan",
    "module": "yearly-weighted-card",
    "feature": "year-switching",
    "hard_constraints": [
      "engine must not change"
    ],
    "risk_signals": [
      "placement consistency",
      "previous defect recurrence"
    ]
  },
  "retrieval_chunks": [
    {
      "chunk_id": "c1",
      "chunk_type": "core_task",
      "text": "year switching for yearly weighted card",
      "scopes": ["mingpan", "yearly-weighted-card", "ui"]
    },
    {
      "chunk_id": "c2",
      "chunk_type": "constraint",
      "text": "engine logic must not change",
      "scopes": ["mingpan", "engine", "ui", "api"]
    },
    {
      "chunk_id": "c3",
      "chunk_type": "risk",
      "text": "avoid recurrence of previous placement inconsistency caused by ui inference",
      "scopes": ["mingpan", "placement", "frontend-inference"]
    },
    {
      "chunk_id": "c4",
      "chunk_type": "pattern",
      "text": "ajax refresh with explicit loading state and no full reload",
      "scopes": ["ui", "api", "async-refresh"]
    },
    {
      "chunk_id": "c5",
      "chunk_type": "similar_case",
      "task_shape": {
        "task_type": "ui-change",
        "feature_shape": "card-refresh",
        "engine_change_allowed": false,
        "likely_layers": ["ui", "api"],
        "risk_signals": ["placement consistency", "async refresh"]
      }
    }
  ],
  "search_profile": {
    "active_only": true,
    "minimum_authority": "reviewed",
    "max_items_per_chunk": 5,
    "require_type_separation": true
  }
}
```

---

## 12. MCP Tooling Design

The MCP layer is responsible for retrieval accuracy and should expose high-level tools, not raw search only.

Recommended tools:

- `retrieve_memory_by_chunks`
- `merge_retrieval_results`
- `build_memory_context_pack`

---

## 13. Detailed Harness ↔ MCP Contracts

## 13.1 Tool: `retrieve_memory_by_chunks`

### Purpose
Retrieve memory separately for each chunk so each retrieval purpose is searched independently.

### Input

```json
{
  "request_id": "mrr-2026-04-11-001",
  "task_id": "task-2026-04-11-001",
  "retrieval_chunks": [
    {
      "chunk_id": "c1",
      "chunk_type": "core_task",
      "text": "year switching for yearly weighted card",
      "scopes": ["mingpan", "yearly-weighted-card", "ui"]
    },
    {
      "chunk_id": "c2",
      "chunk_type": "constraint",
      "text": "engine logic must not change",
      "scopes": ["mingpan", "engine", "ui", "api"]
    },
    {
      "chunk_id": "c3",
      "chunk_type": "risk",
      "text": "avoid recurrence of previous placement inconsistency caused by ui inference",
      "scopes": ["mingpan", "placement", "frontend-inference"]
    },
    {
      "chunk_id": "c4",
      "chunk_type": "pattern",
      "text": "ajax refresh with explicit loading state and no full reload",
      "scopes": ["ui", "api", "async-refresh"]
    },
    {
      "chunk_id": "c5",
      "chunk_type": "similar_case",
      "task_shape": {
        "task_type": "ui-change",
        "feature_shape": "card-refresh",
        "engine_change_allowed": false,
        "likely_layers": ["ui", "api"],
        "risk_signals": ["placement consistency", "async refresh"]
      }
    }
  ],
  "search_profile": {
    "active_only": true,
    "minimum_authority": "reviewed",
    "max_items_per_chunk": 5,
    "require_type_separation": true
  }
}
```

### Output

```json
{
  "task_id": "task-2026-04-11-001",
  "chunk_results": [
    {
      "chunk_id": "c1",
      "chunk_type": "core_task",
      "results": {
        "decisions": [
          {
            "memory_id": "mem-dec-021",
            "title": "Engine is authoritative for placement output",
            "summary": "UI must consume engine-provided placement only.",
            "authority": "approved",
            "confidence": 0.97
          }
        ],
        "best_practices": [
          {
            "memory_id": "mem-bp-011",
            "title": "Use ajax refresh with explicit loading state",
            "summary": "Card refresh should avoid full page reload and show busy indicator.",
            "authority": "reviewed",
            "confidence": 0.91
          }
        ],
        "antipatterns": [],
        "similar_cases": []
      }
    },
    {
      "chunk_id": "c2",
      "chunk_type": "constraint",
      "results": {
        "decisions": [],
        "best_practices": [],
        "antipatterns": [
          {
            "memory_id": "mem-ap-004",
            "title": "Do not infer placement logic in UI",
            "summary": "Frontend inference caused prior mismatch defects.",
            "authority": "approved",
            "confidence": 0.99
          }
        ],
        "similar_cases": []
      }
    },
    {
      "chunk_id": "c3",
      "chunk_type": "risk",
      "results": {
        "decisions": [],
        "best_practices": [],
        "antipatterns": [
          {
            "memory_id": "mem-ap-004",
            "title": "Do not infer placement logic in UI",
            "summary": "Frontend inference caused prior mismatch defects.",
            "authority": "approved",
            "confidence": 0.99
          }
        ],
        "similar_cases": [
          {
            "memory_id": "mem-case-182",
            "title": "UI-only card refresh with no engine changes",
            "summary": "Previous successful refresh kept engine unchanged and used minimal API extension.",
            "authority": "reviewed",
            "confidence": 0.89
          }
        ]
      }
    },
    {
      "chunk_id": "c4",
      "chunk_type": "pattern",
      "results": {
        "decisions": [],
        "best_practices": [
          {
            "memory_id": "mem-bp-011",
            "title": "Use ajax refresh with explicit loading state",
            "summary": "Card refresh should avoid full page reload and show busy indicator.",
            "authority": "reviewed",
            "confidence": 0.91
          }
        ],
        "antipatterns": [],
        "similar_cases": []
      }
    },
    {
      "chunk_id": "c5",
      "chunk_type": "similar_case",
      "results": {
        "decisions": [],
        "best_practices": [],
        "antipatterns": [],
        "similar_cases": [
          {
            "memory_id": "mem-case-182",
            "title": "UI-only card refresh with no engine changes",
            "summary": "Previous successful refresh kept engine unchanged and used minimal API extension.",
            "authority": "reviewed",
            "confidence": 0.89
          }
        ]
      }
    }
  ],
  "notes": [
    "Each chunk was retrieved independently.",
    "Only active records were returned.",
    "Superseded results were removed."
  ]
}
```

### MCP guarantees
This tool must guarantee:

- independent retrieval per chunk
- type-separated results
- active-only filtering
- superseded record removal
- minimum-authority enforcement

---

## 13.2 Tool: `merge_retrieval_results`

### Purpose
Merge chunk-level results into a coherent task-level candidate set without losing important context.

### Input

```json
{
  "task_id": "task-2026-04-11-001",
  "chunk_results": [
    {
      "chunk_id": "c1",
      "chunk_type": "core_task",
      "results": {
        "decisions": [
          {
            "memory_id": "mem-dec-021",
            "title": "Engine is authoritative for placement output",
            "summary": "UI must consume engine-provided placement only.",
            "authority": "approved",
            "confidence": 0.97
          }
        ],
        "best_practices": [
          {
            "memory_id": "mem-bp-011",
            "title": "Use ajax refresh with explicit loading state",
            "summary": "Card refresh should avoid full page reload and show busy indicator.",
            "authority": "reviewed",
            "confidence": 0.91
          }
        ],
        "antipatterns": [],
        "similar_cases": []
      }
    },
    {
      "chunk_id": "c2",
      "chunk_type": "constraint",
      "results": {
        "decisions": [],
        "best_practices": [],
        "antipatterns": [
          {
            "memory_id": "mem-ap-004",
            "title": "Do not infer placement logic in UI",
            "summary": "Frontend inference caused prior mismatch defects.",
            "authority": "approved",
            "confidence": 0.99
          }
        ],
        "similar_cases": []
      }
    },
    {
      "chunk_id": "c3",
      "chunk_type": "risk",
      "results": {
        "decisions": [],
        "best_practices": [],
        "antipatterns": [
          {
            "memory_id": "mem-ap-004",
            "title": "Do not infer placement logic in UI",
            "summary": "Frontend inference caused prior mismatch defects.",
            "authority": "approved",
            "confidence": 0.99
          }
        ],
        "similar_cases": [
          {
            "memory_id": "mem-case-182",
            "title": "UI-only card refresh with no engine changes",
            "summary": "Previous successful refresh kept engine unchanged and used minimal API extension.",
            "authority": "reviewed",
            "confidence": 0.89
          }
        ]
      }
    }
  ]
}
```

### Output

```json
{
  "task_id": "task-2026-04-11-001",
  "merged_results": {
    "decisions": [
      {
        "memory_id": "mem-dec-021",
        "title": "Engine is authoritative for placement output",
        "summary": "UI must consume engine-provided placement only.",
        "authority": "approved",
        "confidence": 0.97,
        "supporting_chunks": ["c1"]
      }
    ],
    "best_practices": [
      {
        "memory_id": "mem-bp-011",
        "title": "Use ajax refresh with explicit loading state",
        "summary": "Card refresh should avoid full page reload and show busy indicator.",
        "authority": "reviewed",
        "confidence": 0.91,
        "supporting_chunks": ["c1", "c4"]
      }
    ],
    "antipatterns": [
      {
        "memory_id": "mem-ap-004",
        "title": "Do not infer placement logic in UI",
        "summary": "Frontend inference caused prior mismatch defects.",
        "authority": "approved",
        "confidence": 0.99,
        "supporting_chunks": ["c2", "c3"]
      }
    ],
    "similar_cases": [
      {
        "memory_id": "mem-case-182",
        "title": "UI-only card refresh with no engine changes",
        "summary": "Previous successful refresh kept engine unchanged and used minimal API extension.",
        "authority": "reviewed",
        "confidence": 0.89,
        "supporting_chunks": ["c3", "c5"]
      }
    ]
  },
  "merge_notes": [
    "Duplicate memory items merged by memory_id.",
    "Multi-supported items were boosted in confidence.",
    "Relevant single-route items were preserved unless suppressed by quality rules."
  ]
}
```

### MCP guarantees
This tool must guarantee:

- deduplication across chunk outputs
- multi-route support preservation
- no type mixing
- recall preservation for important single-route items

---

## 13.3 Tool: `build_memory_context_pack`

### Purpose
Transform merged results into a planning-ready context pack.

### Input

```json
{
  "task_id": "task-2026-04-11-001",
  "requirement_intent": {
    "task_type": "ui-change",
    "domain": "mingpan",
    "module": "yearly-weighted-card",
    "feature": "year-switching",
    "hard_constraints": [
      "engine must not change"
    ],
    "risk_signals": [
      "placement consistency",
      "previous defect recurrence"
    ]
  },
  "merged_results": {
    "decisions": [
      {
        "memory_id": "mem-dec-021",
        "title": "Engine is authoritative for placement output",
        "summary": "UI must consume engine-provided placement only.",
        "authority": "approved",
        "confidence": 0.97
      }
    ],
    "best_practices": [
      {
        "memory_id": "mem-bp-011",
        "title": "Use ajax refresh with explicit loading state",
        "summary": "Card refresh should avoid full page reload and show busy indicator.",
        "authority": "reviewed",
        "confidence": 0.91
      }
    ],
    "antipatterns": [
      {
        "memory_id": "mem-ap-004",
        "title": "Do not infer placement logic in UI",
        "summary": "Frontend inference caused prior mismatch defects.",
        "authority": "approved",
        "confidence": 0.99
      }
    ],
    "similar_cases": [
      {
        "memory_id": "mem-case-182",
        "title": "UI-only card refresh with no engine changes",
        "summary": "Previous successful refresh kept engine unchanged and used minimal API extension.",
        "authority": "reviewed",
        "confidence": 0.89
      }
    ]
  }
}
```

### Output

```json
{
  "task_id": "task-2026-04-11-001",
  "memory_context_pack": {
    "must_follow": [
      "Do not modify engine logic",
      "UI must consume engine-provided placement only"
    ],
    "best_practices": [
      "Use ajax refresh with explicit loading state"
    ],
    "avoid": [
      "Do not infer placement or interpretation logic in UI"
    ],
    "similar_case_guidance": [
      "Previous successful UI-only card refresh used minimal UI state and async fetch"
    ],
    "retrieval_rationale": [
      "Constraint and risk chunks both reinforced the same anti-pattern warning",
      "Core task and pattern chunks both pointed to async refresh best practice"
    ],
    "retrieval_support": {
      "multi_supported_items": [
        {
          "memory_id": "mem-ap-004",
          "supported_by_chunks": ["constraint", "risk"]
        },
        {
          "memory_id": "mem-bp-011",
          "supported_by_chunks": ["core_task", "pattern"]
        }
      ],
      "single_route_important_items": [
        {
          "memory_id": "mem-dec-021",
          "supported_by_chunks": ["core_task"]
        }
      ]
    }
  }
}
```

### MCP guarantees
This tool must return:
- planning-ready context
- concise output
- preserved multi-route support evidence
- preserved important single-route items

---

## 14. Precision and Recall Design

## 14.1 Precision mechanisms

Precision is improved by:

1. requirement interpretation before retrieval
2. explicit chunk taxonomy
3. one-purpose-per-chunk rules
4. compact chunk text
5. scope metadata on every chunk
6. type-separated retrieval in MCP
7. active/authority filtering in MCP

## 14.2 Recall mechanisms

Recall is improved by:

1. multiple chunk routes instead of one query
2. separate retrieval of constraints
3. separate retrieval of risks
4. separate retrieval of patterns
5. separate retrieval of similar cases
6. MCP merging that preserves single-route important context

## 14.3 Balance rule
The system must prefer:

1. narrow focused chunk queries
2. MCP-side controlled merging
3. preservation of multi-supported and important single-route context

instead of:
- one large broad semantic query

---

## 15. Recommended Final Memory Context Pack

The harness should receive:

```json
{
  "task_id": "task-2026-04-11-001",
  "memory_context_pack": {
    "must_follow": [
      "Do not modify engine logic",
      "UI must consume engine-provided placement only"
    ],
    "best_practices": [
      "Use ajax refresh with explicit loading state"
    ],
    "avoid": [
      "Do not infer placement or interpretation logic in UI"
    ],
    "similar_case_guidance": [
      "Previous successful UI-only card refresh used minimal UI state and async fetch"
    ],
    "retrieval_support": {
      "multi_supported_items": [
        {
          "memory_id": "mem-ap-004",
          "supported_by_chunks": ["constraint", "risk"]
        }
      ],
      "single_route_important_items": [
        {
          "memory_id": "mem-dec-021",
          "supported_by_chunks": ["core_task"]
        }
      ]
    }
  }
}
```

This gives the harness:
- directly usable context
- support visibility
- confidence cues
- both precision and recall preservation

---

## 16. Execution Plan Design

After retrieval, the harness should generate a structured execution plan.

### Example `ExecutionPlan`

```json
{
  "task_id": "task-2026-04-11-001",
  "task": "Add year switching via ajax to yearly weighted card",
  "scope": {
    "include": [
      "ui/mingpan/**",
      "api/**"
    ],
    "exclude": [
      "engine/**"
    ]
  },
  "constraints": [
    "Do not modify engine logic",
    "UI must not infer placement or interpretation logic"
  ],
  "steps": [
    "Locate the yearly weighted card component",
    "Add selectedYear UI state with current year as default",
    "Implement year selector UI",
    "Trigger async refresh when year changes",
    "Show busy/loading overlay during refresh",
    "Keep placement rendering dependent on authoritative output only"
  ],
  "forbidden_actions": [
    "Do not move logic from engine into UI",
    "Do not infer placement locally in frontend",
    "Do not perform full page reload"
  ],
  "assumptions": [
    "API can support year parameter without engine logic change or can be extended minimally in UI/API layer only"
  ]
}
```

---

## 17. Worker Execution Packet

The harness should generate a human-copyable worker packet.

### Example packet

```md
# Worker Task

## Goal
Add year switching via ajax to the yearly weighted card.

## Scope
Allowed:
- ui/mingpan/**
- api/**

Forbidden:
- engine/**

## Hard Constraints
- Do not modify engine logic.
- UI must not infer placement or interpretation logic.

## Steps
1. Locate the yearly weighted card component.
2. Add selectedYear state with current year as default.
3. Add year selector UI.
4. Refresh card asynchronously when year changes.
5. Show busy/loading overlay during refresh.
6. Keep rendering dependent on authoritative output only.

## Forbidden Actions
- Do not move engine logic into UI.
- Do not infer placement in frontend.
- Do not use full page reload.

## Expected Output
Return:
- summary of changes
- changed files
- tests run
- assumptions made
- unresolved issues
```

---

## 18. Sequence Diagram

```text
User
  ↓
Harness
  -> interpret raw requirement
  -> produce RequirementIntent
  -> decompose into RetrievalChunkSet
  -> validate chunk quality
  -> build MemoryRetrievalRequest
  -> call retrieve_memory_by_chunks
  -> call merge_retrieval_results
  -> call build_memory_context_pack
  -> assemble trusted memory pack
  -> generate ExecutionPlan
  -> generate WorkerExecutionPacket
Human
  -> copy WorkerExecutionPacket into worker
Worker
  -> execute
  -> return result
```

---

## 19. Minimal Implementation Plan

### Phase 1
Implement harness with:
- requirement interpretation
- dedicated chunking subsystem
- chunk quality gates
- retrieval request generation

### Phase 2
Implement MCP tools:
- retrieve_memory_by_chunks
- merge_retrieval_results
- build_memory_context_pack

### Phase 3
Standardize execution plan and worker packet

### Phase 4
Optionally add later:
- automatic dispatch
- validation
- memory write-back

---

## 20. What This Design Solves

This comprehensive design solves:

- uncontrolled use of long requirements for retrieval
- omission of chunking as a first-class design concern
- poor precision from broad semantic queries
- poor recall from narrow single-query retrieval
- repeated anti-pattern introduction due to missing risk retrieval
- missing architectural constraints due to weak constraint handling
- loss of relevant context during aggressive narrowing
- lack of deterministic handoff to worker agent

---

## 21. What This Design Does Not Cover

This design does not cover:

- validation design
- how knowledge articles are decomposed into database elements
- memory write-back design
- worker automation design
- database schema design

Those belong to separate documents.

---

## 22. Final Summary

This is the **full harness-controlled supervisor design** with a dedicated harness chunking subsystem added to support high-accuracy memory querying. It incorporates the long-term retrieval design and restores the broader supervisor/MCP/worker structure from the earlier harness design. fileciteturn2file0

The key idea is:

1. the harness is the control plane
2. the harness must **specially design** requirement decomposition into short retrieval chunks
3. MCP tools must retrieve memory using those chunked inputs
4. MCP must preserve both precision and recall through controlled merging
5. the harness then generates a structured execution plan for the worker

### Final principle

> High-quality long-term memory retrieval requires a specially designed harness that converts long requirements into short, validated, purpose-specific retrieval chunks before querying MCP memory tools, while preserving all other supervisor-control-plane responsibilities.
