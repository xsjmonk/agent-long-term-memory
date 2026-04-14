from __future__ import annotations

import logging
import sys
import types
from datetime import datetime
from pathlib import Path
from uuid import uuid4

import pytest

import app.pipeline as pipeline_mod
from app.config_loader import load_config
from app.models import (
    CandidateSegment,
    EmbeddingConfig,
    IngestionRunContext,
    LLMInferenceResult,
    LabelEvidence,
    SourceMetadata,
    SpanLevel,
)
from app.pipeline import run_pipeline
from app.models import SelectedSegment


def _prepare_operator_config(tmp_path: Path, docs_dir: Path, *, classification_enable_llm: bool | None = None, force_device: str | None = None) -> None:
    root = Path(__file__).resolve().parents[1]
    config_dir = tmp_path / "config"
    config_dir.mkdir(parents=True, exist_ok=True)

    for name in ["builder_defaults.json", "builder_config.schema.json", "category_ontology.json", "category_ontology.schema.json"]:
        (config_dir / name).write_text((root / "config" / name).read_text(encoding="utf-8"), encoding="utf-8")

    (tmp_path / "models").mkdir(exist_ok=True)
    (tmp_path / "models" / "m.gguf").write_text("x", encoding="utf-8")

    classification_fragment = ""
    if classification_enable_llm is not None:
        classification_fragment = f',"classification":{{"enable_llm":{str(classification_enable_llm).lower()}}}'

    embedding_fragment = ""
    if force_device is not None:
        embedding_fragment = f',"embedding":{{"force_device":"{force_device}"}}'

    config_text = (
        "{"
        f'"database":{{"driver":"postgresql","host":"127.0.0.1","port":5432,"database":"memory_db","username":"postgres","password":"change_me","schema":"public","ssl_mode":"prefer"}},'
        f'"knowledge_base":{{"input_roots":["{docs_dir.as_posix()}"],"include_extensions":[".md"],"exclude_dirs":[],"exclude_globs":[],"follow_symlinks":false}},'
        f'"llm":{{"provider":"llama_cpp","local_model_path":"{(tmp_path / "models" / "m.gguf").as_posix()}","download_if_missing":false,"download_source":{{"type":"huggingface","repo_id":"r","filename":"f","local_dir":"l"}}}}'
        f"{embedding_fragment}"
        f"{classification_fragment}"
        "}"
    )
    (config_dir / "builder_config.jsonc").write_text(config_text, encoding="utf-8")


class _FakeEmbeddings:
    def __init__(self, config: EmbeddingConfig) -> None:
        self.config = config

    def model_metadata(self):
        return ("fake-emb", "1")

    def embed(self, texts: list[str]):
        # Return stable-size vectors regardless of text.
        return [[0.1, 0.2] for _ in texts]


def test_lazy_llm_loaded_only_on_infer_call(monkeypatch: pytest.MonkeyPatch, tmp_path: Path) -> None:
    docs = tmp_path / "docs"
    docs.mkdir()
    (docs / "a.md").write_text("# A\nBody", encoding="utf-8")
    _prepare_operator_config(tmp_path, docs, classification_enable_llm=True)

    init_calls = {"n": 0}

    class CountingLocalLLM:
        def __init__(self, config) -> None:
            init_calls["n"] += 1

        def infer(self, prompt: str, retries: int = 2):
            raise AssertionError("infer() must not be called in this test")

    class FakeDB:
        def __init__(self, config):
            self.config = config
            self._schema_info = {
                "host": config.database.host,
                "port": config.database.port,
                "database_name": config.database.database,
                "schema": config.database.db_schema,
                "current_user": config.database.username,
                "current_schema": config.database.db_schema,
                "missing_tables": [],
                "present_tables_count": 100,
            }
            self._visibility_by_artifact: dict[str, dict[str, int]] = {}
            self._schema_info = {
                "host": config.database.host,
                "port": config.database.port,
                "database_name": config.database.database,
                "schema": config.database.db_schema,
                "current_user": config.database.username,
                "current_schema": config.database.db_schema,
                "missing_tables": [],
                "present_tables_count": 100,
            }
            self._visibility_by_artifact: dict[str, dict[str, int]] = {}
            self._schema_info = {
                "host": config.database.host,
                "port": config.database.port,
                "database_name": config.database.database,
                "schema": config.database.db_schema,
                "current_user": config.database.username,
                "current_schema": config.database.db_schema,
                "missing_tables": [],
                "present_tables_count": 100,
            }
            self._visibility_by_artifact: dict[str, dict[str, int]] = {}
            self._schema_info = {
                "host": config.database.host,
                "port": config.database.port,
                "database_name": config.database.database,
                "schema": config.database.db_schema,
                "current_user": config.database.username,
                "current_schema": config.database.db_schema,
                "missing_tables": [],
                "present_tables_count": 100,
            }
            self._visibility_by_artifact: dict[str, dict[str, int]] = {}
            self._schema_info = {
                "host": config.database.host,
                "port": config.database.port,
                "database_name": config.database.database,
                "schema": config.database.db_schema,
                "current_user": config.database.username,
                "current_schema": config.database.db_schema,
                "missing_tables": [],
                "present_tables_count": 100,
            }
            self._visibility_by_artifact: dict[str, dict[str, int]] = {}

        def create_ingestion_run(self, embedding_model_name, embedding_model_version) -> IngestionRunContext:
            return IngestionRunContext(uuid4(), datetime.now(), embedding_model_name, embedding_model_version, "1.0.0")

        def get_last_build_state(self, source_ref):
            return None

        def list_known_artifacts_for_roots(self, input_roots):
            return []

        def replace_artifact_knowledge(self, run_ctx, source_file, selected, embeddings):
            created_count = len(selected)
            source_artifact_id = str(uuid4())
            self._visibility_by_artifact[source_artifact_id] = {
                "active_items": created_count,
                "labels": created_count,
                "scopes": 0,
                "tags": 0,
                "profiles": 0,
                "embeddings": created_count,
                "source_segments": created_count,
                "visible_items": created_count,
                "visible_embeddings": created_count,
            }
            return {"source_artifact_id": source_artifact_id, "created_count": created_count, "superseded_count": 0}

        def record_artifact_failure(self, source_ref, artifact_hash, error, run_ctx):
            raise AssertionError("no failure expected")

        def finalize_ingestion_run(self, run_id, status, notes):
            self.final_status = status

        def verify_required_schema(self):
            return self._schema_info

        def verify_artifact_visibility(self, source_artifact_id):
            return self._visibility_by_artifact.get(str(source_artifact_id), {"visible_items": 1, "visible_embeddings": 1, "labels": 1, "profiles": 0, "embeddings": 1, "active_items": 1})

        def get_database_totals(self):
            return {"current_database": self._schema_info["database_name"], "source_artifacts": 1, "active_items": 1, "embeddings": 1, "ingestion_runs": 1}

    def fake_infer_categories(candidate, config, local_llm, ontology, allow_unknown_labels, default_confidence_floor, allow_llm):
        # Do not touch local_llm at all.
        labels = [LabelEvidence(label="best_practice", label_role="primary", confidence=0.9, source_method="llm_inference", explanation=None)]
        llm_result = LLMInferenceResult(labels=labels, normalized_retrieval_text="normalized", summary="summary", tags=[])
        return labels, llm_result, []

    monkeypatch.setattr(pipeline_mod, "EmbeddingProvider", _FakeEmbeddings)
    monkeypatch.setattr(pipeline_mod, "LocalLLM", CountingLocalLLM)
    monkeypatch.setattr(pipeline_mod, "infer_categories", fake_infer_categories)

    cfg = load_config(tmp_path)
    stats = run_pipeline(tmp_path, cfg, database=FakeDB(cfg))

    assert init_calls["n"] == 0
    assert stats.created_items > 0


