# Query Embedding API Design for MCP ↔ Python Builder Compatibility
## Shared Harness-Flow API Contract Revision — Accuracy Complete

## 1. Verdict

The current richer single-endpoint contract is **close but not yet sufficient** to fully support **accurate semantic search in the harness-controlled flow**.

It is already strong in these areas:
- one endpoint only
- batch support
- request/response correlation
- chunk/task identity
- model/runtime metadata
- vector-space identity
- fallback signaling

But it is still missing a few contract properties that matter for **accuracy, correctness, and harness-visible diagnostics**:

1. it does **not** explicitly carry a **text-processing identity** as part of the compatibility contract
2. it does **not** report **per-item truncation / effective-input diagnostics**
3. it does **not** echo enough **role-selection identity** per item for deterministic diagnostics in mixed-role batches
4. it keeps warnings only at the response level, which is too weak when only one item/chunk in a batch is degraded

Those gaps matter because the MCP design requires:
- compatibility-before-semantic-search
- explicit degradation/fallback when semantic compatibility is not acceptable
- harness-visible diagnostics when semantic search is degraded
- route-aware chunk retrieval where each chunk can succeed, degrade, or fail independently

So this document revises the contract to close those gaps while still keeping:
- **one endpoint only**
- **FastAPI**
- **JSON only**
- **reuse of existing `EmbeddingProvider`**
- **budget-saving implementation**
- **no auth**
- **no management endpoints**

---

## 2. Purpose

This design defines a **single-endpoint, query-only FastAPI API** to be added to the Python embedding builder so the .NET MCP server can generate semantic-query embeddings that are compatible with the vectors already persisted in the database.

The contract is shared by:
- the Python embedding builder
- the .NET MCP server
- the harness-controlled retrieval flow

The goal is to make the API contract strong enough that MCP can:
1. request embeddings for harness chunks in a route-aware way
2. correlate every returned vector to the exact harness chunk/query item
3. validate semantic compatibility before using semantic search
4. detect when semantic quality may be weakened
5. surface degradation clearly to the harness
6. do all of this with one endpoint and low implementation cost

---

## 3. Builder behavior that this API must preserve

The builder currently embeds text using:
- `sentence-transformers`
- local model path loading when available
- optional auto-download when missing
- `normalize_embeddings = true` by default
- fallback to a `HashingVectorizer`-based 384-dim embedding only when the sentence-transformer model is unavailable

The builder currently generates embeddings through:
- `app.embedding_provider.EmbeddingProvider`

This API must reuse that exact implementation for query embedding generation.

No second embedding pipeline may be added.

---

## 4. Why the previous shared contract is still insufficient

The previous shared-envelope design added:
- request identity
- task identity
- chunk identity
- route identity
- vector-space identity
- runtime embedding metadata

That was a major improvement.

But for **accurate harness-flow semantic search**, the API contract must also tell MCP:

1. whether a specific query item’s text was truncated or otherwise reduced before embedding
2. what text-processing regime defines the vector space
3. which role-hint/query-kind combination the returned vector corresponds to
4. which item in a batch caused warnings

Without this, MCP can know compatibility at a high level but still cannot fully diagnose why one chunk’s semantic quality is weaker than another.

That makes the contract not yet complete enough for the harness flow.

---

## 5. Design goals

The revised API must:

1. let MCP generate **compatible** query embeddings
2. be usable in the **harness-controlled retrieval flow**
3. remain **one endpoint only**
4. remain **JSON-only**
5. keep implementation cost low
6. reuse the builder’s existing embedding implementation
7. preserve request/task/chunk identity
8. preserve route identity (`QueryKind`, `ChunkType`)
9. provide enough runtime metadata for MCP compatibility checks
10. provide enough per-item diagnostics for harness-visible semantic degradation
11. expose a deterministic **text-processing identity**
12. derive bind settings from `builder_config.jsonc`

---

