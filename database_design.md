# Revised Database Design for Embedding Builder Compatibility and Future Context Extensibility

## 1. Why the Current Design Needs Revision

The current `database_design` is already aligned with the harness/MCP retrieval flow in an important way: it supports structured retrieval from task-derived chunks, exact filtering, authority/lifecycle filtering, and separate query paths for `core_task`, `constraint`, `risk`, `pattern`, and `similar_case` retrieval. That part should be preserved.

However, it is not yet strong enough for the embedding builder design and the long-term extensibility requirements.

The main issues are:

1. `memory_type` is too rigid as the main semantic classifier.
   The current design centers retrieval around a fixed type set such as `decision / best_practice / antipattern / similar_case / constraint`. That is good for MCP output channels, but not sufficient as the main storage semantics when future contexts may include:
   - past code commits
   - lessons learned
   - migration notes
   - examples
   - incidents
   - code-structure tracking
   - future context families not yet known

2. The current design does not model dynamic multi-label categorization strongly enough.
   The embedding builder design requires dynamic categories, multi-label classification, and category confidence. The current design has `memory_tags`, but tags alone are too weak to represent authoritative, confidence-bearing semantic labels.

3. The current design does not explicitly support hierarchical span persistence.
   The builder design requires variable granularity and parent/child memory units across file, section, subsection, and paragraph-group levels. The current schema does not make that a first-class concern.

4. The current design is too light on ingestion provenance.
   The builder needs incremental rebuild support using file hashes, builder version, model version, normalization version, and extraction runs. The current design has source traceability, but not builder-grade ingestion provenance.

5. The current design does not reserve a clean extension path for future structure-oriented context.
   Future retrieval may need code structure tracking, symbol relationships, ownership maps, dependency edges, and architecture topology. The schema should leave a first-class but optional path for those without forcing a redesign later.

Because of those gaps, the database design should be revised before designing the final embedding-builder prompts.

---

## 2. Design Goals for the Revision

The revised design must satisfy all of the following simultaneously:

1. Fit the harness-controlled retrieval system driven by prompt task descriptions.
2. Preserve high relevance and retrieval accuracy.
3. Support future context families without schema churn.
4. Remain practical for a Python embedding builder.
5. Stay compatible with MCP result channels such as decisions, anti-patterns, best practices, similar cases, and constraints.

---

## 3. Core Revision Principle

The key change is:

- keep a **stable retrieval class** for MCP-facing output behavior
- add a **dynamic semantic label layer** for builder-assigned meaning
- add **hierarchical span storage**
- add **builder provenance and extraction lineage**
- add an **optional structure graph extension** for future code-structure contexts

In other words:

- do **not** delete the current retrieval-oriented concepts
- do **not** make dynamic categories replace everything
- instead, split storage semantics into two levels:
  1. stable retrieval behavior
  2. extensible semantic meaning

This keeps retrieval predictable while making the corpus extensible.

---

## 4. Revised Storage Model

Use PostgreSQL + pgvector as the preferred first implementation target.

The revised database still uses a hybrid model:

- relational tables for filtering, provenance, hierarchy, labels, lifecycle, links, and query control
- pgvector for semantic retrieval

That remains the best fit for:

- structured filtering from harness chunk metadata
- retrieval-class separation in MCP
- dynamic categories from the embedding builder
- builder-driven incremental rebuilds
- future graph-like extension for structure retrieval

---

## 5. Revised Conceptual Model

The schema should center on **Knowledge Items**, not only on fixed `memory_type` records.

A knowledge item is a retrieval-ready unit produced by ingestion.

A knowledge item may represent:

- a best practice
- an anti-pattern
- a decision
- a constraint
- a similar case
- a lesson learned
- an implementation note
- a feature description
- a migration note
- an incident insight
- an example
- a commit-derived insight
- a code-structure fact
- another future context family

The system should not require a new top-level enum every time a new family appears.

---

## 6. Revised Core Tables

## 6.1 Table: `knowledge_items`

### Purpose
Store the main retrieval units produced by the embedding builder.

### Key columns

