from __future__ import annotations

import logging
from pathlib import Path

from fastapi import FastAPI, HTTPException
from pydantic import BaseModel, Field, field_validator

from app.config_loader import load_config
from app.embedding_provider import EmbeddingProvider

logger = logging.getLogger(__name__)

_PING_TEXT = "ping"
_MAX_ITEMS = 64
_MAX_TEXT_CHARS = 4096


class EmbedQueryItem(BaseModel):
    item_id: str
    chunk_id: str | None = None
    chunk_type: str | None = None
    query_kind: str
    retrieval_role_hint: str | None = None
    text: str
    structured_scopes: dict | None = None
    task_shape: dict | None = None

    @field_validator("item_id")
    @classmethod
    def validate_item_id(cls, v: str) -> str:
        if not v or not str(v).strip():
            raise ValueError("`item_id` must be non-empty.")
        return v

    @field_validator("query_kind")
    @classmethod
    def validate_query_kind(cls, v: str) -> str:
        if not v or not str(v).strip():
            raise ValueError("`query_kind` must be non-empty.")
        return v

    @field_validator("text")
    @classmethod
    def validate_text(cls, v: str) -> str:
        if v is None:
            raise ValueError("`text` must be provided.")
        raw = str(v)
        trimmed = raw.strip()
        if not trimmed:
            raise ValueError("`text` must be non-empty after trimming.")
        if len(trimmed) > _MAX_TEXT_CHARS:
            raise ValueError(f"`text` must be at most {_MAX_TEXT_CHARS} characters (after trimming).")
        return raw


class EmbedQueryRequest(BaseModel):
    schema_version: str
    request_id: str
    task_id: str | None = None
    caller: str = "mcp"
    purpose: str = "harness_retrieval"
    items: list[EmbedQueryItem]

    @field_validator("items")
    @classmethod
    def validate_items(cls, v: list[EmbedQueryItem]) -> list[EmbedQueryItem]:
        if not v:
            raise ValueError("`items` must not be empty.")
        if len(v) > _MAX_ITEMS:
            raise ValueError(f"`items` must contain at most {_MAX_ITEMS} items.")
        return v

    @field_validator("items")
    @classmethod
    def validate_unique_item_ids(cls, v: list[EmbedQueryItem]) -> list[EmbedQueryItem]:
        ids = [it.item_id for it in v]
        if len(set(ids)) != len(ids):
            raise ValueError("Duplicate `item_id` values are not allowed within the request.")
        return v


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
    warnings: list[str] = Field(default_factory=list)


class EmbedQueryResponse(BaseModel):
    schema_version: str
    request_id: str
    task_id: str | None = None
    provider: str
    model_name: str
    model_version: str | None = None
    normalize_embeddings: bool
    dimension: int
    fallback_mode: bool
    text_processing_id: str
    vector_space_id: str
    items: list[EmbedQueryResultItem]
    warnings: list[str] = Field(default_factory=list)


def _compute_text_processing_id() -> str:
    # Budget-saving: current API preprocessing is only outer whitespace trim.
    return "raw-text|trim:true|truncate:model-default"


def _compute_vector_space_id(
    *,
    provider: str,
    model_name: str,
    model_version: str | None,
    normalize_embeddings: bool,
    dimension: int,
    fallback_mode: bool,
    text_processing_id: str,
) -> str:
    mv = model_version if model_version is not None else "null"
    return (
        f"{provider}|{model_name}|{mv}|norm:{str(normalize_embeddings).lower()}|dim:{dimension}|"
        f"fallback:{str(fallback_mode).lower()}|text:{text_processing_id}"
    )


app = FastAPI(
    title="Embedding Query API",
    version="1.0",
    # Strict surface: only the explicit API endpoint should be reachable.
    openapi_url=None,
    docs_url=None,
    redoc_url=None,
)


