
# Consolidated Complete Embedding Builder Design for Long-Term Memory

## 1. Purpose

This document defines the complete **embedding database builder component** for the long-term memory system.

The builder is responsible for taking many `.md` and `.txt` files and producing retrieval-ready database contents for the harness + MCP memory retrieval architecture.

This consolidated version is intended to be the canonical design. It preserves the detailed functional scope from the original builder design and also integrates the category catalog / ontology design directly into the same document.

This component must:

1. use **Python**
2. support **high-accuracy relevance** for context querying
3. remain compatible with the larger system:
   - harness as control plane
   - MCP as retrieval layer
   - worker as execution layer
4. infer and assign categories such as:
   - best practice
   - project history
   - anti-pattern
   - implementation
   - feature description
   - decision
   - constraint
   - incident
   - example
   - migration note
   - lesson learned
   - commit context
   - structure fact
5. support **dynamic categories**
6. support **variable span size**
   - sometimes a few sentences
   - sometimes a full section
   - sometimes a full markdown file
7. write output into the revised long-term memory schema using:
   - `knowledge_items`
   - `knowledge_labels`
   - `knowledge_scopes`
   - `knowledge_tags`
   - `source_artifacts`
   - `source_segments`
   - `knowledge_item_segments`
   - `retrieval_profiles`
   - `knowledge_embeddings`
   - `ingestion_runs`
   - `artifact_build_states`
   - optional future tables such as `structure_entities` and `knowledge_item_entities`
8. preserve enough provenance and hierarchy to support future contexts such as:
   - best practices
   - anti-patterns
   - lessons learned
   - code commits
   - migration notes
   - postmortems
   - code structure tracking
   - ownership maps
   - symbol and dependency relations
9. use a **category catalog / ontology** so that category behavior is explicit, governable, versioned, and configurable rather than buried in code

This builder is not the retrieval engine.  
Its job is to create a database that enables accurate retrieval later.

---

## 2. Why a Separate Builder Is Needed

The retrieval layer can only be accurate if the stored memory is:

- structurally segmented
- semantically meaningful
- richly labeled
- embedding-ready
- retrieval-friendly
- traceable back to the source
- consistent with authority and lifecycle rules
- normalized enough to support stable query behavior

Raw markdown or text files are not enough.

A source file often mixes:

- project history
- best practices
- anti-patterns
- implementation steps
- feature descriptions
- examples
- decisions
- constraints
- migration notes
- lessons learned
- references to modules, APIs, or services
- warnings or exceptions to the rule

If the builder simply stores a full file as one vectorized document, retrieval quality will be poor because:

- the semantic center is mixed
- categories are blended
- relevant fine-grained context is hidden inside larger text
- a task query may match the wrong portion of the file
- MCP cannot reliably separate guidance from examples or warnings
- future context classes such as commit-derived notes or structure-aware knowledge cannot be attached cleanly
- database lifecycle and provenance become weak

Therefore, the builder must perform:

1. file discovery
2. structural parsing
3. hierarchical segmentation
4. dynamic category inference
5. category resolution against a catalog / ontology
6. optional summarization / normalization
7. retrieval-profile generation
8. embedding generation
9. database persistence
10. provenance persistence
11. incremental rebuild-state persistence
12. quality validation

---

## 3. Design Goals

The builder must optimize for these properties.

### 3.1 High relevance
The stored memory should make it easy for MCP tools to retrieve exactly the context needed for a task.

### 3.2 High recall without overload
The database should preserve enough fine-grained content that relevant context is not lost, but not explode into useless tiny fragments.

### 3.3 Dynamic categorization
The system must support evolving categories and metadata labels, not only a fixed enum.

### 3.4 Variable granularity
The builder must support category spans at different levels:
- paragraph/group level
- subsection level
- full section level
- whole file level

### 3.5 Compatibility with harness/MCP system
The builder output must support:
- chunk-based retrieval
- scope filtering
- authority filtering
- similar-case matching
- context pack assembly

### 3.6 Compatibility with revised database design
The builder output must map naturally to the revised storage model:
- stable retrieval classes for predictable query behavior
- dynamic labels for extensibility
- explicit scopes and tags for exact filtering
- source artifacts and segments for traceability
- retrieval profiles for chunk-route-specific matching
- ingestion runs and build states for incremental rebuilds

### 3.7 Extensibility for future contexts
The builder must allow future addition of contexts such as:
- best practices
- lessons learned
- code commits
- anti-patterns
- implementation notes
- structure entities
- code ownership or module topology

without needing a redesign of the ingestion architecture.

### 3.8 Strong governance over categories
The builder must not bury category semantics in scattered hardcoded dictionaries. Category meaning, category-to-retrieval-class mapping, confidence thresholds, inheritance, synonyms, and extraction hints should be governable.

### 3.9 Local-first operation
The builder must work with local embeddings and a local LLM. If the configured local LLM path does not exist, the system may bootstrap by downloading a configured model locally and then use that local copy afterward.

### 3.10 Operational simplicity
A user should be able to configure the builder through a single JSON config file and start the process using:

```text
python agent_embedding_builder.py
```

---

## 4. High-Level Architecture

```text
Markdown/TXT files
  ↓
File discovery
  ↓
Source artifact registration
  ↓
Markdown / text parser
  ↓
Hierarchical segmenter
  ↓
Category inference and extraction
  ↓
Category catalog / ontology validation and consolidation
  ↓
Chunk normalization
  ↓
Retrieval-profile generation
  ↓
Local embedding generation
  ↓
Optional reranker/index enrichment
  ↓
Database writer
  ↓
Run-state persistence
  ↓
Retrieval-ready long-term memory database
```