def test_disable_llm_by_classification_enable_llm(monkeypatch: pytest.MonkeyPatch, tmp_path: Path) -> None:
    docs = tmp_path / "docs"
    docs.mkdir()
    (docs / "a.md").write_text("# A\nBody", encoding="utf-8")
    _prepare_operator_config(tmp_path, docs, classification_enable_llm=False)

    init_calls = {"n": 0}

    class CountingLocalLLM:
        def __init__(self, config) -> None:
            init_calls["n"] += 1

    class FakeDB:
        def __init__(self, config):
            self.config = config

            self._schema_info = {
                "host": config.database.host,
                "port": config.database.port,
                "database_name": config.database.database,
                "schema": config.database.db_schema,
                "current_user": config.database.username,
                "current_schema": config.database.db_schema,
                "missing_tables": [],
                "present_tables_count": 100,
            }
            self._visibility_by_artifact: dict[str, dict[str, int]] = {}

        def create_ingestion_run(self, embedding_model_name, embedding_model_version) -> IngestionRunContext:
            return IngestionRunContext(uuid4(), datetime.now(), embedding_model_name, embedding_model_version, "1.0.0")

        def get_last_build_state(self, source_ref):
            return None

        def list_known_artifacts_for_roots(self, input_roots):
            return []

        def replace_artifact_knowledge(self, run_ctx, source_file, selected, embeddings):
            created_count = len(selected)
            source_artifact_id = str(uuid4())
            self._visibility_by_artifact[source_artifact_id] = {
                "active_items": created_count,
                "labels": created_count,
                "scopes": 0,
                "tags": 0,
                "profiles": 0,
                "embeddings": created_count,
                "source_segments": created_count,
                "visible_items": created_count,
                "visible_embeddings": created_count,
            }
            return {"source_artifact_id": source_artifact_id, "created_count": created_count, "superseded_count": 0}

        def record_artifact_failure(self, source_ref, artifact_hash, error, run_ctx):
            raise AssertionError("no failure expected")

        def finalize_ingestion_run(self, run_id, status, notes):
            self.final_status = status

        def verify_required_schema(self):
            return self._schema_info

        def verify_artifact_visibility(self, source_artifact_id):
            return self._visibility_by_artifact.get(str(source_artifact_id), {"visible_items": 1, "visible_embeddings": 1, "labels": 1, "profiles": 0, "embeddings": 1, "active_items": 1})

        def get_database_totals(self):
            return {"current_database": self._schema_info["database_name"], "source_artifacts": 1, "active_items": 1, "embeddings": 1, "ingestion_runs": 1}

    def fake_infer_categories(candidate, config, local_llm, ontology, allow_unknown_labels, default_confidence_floor, allow_llm):
        assert local_llm is None
        labels: list[LabelEvidence] = []
        llm_result = LLMInferenceResult(normalized_retrieval_text="normalized", summary="summary", labels=[])
        return labels, llm_result, []

    monkeypatch.setattr(pipeline_mod, "EmbeddingProvider", _FakeEmbeddings)
    monkeypatch.setattr(pipeline_mod, "LocalLLM", CountingLocalLLM)
    monkeypatch.setattr(pipeline_mod, "infer_categories", fake_infer_categories)

    cfg = load_config(tmp_path)
    stats = run_pipeline(tmp_path, cfg, database=FakeDB(cfg))

    assert init_calls["n"] == 0
    assert stats.created_items > 0


