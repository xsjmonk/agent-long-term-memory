from __future__ import annotations

import json
from pathlib import Path

import pytest
from pydantic import ValidationError

import app.query_api as query_api


class _FakeEmbeddingProviderBase:
    def __init__(self, config):
        self.config = config
        # query_api reports fallback_mode by checking provider.model_path against "__hashing_fallback__".
        self.model_path = "__fake__"
        self._dimension = 3

    def model_metadata(self) -> tuple[str, str | None]:
        return ("fake-embedding-model", "v1")

    def embed(self, texts: list[str]) -> list[list[float]]:
        return [[0.1] * self._dimension for _ in texts]


class FakeEmbeddingProvider(_FakeEmbeddingProviderBase):
    def __init__(self, config):
        super().__init__(config)
        self._dimension = 3
        self.model_path = "fake-real-model"


class FakeHashingFallbackEmbeddingProvider(_FakeEmbeddingProviderBase):
    def __init__(self, config):
        super().__init__(config)
        self._dimension = 384
        self.model_path = "__hashing_fallback__"

    def model_metadata(self) -> tuple[str, str | None]:
        return (self.model_path, "hashing-384")

    def embed(self, texts: list[str]) -> list[list[float]]:
        # Keep deterministic vectors: correct dimension is what we test.
        return [[0.2] * self._dimension for _ in texts]


def _write_operator_config(tmp_path: Path) -> Path:
    # Minimal operator-facing config; runtime defaults are sourced from the repo's `builder_defaults.json`.
    cfg = {
        "database": {
            "driver": "postgresql",
            "host": "127.0.0.1",
            "port": 5432,
            "database": "x",
            "username": "u",
            "password": "p",
            "schema": "public",
            "ssl_mode": "prefer",
        },
        "knowledge_base": {
            "input_roots": ["./docs"],
            "include_extensions": [".md"],
            "exclude_dirs": [],
            "exclude_globs": [],
            "follow_symlinks": False,
        },
        "llm": {
            "provider": "llama_cpp",
            "local_model_path": "./models/fake.gguf",
            "download_if_missing": False,
            "download_source": {
                "type": "huggingface",
                "repo_id": "x/y",
                "filename": "fake.gguf",
                "local_dir": "./models",
            },
        },
        "api": {"host": "127.0.0.1", "port": 8777},
    }

    path = tmp_path / "builder_config.jsonc"
    path.write_text(json.dumps(cfg), encoding="utf-8")
    return path


def test_embed_query_returns_vectors_and_metadata(tmp_path: Path, monkeypatch: pytest.MonkeyPatch) -> None:
    op_cfg = _write_operator_config(tmp_path)
    monkeypatch.setenv("EMBEDDING_BUILDER_CONFIG", str(op_cfg))

    monkeypatch.setattr(query_api, "EmbeddingProvider", FakeEmbeddingProvider)

    # Trigger FastAPI startup handler directly to avoid TestClient/httpx dependency.
    query_api._startup()

    req = query_api.EmbedQueryRequest(
        schema_version="1.1",
        request_id="req-1",
        items=[
            {
                "item_id": "item-1",
                "chunk_id": "chunk-1",
                "chunk_type": "paragraph",
                "query_kind": "keyword",
                "retrieval_role_hint": "core_task",
                "text": "  hello world  ",
            },
            {
                "item_id": "item-2",
                "chunk_id": "chunk-2",
                "chunk_type": "paragraph",
                "query_kind": "semantic",
                "retrieval_role_hint": "risk",
                "text": "another query",
            },
        ],
    )
    resp = query_api.embed_query(req)
    payload = resp.model_dump()

    assert payload["schema_version"] == "1.1"
    assert payload["request_id"] == "req-1"
    assert payload["task_id"] is None

    for key in [
        "provider",
        "model_name",
        "model_version",
        "normalize_embeddings",
        "dimension",
        "fallback_mode",
        "text_processing_id",
        "vector_space_id",
        "warnings",
        "items",
    ]:
        assert key in payload

    assert payload["fallback_mode"] is False
    assert payload["normalize_embeddings"] is True

    assert isinstance(payload["dimension"], int)
    assert payload["dimension"] == 3
    assert payload["text_processing_id"] == "raw-text|trim:true|truncate:model-default"
    assert "text:raw-text|trim:true|truncate:model-default" in payload["vector_space_id"]

    assert isinstance(payload["items"], list)
    assert len(payload["items"]) == 2

    # Ordering + per-item identity + vector dimension.
    assert payload["items"][0]["item_id"] == "item-1"
    assert payload["items"][1]["item_id"] == "item-2"

    assert payload["items"][0]["chunk_id"] == "chunk-1"
    assert payload["items"][0]["chunk_type"] == "paragraph"
    assert payload["items"][0]["query_kind"] == "keyword"
    assert payload["items"][0]["retrieval_role_hint"] == "core_task"

    assert payload["items"][1]["chunk_id"] == "chunk-2"
    assert payload["items"][1]["chunk_type"] == "paragraph"
    assert payload["items"][1]["query_kind"] == "semantic"
    assert payload["items"][1]["retrieval_role_hint"] == "risk"

    # Diagnostics are derived from API-side trimming only.
    assert payload["items"][0]["input_char_count"] == len("  hello world  ")
    assert payload["items"][0]["effective_text_char_count"] == len("hello world")
    assert payload["items"][0]["truncated"] is True

    assert payload["items"][1]["input_char_count"] == len("another query")
    assert payload["items"][1]["effective_text_char_count"] == len("another query")
    assert payload["items"][1]["truncated"] is False

    for item in payload["items"]:
        assert item["input_char_count"] >= item["effective_text_char_count"]
        assert item["truncated"] == (item["effective_text_char_count"] < item["input_char_count"])
        assert "warnings" in item
        assert len(item["vector"]) == payload["dimension"]
        for key in [
            "item_id",
            "chunk_id",
            "chunk_type",
            "query_kind",
            "retrieval_role_hint",
            "vector",
            "input_char_count",
            "effective_text_char_count",
            "truncated",
            "warnings",
        ]:
            assert key in item