This pipeline preserves the original architecture and adds the category catalog / ontology layer needed to keep categorization explainable, configurable, and extensible.

---

## 5. Recommended Technology Stack

## 5.1 Language
Use **Python**.

Reason:
- strongest ecosystem for local embeddings
- strongest ecosystem for local rerankers/classifiers
- easiest integration with markdown processing and vector DB tooling
- easy orchestration for batch ingestion and incremental rebuilds

## 5.2 Runtime environment
Use **conda** to create and manage the Python runtime environment.

## 5.3 Dependency management
Use **uv** for Python dependency management and reproducible lockfile handling.

## 5.4 Recommended libraries

### Parsing / modeling
- `markdown-it-py` or `mistune` for markdown parsing
- `PyYAML` for front matter parsing if needed
- `pydantic` for structured schemas and config validation
- `jsonschema` for category ontology validation

### Embeddings
- `sentence-transformers` for local embedding generation
- optionally `FastEmbed` for lighter local embedding workflows

### Reranking / classification support
- `sentence-transformers` cross-encoder rerankers
- or a local small LLM / classifier layer for category extraction
- optionally local zero-shot classification for dynamic labels

### Local LLM serving / inference
Typical acceptable options:
- `llama-cpp-python` for local GGUF models
- a local HTTP server such as Ollama or vLLM if later desired
- direct Hugging Face local model loading if the model fits the machine

### Database writing
- PostgreSQL + pgvector as the preferred implementation
- or SQLite + sqlite-vec for local-first development
- or Qdrant if using a separate vector store

### Provenance / hashing / diff support
- `hashlib` for source hashes
- `orjson` or `ujson` for fast structured payload persistence if needed
- SQLAlchemy or an equivalent thin DB layer if transaction handling is needed

### Logging / CLI
- `typer` or `argparse` for CLI
- standard `logging` or `structlog` for structured logs

---

## 6. Builder Runtime Configuration

The builder must use a **single JSON config file** as the main runtime configuration.

Recommended file name:

```text
builder_config.json
```

The config must include at least:

- database connection settings
- source directories for `.md` and `.txt`
- include/exclude rules
- local LLM settings
- embedding model settings
- category ontology / catalog path
- thresholds and feature flags
- incremental rebuild settings
- logging settings

### 6.1 Example config structure

```json
{
  "database": {
    "driver": "postgresql",
    "host": "127.0.0.1",
    "port": 5432,
    "database": "memory_db",
    "username": "user",
    "password": "password",
    "schema": "public"
  },
  "knowledge_base": {
    "input_roots": [
      "D:/kb/docs",
      "D:/kb/notes"
    ],
    "include_extensions": [".md", ".txt"],
    "exclude_dirs": [".git", "node_modules", "__pycache__"],
    "exclude_globs": ["*.tmp", "*.bak"]
  },
  "llm": {
    "provider": "llama_cpp",
    "local_model_path": "D:/models/qwen2.5-7b-instruct-q4_k_m.gguf",
    "download_if_missing": true,
    "download_source": {
      "type": "huggingface",
      "repo_id": "Qwen/Qwen2.5-7B-Instruct-GGUF",
      "filename": "qwen2.5-7b-instruct-q4_k_m.gguf"
    },
    "context_length": 8192,
    "temperature": 0.0,
    "max_tokens": 1200
  },
  "embedding": {
    "model_name": "sentence-transformers/all-MiniLM-L6-v2",
    "local_model_path": "",
    "batch_size": 32,
    "normalize_embeddings": true
  },
  "category_governance": {
    "ontology_path": "./config/category_ontology.json",
    "schema_path": "./config/category_ontology.schema.json",
    "allow_unknown_labels": false,
    "default_confidence_floor": 0.55
  },
  "builder": {
    "builder_version": "1.0.0",
    "normalization_version": "1.0.0",
    "classifier_version": "1.0.0",
    "schema_compatibility_version": "1.0.0",
    "max_paragraph_group_size": 6,
    "min_span_chars": 120,
    "max_span_chars": 6000,
    "enable_parent_child_storage": true,
    "enable_retrieval_profiles": true,
    "enable_case_shape_inference": true,
    "active_status_default": "active",
    "authority_label_default": "reviewed"
  },
  "rebuild": {
    "mode": "changed",
    "rebuild_on_model_change": true,
    "rebuild_on_normalization_change": true,
    "rebuild_on_classifier_change": true
  },
  "logging": {
    "level": "INFO",
    "json_logs": false
  }
}
```

---

## 7. Builder Responsibilities

The builder must perform the following jobs.

### 7.1 Discover source files
- scan configured directories
- detect `.md` and `.txt` files
- ignore excluded folders/files
- track modification timestamps and hashes

### 7.2 Register source artifacts
The builder must register each input file as a source artifact.

Store or derive:
- artifact id
- file path
- source type
- file hash
- timestamps
- builder version compatibility

### 7.3 Parse markdown and text structure
For markdown:
- headings
- paragraphs
- bullet lists
- code blocks
- tables
- block quotes
- front matter metadata

For plain text:
- paragraph blocks
- heuristic section breaks
- optional delimiter-based sectioning
- line-based grouping when needed