def test_embedding_provider_quiet_mode_sets_show_progress_bar_false(monkeypatch: pytest.MonkeyPatch, tmp_path: Path, caplog: pytest.LogCaptureFixture) -> None:
    # Ensure a local embedding directory exists so provider does not download.
    (tmp_path / "emb").mkdir(parents=True, exist_ok=True)

    encode_kwargs: dict[str, object] = {}

    class FakeSentenceTransformer:
        def __init__(self, model_name_or_path, device=None):
            self.model_name_or_path = model_name_or_path
            self.device = device

        def encode(self, texts, **kwargs):
            nonlocal encode_kwargs
            encode_kwargs = kwargs
            return [[float(len(t or "")), 0.0] for t in texts]

    # Torch isn't used when force_device="cpu", but we still patch to keep behavior deterministic.
    fake_torch = types.SimpleNamespace(
        cuda=types.SimpleNamespace(is_available=lambda: False),
        backends=types.SimpleNamespace(mps=types.SimpleNamespace(is_available=lambda: False)),
    )
    monkeypatch.setitem(sys.modules, "torch", fake_torch)
    monkeypatch.setitem(sys.modules, "sentence_transformers", types.SimpleNamespace(SentenceTransformer=FakeSentenceTransformer))

    from app.embedding_provider import EmbeddingProvider

    provider = EmbeddingProvider(
        EmbeddingConfig(
            provider="sentence_transformers",
            model_name="fake-emb-model",
            local_model_path=str(tmp_path / "emb"),
            download_if_missing=False,
            batch_size=4,
            normalize_embeddings=True,
            force_device="cpu",
        )
    )

    caplog.set_level(logging.INFO)
    _ = provider.embed(["hello", "world"])

    assert encode_kwargs.get("show_progress_bar") is False
    assert encode_kwargs.get("convert_to_numpy") is True
    # Per-call INFO spam must not be present.
    assert "text item(s) using device" not in caplog.text


def test_fallback_profile_embeddings_are_batched(monkeypatch: pytest.MonkeyPatch, tmp_path: Path) -> None:
    docs = tmp_path / "docs"
    docs.mkdir()
    (docs / "a.md").write_text("# A\nBody", encoding="utf-8")
    _prepare_operator_config(tmp_path, docs, classification_enable_llm=False)

    calls: list[list[str]] = []

    class RecordingEmbeddings:
        def __init__(self, config: EmbeddingConfig) -> None:
            pass

        def model_metadata(self):
            return ("fake-emb", "1")

        def embed(self, texts: list[str]):
            calls.append(list(texts))
            # Return vectors matching the number of input texts.
            return [[float(len(t or "")), 0.0] for t in texts]

    def fake_generate_candidates(_document, _segmentation):
        # One deterministic candidate.
        candidate = CandidateSegment(
            source_path=Path("x"),
            source_type="text",
            span_level="file",  # type: ignore[arg-type]
            text="cand",
            heading_path=[],
            start_line=1,
            end_line=1,
            segment_hash="seg1",
            metadata=SourceMetadata(),
        )
        return [candidate]

    def fake_select_segments(candidates, specificity_scores):
        return list(candidates)

    def fake_infer_categories(candidate, config, local_llm, ontology, allow_unknown_labels, default_confidence_floor, allow_llm):
        labels: list[LabelEvidence] = []
        llm_result = LLMInferenceResult(normalized_retrieval_text="normalized", summary="sum", labels=labels)
        return labels, llm_result, []

    def fake_build_retrieval_profiles(item: SelectedSegment, _config):
        # Two profile embeddings + one default that is intentionally duplicated (should be skipped).
        return [("risk", "profile-a"), ("pattern", "profile-b"), ("default", item.normalized_retrieval_text), ("summary", item.summary or "")]

    class FakeDB:
        def __init__(self, config):
            self.config = config
            self._schema_info = {
                "host": config.database.host,
                "port": config.database.port,
                "database_name": config.database.database,
                "schema": config.database.db_schema,
                "current_user": config.database.username,
                "current_schema": config.database.db_schema,
                "missing_tables": [],
                "present_tables_count": 100,
            }
            self._visibility_by_artifact: dict[str, dict[str, int]] = {}

        def create_ingestion_run(self, embedding_model_name, embedding_model_version) -> IngestionRunContext:
            return IngestionRunContext(uuid4(), datetime.now(), embedding_model_name, embedding_model_version, "1.0.0")

        def get_last_build_state(self, source_ref):
            return None

        def list_known_artifacts_for_roots(self, input_roots):
            return []

        def replace_artifact_knowledge(self, run_ctx, source_file, selected, embeddings):
            assert "profile_rows" in embeddings
            created_count = len(selected)
            source_artifact_id = str(uuid4())
            self._visibility_by_artifact[source_artifact_id] = {
                "active_items": created_count,
                "labels": created_count,
                "scopes": 0,
                "tags": 0,
                "profiles": len(embeddings.get("profile_rows", [])),
                "embeddings": len(embeddings.get("profile_rows", [])) + created_count,
                "source_segments": created_count,
                "visible_items": created_count,
                "visible_embeddings": len(embeddings.get("profile_rows", [])) + created_count,
            }
            return {"source_artifact_id": source_artifact_id, "created_count": created_count, "superseded_count": 0}

        def record_artifact_failure(self, source_ref, artifact_hash, error, run_ctx):
            raise AssertionError("no failure expected")

        def finalize_ingestion_run(self, run_id, status, notes):
            self.final_status = status

        def verify_required_schema(self):
            return self._schema_info

        def verify_artifact_visibility(self, source_artifact_id):
            return self._visibility_by_artifact.get(str(source_artifact_id), {"visible_items": 1, "visible_embeddings": 1, "labels": 1, "profiles": 0, "embeddings": 1, "active_items": 1})

        def get_database_totals(self):
            return {"current_database": self._schema_info["database_name"], "source_artifacts": 1, "active_items": 1, "embeddings": 1, "ingestion_runs": 1}

    monkeypatch.setattr(pipeline_mod, "EmbeddingProvider", RecordingEmbeddings)
    monkeypatch.setattr(pipeline_mod, "generate_candidates", fake_generate_candidates)
    monkeypatch.setattr(pipeline_mod, "select_segments_with_duplicate_control", fake_select_segments)
    monkeypatch.setattr(pipeline_mod, "infer_categories", fake_infer_categories)
    monkeypatch.setattr(pipeline_mod, "build_retrieval_profiles", fake_build_retrieval_profiles)
    monkeypatch.setattr(pipeline_mod, "resolve_retrieval_class", lambda _labels, _ontology: "reference")  # type: ignore[assignment]

    cfg = load_config(tmp_path)
    run_pipeline(tmp_path, cfg, database=FakeDB(cfg))

    # Expect a single batched embed call for the two profile texts.
    profile_batched_calls = [c for c in calls if "profile-a" in c or "profile-b" in c]
    assert len(profile_batched_calls) >= 1
    # The profile texts must never be embedded one-by-one.
    assert all(not (c == ["profile-a"] or c == ["profile-b"]) for c in profile_batched_calls)
    assert any(set(c) == {"profile-a", "profile-b"} for c in profile_batched_calls)