| Column | Type | Purpose |
|---|---|---|
| id | uuid | Primary key |
| retrieval_class | text | Stable MCP-facing class: `decision`, `best_practice`, `antipattern`, `similar_case`, `constraint`, `reference`, `structure` |
| title | text | Short stable title |
| summary | text | Concise retrieval-facing summary |
| details | text nullable | Longer explanation or normalized detail text |
| normalized_retrieval_text | text | Primary semantic retrieval text |
| span_level | text | `file`, `section`, `subsection`, `paragraph_group`, `synthetic` |
| authority_level | integer | Numeric authority rank |
| authority_label | text | `draft`, `observed`, `reviewed`, `approved`, `canonical` |
| status | text | `active`, `deprecated`, `superseded`, `archived` |
| confidence | numeric nullable | Overall record confidence |
| domain | text nullable | Project/domain grouping |
| module | text nullable | Module grouping |
| feature | text nullable | Feature grouping |
| source_type | text nullable | `design_doc`, `adr`, `pr`, `commit`, `incident`, `note`, `spec`, `code_map`, etc. |
| parent_item_id | uuid nullable | Parent knowledge item for hierarchy |
| valid_from | timestamp nullable | Optional activation time |
| valid_to | timestamp nullable | Optional end time |
| superseded_by | uuid nullable | Replacement link |
| created_at | timestamp | Created time |
| updated_at | timestamp | Last updated time |
| ingestion_run_id | uuid nullable | Trace back to builder run |
| source_artifact_id | uuid nullable | Trace back to source artifact |
| notes | text nullable | Internal notes |

### Why this change matters

- `retrieval_class` preserves MCP result channels and retrieval behavior.
- `normalized_retrieval_text` becomes first-class instead of being hidden only in embedding-side text rows.
- `span_level` and `parent_item_id` make hierarchical storage explicit.
- `source_type` becomes much more extensible.

### Indexes

- `(retrieval_class, status, authority_level)`
- `(domain, module, feature)`
- `(source_type, status)`
- `(parent_item_id)`
- `(superseded_by)`
- `(updated_at)`
- `(ingestion_run_id)`

---

## 6.2 Table: `knowledge_labels`

### Purpose
Store dynamic semantic labels with confidence and provenance.

This table is the main fix for extensibility.

### Key columns

| Column | Type | Purpose |
|---|---|---|
| id | uuid | Primary key |
| knowledge_item_id | uuid | FK |
| label | text | Dynamic label such as `project_history`, `implementation`, `lesson_learned`, `migration_note`, `commit_context` |
| label_role | text | `primary`, `secondary`, `system`, `derived` |
| confidence | numeric nullable | Confidence for the label |
| source_method | text | `front_matter`, `heading_rule`, `rule_based`, `llm_inference`, `manual`, `imported` |
| created_at | timestamp | Audit time |

### Why this change matters

This table allows the builder to assign dynamic multi-label categories without changing the core schema.

Examples:

- `best_practice`
- `anti_pattern`
- `project_history`
- `feature_description`
- `implementation`
- `lesson_learned`
- `migration_note`
- `incident`
- `example`
- `commit_context`
- `structure_fact`

### Indexes

- `(label, label_role)`
- `(knowledge_item_id)`
- `(label, confidence)`

---

## 6.3 Table: `knowledge_scopes`

### Purpose
Store normalized scope tokens used by harness chunk scopes and MCP filtering.

### Key columns

| Column | Type | Purpose |
|---|---|---|
| id | uuid | Primary key |
| knowledge_item_id | uuid | FK |
| scope_type | text | `domain`, `layer`, `module`, `feature`, `concern`, `language`, `service`, `repo`, `team`, `symbol` |
| scope_value | text | Scope token |
| weight | numeric nullable | Optional scope strength |

### Why this change matters

The harness chunk model depends on structured scopes. This table should stay central and should become slightly more extensible for repo/service/symbol-level matching.

### Indexes

- `(scope_type, scope_value)`
- `(knowledge_item_id)`

---

## 6.4 Table: `knowledge_tags`

### Purpose
Store lightweight retrieval tags and lexical hints.

Tags remain useful but are no longer overloaded as semantic labels.

### Key columns

