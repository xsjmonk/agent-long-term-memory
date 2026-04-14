# Supervisor-Driven AI Coding System (Production Design)

## 1. Purpose
This document defines a **production-grade architecture** for a Supervisor-driven AI coding system with:
- Deterministic memory retrieval
- Structured planning
- Replaceable execution agents (Cursor / Codex / Claude)
- Long-term knowledge accumulation

---

## 2. High-Level Architecture

```
                ┌──────────────────────────┐
                │     Supervisor Agent     │
                │--------------------------│
                │ - Task understanding     │
                │ - Memory retrieval       │
                │ - Ranking & filtering    │
                │ - Plan generation        │
                │ - Validation             │
                └──────────┬───────────────┘
                           │
                 Execution Plan (JSON/YAML)
                           │
        ┌──────────────────┼──────────────────┐
        │                  │                  │
   Cursor CLI         Codex API         Claude Code
 (Worker Agent)      (Worker Agent)     (Worker Agent)
        │                  │                  │
        └──────────────┬───┴──────────────┬───┘
                       │                  │
               Code changes / tests / outputs
                       │
               ┌───────▼────────┐
               │ Memory Writer  │
               └────────────────┘
```

---

## 3. Core Components

### 3.1 Supervisor Agent
Responsible for:
- Task decomposition
- Memory retrieval
- Authority enforcement
- Plan generation
- Validation
- Memory write-back

---

### 3.2 Worker Agents
Examples:
- Cursor CLI (preferred executor)
- Codex (API)
- Claude Code

Responsibilities:
- Execute plan
- Modify code
- Run tests

---

### 3.3 Memory System

#### Storage
- SQLite / PostgreSQL (structured)
- Vector DB (semantic search)

---

## 4. Memory Schema

### Base Table: MemoryItems

| Field | Type |
|------|------|
| Id | string |
| Type | decision / antipattern / practice / case |
| Title | string |
| Summary | string |
| Details | string |
| Authority | approved / inferred |
| Confidence | float |
| Status | active / superseded |
| CreatedAt | datetime |
| UpdatedAt | datetime |

---

### CaseFeatures

| Field | Type |
|------|------|
| TaskType | string |
| Layer | ui / engine |
| Module | string |
| EngineChanged | bool |
| RiskTags | json |

---

## 5. Retrieval Pipeline

### Step 1: Intent Extraction

```json
{
  "task_type": "ui-change",
  "module": "mingpan",
  "constraints": ["no engine change"]
}
```

---

### Step 2: Multi-Source Retrieval
- semantic search
- tag match
- scope match

---

### Step 3: Authority Filter
Remove:
- deprecated memory
- low-confidence items

---

### Step 4: Ranking Formula

```
score =
  0.30 semantic +
  0.20 scope +
  0.15 tag +
  0.15 authority +
  0.10 recency +
  0.10 evidence
```

---

### Step 5: Memory Pack

```yaml
decisions:
antipatterns:
best_practices:
cases:
```

---

## 6. Plan Contract (Supervisor → Worker)

```json
{
  "task": "Update yearly card",
  "scope": {
    "include": ["ui/**"],
    "exclude": ["engine/**"]
  },
  "constraints": [
    "no engine change",
    "no UI inference"
  ],
  "steps": [
    "locate component",
    "add state",
    "update API"
  ],
  "validation": [
    "tests pass",
    "no engine files changed"
  ]
}
```

---

## 7. Validation Layer

Supervisor checks:
- constraint violations
- antipattern conflicts
- test results
- scope violations

---

## 8. Memory Write-Back

Only store:
- approved decisions
- reusable patterns
- confirmed bugs
- successful cases

Avoid:
- raw chat
- temporary reasoning

---

## 9. Execution Patterns

### Pattern 1: Plan → Execute
Supervisor creates plan → worker executes

### Pattern 2: Validate → Retry
If validation fails:
- refine plan
- resend

### Pattern 3: Learn → Persist
After success:
- extract reusable knowledge
- store in memory

---

## 10. API Design

### Retrieve Memory

```json
POST /memory/retrieve
{
  "task": "...",
  "types": ["decision", "antipattern"]
}
```

---

### Write Memory

```json
POST /memory/write
{
  "type": "antipattern",
  "content": "..."
}
```

---

## 11. Advantages

- Deterministic memory usage
- High accuracy
- Reusable knowledge
- Replaceable workers
- Scalable architecture

---

## 12. Final Principle

> Supervisor owns memory and decisions  
> Worker owns execution