def test_database_stream_write_visibility_and_superseded_stats(monkeypatch: pytest.MonkeyPatch, tmp_path: Path, caplog: pytest.LogCaptureFixture) -> None:
    docs = tmp_path / "docs"
    docs.mkdir()
    doc_path = docs / "a.md"
    doc_path.write_text("# A\nBody v1", encoding="utf-8")
    _prepare_operator_config(tmp_path, docs, classification_enable_llm=False)

    class FakeDBStreaming:
        def __init__(self, config):
            self.config = config
            self.build_states: dict[str, dict] = {}
            self.artifact_items: dict[str, list[dict]] = {}
            self.called = []
            self.final_status: str | None = None
            self.finalized_notes = None
            self._schema_info = {
                "host": config.database.host,
                "port": config.database.port,
                "database_name": config.database.database,
                "schema": config.database.db_schema,
                "current_user": config.database.username,
                "current_schema": config.database.db_schema,
                "missing_tables": [],
                "present_tables_count": 100,
            }
            self._visibility_by_artifact: dict[str, dict[str, int]] = {}

        def create_ingestion_run(self, embedding_model_name, embedding_model_version) -> IngestionRunContext:
            return IngestionRunContext(uuid4(), datetime.now(), embedding_model_name, embedding_model_version, "1.0.0")

        def get_last_build_state(self, source_ref):
            return self.build_states.get(source_ref)

        def list_known_artifacts_for_roots(self, input_roots):
            return []

        def replace_artifact_knowledge_stream(self, run_ctx, source_file, segments, embed_item_fn):
            source_ref = source_file.path.as_posix()
            self.called.append(len(segments))
            source_artifact_id = str(uuid4())

            # Simulate marking previous active rows as superseded.
            existing = self.artifact_items.get(source_ref, [])
            superseded_count = sum(1 for r in existing if r["status"] == "active")
            for r in existing:
                if r["status"] == "active":
                    r["status"] = "superseded"
                    r["valid_to"] = datetime.now()

            # Optional: execute embed callback to emulate real write flow.
            for idx, seg in enumerate(segments):
                _ = embed_item_fn(idx, seg)
                self.artifact_items.setdefault(source_ref, []).append({"status": "active", "valid_to": None})

            self.build_states[source_ref] = {
                "artifact_hash": source_file.content_hash,
                "builder_version": self.config.builder.builder_version,
                "classifier_version": self.config.builder.classifier_version,
                "embedding_model_version": run_ctx.embedding_model_version,
                "normalization_version": self.config.builder.normalization_version,
                "ontology_version": run_ctx.ontology_version,
                "schema_compatibility_version": self.config.builder.schema_compatibility_version,
                "build_status": "succeeded",
            }
            # Visible row counts for verification.
            active_count = sum(1 for r in self.artifact_items.get(source_ref, []) if r["status"] == "active")
            self._visibility_by_artifact[source_artifact_id] = {
                "active_items": active_count,
                "labels": active_count,
                "scopes": 0,
                "tags": 0,
                "profiles": 0,
                "embeddings": active_count,
                "source_segments": active_count,
                "visible_items": active_count,
                "visible_embeddings": active_count,
            }
            return {"source_artifact_id": source_artifact_id, "superseded_count": superseded_count, "created_count": len(segments)}

        def record_artifact_failure(self, source_ref, artifact_hash, error, run_ctx):
            raise AssertionError(f"no failure expected: {error}")

        def finalize_ingestion_run(self, run_id, status, notes):
            self.final_status = status
            self.finalized_notes = notes
        
        def verify_required_schema(self):
            return self._schema_info

        def verify_artifact_visibility(self, source_artifact_id):
            return self._visibility_by_artifact.get(str(source_artifact_id), {"visible_items": 1, "visible_embeddings": 1, "labels": 1, "profiles": 0, "embeddings": 1, "active_items": 1})

        def get_database_totals(self):
            # Active rows in this fake DB are the "embeddings" count.
            active_items_count = sum(1 for it in self.artifact_items.values() for r in it if r["status"] == "active")
            return {"current_database": self._schema_info["database_name"], "source_artifacts": 1, "active_items": active_items_count, "embeddings": active_items_count, "ingestion_runs": 1}

    class FakeEmbeddings:
        def __init__(self, config):
            pass

        def model_metadata(self):
            return ("fake-emb", "1")

        def embed(self, texts: list[str]):
            # Vectors length doesn't matter for this test; only shape.
            return [[0.1, 0.2] for _ in texts]

    def fake_generate_candidates(_document, _segmentation):
        candidate = CandidateSegment(
            source_path=Path("x"),
            source_type="text",
            span_level="file",  # type: ignore[arg-type]
            text="cand",
            heading_path=[],
            start_line=1,
            end_line=1,
            segment_hash="seg1",
            metadata=SourceMetadata(),
        )
        return [candidate]

    def fake_select_segments(candidates, specificity_scores):
        return list(candidates)

    def fake_infer_categories(candidate, config, local_llm, ontology, allow_unknown_labels, default_confidence_floor, allow_llm):
        labels: list[LabelEvidence] = []
        llm_result = LLMInferenceResult(normalized_retrieval_text="normalized", summary="sum", labels=labels)
        return labels, llm_result, []

    def fake_build_retrieval_profiles(item: SelectedSegment, _config):
        return [("summary", item.summary or ""), ("default", item.normalized_retrieval_text), ("risk", "profile-a")]

    monkeypatch.setattr(pipeline_mod, "EmbeddingProvider", FakeEmbeddings)
    monkeypatch.setattr(pipeline_mod, "generate_candidates", fake_generate_candidates)
    monkeypatch.setattr(pipeline_mod, "select_segments_with_duplicate_control", fake_select_segments)
    monkeypatch.setattr(pipeline_mod, "infer_categories", fake_infer_categories)
    monkeypatch.setattr(pipeline_mod, "build_retrieval_profiles", fake_build_retrieval_profiles)
    monkeypatch.setattr(pipeline_mod, "resolve_retrieval_class", lambda _labels, _ontology: "reference")  # type: ignore[assignment]

    caplog.set_level(logging.INFO)
    cfg = load_config(tmp_path)
    db = FakeDBStreaming(cfg)

    stats1 = run_pipeline(tmp_path, cfg, database=db)
    assert db.called == [1]
    assert stats1.created_items == 1
    assert db.build_states[doc_path.as_posix()]["build_status"] == "succeeded"

    # Second run with changed file content should supersede previous active rows.
    caplog.clear()
    doc_path.write_text("# A\nBody v2", encoding="utf-8")
    stats2 = run_pipeline(tmp_path, cfg, database=db)

    assert db.called == [1, 1]
    assert stats2.superseded_items == 1

    # Artifact-level and run-level summaries must be visible and consistent.
    assert "Stored artifact path=" in caplog.text
    assert f"superseded=1" in caplog.text
    assert "Run completed status=" in caplog.text
    assert "superseded_items=1" in caplog.text