### 7.4 Build hierarchical document model
The builder must keep document hierarchy, not flatten immediately.

Hierarchy example:
- file
  - section
    - subsection
      - paragraph groups

### 7.5 Segment content into candidate knowledge units
The builder must create candidate spans at multiple levels:
- paragraph-group candidates
- subsection candidates
- section candidates
- whole-file candidate

### 7.6 Infer categories
The builder must determine which categories each candidate span belongs to.

### 7.7 Resolve stable retrieval class
The builder must assign a stable retrieval-facing class to each persisted knowledge item.

Examples:
- decision
- constraint
- best_practice
- anti_pattern
- similar_case
- implementation_note
- historical_note
- example
- structure

This stable class is for predictable retrieval behavior, not to replace dynamic labels.

### 7.8 Normalize retrieval text
The builder may create:
- original text
- normalized retrieval text
- short summary
- compact retrieval profile
- route-specific retrieval profiles

### 7.9 Generate embeddings locally
The builder must create vectors for:
- normalized retrieval text
- optionally summary text
- optionally details text
- optionally route-specific retrieval profiles

### 7.10 Persist records into the database
Store:
- text
- metadata
- categories
- retrieval class
- source traceability
- embeddings
- hierarchical relationships
- scopes and tags
- retrieval profiles

### 7.11 Persist segment provenance
The builder must write source segment records and connect them to knowledge items.

### 7.12 Persist ingestion run state
The builder must record:
- ingestion run id
- builder version
- normalization version
- embedding model version
- classifier version
- success/failure outcomes

### 7.13 Persist build state for incremental rebuilds
The builder must record artifact build states so that changed files can be rebuilt deterministically.

### 7.14 Validate output quality
The builder must reject or quarantine invalid outputs when category resolution, provenance linkage, or embedding generation fails critically.

---

## 8. Core Design Principle: Hierarchical Memory Construction

The biggest challenge is that a useful retrieval unit may be:

- a few sentences
- a subsection
- a whole section
- a whole markdown file
- a synthetic summary that represents several nearby source segments

Therefore, the builder must **not** assume a fixed chunk size.

Instead, it should use a hierarchical approach.

## 8.1 Hierarchy levels

### Level A — File
Whole document.

Useful when:
- entire file is one category
- document is short and coherent
- front matter already describes the whole file clearly

### Level B — Section
A heading plus its content.

Useful when:
- sections have distinct themes
- categories differ by section

### Level C — Subsection
A lower heading plus its content.

Useful when:
- section is still too broad
- category meaning changes within a section

### Level D — Paragraph group
2–6 related paragraphs or list items.

Useful when:
- fine-grained guidance is embedded inside a broader narrative
- anti-pattern or best practice is only a small span

### Level E — Synthetic
A synthetic item derived from multiple nearby segments, when a normalized retrieval unit is more valuable than any single raw span.

Useful when:
- multiple short fragments together define one reusable concept
- a retrieval profile or example summary is needed
- a case shape is derived from several related pieces of evidence

## 8.2 Hierarchy preservation rule
The builder must preserve hierarchy in storage even if only selected spans become standalone knowledge items.

At minimum it should preserve:
- source artifact reference
- heading path
- span level
- parent segment or parent knowledge item relationship when applicable

## 8.3 Parent-child knowledge rule
Sometimes both a broad parent span and a narrower child span should be stored.

Examples:
- section-level best practice summary
- paragraph-level anti-pattern inside that section

This is allowed as long as:
- parent-child linkage is explicit
- duplicate retrieval overload is controlled
- the child is meaningfully more specific than the parent

---

## 9. Dynamic Category Design

This is a hard requirement.

The system must support categories such as:
- best practice
- project history
- anti-pattern
- implementation
- feature description
- decision
- constraint
- incident
- example
- migration note
- lesson learned
- commit context
- structure fact

But categories must be dynamic and may evolve.

So category design must be flexible.

## 9.1 Category model

Use **multi-label categories**, not single-label.

One span may be:
- `project_history`
- and `anti_pattern`

Another may be:
- `best_practice`
- and `implementation`

Another may be:
- `incident`
- `lesson_learned`
- and `constraint`

## 9.2 Category sources

Categories may come from:

### A. File metadata
Examples:
- YAML front matter
- naming conventions
- directory conventions

### B. Section headings
Examples:
- “Best Practices”
- “Known Issues”
- “Feature Overview”
- “Implementation Notes”

### C. Local LLM / classifier inference
Examples:
- a paragraph implicitly describes an anti-pattern
- a subsection is clearly project history even if not titled that way

### D. Rule-based inference
Examples:
- phrases like “do not”, “avoid”, “common mistake” strongly suggest anti-pattern
- phrases like “should”, “recommended”, “best way” suggest best practice
- phrases with dates, milestones, versions may indicate project history

### E. Category catalog / ontology rules
Examples:
- synonyms
- allowed parents/children
- permitted retrieval classes
- label-specific scope expectations
- confidence floors
- negative hints
- dominance rules for stable retrieval class resolution

## 9.3 Category representation

Do not use a rigid enum only.

Store categories as:
- primary labels
- secondary labels
- arbitrary tags

Recommended internal structure:

```json
{
  "primary_labels": ["best_practice"],
  "secondary_labels": ["implementation"],
  "tags": ["ajax-refresh", "loading-state", "ui"]
}
```

## 9.4 Category confidence

Each assigned category should carry confidence:

```json
{
  "label": "anti_pattern",
  "confidence": 0.92,
  "source": "llm_inference"
}
```

This is important because later MCP retrieval can prefer high-confidence category labels.

## 9.5 Stable retrieval class versus dynamic labels

The revised database design requires a distinction between:

### A. stable retrieval class
A single builder-resolved class used for predictable routing and query behavior.

Examples:
- `decision`
- `constraint`
- `best_practice`
- `anti_pattern`
- `similar_case`
- `historical_note`
- `implementation_note`
- `example`
- `structure`

### B. dynamic labels
Open-ended semantic labels used for extensibility.

Examples:
- `project_history`
- `lesson_learned`
- `migration_note`
- `commit_context`
- `structure_tracking_candidate`

### Resolution rule
The builder should not collapse all meaning into the stable retrieval class.
It should:
- choose one stable retrieval class
- preserve multiple dynamic labels with confidence
- preserve tags and scopes separately

This keeps both predictability and extensibility.

---

## 10. Category Catalog / Ontology Design

A category catalog does make sense, but it should **not** be a vague markdown note or a loose checklist. The canonical form should be a **machine-readable category ontology / catalog** that is versioned, validated, and loaded through config.

That gives the builder:

- predictable category governance
- configurable behavior
- explainable categorization
- easier testing
- easier future evolution without code churn

### 10.1 Canonical artifact

Use a file such as:

```text
config/category_ontology.json
```

Optional companion files:
- `config/category_ontology.schema.json`
- `docs/category_ontology_guide.md`

### 10.2 Why this is better than markdown-only catalog

A markdown-only category list is human-readable but weak for execution:
- harder to validate
- harder to load deterministically
- harder to test
- harder to version semantically
- harder to enforce in code

A machine-readable ontology / catalog can still have a human-facing markdown guide, but the JSON artifact should be canonical.

### 10.3 Category ontology responsibilities

The ontology / catalog should define:

- allowed category labels
- label descriptions
- synonyms and aliases
- label roles
- allowed parent-child relations
- allowed retrieval classes
- default retrieval-class mapping
- default confidence floors
- LLM prompt hints
- rule-based lexical hints
- negative hints
- scope hints
- whether a label may be primary
- whether a label is deprecated
- whether a label is experimental
- migration path for renamed labels

### 10.4 Example ontology structure

```json
{
  "version": "1.0.0",
  "labels": [
    {
      "name": "best_practice",
      "description": "Reusable recommended engineering guidance.",
      "may_be_primary": true,
      "default_retrieval_class": "best_practice",
      "aliases": ["best-practice", "recommended_pattern"],
      "parents": [],
      "children": [],
      "confidence_floor": 0.65,
      "scope_hints": ["layer", "module", "feature", "concern"],
      "positive_lexical_hints": ["should", "recommended", "best way", "preferred"],
      "negative_lexical_hints": ["do not", "avoid"],
      "llm_guidance": "Use when the text prescribes a reusable recommended way of doing something."
    },
    {
      "name": "anti_pattern",
      "description": "Known bad approach, hazard, or repeat failure mode.",
      "may_be_primary": true,
      "default_retrieval_class": "anti_pattern",
      "aliases": ["anti-pattern", "bad_practice", "pitfall"],
      "parents": [],
      "children": ["frontend_inference"],
      "confidence_floor": 0.65,
      "scope_hints": ["layer", "module", "concern"],
      "positive_lexical_hints": ["do not", "avoid", "common mistake", "pitfall"],
      "negative_lexical_hints": ["recommended", "preferred"],
      "llm_guidance": "Use when the text warns against an approach or documents a repeat failure mode."
    },
    {
      "name": "project_history",
      "description": "Historical project evolution, milestone, or timeline information.",
      "may_be_primary": true,
      "default_retrieval_class": "historical_note",
      "aliases": ["history", "timeline"],
      "parents": [],
      "children": ["migration_note", "incident"],
      "confidence_floor": 0.55,
      "scope_hints": ["domain", "module", "feature", "repo"],
      "positive_lexical_hints": ["version", "milestone", "released", "previously", "timeline"],
      "negative_lexical_hints": [],
      "llm_guidance": "Use when the text mainly documents what happened over time."
    }
  ],
  "retrieval_class_resolution": {
    "priority_order": [
      "constraint",
      "decision",
      "anti_pattern",
      "best_practice",
      "similar_case",
      "implementation_note",
      "historical_note",
      "example",
      "reference",
      "structure"
    ]
  }
}
```

### 10.5 Catalog loading rules

The builder must:
1. load the ontology path from config
2. validate it against JSON Schema
3. fail fast on invalid canonical artifacts
4. log the ontology version into `ingestion_runs`
5. record the ontology version into build metadata if it affects classification

### 10.6 Ontology change behavior

If the category ontology version changes, a rebuild may be required even when source files have not changed.

The builder should support:
- full rebuild
- changed-only rebuild if the affected labels can be isolated
- label migration for renamed labels when safe

### 10.7 Human-facing guidance

A markdown file may still exist for human explanation, but it should be generated from or manually aligned to the ontology.

That markdown guide may include:
- meaning of each label
- examples
- non-examples
- mapping notes
- how authors should use front matter
- how reviewers should validate categorization

But the markdown guide should not be the canonical execution artifact.

---

## 11. Front Matter and Source-Embedded Metadata

The builder should support front matter for markdown documents.

Example:

```yaml
---
title: Async Refresh Guidance
domain: mingpan
module: yearly-weighted-card
feature: year-switching
labels:
  - best_practice
  - implementation
authority_label: reviewed
status: active
tags:
  - ajax-refresh
  - loading-state
---
```

### 11.1 Role of front matter
Front matter is optional and advisory, not absolute truth.

It should:
- seed metadata extraction
- provide initial labels
- provide authority/status hints
- help scope extraction
- reduce ambiguity for classification

### 11.2 Validation rule
Front matter labels should be checked against the category ontology.

Behavior:
- unknown labels rejected or downgraded depending on config
- deprecated labels rewritten or warned
- aliases normalized to canonical label names

### 11.3 Text files
For `.txt` files, similar metadata can be supplied by:
- adjacent sidecar metadata files
- path conventions
- catalog rules
- LLM and rule-based inference

---

## 12. Category Inference Pipeline

The builder should not rely on embeddings alone for category inference.

It should use a layered pipeline.

## 12.1 Category inference stages

### Stage 1 — Metadata extraction
Read file-level metadata such as:
- front matter labels
- author
- source type
- date
- module
- domain
- explicit category labels

### Stage 2 — Structural hint extraction
Use heading names, section names, and file names as category hints.

Examples:
- “Best Practices” → best_practice
- “Anti-Patterns” → anti_pattern
- “Feature Overview” → feature_description
- “Migration History” → project_history

### Stage 3 — Rule-based classification
Apply lightweight lexical/rule classifiers.

Examples:
- “do not”, “avoid”, “common mistake” → anti_pattern candidate
- “recommended”, “should”, “best approach” → best_practice candidate
- version/date/milestone language → project_history candidate

### Stage 4 — Local model classification / extraction
Run a local classification step over candidate spans.

This can be:
- small local LLM
- local classifier model
- embedding+prototype classifier
- local prompt-driven extractor

This step should return:
- category labels
- confidence
- short explanation
- retrieval-facing normalized text
- optional scope hints

### Stage 5 — Ontology normalization
Normalize labels and hints against the category ontology:
- aliases → canonical labels
- deprecated labels → migrated labels or warnings
- forbidden label pairings → corrected or flagged
- label floors → confidence filtering

### Stage 6 — Category consolidation
Merge metadata, rule-based, ontology-based, and model-based signals into final labels.

### Stage 7 — Stable retrieval class resolution
After multi-label inference, resolve one stable retrieval class for each persisted knowledge item.

Examples:
- dynamic labels = `best_practice`, `implementation` → retrieval class = `best_practice`
- dynamic labels = `anti_pattern`, `lesson_learned` → retrieval class = `anti_pattern`
- dynamic labels = `project_history`, `incident`, `decision` → retrieval class may be `historical_note` or `decision` depending on the dominant retrieval function

### Stage 8 — Scope and tag extraction
Extract:
- domain
- module
- feature
- layer
- concern scopes
- retrieval tags

### Stage 9 — Explanation persistence
Optionally store a compact explanation of how final labels were chosen, useful for diagnostics and audits.

## 12.2 Why embeddings alone are not enough

Embeddings are good for semantic similarity, but category assignment needs:
- discourse role understanding
- instruction vs warning vs historical note distinction
- multi-label semantics
- variable span boundary recognition
- policy and ontology alignment

Therefore, category inference must be a separate layer.

## 12.3 Why stable retrieval class is also needed

Dynamic labels alone are not enough because retrieval-time MCP tools need predictable storage semantics.

Examples:
- risk queries should reliably find `anti_pattern` and relevant `constraint` guidance
- pattern queries should reliably find `best_practice` and implementation guidance
- similar-case queries should reliably find `similar_case` and structurally comparable history

So the builder must produce both:
- stable retrieval class
- dynamic labels

---

## 13. Span Selection Design

Once candidate spans exist, the builder must decide which spans to persist as knowledge items.

This is critical.

## 13.1 Candidate span types

For each document, create candidates at:
- file level
- section level
- subsection level
- paragraph-group level
- optional synthetic level

## 13.2 Span selection rule

Select a candidate span as a persisted knowledge item if it is:

- semantically coherent
- category-coherent
- useful as a retrieval target
- not dominated by irrelevant neighboring text
- traceable to a meaningful source segment

## 13.3 Keep full-section when appropriate
If a whole section is strongly category-coherent, keep it as a section-level knowledge item.

Example:
- entire section “Best Practices for Async Refresh”

## 13.4 Keep paragraph-group when necessary
If only a small part of a section contains an anti-pattern or key lesson, preserve that smaller span as its own unit.

Example:
- 3 paragraphs inside a project history section describing a specific anti-pattern

## 13.5 Allow both parent and child knowledge items
Sometimes both are useful:
- section-level best practice summary
- paragraph-level anti-pattern extracted from inside that section

This is allowed as long as parent-child linkage is stored.

## 13.6 Duplicate-control rule
When both parent and child are kept, the builder must avoid storing meaningless near-duplicates.

The child should be kept only if it adds one or more of:
- narrower category meaning
- stronger retrieval precision
- important risk or constraint signal
- reusable example value

## 13.7 Synthetic-item rule
The builder may create a synthetic retrieval profile or summary text for a span, but it must still preserve linkage to the original source segment(s).

---

## 14. Builder Output Knowledge Item Model

The builder should output normalized knowledge items.

## 14.1 Example knowledge item