| Column | Type | Purpose |
|---|---|---|
| id | uuid | Primary key |
| knowledge_item_id | uuid | FK |
| tag | text | Retrieval tag |
| tag_source | text nullable | Where the tag came from |

### Indexes

- `(tag)`
- `(knowledge_item_id)`

---

## 6.5 Table: `knowledge_relations`

### Purpose
Link related items across guidance, history, examples, incidents, and structure.

### Key columns

| Column | Type | Purpose |
|---|---|---|
| id | uuid | Primary key |
| from_item_id | uuid | FK |
| to_item_id | uuid | FK |
| relation_type | text | `related`, `supports`, `conflicts_with`, `depends_on`, `exemplifies`, `derived_from`, `summarizes`, `same_source_family` |
| strength | numeric nullable | Optional relation weight |
| created_at | timestamp | Audit time |

### Why this matters

This table is especially important when one commit-derived lesson supports an anti-pattern, or when a structure fact supports a constraint.

### Indexes

- `(from_item_id)`
- `(to_item_id)`
- `(relation_type)`

---

## 6.6 Table: `source_artifacts`

### Purpose
Represent the original source objects ingested by the embedding builder.

A source artifact may be:

- markdown file
- ADR
- design doc
- PR description
- commit message bundle
- incident report
- generated structure map
- future source family

### Key columns

| Column | Type | Purpose |
|---|---|---|
| id | uuid | Primary key |
| source_type | text | `markdown`, `adr`, `spec`, `pr`, `commit`, `incident`, `code_map`, etc. |
| source_ref | text | Stable external/internal identifier |
| source_path | text nullable | File path or logical path |
| repo_name | text nullable | Repository name |
| branch_name | text nullable | Branch if relevant |
| commit_sha | text nullable | Commit SHA if relevant |
| title | text nullable | Display title |
| artifact_hash | text nullable | Content hash for incremental rebuild |
| observed_at | timestamp nullable | Source timestamp |
| created_at | timestamp | Row creation time |
| updated_at | timestamp | Last refresh time |

### Why this matters

The current `memory_sources` concept is too small for builder-grade provenance. This new table is the canonical source registry.

### Indexes

- `(source_type, source_ref)`
- `(artifact_hash)`
- `(repo_name, commit_sha)`
- `(source_path)`

---

## 6.7 Table: `source_segments`

### Purpose
Represent exact source spans that produced a knowledge item.

### Key columns

| Column | Type | Purpose |
|---|---|---|
| id | uuid | Primary key |
| source_artifact_id | uuid | FK |
| heading_path | jsonb nullable | Section path for markdown |
| start_offset | integer nullable | Character start |
| end_offset | integer nullable | Character end |
| start_line | integer nullable | Line start |
| end_line | integer nullable | Line end |
| span_level | text | `file`, `section`, `subsection`, `paragraph_group`, `synthetic` |
| segment_hash | text nullable | Span identity hash |

### Why this matters

The builder design explicitly requires variable span size and hierarchy. This table lets the system trace each knowledge item back to exact source spans.

### Indexes

- `(source_artifact_id)`
- `(segment_hash)`
- `(span_level)`

---

## 6.8 Table: `knowledge_item_segments`

### Purpose
Map knowledge items to one or more source segments.

### Key columns

| Column | Type | Purpose |
|---|---|---|
| knowledge_item_id | uuid | FK |
| source_segment_id | uuid | FK |
| role | text | `primary_origin`, `supporting_origin`, `merged_origin` |

### Primary key

- `(knowledge_item_id, source_segment_id, role)`

---

## 6.9 Table: `case_shapes`

### Purpose
Preserve structured similar-case matching.

This table should stay, because similar-case retrieval is one of the strongest parts of the current design.

### Key columns

| Column | Type | Purpose |
|---|---|---|
| id | uuid | Primary key |
| knowledge_item_id | uuid | FK |
| task_type | text | `ui-change`, `api-change`, `bugfix`, `refactor`, etc. |
| feature_shape | text | `card-refresh`, `selector-add`, `async-load`, etc. |
| engine_change_allowed | boolean nullable | Whether core logic changes were allowed |
| likely_layers | jsonb | Layer list |
| risk_signals | jsonb | Risk cue list |
| complexity | text nullable | `low`, `medium`, `high` |