def test_llm_budget_cap_limits_infer_calls(monkeypatch: pytest.MonkeyPatch, tmp_path: Path, caplog: pytest.LogCaptureFixture) -> None:
    """
    Budget cap: only the first `max_llm_segments_per_run` segments may attempt local LLM inference.
    """

    docs = tmp_path / "docs"
    docs.mkdir()
    (docs / "a.md").write_text("# A\nBody", encoding="utf-8")
    _prepare_operator_config(tmp_path, docs, classification_enable_llm=True)

    cfg = load_config(tmp_path)
    cfg.classification.max_llm_segments_per_run = 2
    # Avoid extra heuristics.
    cfg.builder.enable_case_shape_inference = False

    local_infer_calls = {"n": 0}

    class FakeLocalLLMRuntime:
        def __init__(self, _config) -> None:
            pass

        def infer(self, _prompt: str, retries: int = 2):
            local_infer_calls["n"] += 1
            return LLMInferenceResult(normalized_retrieval_text="normalized", summary="sum", labels=[], tags=[])

    # Candidates needing LLM: pipeline will call infer_categories allow_llm=False first,
    # then allow_llm=True only for budgeted segments.
    candidates: list[CandidateSegment] = []
    for i in range(5):
        candidates.append(
            CandidateSegment(
                source_path=Path("x"),
                source_type="text",
                span_level="file",  # type: ignore[arg-type]
                text=f"cand-{i}",
                heading_path=[],
                start_line=1,
                end_line=1,
                segment_hash=f"seg{i}",
                metadata=SourceMetadata(),
            )
        )

    class FakeEmbeddings:
        def __init__(self, config):
            pass

        def model_metadata(self):
            return ("fake-emb", "1")

        def embed(self, texts: list[str]):
            return [[0.1, 0.2] for _ in texts]

    class FakeDB:
        def __init__(self, config):
            self.config = config
            self._schema_info = {
                "host": config.database.host,
                "port": config.database.port,
                "database_name": config.database.database,
                "schema": config.database.db_schema,
                "current_user": config.database.username,
                "current_schema": config.database.db_schema,
                "missing_tables": [],
                "present_tables_count": 100,
            }

        def verify_required_schema(self):
            return self._schema_info

        def verify_artifact_visibility(self, source_artifact_id):
            return {"visible_items": 1, "visible_embeddings": 1, "labels": 0, "profiles": 0, "embeddings": 1, "active_items": 1}

        def create_ingestion_run(self, embedding_model_name, embedding_model_version) -> IngestionRunContext:
            return IngestionRunContext(uuid4(), datetime.now(), embedding_model_name, embedding_model_version, "1.0.0")

        def get_last_build_state(self, source_ref):
            return None

        def list_known_artifacts_for_roots(self, input_roots):
            return []

        def replace_artifact_knowledge(self, run_ctx, source_file, selected, embeddings):
            return {"source_artifact_id": str(uuid4()), "created_count": len(selected), "superseded_count": 0}

        def record_artifact_failure(self, source_ref, artifact_hash, error, run_ctx):
            raise AssertionError(f"no failure expected: {error}")

        def finalize_ingestion_run(self, run_id, status, notes):
            self.final_status = status

        def get_database_totals(self):
            return {"current_database": self._schema_info["database_name"], "source_artifacts": 1, "active_items": 1, "embeddings": 1, "ingestion_runs": 1}

    def fake_generate_candidates(_document, _segmentation):
        return list(candidates)

    def fake_select_segments(cands, _specificity_scores):
        return list(cands)

    def fake_infer_categories(candidate, config, local_llm, ontology, allow_unknown_labels, default_confidence_floor, allow_llm):
        # Rule-only pass: confidence too low to meet threshold => LLM should be attempted (until budget exhausted).
        if not allow_llm:
            labels = [LabelEvidence(label="reference", label_role="secondary", confidence=0.1, source_method="rule_based", explanation=None)]
            llm_result = LLMInferenceResult(normalized_retrieval_text="normalized", summary="sum", labels=[])
            return labels, llm_result, []

        # Budgeted LLM pass.
        _ = local_llm.infer("prompt")  # triggers LocalLLM.infer counting via LazyLocalLLM
        labels = [LabelEvidence(label="reference", label_role="secondary", confidence=0.1, source_method="llm_inference", explanation=None)]
        llm_result = LLMInferenceResult(normalized_retrieval_text="normalized", summary="sum", labels=[])
        return labels, llm_result, []

    monkeypatch.setattr(pipeline_mod, "EmbeddingProvider", FakeEmbeddings)
    monkeypatch.setattr(pipeline_mod, "LocalLLM", FakeLocalLLMRuntime)
    monkeypatch.setattr(pipeline_mod, "generate_candidates", fake_generate_candidates)
    monkeypatch.setattr(pipeline_mod, "select_segments_with_duplicate_control", fake_select_segments)
    monkeypatch.setattr(pipeline_mod, "infer_categories", fake_infer_categories)
    monkeypatch.setattr(pipeline_mod, "build_retrieval_profiles", lambda _item, _config: [])
    monkeypatch.setattr(pipeline_mod, "resolve_retrieval_class", lambda _labels, _ontology: "reference")  # type: ignore[assignment]
    monkeypatch.setattr(pipeline_mod, "extract_scopes_and_tags", lambda _candidate, _labels, _ontology, _tags: ([], []))  # type: ignore[assignment]

    caplog.set_level(logging.INFO)
    stats = run_pipeline(tmp_path, cfg, database=FakeDB(cfg))

    assert local_infer_calls["n"] == 2
    assert "llm_attempted_segments=2" in caplog.text
    assert "llm_skipped_by_budget=3" in caplog.text

    # Artifact-level summary should reflect budget exhaustion.
    assert "llm_attempted=2" in caplog.text
    assert "llm_budget_exhausted=true" in caplog.text

    # Sanity: run still writes at least something.
    assert stats.created_items > 0