## 6. What the API must not do

This API must **not** become a builder-management surface.

Do **not** add:
- `/health`
- `/model-info`
- build orchestration endpoints
- ingestion-run endpoints
- admin endpoints
- auth
- database endpoints
- corpus embedding endpoints
- debugging UI
- broad metadata inspection endpoints
- multiple embedding endpoints

This remains a **single query-only API**.

---

## 7. One endpoint only

Only one endpoint is allowed:

```text
POST /embed-query
```

No other endpoint is required.

---

## 8. High-level contract shape

The API uses:
- one request envelope
- one response envelope
- one or more query items per request
- one result item per request item

### Budget-saving implementation rule
For v1 implementation:
- the builder may use **only `text`** to compute embeddings
- all non-text input properties are **validated and echoed**
- route-specific or task-shape-specific embedding behavior is **not required in v1**

This keeps the implementation cheap while still defining a strong shared contract.

---

## 9. Final endpoint contract

## 9.1 Endpoint
```text
POST /embed-query
```

### Request purpose
- MCP submits one or more query items
- each query item corresponds to one harness-controlled retrieval unit or lower-level MCP query

### Response purpose
- builder returns:
  - vectors
  - runtime embedding metadata
  - vector-space identity
  - text-processing identity
  - per-item echoed identity
  - per-item preprocessing/quality diagnostics

This is enough for MCP to:
- correlate vectors to chunks
- validate compatibility
- detect semantic degradation
- report degradation to the harness

---

## 10. Request schema

## 10.1 Envelope

```python
class EmbedQueryRequest(BaseModel):
    schema_version: str
    request_id: str
    task_id: str | None = None
    caller: str = "mcp"
    purpose: str = "harness_retrieval"
    items: list["EmbedQueryItem"]
```

### Field meanings

- `schema_version`
  - version of this API contract
  - initial value: `"1.1"`

- `request_id`
  - unique MCP correlation id for the embedding request

- `task_id`
  - optional harness task id
  - correlates chunk embeddings belonging to one harness task

- `caller`
  - fixed caller identity
  - recommended: `"mcp"`

- `purpose`
  - recommended values:
    - `"harness_retrieval"`
    - `"direct_search"`
  - v1 builder behavior does not change by purpose, but the field is part of the shared contract

- `items`
  - one or more query items

## 10.2 Per-item schema

```python
class EmbedQueryItem(BaseModel):
    item_id: str
    chunk_id: str | None = None
    chunk_type: str | None = None
    query_kind: str
    retrieval_role_hint: str | None = None
    text: str
    structured_scopes: dict | None = None
    task_shape: dict | None = None
```

### Field meanings

- `item_id`
  - required unique id within the request
  - used to correlate returned vector to the caller-side query item

- `chunk_id`
  - optional harness chunk id

- `chunk_type`
  - optional harness chunk type
  - expected values:
    - `core_task`
    - `constraint`
    - `risk`
    - `pattern`
    - `similar_case`

- `query_kind`
  - required MCP query route
  - expected values aligned with MCP:
    - `CoreTask`
    - `Constraint`
    - `Risk`
    - `Pattern`
    - `SimilarCase`
    - `Summary`
    - `Details`

- `retrieval_role_hint`
  - optional role hint that MCP expects semantic search to use
  - examples:
    - `CoreTask`
    - `Constraint`
    - `normalized_retrieval_text`

- `text`
  - actual query text to embed
  - this is the only field used for vector generation in v1

- `structured_scopes`
  - optional scope payload
  - builder does not need to use it for vector generation in v1

- `task_shape`
  - optional structured similar-case payload
  - builder does not need to use it for vector generation in v1

---

## 11. Request validation rules

The API must validate:

1. `items` must not be empty
2. every `item_id` must be non-empty
3. every `query_kind` must be non-empty
4. every `text` must be non-empty after trimming
5. `item_id` values must be unique within one request
6. recommended hard cap: `64` items per request
7. recommended max chars per `text`: `4096`

