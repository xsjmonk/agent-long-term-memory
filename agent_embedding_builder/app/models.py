from __future__ import annotations

from dataclasses import dataclass, field
from datetime import datetime
from pathlib import Path
from typing import Any, Literal
from uuid import UUID, uuid4

from pydantic import BaseModel, ConfigDict, Field


SpanLevel = Literal["file", "section", "subsection", "paragraph_group", "synthetic"]
RetrievalClass = Literal[
    "constraint",
    "decision",
    "antipattern",
    "best_practice",
    "similar_case",
    "implementation_note",
    "historical_note",
    "example",
    "reference",
    "structure",
]


class DatabaseConfig(BaseModel):
    model_config = ConfigDict(populate_by_name=True)
    driver: str = "postgresql"
    host: str
    port: int = 5432
    database: str
    username: str
    password: str
    db_schema: str = Field(default="public", alias="schema")
    ssl_mode: str = "prefer"


class KnowledgeBaseConfig(BaseModel):
    input_roots: list[str]
    include_extensions: list[str] = [".md", ".txt"]
    exclude_dirs: list[str] = [".git", "node_modules", "__pycache__", ".venv"]
    exclude_globs: list[str] = []
    follow_symlinks: bool = False


class DownloadSourceConfig(BaseModel):
    type: str = "huggingface"
    repo_id: str
    filename: str
    local_dir: str


class LLMConfig(BaseModel):
    provider: str = "llama_cpp"
    local_model_path: str
    download_if_missing: bool = True
    download_source: DownloadSourceConfig
    context_length: int = 8192
    temperature: float = 0.0
    max_tokens: int = 1200
    gpu_layers: int = 0
    seed: int = 42


class EmbeddingConfig(BaseModel):
    provider: str = "sentence_transformers"
    model_name: str
    local_model_path: str
    download_if_missing: bool = True
    batch_size: int = 32
    normalize_embeddings: bool = True
    # Operator-facing device override for embedding execution.
    # Allowed: "", "cpu", "cuda", "mps"
    force_device: Literal["", "cpu", "cuda", "mps"] = ""


class CategoryGovernanceConfig(BaseModel):
    ontology_path: str
    schema_path: str
    allow_unknown_labels: bool = False


class BuilderConfigSection(BaseModel):
    builder_version: str
    classifier_version: str
    normalization_version: str
    schema_compatibility_version: str
    max_file_size_bytes: int = 5 * 1024 * 1024
    write_raw_text_to_db: bool = True
    parallelism: int = 1
    enable_parent_child_storage: bool = True
    enable_retrieval_profiles: bool = True
    enable_case_shape_inference: bool = True
    default_authority_label: str = "reviewed"
    default_status: str = "active"
    mode: Literal["build-all", "build-changed"] = "build-changed"


class SegmentationConfig(BaseModel):
    min_paragraph_chars: int = 120
    max_paragraph_group_chars: int = 2200
    prefer_parent_and_child_when_child_adds_specificity: bool = True
    allow_file_level_items: bool = True
    allow_section_level_items: bool = True
    allow_subsection_level_items: bool = True
    allow_paragraph_group_items: bool = True


class ClassificationConfig(BaseModel):
    enable_llm: bool = True
    enable_rule_based: bool = True
    enable_heading_rules: bool = True
    min_primary_label_confidence: float = 0.60
    # Internal LLM budget cap. Used to aggressively limit memory growth from repeated per-segment LLM inference.
    max_llm_segments_per_run: int = 40


class RetrievalProfilesConfig(BaseModel):
    generate_summary: bool = True
    generate_default_profile: bool = True
    generate_route_profiles: bool = True
    route_types: list[str] = ["core_task", "constraint", "risk", "pattern", "similar_case"]


class RebuildConfig(BaseModel):
    enabled: bool = True
    rebuild_on_builder_version_change: bool = True
    rebuild_on_classifier_version_change: bool = True
    rebuild_on_embedding_model_change: bool = True
    rebuild_on_normalization_version_change: bool = True
    rebuild_on_ontology_version_change: bool = True
    rebuild_on_schema_compatibility_version_change: bool = True