def test_rule_sufficient_shortcut_skips_llm(monkeypatch: pytest.MonkeyPatch, tmp_path: Path) -> None:
    docs = tmp_path / "docs"
    docs.mkdir()
    (docs / "a.md").write_text("# A\nBody", encoding="utf-8")
    _prepare_operator_config(tmp_path, docs, classification_enable_llm=True)

    cfg = load_config(tmp_path)
    cfg.classification.max_llm_segments_per_run = 1
    cfg.builder.enable_case_shape_inference = False

    local_infer_calls = {"n": 0}

    class FakeLocalLLMRuntime:
        def __init__(self, _config) -> None:
            pass

        def infer(self, _prompt: str, retries: int = 2):
            local_infer_calls["n"] += 1
            return LLMInferenceResult(normalized_retrieval_text="normalized", summary="sum", labels=[], tags=[])

    class FakeEmbeddings:
        def __init__(self, _config):
            pass

        def model_metadata(self):
            return ("fake-emb", "1")

        def embed(self, texts: list[str]):
            return [[0.1, 0.2] for _ in texts]

    class FakeDB:
        def __init__(self, config):
            self.config = config
            self._schema_info = {
                "host": config.database.host,
                "port": config.database.port,
                "database_name": config.database.database,
                "schema": config.database.db_schema,
                "current_user": config.database.username,
                "current_schema": config.database.db_schema,
                "missing_tables": [],
                "present_tables_count": 100,
            }

        def verify_required_schema(self):
            return self._schema_info

        def verify_artifact_visibility(self, source_artifact_id):
            return {"visible_items": 1, "visible_embeddings": 1, "labels": 0, "profiles": 0, "embeddings": 1, "active_items": 1}

        def create_ingestion_run(self, embedding_model_name, embedding_model_version) -> IngestionRunContext:
            return IngestionRunContext(uuid4(), datetime.now(), embedding_model_name, embedding_model_version, "1.0.0")

        def get_last_build_state(self, source_ref):
            return None

        def list_known_artifacts_for_roots(self, input_roots):
            return []

        def replace_artifact_knowledge(self, run_ctx, source_file, selected, embeddings):
            return {"source_artifact_id": str(uuid4()), "created_count": len(selected), "superseded_count": 0}

        def record_artifact_failure(self, source_ref, artifact_hash, error, run_ctx):
            raise AssertionError(f"no failure expected: {error}")

        def finalize_ingestion_run(self, run_id, status, notes):
            self.final_status = status

        def get_database_totals(self):
            return {"current_database": self._schema_info["database_name"], "source_artifacts": 1, "active_items": 1, "embeddings": 1, "ingestion_runs": 1}

    candidates = [
        CandidateSegment(
            source_path=Path("x"),
            source_type="text",
            span_level="file",  # type: ignore[arg-type]
            text="cand",
            heading_path=[],
            start_line=1,
            end_line=1,
            segment_hash="seg0",
            metadata=SourceMetadata(),
        )
    ]

    def fake_generate_candidates(_document, _segmentation):
        return list(candidates)

    def fake_select_segments(cands, _specificity_scores):
        return list(cands)

    def fake_infer_categories(candidate, config, local_llm, ontology, allow_unknown_labels, default_confidence_floor, allow_llm):
        if not allow_llm:
            # Confidence high enough to satisfy threshold => pipeline should never attempt allow_llm=True.
            labels = [LabelEvidence(label="best_practice", label_role="primary", confidence=0.99, source_method="rule_based", explanation=None)]
            llm_result = LLMInferenceResult(normalized_retrieval_text="normalized", summary="sum", labels=[])
            return labels, llm_result, []
        raise AssertionError("LLM must not be called for rule-sufficient segments")

    monkeypatch.setattr(pipeline_mod, "EmbeddingProvider", FakeEmbeddings)
    monkeypatch.setattr(pipeline_mod, "LocalLLM", FakeLocalLLMRuntime)
    monkeypatch.setattr(pipeline_mod, "generate_candidates", fake_generate_candidates)
    monkeypatch.setattr(pipeline_mod, "select_segments_with_duplicate_control", fake_select_segments)
    monkeypatch.setattr(pipeline_mod, "infer_categories", fake_infer_categories)
    monkeypatch.setattr(pipeline_mod, "build_retrieval_profiles", lambda _item, _config: [])
    monkeypatch.setattr(pipeline_mod, "resolve_retrieval_class", lambda _labels, _ontology: "best_practice")  # type: ignore[assignment]
    monkeypatch.setattr(pipeline_mod, "extract_scopes_and_tags", lambda _candidate, _labels, _ontology, _tags: ([], []))  # type: ignore[assignment]

    stats = run_pipeline(tmp_path, cfg, database=FakeDB(cfg))
    assert local_infer_calls["n"] == 0
    assert stats.created_items > 0