If caller needs more than that, caller must batch.

---

## 12. Response schema

## 12.1 Envelope

```python
class EmbedQueryResponse(BaseModel):
    schema_version: str
    request_id: str
    task_id: str | None = None
    provider: str
    model_name: str
    model_version: str | None
    normalize_embeddings: bool
    dimension: int
    fallback_mode: bool
    text_processing_id: str
    vector_space_id: str
    items: list["EmbedQueryResultItem"]
    warnings: list[str] = []
```

### New required fields beyond the previous revision

#### `text_processing_id`
A deterministic identity for the text-preprocessing regime used before embedding.

Purpose:
- compatibility is not only model metadata
- if input text normalization or truncation policy changes, semantic space behavior changes too
- MCP needs this for correctness and diagnostics

Recommended v1 composition:
- whitespace normalization behavior
- truncation policy identity
- any explicit lowercasing/normalization regime
- current example:
  - `"raw-text|trim:true|truncate:model-default"`

Even if the current builder uses near-raw text behavior, this field must still be present.

#### `vector_space_id`
A deterministic identity string for the vector space.

Required composition:
- `provider`
- `model_name`
- `model_version`
- `normalize_embeddings`
- `dimension`
- `fallback_mode`
- `text_processing_id`

Example:
```text
sentence_transformers|D:/models/all-MiniLM-L6-v2|null|norm:true|dim:384|fallback:false|text:raw-text|trim:true|truncate:model-default
```

This is stronger than the previous revision because it includes text-processing identity.

## 12.2 Per-item response schema

```python
class EmbedQueryResultItem(BaseModel):
    item_id: str
    chunk_id: str | None = None
    chunk_type: str | None = None
    query_kind: str
    retrieval_role_hint: str | None = None
    vector: list[float]
    input_char_count: int
    effective_text_char_count: int
    truncated: bool
    warnings: list[str] = []
```

### Why these added fields are needed

#### `chunk_type`
Needed for deterministic harness diagnostics in mixed chunk batches.

#### `retrieval_role_hint`
Needed because `query_kind` and the intended DB embedding role are not always identical. MCP must be able to correlate the returned vector to the intended role hint when deciding diagnostics.

#### `input_char_count`
Original caller text length.

#### `effective_text_char_count`
Length of text actually passed into embedding after any trimming or preprocessing.

#### `truncated`
Whether the item’s text was truncated/reduced before embedding.

This is critical for accurate semantic diagnostics in the harness flow. A chunk can be “compatible” but still semantically weakened because the effective input was reduced.

#### `warnings`
Per-item warnings are needed because only one item in a batch may be degraded.

Examples:
- `"text-truncated-before-embedding"`
- `"hashing-fallback-active"`
- `"retrieval-role-hint-ignored-by-builder-v1"`

---

## 13. Response validation rules

The API must ensure:
1. `len(items)` equals request item count
2. item ordering matches request item ordering
3. every result `item_id` matches the request item
4. every vector length matches `dimension`
5. all vectors have identical dimension
6. `effective_text_char_count <= input_char_count`
7. `truncated == true` only when the effective embedded text is shorter than the original caller text for API-side preprocessing purposes

No partial success response is allowed.

---

## 14. Example request