class LoggingConfig(BaseModel):
    level: str = "INFO"
    json_logs: bool = False


class SyncConfig(BaseModel):
    reconcile_removed_files: bool = True
    archive_removed_artifacts: bool = True


class ApiConfig(BaseModel):
    # Operator-facing configuration for the query-only embedding API.
    host: str = "127.0.0.1"
    port: int = 8777


class AppConfig(BaseModel):
    database: DatabaseConfig
    knowledge_base: KnowledgeBaseConfig
    llm: LLMConfig
    embedding: EmbeddingConfig
    api: ApiConfig = Field(default_factory=ApiConfig)
    category_governance: CategoryGovernanceConfig
    builder: BuilderConfigSection
    segmentation: SegmentationConfig
    classification: ClassificationConfig
    retrieval_profiles: RetrievalProfilesConfig
    rebuild: RebuildConfig
    logging: LoggingConfig
    sync: SyncConfig = Field(default_factory=SyncConfig)
    metadata_internal: dict[str, Any] = Field(default_factory=dict)
    builder_internal: dict[str, Any] = Field(default_factory=dict)
    llm_internal: dict[str, Any] = Field(default_factory=dict)
    embedding_internal: dict[str, Any] = Field(default_factory=dict)
    segmentation_internal: dict[str, Any] = Field(default_factory=dict)
    classification_internal: dict[str, Any] = Field(default_factory=dict)
    retrieval_profiles_internal: dict[str, Any] = Field(default_factory=dict)
    rebuild_internal: dict[str, Any] = Field(default_factory=dict)
    category_internal: dict[str, Any] = Field(default_factory=dict)


class SourceMetadata(BaseModel):
    title: str | None = None
    domain: str | None = None
    module: str | None = None
    feature: str | None = None
    labels: list[str] = Field(default_factory=list)
    authority_label: str | None = None
    status: str | None = None
    tags: list[str] = Field(default_factory=list)
    source_type: str | None = None


@dataclass(slots=True)
class SourceFile:
    path: Path
    extension: str
    content_hash: str
    size_bytes: int
    modified_at: datetime


@dataclass(slots=True)
class ParsedSection:
    heading: str
    level: int
    body: str
    heading_path: list[str]
    start_line: int
    end_line: int


@dataclass(slots=True)
class ParsedDocument:
    path: Path
    source_type: str
    title: str
    raw_text: str
    metadata: dict[str, Any]
    sections: list[ParsedSection] = field(default_factory=list)
    paragraphs: list[str] = field(default_factory=list)


@dataclass(slots=True)
class CandidateSegment:
    source_path: Path
    source_type: str
    span_level: SpanLevel
    text: str
    heading_path: list[str]
    start_line: int | None
    end_line: int | None
    parent_key: str | None = None
    segment_hash: str = ""
    metadata: SourceMetadata = field(default_factory=SourceMetadata)


class LabelEvidence(BaseModel):
    label: str
    label_role: str = "secondary"
    confidence: float
    source_method: str
    explanation: str | None = None


class LLMInferenceResult(BaseModel):
    labels: list[LabelEvidence] = Field(default_factory=list)
    normalized_retrieval_text: str = ""
    summary: str | None = None
    rationale: str | None = None
    scope_hints: list[str] = Field(default_factory=list)
    tags: list[str] = Field(default_factory=list)


@dataclass(slots=True)
class SelectedSegment:
    candidate: CandidateSegment
    labels: list[LabelEvidence]
    retrieval_class: RetrievalClass
    normalized_retrieval_text: str
    summary: str | None
    scopes: list[tuple[str, str, float | None]]
    tags: list[str]
    retrieval_profiles: list[tuple[str, str]]
    confidence: float
    explanation_payload: dict[str, Any]
    parent_item_id: UUID | None = None
    case_shape: dict[str, Any] | None = None


@dataclass(slots=True)
class IngestionRunContext:
    run_id: UUID
    started_at: datetime
    embedding_model_name: str
    embedding_model_version: str | None
    ontology_version: str


def new_uuid() -> UUID:
    return uuid4()