def test_embed_query_validation_failures(tmp_path: Path, monkeypatch: pytest.MonkeyPatch) -> None:
    op_cfg = _write_operator_config(tmp_path)
    monkeypatch.setenv("EMBEDDING_BUILDER_CONFIG", str(op_cfg))
    monkeypatch.setattr(query_api, "EmbeddingProvider", FakeEmbeddingProvider)

    # No need to call _startup for request-validation failures.
    with pytest.raises(ValidationError):
        query_api.EmbedQueryRequest(schema_version="1.1", request_id="req-1", items=[])

    with pytest.raises(ValidationError):
        query_api.EmbedQueryRequest(
            schema_version="1.1",
            request_id="req-1",
            items=[
                {
                    "item_id": "dup",
                    "query_kind": "keyword",
                    "text": "a",
                },
                {
                    "item_id": "dup",
                    "query_kind": "semantic",
                    "text": "b",
                },
            ],
        )

    with pytest.raises(ValidationError):
        query_api.EmbedQueryRequest(
            schema_version="1.1",
            request_id="req-1",
            items=[{"item_id": "x", "query_kind": "keyword", "text": "   "}],
        )

    with pytest.raises(ValidationError):
        query_api.EmbedQueryRequest(
            schema_version="1.1",
            request_id="req-1",
            items=[{"item_id": "x", "query_kind": "   ", "text": "hello"}],
        )


def test_embed_query_hashing_fallback_reports_fallback_mode_true(tmp_path: Path, monkeypatch: pytest.MonkeyPatch) -> None:
    op_cfg = _write_operator_config(tmp_path)
    monkeypatch.setenv("EMBEDDING_BUILDER_CONFIG", str(op_cfg))
    monkeypatch.setattr(query_api, "EmbeddingProvider", FakeHashingFallbackEmbeddingProvider)

    query_api._startup()

    req = query_api.EmbedQueryRequest(
        schema_version="1.1",
        request_id="req-2",
        items=[{"item_id": "i1", "query_kind": "keyword", "text": "ping"}],
    )
    resp = query_api.embed_query(req)
    payload = resp.model_dump()

    assert payload["fallback_mode"] is True
    assert payload["dimension"] == 384
    assert payload["warnings"], "Expected a top-level warning when hashing fallback is active."
    assert "fallback" in payload["warnings"][0].lower()
    assert "fallback:true" in payload["vector_space_id"].lower()
    assert len(payload["items"]) == 1
    assert len(payload["items"][0]["vector"]) == 384
    assert payload["items"][0]["input_char_count"] == len("ping")
    assert payload["items"][0]["effective_text_char_count"] == len("ping")
    assert payload["items"][0]["truncated"] is False


def test_embed_query_vector_space_id_changes_with_fallback_mode(tmp_path: Path, monkeypatch: pytest.MonkeyPatch) -> None:
    op_cfg = _write_operator_config(tmp_path)
    monkeypatch.setenv("EMBEDDING_BUILDER_CONFIG", str(op_cfg))

    monkeypatch.setattr(query_api, "EmbeddingProvider", FakeEmbeddingProvider)
    query_api._startup()
    req = query_api.EmbedQueryRequest(
        schema_version="1.1",
        request_id="req-3a",
        items=[{"item_id": "i1", "query_kind": "keyword", "text": "hello"}],
    )
    resp_a = query_api.embed_query(req).model_dump()

    monkeypatch.setattr(query_api, "EmbeddingProvider", FakeHashingFallbackEmbeddingProvider)
    query_api._startup()
    req2 = query_api.EmbedQueryRequest(
        schema_version="1.1",
        request_id="req-3b",
        items=[{"item_id": "i1", "query_kind": "keyword", "text": "hello"}],
    )
    resp_b = query_api.embed_query(req2).model_dump()

    assert "text:" in resp_a["vector_space_id"]
    assert resp_a["text_processing_id"] == "raw-text|trim:true|truncate:model-default"
    assert resp_b["text_processing_id"] == "raw-text|trim:true|truncate:model-default"
    assert resp_a["vector_space_id"] != resp_b["vector_space_id"]