```json
{
  "schema_version": "1.1",
  "request_id": "req-2026-04-13-001",
  "task_id": "task-2026-04-13-001",
  "caller": "mcp",
  "purpose": "harness_retrieval",
  "items": [
    {
      "item_id": "i1",
      "chunk_id": "c1",
      "chunk_type": "core_task",
      "query_kind": "CoreTask",
      "retrieval_role_hint": "CoreTask",
      "text": "year switching for yearly weighted card",
      "structured_scopes": {
        "domains": ["mingpan"],
        "modules": ["yearly-weighted-card"],
        "layers": ["ui"]
      }
    },
    {
      "item_id": "i2",
      "chunk_id": "c2",
      "chunk_type": "constraint",
      "query_kind": "Constraint",
      "retrieval_role_hint": "Constraint",
      "text": "engine logic must not change",
      "structured_scopes": {
        "domains": ["mingpan"],
        "layers": ["ui", "api", "engine"]
      }
    },
    {
      "item_id": "i3",
      "chunk_id": "c5",
      "chunk_type": "similar_case",
      "query_kind": "SimilarCase",
      "retrieval_role_hint": "SimilarCase",
      "text": "ui-only card refresh with no engine change and placement consistency risk",
      "task_shape": {
        "task_type": "ui-change",
        "feature_shape": "card-refresh",
        "engine_change_allowed": false,
        "likely_layers": ["ui", "api"],
        "risk_signals": ["placement consistency", "async refresh"]
      }
    }
  ]
}
```

---

## 15. Example response

```json
{
  "schema_version": "1.1",
  "request_id": "req-2026-04-13-001",
  "task_id": "task-2026-04-13-001",
  "provider": "sentence_transformers",
  "model_name": "D:/models/all-MiniLM-L6-v2",
  "model_version": null,
  "normalize_embeddings": true,
  "dimension": 384,
  "fallback_mode": false,
  "text_processing_id": "raw-text|trim:true|truncate:model-default",
  "vector_space_id": "sentence_transformers|D:/models/all-MiniLM-L6-v2|null|norm:true|dim:384|fallback:false|text:raw-text|trim:true|truncate:model-default",
  "items": [
    {
      "item_id": "i1",
      "chunk_id": "c1",
      "chunk_type": "core_task",
      "query_kind": "CoreTask",
      "retrieval_role_hint": "CoreTask",
      "vector": [0.0123, -0.0456, 0.0081],
      "input_char_count": 36,
      "effective_text_char_count": 36,
      "truncated": false,
      "warnings": []
    },
    {
      "item_id": "i2",
      "chunk_id": "c2",
      "chunk_type": "constraint",
      "query_kind": "Constraint",
      "retrieval_role_hint": "Constraint",
      "vector": [0.0021, -0.0112, 0.0972],
      "input_char_count": 28,
      "effective_text_char_count": 28,
      "truncated": false,
      "warnings": []
    },
    {
      "item_id": "i3",
      "chunk_id": "c5",
      "chunk_type": "similar_case",
      "query_kind": "SimilarCase",
      "retrieval_role_hint": "SimilarCase",
      "vector": [0.0421, -0.0099, 0.0011],
      "input_char_count": 67,
      "effective_text_char_count": 67,
      "truncated": false,
      "warnings": []
    }
  ],
  "warnings": []
}
```

---

## 16. Compatibility contract with MCP

The MCP server must use the response for:
- vector extraction
- semantic compatibility checks
- semantic quality diagnostics
- harness-visible degradation reporting

## 16.1 Minimum MCP compatibility checks
MCP should verify:
- `dimension` matches DB vectors
- `vector_space_id` or equivalent raw metadata is compatible with DB embeddings
- `fallback_mode` is acceptable
- `normalize_embeddings` matches expected DB build behavior
- `text_processing_id` is acceptable for the stored embedding regime

## 16.2 Why `text_processing_id` is required
The MCP design requires compatibility before semantic search. That is not only about model identity. If the text-processing regime changes, semantic behavior can change even when the model is the same.

So `text_processing_id` must be part of the API contract.

## 16.3 Why per-item truncation diagnostics are required
The MCP design also requires the harness to be able to tell when semantic retrieval is degraded.

A query item may be:
- semantically compatible
- but still weakened because input text was truncated or reduced before embedding

Without `truncated` and `effective_text_char_count`, the harness cannot see that.

So these fields are required for accurate harness diagnostics.

---