```json
{
  "knowledge_item_id": "ki-2026-001",
  "source_artifact_id": "sa-2026-014",
  "source_path": [
    "Yearly Weighted Card",
    "Best Practices",
    "Async Refresh"
  ],
  "span_level": "section",
  "raw_text": "...",
  "normalized_retrieval_text": "Use ajax refresh with explicit loading state for yearly weighted card updates. Avoid full page reload.",
  "summary": "Recommended async refresh pattern for yearly weighted card.",
  "retrieval_class": "best_practice",
  "primary_labels": ["best_practice"],
  "secondary_labels": ["implementation"],
  "tags": ["ajax-refresh", "loading-state", "ui"],
  "label_confidences": [
    {
      "label": "best_practice",
      "confidence": 0.95,
      "source": "heading+llm"
    }
  ],
  "domain": "mingpan",
  "module": "yearly-weighted-card",
  "feature": "year-switching",
  "source_type": "design_doc",
  "authority_level": "reviewed",
  "status": "active",
  "parent_knowledge_item_id": null,
  "child_knowledge_item_ids": [],
  "retrieval_profiles": [
    {
      "profile_type": "default",
      "profile_text": "Use ajax refresh with explicit loading state for yearly weighted card updates. Avoid full page reload."
    },
    {
      "profile_type": "pattern",
      "profile_text": "ajax refresh pattern with explicit loading state and no full reload"
    }
  ],
  "source_segments": ["ss-2026-551"]
}
```

## 14.2 Required persisted fields
Every persisted knowledge item should have at minimum:
- stable id
- source artifact reference
- source path / heading path
- span level
- raw or recoverable source text
- normalized retrieval text
- retrieval class
- at least one label
- scopes and tags where derivable
- authority and lifecycle status
- provenance linkage to one or more source segments

---

## 15. Retrieval Profiles

The builder should support route-specific retrieval profiles.

Supported profile types may include:
- `summary`
- `default`
- `core_task`
- `constraint`
- `risk`
- `pattern`
- `similar_case`

Not every knowledge item needs every profile type.

### 15.1 Why profiles matter
The harness does not retrieve with one broad query. It retrieves through chunk purposes.

Therefore, one knowledge item may need several optimized texts:
- a compact summary
- a risk-oriented warning form
- a pattern-oriented implementation form
- a similar-case form

### 15.2 Generation rule
Generate specialized profiles only when they add retrieval value. Avoid profile spam.

---

## 16. Local Embedding Design

The builder must use a local embedding model.

## 16.1 Embedding targets

At minimum, embed:
- `normalized_retrieval_text`

Optionally also embed:
- `summary`
- `raw_text` (or reduced details text)
- route-specific retrieval profiles

## 16.2 Why normalized retrieval text matters

Raw documents often include:
- formatting noise
- long narrative context
- repeated boilerplate
- mixed concerns

The builder should create a retrieval-optimized text form so embeddings are cleaner and more aligned to future task queries.

## 16.3 Embedding strategy

Recommended:
- use one embedding per knowledge item for `normalized_retrieval_text`
- optionally one additional embedding for `summary`
- optionally additional embeddings for route-specific retrieval profiles

Do not embed the whole raw file only.

## 16.4 Retrieval-profile embeddings

The builder should be able to generate one or more retrieval-oriented text variants such as:
- default profile
- constraint-oriented profile
- pattern-oriented profile
- risk-oriented profile
- similar-case profile

## 16.5 Model-version persistence
The builder must persist:
- embedding model name
- embedding model version if available
- generation timestamp

This is required for deterministic rebuild and later migration.

---

## 17. High-Accuracy Relevance Design

This is the central nonfunctional requirement.

The builder must support high retrieval accuracy later.

That means the database should store knowledge items in a way that helps MCP retrieval achieve both precision and recall.

## 17.1 Precision mechanisms

The builder improves precision by:
- generating coherent spans
- assigning categories correctly
- using ontology-governed categories
- generating normalized retrieval text
- attaching scope metadata
- attaching authority/status metadata
- separating distinct meanings into different knowledge items
- assigning a stable retrieval class
- attaching provenance to original segments

## 17.2 Recall mechanisms

The builder improves recall by:
- preserving multiple granularity levels
- allowing parent-child relationships
- storing multiple labels per span
- keeping dynamic tags
- preserving category signals from metadata and inference
- allowing both fine-grained and broad retrieval targets
- storing multiple retrieval profiles where useful

## 17.3 Why this helps harness/MCP retrieval

Later, when the harness decomposes a task into chunks, MCP can query the database using:
- semantic similarity
- label/category filters
- retrieval-class filters
- scope filters
- status filters
- authority thresholds
- provenance-aware suppression or traceability

## 17.4 Support for future context families
The builder should make it possible to ingest future knowledge families without redesigning the whole system.

Examples:
- lessons learned
- commit-derived notes
- release migration notes
- postmortem constraints
- structure entities and code topology references

This works when the builder keeps:
- retrieval class
- dynamic labels
- scopes
- tags
- source type
- provenance
- ontology governance

---

## 18. Compatibility with the Entire System

The builder must produce data compatible with the harness + MCP retrieval system.

## 18.1 Compatibility requirements

The built database must support:
- chunk-based retrieval
- category-based filtering
- domain/module/feature filtering
- authority filtering
- active-only retrieval
- similar-case retrieval
- context pack assembly

## 18.2 Mapping to harness retrieval chunks