@app.on_event("startup")
def _startup() -> None:
    repo_root = Path(__file__).resolve().parents[1]
    config = load_config(repo_root)

    provider = EmbeddingProvider(config.embedding)
    model_name, model_version = provider.model_metadata()

    vectors = provider.embed([_PING_TEXT])
    if not vectors or not vectors[0]:
        raise RuntimeError("Failed to compute embedding dimension at API startup.")
    dimension = len(vectors[0])

    # Contract rule: fallback_mode derives from model_name identity.
    fallback_mode = bool(model_name == "__hashing_fallback__")

    text_processing_id = _compute_text_processing_id()
    vector_space_id = _compute_vector_space_id(
        provider=config.embedding.provider,
        model_name=model_name,
        model_version=model_version,
        normalize_embeddings=config.embedding.normalize_embeddings,
        dimension=dimension,
        fallback_mode=fallback_mode,
        text_processing_id=text_processing_id,
    )

    app.state.embedding_provider = provider
    app.state.embed_runtime = {
        "provider": config.embedding.provider,
        "model_name": model_name,
        "model_version": model_version,
        "normalize_embeddings": config.embedding.normalize_embeddings,
        "dimension": dimension,
        "fallback_mode": fallback_mode,
        "text_processing_id": text_processing_id,
        "vector_space_id": vector_space_id,
        "fallback_warnings": ["hashing-fallback-active"] if fallback_mode else [],
    }

    logger.info(
        "Query API embedding provider ready provider=%s model=%s/%s dimension=%s normalize=%s fallback=%s",
        config.embedding.provider,
        model_name,
        model_version,
        dimension,
        config.embedding.normalize_embeddings,
        fallback_mode,
    )


@app.post("/embed-query")
def embed_query(req: EmbedQueryRequest) -> EmbedQueryResponse:
    provider: EmbeddingProvider = app.state.embedding_provider
    meta: dict = app.state.embed_runtime

    # Budget-saving: only `text` impacts embedding generation; all other request fields are validated+echoed.
    trimmed_texts = [it.text.strip() for it in req.items]
    vectors = provider.embed(trimmed_texts)

    if len(vectors) != len(req.items):
        raise HTTPException(status_code=500, detail="Embedding failed: vector count mismatch.")

    expected_dim = int(meta["dimension"])
    for vec in vectors:
        if len(vec) != expected_dim:
            raise HTTPException(status_code=500, detail="Embedding failed: vector dimension mismatch.")

    items: list[EmbedQueryResultItem] = []
    for it, vec, trimmed in zip(req.items, vectors, trimmed_texts):
        input_char_count = len(it.text)
        effective_text_char_count = len(trimmed)
        items.append(
            EmbedQueryResultItem(
                item_id=it.item_id,
                chunk_id=it.chunk_id,
                chunk_type=it.chunk_type,
                query_kind=it.query_kind,
                retrieval_role_hint=it.retrieval_role_hint,
                vector=vec,
                input_char_count=input_char_count,
                effective_text_char_count=effective_text_char_count,
                truncated=(effective_text_char_count < input_char_count),
                warnings=["hashing-fallback-active"] if meta["fallback_mode"] else [],
            )
        )

    response = EmbedQueryResponse(
        schema_version=req.schema_version,
        request_id=req.request_id,
        task_id=req.task_id,
        provider=meta["provider"],
        model_name=meta["model_name"],
        model_version=meta["model_version"],
        normalize_embeddings=meta["normalize_embeddings"],
        dimension=meta["dimension"],
        fallback_mode=meta["fallback_mode"],
        text_processing_id=meta["text_processing_id"],
        vector_space_id=meta["vector_space_id"],
        items=items,
        warnings=list(meta["fallback_warnings"]),
    )

    # Post-checks: ensures the contract is satisfied even if the provider misbehaves.
    if len(response.items) != len(req.items):
        raise HTTPException(status_code=500, detail="API contract violated: response item count mismatch.")
    for idx, (req_item, resp_item) in enumerate(zip(req.items, response.items)):
        if resp_item.item_id != req_item.item_id:
            raise HTTPException(status_code=500, detail=f"API contract violated: ordering mismatch at index {idx}.")
        if len(resp_item.vector) != response.dimension:
            raise HTTPException(status_code=500, detail=f"API contract violated: vector dimension mismatch at index {idx}.")
        if resp_item.effective_text_char_count > resp_item.input_char_count:
            raise HTTPException(status_code=500, detail=f"API contract violated: effective_text_char_count > input_char_count at index {idx}.")

    return response