## 17. Builder-side implementation design

## 17.1 Reuse existing `EmbeddingProvider`
The API server must reuse:
- `app.embedding_provider.EmbeddingProvider`

Do not create a second embedding implementation.

## 17.2 Add one API module
Create or revise:

```text
app/query_api.py
```

Responsibilities:
- load config
- create one shared `EmbeddingProvider`
- expose `POST /embed-query`
- compute model metadata and dimension
- compute `text_processing_id`
- compute `vector_space_id`
- validate request envelopes
- return JSON responses

## 17.3 Compute dimension
On API startup:
- embed one short fixed string such as `"ping"`
- use resulting vector length as canonical `dimension`

## 17.4 Runtime metadata
Use `EmbeddingProvider.model_metadata()` for:
- `model_name`
- `model_version`

Set:
- `provider = config.embedding.provider`
- `normalize_embeddings = config.embedding.normalize_embeddings`
- `fallback_mode = (model_name == "__hashing_fallback__")`

## 17.5 Text-processing identity
Define one deterministic helper in the API module that returns the v1 text-processing identity string.

Even if preprocessing is simple in v1, the identity string must still be explicit.

---

## 18. Runtime rules

### 18.1 Local-only default
Default bind must be:
- `127.0.0.1`

### 18.2 No auth
No authentication is needed.

### 18.3 No build orchestration
The API must not start DB builds.

### 18.4 No DB dependency
`POST /embed-query` must work without PostgreSQL connectivity.

### 18.5 Builder config required
The API still depends on builder config because:
- embedding behavior comes from it
- API host/port come from it

---

## 19. Error behavior

### 19.1 Invalid request
Return normal FastAPI 400/422 behavior.

### 19.2 Model init failure
Fail API startup clearly.

### 19.3 Fallback mode
If hashing fallback is active:
- API remains usable
- `fallback_mode = true`
- `vector_space_id` must reflect fallback mode
- top-level `warnings` should include a clear warning

### 19.4 Batch failure
Do not return partial success.

---

## 20. Budget-saving implementation guidance

To keep cost low:

1. reuse `EmbeddingProvider` exactly
2. add only one Python module for the API
3. add only two lightweight packages:
   - `fastapi`
   - `uvicorn`
4. do not add auth
5. do not add admin APIs
6. do not add database APIs
7. do not add background workers
8. do not add streaming
9. do not add gRPC or protobuf
10. keep one endpoint only
11. let non-text request properties be validated/echoed, not behavior-changing, in v1

This gives a stronger shared contract without expanding runtime complexity much.

---

## 21. Recommended MCP usage pattern

The .NET MCP server should use this API in `LocalHttpQueryEmbeddingService`.

Recommended flow:
1. build request envelope with:
   - `request_id`
   - `task_id`
   - `items[]`
2. send to `POST /embed-query`
3. receive vectors plus runtime metadata and echoed item identities
4. compare `vector_space_id`, `text_processing_id`, and raw metadata with DB metadata if compatibility validation is enabled
5. if compatible, allow semantic search
6. if per-item truncation or warnings indicate degraded semantic quality, record that in harness-visible diagnostics

This is accurate enough for the harness-controlled flow and still budget-saving.

---

## 22. Final recommendation

The embedding API contract should be revised to this **accuracy-complete shared contract**.

It keeps:
- one endpoint only
- FastAPI
- JSON only
- budget-saving implementation
- direct reuse of `EmbeddingProvider`

But it adds the missing pieces needed for full MCP/harness semantic-search correctness:
- `text_processing_id`
- stronger `vector_space_id`
- per-item `chunk_type`
- per-item `retrieval_role_hint`
- per-item `input_char_count`
- per-item `effective_text_char_count`
- per-item `truncated`
- per-item `warnings`

This is the minimal contract revision needed to make the embedding API sufficient for **accurate semantic search in the harness-controlled flow**.