### Indexes

- `(task_type, feature_shape)`
- `(engine_change_allowed)`
- JSON indexes on `likely_layers` and `risk_signals` if supported

---

## 6.10 Table: `retrieval_profiles`

### Purpose
Store specialized retrieval texts per knowledge item for different prompt-derived query routes.

### Why this is worth adding

The harness does not retrieve with one raw query. It retrieves via chunk purposes such as `core_task`, `constraint`, `risk`, `pattern`, and `similar_case`.

A single item may need different search phrasing for different routes.

### Key columns

| Column | Type | Purpose |
|---|---|---|
| id | uuid | Primary key |
| knowledge_item_id | uuid | FK |
| profile_type | text | `summary`, `details`, `core_task`, `constraint`, `risk`, `pattern`, `similar_case` |
| profile_text | text | Retrieval text optimized for that route |
| created_at | timestamp | Audit time |

### Why this matters

This makes prompt-task-based retrieval more accurate than using only a generic embedding text.

### Indexes

- `(knowledge_item_id)`
- `(profile_type)`

---

## 6.11 Table: `knowledge_embeddings`

### Purpose
Store embeddings for normalized retrieval text and specialized profiles.

### Key columns

| Column | Type | Purpose |
|---|---|---|
| id | uuid | Primary key |
| knowledge_item_id | uuid | FK |
| profile_id | uuid nullable | FK to retrieval_profiles if embedding is profile-specific |
| embedding_role | text | `normalized_retrieval_text`, `summary`, `details`, `core_task`, `constraint`, `risk`, `pattern`, `similar_case` |
| embedding_text | text | Embedded text |
| embedding | vector | Vector |
| model_name | text | Embedding model |
| model_version | text nullable | Model version |
| created_at | timestamp | Audit time |

### Indexes

- vector index on `embedding`
- `(knowledge_item_id)`
- `(embedding_role)`
- `(profile_id)`

---

## 6.12 Table: `ingestion_runs`

### Purpose
Track builder runs for reproducibility and incremental rebuilds.

### Key columns

| Column | Type | Purpose |
|---|---|---|
| id | uuid | Primary key |
| builder_version | text | Builder code version |
| classifier_version | text nullable | Labeling/classification version |
| embedding_model | text | Embedding model |
| embedding_model_version | text nullable | Version |
| normalization_version | text | Retrieval-text normalization version |
| started_at | timestamp | Start time |
| finished_at | timestamp nullable | Finish time |
| status | text | `running`, `completed`, `failed`, `partial` |
| notes | text nullable | Run notes |

### Why this matters

This is necessary for reliable incremental rebuild logic.

### Indexes

- `(started_at)`
- `(status)`

---

## 6.13 Table: `artifact_build_states`

### Purpose
Track per-source rebuild state.

### Key columns

| Column | Type | Purpose |
|---|---|---|
| source_artifact_id | uuid | FK |
| last_ingestion_run_id | uuid | FK |
| artifact_hash | text | Last built hash |
| builder_version | text | Last builder version |
| classifier_version | text nullable | Last classifier version |
| embedding_model_version | text nullable | Last embedding model version |
| normalization_version | text | Last normalization version |
| last_built_at | timestamp | Last successful build |

### Primary key

- `(source_artifact_id)`

---

## 6.14 Optional Future Extension: `structure_entities`

### Purpose
Support future code-structure tracking without redesigning the core schema.

This table is optional now, but the design should reserve it explicitly.

### Key columns

| Column | Type | Purpose |
|---|---|---|
| id | uuid | Primary key |
| entity_type | text | `repo`, `project`, `module`, `package`, `namespace`, `class`, `method`, `api`, `table`, `queue` |
| canonical_name | text | Stable identifier |
| repo_name | text nullable | Repository |
| file_path | text nullable | File path |
| language | text nullable | Language |
| metadata | jsonb nullable | Flexible structure metadata |
| created_at | timestamp | Audit time |
| updated_at | timestamp | Audit time |

### Indexes