Later, MCP retrieval may search for:
- core task
- constraint
- risk
- pattern
- similar case

The builder’s labels and metadata should support all of these.

Examples:
- `best_practice` helps pattern retrieval
- `anti_pattern` helps risk retrieval
- `project_history` helps similar-case and historical context
- `feature_description` helps core-task retrieval
- `implementation` helps pattern and example retrieval

## 18.3 Mapping to revised database query behavior

Examples:
- core task queries may use `knowledge_items` + `knowledge_scopes` + `retrieval_profiles`
- constraint queries may prefer `retrieval_class in ('constraint','decision','anti_pattern')`
- risk queries may prefer `anti_pattern`, risk tags, and concern scopes
- pattern queries may prefer `best_practice`, implementation-oriented labels, and pattern profiles
- similar-case queries may prefer `similar_case` items plus `case_shapes`

## 18.4 Compatibility with manual and future automated ingestion
The design should allow:
- batch ingestion from markdown and text docs today
- future ingestion from commit summaries or structured code analyzers
- future mixed-source corpora where markdown is only one source type

---

## 19. Database Schema Requirements

At minimum, persist to the revised database design using these tables.

### A. `knowledge_items`
Store:
- id
- title or short name if derivable
- retrieval class
- summary
- details / normalized text references
- domain/module/feature
- authority/status
- parent-child references where needed

### B. `knowledge_labels`
Store:
- knowledge_item_id
- label
- label role if used
- confidence
- source of inference

### C. `knowledge_scopes`
Store:
- knowledge_item_id
- scope_type
- scope_value

### D. `knowledge_tags`
Store:
- knowledge_item_id
- tag

### E. `source_artifacts`
Store:
- source file identity
- path
- type
- file hash
- timestamps
- source metadata

### F. `source_segments`
Store:
- source artifact id
- heading path
- span offsets or structural coordinates
- raw text or resolvable text reference
- span level

### G. `knowledge_item_segments`
Store:
- knowledge_item_id
- source_segment_id
- linkage role

### H. `case_shapes`
When applicable, store:
- task type
- feature shape
- engine change allowed
- likely layers
- risk signals
- complexity

### I. `retrieval_profiles`
Store:
- knowledge_item_id
- profile_type
- profile_text

### J. `knowledge_embeddings`
Store:
- knowledge_item_id
- embedding role
- embedding vector
- embedding model
- generation metadata

### K. `ingestion_runs`
Store:
- run id
- builder version
- normalization version
- embedding model version
- classifier version
- ontology version
- timestamps
- result summary

### L. `artifact_build_states`
Store:
- source artifact id
- last processed hash
- last successful run id
- current build status
- failure info if any

### M. Optional future extension
When structure tracking is introduced, the builder may also populate:
- `structure_entities`
- `knowledge_item_entities`

---

## 20. Recommended Builder Pipeline in Detail

## 20.1 File discovery
Inputs:
- source root paths
- include/exclude rules

Outputs:
- list of input files
- file hashes for incremental rebuilds

## 20.2 Source artifact registration
Before deep processing, register or update the source artifact record.

Outputs:
- artifact id
- file hash
- source type
- path metadata

## 20.3 Parse
Convert each file into a structured document tree.

Outputs:
- headings
- nested sections
- block elements
- front matter metadata or text heuristics

## 20.4 Candidate generation
Create candidate spans at:
- file level
- section level
- subsection level
- paragraph-group level

Also create source segment candidates with:
- heading path
- span level
- text boundaries
- raw content

## 20.5 Category inference
For each candidate span:
- collect metadata hints
- collect structural hints
- run rule-based signals
- run local classification / extraction
- normalize against ontology
- assign multi-label categories with confidence
- resolve stable retrieval class

## 20.6 Scope and tag extraction
For each selected span:
- extract scopes
- extract tags
- infer layers or concerns if they are clear

## 20.7 Span selection
Choose which candidates become persisted knowledge items.

Keep:
- coherent category spans
- retrieval-useful spans
- important fine-grained spans
- important broad spans

## 20.8 Retrieval text normalization
Create:
- normalized retrieval text
- summary
- tags
- retrieval profiles

## 20.9 Embedding generation
Generate local embeddings for:
- normalized retrieval text
- optionally summary
- optionally selected retrieval profiles

## 20.10 Database write
Persist:
- knowledge items
- labels
- scopes
- tags
- source artifacts
- source segments
- item-segment links
- retrieval profiles
- embeddings
- hierarchy
- case shapes when applicable

## 20.11 Ingestion run persistence
Write run-level metadata for each build invocation.

## 20.12 Build-state persistence
Update artifact build states so changed files can be rebuilt efficiently.

---

## 21. Incremental Rebuild Strategy

The builder should support incremental indexing.

### Rebuild key
Use:
- file hash
- builder version
- model version
- normalization version
- classifier version
- ontology version
- schema compatibility version if needed

If any changes:
- rebuild only affected files when safe
- otherwise trigger broader rebuilds

## 21.1 Artifact-level rebuild rule
If the source file hash changed, the file must be reconsidered for rebuild.

## 21.2 Pipeline-version rebuild rule
Even when source content is unchanged, a rebuild may be required if:
- builder logic changed
- label inference changed
- retrieval-profile generation changed
- embedding model changed
- schema mapping changed
- ontology version changed

## 21.3 Rebuild behavior
Recommended behavior:
- mark previous derived records from the artifact as replaced or rebuild-scoped inactive
- regenerate segments and items for the artifact
- preserve historical run traceability
- only mark the artifact successful when all required writes complete