def test_category_inference_llm_fallback_is_debug_only_and_truncates_prompt(monkeypatch: pytest.MonkeyPatch, tmp_path: Path, caplog: pytest.LogCaptureFixture) -> None:
    """
    Ensure category_inference fallback does not spam WARNING and that the prompt truncation is 2000 chars.
    """

    from app.category_inference import infer_categories as real_infer_categories
    from app.category_ontology import load_category_ontology
    from app.models import ClassificationConfig

    root = Path(__file__).resolve().parents[1]
    ontology = load_category_ontology(root / "config" / "category_ontology.json", root / "config" / "category_ontology.schema.json")

    candidate = CandidateSegment(
        source_path=Path("x"),
        source_type="text",
        span_level="file",  # type: ignore[arg-type]
        text=("A" * 2105) + "TAIL",
        heading_path=[],
        start_line=1,
        end_line=1,
        metadata=SourceMetadata(),
    )

    class RaisingLocalLLM:
        def __init__(self) -> None:
            self.captured_prompt: str | None = None

        def infer(self, prompt: str):
            self.captured_prompt = prompt
            raise RuntimeError("boom")

    local_llm = RaisingLocalLLM()
    cfg = ClassificationConfig(enable_llm=True, enable_rule_based=True, enable_heading_rules=True, min_primary_label_confidence=0.6)

    caplog.set_level(logging.WARNING, logger="app.category_inference")

    _labels, _llm_result, warnings = real_infer_categories(
        candidate=candidate,
        config=cfg,
        local_llm=local_llm,
        ontology=ontology,
        allow_unknown_labels=False,
        default_confidence_floor=0.55,
        allow_llm=True,
    )

    assert any(str(w).startswith("local_llm_fallback:") for w in warnings)
    assert not any("Local LLM classification fallback used" in rec.message for rec in caplog.records)
    assert len(caplog.records) == 0

    assert local_llm.captured_prompt is not None
    # Ensure prompt includes only first 2000 chars of candidate.text.
    assert candidate.text[:2000] in local_llm.captured_prompt
    assert "TAIL" not in local_llm.captured_prompt


def test_startup_db_verification_fails_fast_on_missing_tables(tmp_path: Path) -> None:
    root = Path(__file__).resolve().parents[1]
    docs = tmp_path / "docs"
    docs.mkdir()
    (docs / "a.md").write_text("# A\nBody", encoding="utf-8")
    _prepare_operator_config(tmp_path, docs, classification_enable_llm=False)

    cfg = load_config(tmp_path)

    class FakeDB:
        def verify_required_schema(self):
            raise RuntimeError(
                "Database schema verification failed: host=127.0.0.1 port=5432 database=memory_db schema=public missing_tables=[knowledge_items, source_artifacts]"
            )

    with pytest.raises(RuntimeError, match=r"missing_tables=.*knowledge_items"):
        run_pipeline(tmp_path, cfg, database=FakeDB())


def test_post_write_visibility_zero_marks_artifact_failed(monkeypatch: pytest.MonkeyPatch, tmp_path: Path) -> None:
    docs = tmp_path / "docs"
    docs.mkdir()
    (docs / "a.md").write_text("# A\nBody", encoding="utf-8")
    _prepare_operator_config(tmp_path, docs, classification_enable_llm=False)

    cfg = load_config(tmp_path)
    cfg.builder.enable_case_shape_inference = False

    class FakeEmbeddings:
        def __init__(self, config):
            pass

        def model_metadata(self):
            return ("fake-emb", "1")

        def embed(self, texts: list[str]):
            return [[0.1, 0.2] for _ in texts]

    class FakeDB:
        def __init__(self, config):
            self.config = config
            self.failed = 0
            self._schema_info = {
                "host": config.database.host,
                "port": config.database.port,
                "database_name": config.database.database,
                "schema": config.database.db_schema,
                "current_user": config.database.username,
                "current_schema": config.database.db_schema,
                "missing_tables": [],
                "present_tables_count": 100,
            }

        def verify_required_schema(self):
            return self._schema_info

        def verify_artifact_visibility(self, source_artifact_id):
            return {"visible_items": 0, "visible_embeddings": 0, "labels": 0, "profiles": 0, "embeddings": 0, "active_items": 0}

        def create_ingestion_run(self, embedding_model_name, embedding_model_version) -> IngestionRunContext:
            return IngestionRunContext(uuid4(), datetime.now(), embedding_model_name, embedding_model_version, "1.0.0")

        def get_last_build_state(self, source_ref):
            return None

        def list_known_artifacts_for_roots(self, input_roots):
            return []

        def replace_artifact_knowledge_stream(self, run_ctx, source_file, segments, embed_item_fn):
            return {"source_artifact_id": str(uuid4()), "created_count": len(segments), "superseded_count": 0}

        def record_artifact_failure(self, source_ref, artifact_hash, error, run_ctx):
            self.failed += 1

        def finalize_ingestion_run(self, run_id, status, notes):
            self.final_status = status

        def get_database_totals(self):
            return {"current_database": self._schema_info["database_name"], "source_artifacts": 1, "active_items": 0, "embeddings": 0, "ingestion_runs": 1}

    # Keep processing minimal.
    def fake_generate_candidates(_document, _segmentation):
        return [
            CandidateSegment(
                source_path=Path("x"),
                source_type="text",
                span_level="file",  # type: ignore[arg-type]
                text="cand",
                heading_path=[],
                start_line=1,
                end_line=1,
                segment_hash="seg1",
                metadata=SourceMetadata(),
            )
        ]

    def fake_select_segments(cands, _specificity_scores):
        return list(cands)

    def fake_infer_categories(candidate, config, local_llm, ontology, allow_unknown_labels, default_confidence_floor, allow_llm):
        labels = [LabelEvidence(label="reference", label_role="primary", confidence=0.4, source_method="fallback", explanation=None)]
        llm_result = LLMInferenceResult(normalized_retrieval_text="normalized", summary="sum", labels=[])
        return labels, llm_result, []

    monkeypatch.setattr(pipeline_mod, "EmbeddingProvider", FakeEmbeddings)
    monkeypatch.setattr(pipeline_mod, "generate_candidates", fake_generate_candidates)
    monkeypatch.setattr(pipeline_mod, "select_segments_with_duplicate_control", fake_select_segments)
    monkeypatch.setattr(pipeline_mod, "infer_categories", fake_infer_categories)
    monkeypatch.setattr(pipeline_mod, "build_retrieval_profiles", lambda _item, _config: [])
    monkeypatch.setattr(pipeline_mod, "resolve_retrieval_class", lambda _labels, _ontology: "reference")  # type: ignore[assignment]
    monkeypatch.setattr(pipeline_mod, "extract_scopes_and_tags", lambda _candidate, _labels, _ontology, _tags: ([], []))  # type: ignore[assignment]

    db = FakeDB(cfg)
    stats = run_pipeline(tmp_path, cfg, database=db)
    assert stats.failed_files == 1
    assert db.failed == 1