- `(entity_type, canonical_name)`
- `(repo_name, file_path)`

---

## 6.15 Optional Future Extension: `knowledge_item_entities`

### Purpose
Link knowledge items to structure entities.

### Key columns

| Column | Type | Purpose |
|---|---|---|
| knowledge_item_id | uuid | FK |
| structure_entity_id | uuid | FK |
| relation_type | text | `about`, `touches`, `owned_by`, `located_in`, `depends_on` |

### Primary key

- `(knowledge_item_id, structure_entity_id, relation_type)`

---

## 7. Retrieval Semantics in the Revised Design

## 7.1 Stable retrieval behavior

MCP should continue to retrieve through stable output classes:

- decisions
- best practices
- anti-patterns
- similar cases
- constraints
- optional future channels like references or structure facts

That is why `retrieval_class` remains explicit.

## 7.2 Dynamic semantic meaning

The actual semantic richness should come from `knowledge_labels`.

Examples:

- a record with `retrieval_class = antipattern` may also have labels:
  - `lesson_learned`
  - `incident`
  - `frontend_inference`

- a record with `retrieval_class = best_practice` may also have labels:
  - `implementation`
  - `example`
  - `migration_note`

- a record with `retrieval_class = structure` may also have labels:
  - `code_structure_tracking`
  - `dependency_fact`

This split is what makes the schema extensible without losing retrieval predictability.

---

## 8. How This Revised Design Meets the Three Requirements

## 8.1 Requirement 1: fit the system for context retrieval from prompt task descriptions

This is satisfied by preserving and strengthening:

- structured scopes
- case shapes
- retrieval profiles by route
- embeddings tied to retrieval-purpose text
- authority/status filters
- stable retrieval classes for MCP result separation

This directly matches the harness flow where prompt descriptions become chunked retrieval requests.

## 8.2 Requirement 2: provide high relevant context and accurate results

This is improved by adding:

- normalized retrieval text as a first-class field
- profile-specific retrieval text
- hierarchical span storage
- exact source-span provenance
- label confidence
- builder-run provenance for reliable rebuilds

These changes improve both precision and recall.

## 8.3 Requirement 3: be extensible for best practices, commits, lessons learned, anti-patterns, future code structure tracking, and other contexts

This is satisfied by:

- replacing fixed semantic dependence on `memory_type` with a split model of `retrieval_class + dynamic labels`
- generalizing source artifacts and provenance
- reserving optional structure entity tables
- not requiring enum expansion for each future family

This is the most important revision.

---

## 9. Migration Guidance from the Current Design

The current design does not need a destructive rewrite. It should be evolved.

### Keep

- hybrid relational + vector architecture
- authority model
- lifecycle model
- scopes
- tags
- case-shape table
- relationship table concept
- retrieval-path thinking

### Revise

- `memory_records` → `knowledge_items`
- `memory_type` → `retrieval_class`
- make `normalized_retrieval_text` first-class
- add `span_level` and `parent_item_id`
- replace lightweight semantic overloading of tags with `knowledge_labels`
- replace thin `memory_sources` with `source_artifacts + source_segments`
- add `ingestion_runs` and `artifact_build_states`
- optionally add structure extension tables

### Compatibility note

MCP can still expose the same output shape:

```json
{
  "decisions": [],
  "best_practices": [],
  "antipatterns": [],
  "similar_cases": [],
  "constraints": []
}
```

The change is mostly in ingestion/storage richness, not in the external retrieval contract.

---

## 10. Final Recommendation

Yes, the database design should be changed before finalizing the embedding-builder prompts.

The right change is not to abandon the current design, but to evolve it into a richer schema with:

1. `knowledge_items` as the core retrieval unit
2. `retrieval_class` for stable MCP-facing output classes
3. `knowledge_labels` for dynamic multi-label semantics with confidence
4. hierarchical span storage and parent/child knowledge items
5. source-artifact and source-segment provenance
6. builder-run and rebuild provenance
7. optional future structure-graph extension

That revised schema fits the harness/MCP architecture, improves retrieval accuracy, and gives a durable path for future context families such as commits, lessons learned, and code structure tracking.