## 21.4 Failure rule
If a rebuild fails:
- keep the last successful artifact build state
- record the failed run in `ingestion_runs`
- record failure details in `artifact_build_states`
- avoid half-valid active records

---

## 22. Quality Controls

The builder should include quality checks.

## 22.1 Category quality checks
- no empty labels
- no impossible category combinations unless explicitly allowed
- minimum confidence thresholds for primary labels
- stable retrieval class must be resolvable for every persisted item
- ontology validation must pass before build execution

## 22.2 Span quality checks
- span not too small to be meaningless
- span not too large to be semantically mixed
- span category coherence acceptable
- parent-child retention justified when both are kept

## 22.3 Embedding quality checks
- embeddings generated for all persisted knowledge items
- model version recorded
- failed embeddings retried / logged
- retrieval-profile embeddings only generated for valid profile texts

## 22.4 Retrieval-readiness checks
- every persisted item has at least one:
  - primary label
  - summary / normalized retrieval text
  - embedding
  - source traceability
- scopes and tags are stored when derivable

## 22.5 Provenance quality checks
- every persisted item links to at least one source segment
- every source segment links back to one source artifact
- heading path / structural path is valid

## 22.6 Run-state quality checks
- every run has a recorded status
- every rebuilt artifact has an updated build state
- no artifact is marked successful when required writes failed

---

## 23. Recommended Service Interface

This builder can run as:
- CLI
- scheduled batch job
- local service

### Suggested commands
Internally the service may support:
- `build all`
- `build changed`
- `rebuild file <path>`
- `export stats`
- `validate corpus`
- `show artifact <path>`
- `show run <run_id>`

### User-facing requirement
The main entrypoint should still support:

```text
python agent_embedding_builder.py
```

That command should load the JSON config, initialize local models if needed, and run according to configured rebuild mode.

### Suggested outputs
- total files processed
- total knowledge items created
- label distribution
- retrieval-class distribution
- average span size
- embedding failures
- files skipped
- changed files rebuilt
- artifacts failed
- run summary

---

## 24. Example Category Catalog Entries

Below is an example starter catalog embedded in the design.

### 24.1 Starter canonical labels
Recommended starter labels:
- `best_practice`
- `anti_pattern`
- `project_history`
- `implementation`
- `feature_description`
- `decision`
- `constraint`
- `incident`
- `example`
- `migration_note`
- `lesson_learned`
- `similar_case`
- `commit_context`
- `structure_fact`
- `reference`

### 24.2 Example mapping guidance

| Label | Typical meaning | Default retrieval class |
|---|---|---|
| best_practice | reusable recommendation | best_practice |
| anti_pattern | known bad approach or pitfall | anti_pattern |
| project_history | timeline or evolution note | historical_note |
| implementation | concrete how-to or implementation note | implementation_note |
| feature_description | what a feature is / does | reference |
| decision | chosen architecture or policy | decision |
| constraint | must-follow boundary | constraint |
| incident | failure/event record | historical_note |
| example | illustrative concrete sample | example |
| migration_note | transition or upgrade note | historical_note |
| lesson_learned | distilled learning from experience | anti_pattern or best_practice depending on polarity |
| similar_case | prior structurally similar case | similar_case |
| commit_context | insight derived from commits | historical_note or reference |
| structure_fact | structural/topology fact | structure |
| reference | neutral descriptive reference | reference |

### 24.3 Example label interactions

- `anti_pattern + lesson_learned` often resolves to retrieval class `anti_pattern`
- `best_practice + implementation` often resolves to `best_practice`
- `incident + constraint` may resolve to `constraint` if the main reusable function is prescriptive
- `project_history + migration_note` often resolves to `historical_note`
- `structure_fact + constraint` may resolve to `constraint` if the text is operationally prescriptive

---

## 25. What This Design Solves

This builder design solves:

- inability to use raw markdown or text directly as retrieval memory
- lack of category inference for best practice / anti-pattern / project history / implementation / feature description
- dynamic categories not fitting a rigid schema
- variable category span size
- poor retrieval caused by whole-file embeddings only
- incompatibility between ingestion and harness/MCP retrieval flow
- loss of provenance between stored knowledge and source text
- weak rebuild-state handling for iterative corpus updates
- inability to cleanly extend toward future context families
- hidden category logic in hardcoded code paths

---

## 26. What This Design Does Not Solve

This document does not cover:
- retrieval-time MCP algorithms in detail
- harness chunking logic
- validation design outside builder quality gates
- write-back / human curation design
- model fine-tuning
- full code-structure extraction logic

Those are separate components.

However, this design intentionally keeps the builder ready for those future additions.

---

## 27. Final Recommendation

Build the embedding database builder as a **Python component** with:

- hierarchical markdown and text parsing
- multi-level span generation
- dynamic multi-label inference
- stable retrieval-class assignment
- a machine-readable category ontology / catalog
- scope and tag extraction
- retrieval-text normalization
- optional route-specific retrieval-profile generation
- local embeddings
- provenance persistence via source artifacts and source segments
- incremental rebuild support via ingestion runs and artifact build states
- compatibility with the revised long-term memory schema
- one-command startup through `python agent_embedding_builder.py`

This consolidated design preserves the detailed functional scope from the original design, integrates the category catalog directly into the design, and keeps categorization governable instead of hardcoded.