def test_final_db_totals_summary_logged(monkeypatch: pytest.MonkeyPatch, tmp_path: Path, caplog: pytest.LogCaptureFixture) -> None:
    docs = tmp_path / "docs"
    docs.mkdir()
    (docs / "a.md").write_text("# A\nBody", encoding="utf-8")
    _prepare_operator_config(tmp_path, docs, classification_enable_llm=False)
    cfg = load_config(tmp_path)
    cfg.builder.enable_case_shape_inference = False

    class FakeEmbeddings:
        def __init__(self, config):
            pass

        def model_metadata(self):
            return ("fake-emb", "1")

        def embed(self, texts: list[str]):
            return [[0.1, 0.2] for _ in texts]

    class FakeDB:
        def __init__(self, config):
            self.config = config
            self._schema_info = {
                "host": config.database.host,
                "port": config.database.port,
                "database_name": config.database.database,
                "schema": config.database.db_schema,
                "current_user": config.database.username,
                "current_schema": config.database.db_schema,
                "missing_tables": [],
                "present_tables_count": 100,
            }

        def verify_required_schema(self):
            return self._schema_info

        def verify_artifact_visibility(self, source_artifact_id):
            return {"visible_items": 1, "visible_embeddings": 1, "labels": 0, "profiles": 0, "embeddings": 1, "active_items": 1}

        def create_ingestion_run(self, embedding_model_name, embedding_model_version) -> IngestionRunContext:
            return IngestionRunContext(uuid4(), datetime.now(), embedding_model_name, embedding_model_version, "1.0.0")

        def get_last_build_state(self, source_ref):
            return None

        def list_known_artifacts_for_roots(self, input_roots):
            return []

        def replace_artifact_knowledge(self, run_ctx, source_file, segments, embeddings):
            return {"source_artifact_id": str(uuid4()), "created_count": len(segments), "superseded_count": 0}

        def record_artifact_failure(self, source_ref, artifact_hash, error, run_ctx):
            raise AssertionError(f"no failure expected: {error}")

        def finalize_ingestion_run(self, run_id, status, notes):
            self.final_status = status

        def get_database_totals(self):
            return {"current_database": "memory_db", "source_artifacts": 42, "active_items": 10, "embeddings": 77, "ingestion_runs": 3}

    def fake_generate_candidates(_document, _segmentation):
        return [
            CandidateSegment(
                source_path=Path("x"),
                source_type="text",
                span_level="file",  # type: ignore[arg-type]
                text="cand",
                heading_path=[],
                start_line=1,
                end_line=1,
                segment_hash="seg1",
                metadata=SourceMetadata(),
            )
        ]

    def fake_select_segments(cands, _specificity_scores):
        return list(cands)

    def fake_infer_categories(candidate, config, local_llm, ontology, allow_unknown_labels, default_confidence_floor, allow_llm):
        labels = [LabelEvidence(label="reference", label_role="primary", confidence=0.9, source_method="fallback", explanation=None)]
        llm_result = LLMInferenceResult(normalized_retrieval_text="normalized", summary="sum", labels=[])
        return labels, llm_result, []

    monkeypatch.setattr(pipeline_mod, "EmbeddingProvider", FakeEmbeddings)
    monkeypatch.setattr(pipeline_mod, "generate_candidates", fake_generate_candidates)
    monkeypatch.setattr(pipeline_mod, "select_segments_with_duplicate_control", fake_select_segments)
    monkeypatch.setattr(pipeline_mod, "infer_categories", fake_infer_categories)
    monkeypatch.setattr(pipeline_mod, "build_retrieval_profiles", lambda _item, _config: [])
    monkeypatch.setattr(pipeline_mod, "resolve_retrieval_class", lambda _labels, _ontology: "reference")  # type: ignore[assignment]
    monkeypatch.setattr(pipeline_mod, "extract_scopes_and_tags", lambda _candidate, _labels, _ontology, _tags: ([], []))  # type: ignore[assignment]

    caplog.set_level(logging.INFO)
    run_pipeline(tmp_path, cfg, database=FakeDB(cfg))

    assert "Database totals current_database=memory_db source_artifacts=42 active_items=10 embeddings=77 ingestion_runs=3" in caplog.text

