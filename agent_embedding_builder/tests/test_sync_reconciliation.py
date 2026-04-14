from __future__ import annotations

import json
from datetime import datetime
from pathlib import Path
from uuid import uuid4

import pytest

from app.config_loader import load_config
from app.path_utils import normalize_fs_path, path_is_under_roots
from app.pipeline import run_pipeline


def _write_runtime_config(tmp_path: Path, docs_dir: Path) -> None:
    root = Path(__file__).resolve().parents[1]
    config_dir = tmp_path / "config"
    config_dir.mkdir(exist_ok=True)
    for name in ["builder_defaults.json", "builder_config.schema.json", "category_ontology.json", "category_ontology.schema.json"]:
        config_dir.joinpath(name).write_text((root / "config" / name).read_text(encoding="utf-8"), encoding="utf-8")
    (tmp_path / "models").mkdir(exist_ok=True)
    (tmp_path / "models" / "qwen-instruct.gguf").write_text("x", encoding="utf-8")
    (tmp_path / "models" / "embeddings").mkdir(exist_ok=True)
    config_dir.joinpath("builder_config.jsonc").write_text(
        (
            "{"
            '"database":{"driver":"postgresql","host":"127.0.0.1","port":5432,"database":"memory_db","username":"postgres","password":"change_me","schema":"public","ssl_mode":"prefer"},'
            f'"knowledge_base":{{"input_roots":["{docs_dir.as_posix()}"],"include_extensions":[".md",".txt"],"exclude_dirs":[],"exclude_globs":[],"follow_symlinks":false}},'
            '"llm":{"provider":"llama_cpp","local_model_path":"../models/qwen-instruct.gguf","download_if_missing":false,"download_source":{"type":"huggingface","repo_id":"r","filename":"f","local_dir":"../models"}}'
            "}"
        ),
        encoding="utf-8",
    )


class FakeEmbeddings:
    def __init__(self, config):
        pass

    def model_metadata(self):
        return ("fake-emb", "1")

    def embed(self, texts):
        return [[float(len(text or "")), 0.0] for text in texts]


class FakeLLM:
    def __init__(self, config):
        pass

    def infer(self, prompt):
        from app.models import LLMInferenceResult

        return LLMInferenceResult(normalized_retrieval_text="normalized", summary="summary")


class FakeDatabase:
    def __init__(self, config):
        self.config = config
        self.artifacts: dict[str, dict] = {}
        self.build_states: dict[str, dict] = {}
        self.knowledge_items: list[dict] = []
        self.finalized_notes: dict | None = None
        self.final_status: str | None = None
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

    def create_ingestion_run(self, embedding_model_name, embedding_model_version):
        from app.models import IngestionRunContext

        return IngestionRunContext(uuid4(), datetime.now(), embedding_model_name, embedding_model_version, getattr(self.config, "_ontology_version", "1.0.0"))

    def get_last_build_state(self, source_ref):
        return self.build_states.get(source_ref)

    def list_known_artifacts_for_roots(self, input_roots):
        return [
            {
                "source_artifact_id": artifact["source_artifact_id"],
                "source_ref": artifact["source_ref"],
                "source_path": artifact["source_path"],
                "status": artifact["status"],
                "artifact_hash": artifact["artifact_hash"],
            }
            for artifact in self.artifacts.values()
            if path_is_under_roots(artifact["source_path"], input_roots)
        ]

    def _activate_artifact(self, source_file):
        source_ref = source_file.path.as_posix()
        artifact = self.artifacts.get(source_ref)
        if artifact is None:
            artifact = {
                "source_artifact_id": str(uuid4()),
                "source_ref": source_ref,
                "source_path": source_ref,
                "artifact_hash": source_file.content_hash,
                "status": "active",
                "removed_at": None,
                "last_seen_at": datetime.now(),
            }
            self.artifacts[source_ref] = artifact
        artifact.update(
            {
                "source_path": source_ref,
                "artifact_hash": source_file.content_hash,
                "status": "active",
                "removed_at": None,
                "last_seen_at": datetime.now(),
            }
        )
        return artifact

    def record_artifact_seen(self, source_file):
        return self._activate_artifact(source_file)["source_artifact_id"]

    def replace_artifact_knowledge(self, run_ctx, source_file, segments, embeddings):
        artifact = self._activate_artifact(source_file)
        superseded_count = 0
        for row in self.knowledge_items:
            if row["source_ref"] == artifact["source_ref"] and row["status"] == "active":
                row["status"] = "superseded"
                row["valid_to"] = datetime.now()
                superseded_count += 1
        for idx, _item in enumerate(segments):
            self.knowledge_items.append(
                {
                    "knowledge_item_id": str(uuid4()),
                    "source_ref": artifact["source_ref"],
                    "source_artifact_id": artifact["source_artifact_id"],
                    "status": "active",
                    "valid_to": None,
                    "embedding_row": embeddings.get("normalized_retrieval_text", [])[idx] if idx < len(embeddings.get("normalized_retrieval_text", [])) else None,
                }
            )
        self.build_states[artifact["source_ref"]] = {
            "artifact_hash": source_file.content_hash,
            "builder_version": self.config.builder.builder_version,
            "classifier_version": self.config.builder.classifier_version,
            "embedding_model_version": run_ctx.embedding_model_version,
            "normalization_version": self.config.builder.normalization_version,
            "ontology_version": run_ctx.ontology_version,
            "schema_compatibility_version": self.config.builder.schema_compatibility_version,
            "build_status": "succeeded",
        }
        return {"source_artifact_id": artifact["source_artifact_id"], "superseded_count": superseded_count, "created_count": len(segments)}

    def archive_removed_artifact(self, source_artifact_id, run_ctx):
        archived_count = 0
        for artifact in self.artifacts.values():
            if artifact["source_artifact_id"] != source_artifact_id:
                continue
            artifact["status"] = "removed"
            artifact["removed_at"] = datetime.now()
            for row in self.knowledge_items:
                if row["source_artifact_id"] == source_artifact_id and row["status"] == "active":
                    row["status"] = "archived"
                    row["valid_to"] = datetime.now()
                    archived_count += 1
            self.build_states[artifact["source_ref"]] = {
                **self.build_states.get(artifact["source_ref"], {}),
                "artifact_hash": artifact["artifact_hash"],
                "builder_version": self.config.builder.builder_version,
                "classifier_version": self.config.builder.classifier_version,
                "embedding_model_version": run_ctx.embedding_model_version,
                "normalization_version": self.config.builder.normalization_version,
                "ontology_version": run_ctx.ontology_version,
                "schema_compatibility_version": self.config.builder.schema_compatibility_version,
                "build_status": "removed",
            }
            break
        return archived_count

    def record_artifact_failure(self, source_ref, artifact_hash, error, run_ctx):
        self.build_states[source_ref] = {
            "artifact_hash": artifact_hash,
            "builder_version": self.config.builder.builder_version,
            "classifier_version": self.config.builder.classifier_version,
            "embedding_model_version": run_ctx.embedding_model_version,
            "normalization_version": self.config.builder.normalization_version,
            "ontology_version": run_ctx.ontology_version,
            "schema_compatibility_version": self.config.builder.schema_compatibility_version,
            "build_status": "failed",
        }

    def finalize_ingestion_run(self, run_id, status, notes):
        self.final_status = status
        self.finalized_notes = notes

    def verify_required_schema(self):
        return self._schema_info

    def verify_artifact_visibility(self, source_artifact_id):
        active_items = [
            it
            for it in self.knowledge_items
            if it["source_artifact_id"] == source_artifact_id and it["status"] == "active"
        ]
        visible_items = len(active_items)
        return {
            "active_items": visible_items,
            "labels": visible_items,
            "scopes": 0,
            "tags": 0,
            "profiles": 0,
            "embeddings": visible_items,
            "source_segments": visible_items,
            "visible_items": visible_items,
            "visible_embeddings": visible_items,
        }

    def get_database_totals(self):
        active_items_count = sum(1 for it in self.knowledge_items if it["status"] == "active")
        return {
            "current_database": self._schema_info["database_name"],
            "source_artifacts": len(self.artifacts),
            "active_items": active_items_count,
            "embeddings": active_items_count,
            "ingestion_runs": 1,
        }


def _run(tmp_path: Path, docs_dir: Path, monkeypatch: pytest.MonkeyPatch, db: FakeDatabase | None = None):
    import app.pipeline as pipeline_mod

    _write_runtime_config(tmp_path, docs_dir)
    monkeypatch.setattr(pipeline_mod, "EmbeddingProvider", FakeEmbeddings)
    monkeypatch.setattr(pipeline_mod, "LocalLLM", FakeLLM)
    config = load_config(tmp_path)
    db = db or FakeDatabase(config)
    stats = run_pipeline(tmp_path, config, database=db)
    return config, db, stats


def test_added_file_creates_active_artifact_and_items(tmp_path: Path, monkeypatch: pytest.MonkeyPatch) -> None:
    docs = tmp_path / "docs"
    docs.mkdir()
    (docs / "a.md").write_text("# Title\nUseful implementation guidance.", encoding="utf-8")
    _config, db, stats = _run(tmp_path, docs, monkeypatch)
    artifact = next(iter(db.artifacts.values()))
    assert artifact["status"] == "active"
    assert any(item["status"] == "active" for item in db.knowledge_items)
    assert stats.created_items > 0


def test_modified_file_supersedes_old_items(tmp_path: Path, monkeypatch: pytest.MonkeyPatch) -> None:
    docs = tmp_path / "docs"
    docs.mkdir()
    path = docs / "a.md"
    path.write_text("# Title\nVersion A", encoding="utf-8")
    _config, db, _stats = _run(tmp_path, docs, monkeypatch)
    path.write_text("# Title\nVersion B with more detail", encoding="utf-8")
    _config, db, stats = _run(tmp_path, docs, monkeypatch, db=db)
    assert any(item["status"] == "superseded" and item["valid_to"] is not None for item in db.knowledge_items)
    assert sum(1 for item in db.knowledge_items if item["status"] == "active") > 0
    assert stats.superseded_items > 0


def test_deleted_file_reconciliation_archives_active_rows(tmp_path: Path, monkeypatch: pytest.MonkeyPatch) -> None:
    docs = tmp_path / "docs"
    docs.mkdir()
    path = docs / "a.md"
    path.write_text("# Title\nPresent", encoding="utf-8")
    _config, db, _stats = _run(tmp_path, docs, monkeypatch)
    path.unlink()
    _config, db, stats = _run(tmp_path, docs, monkeypatch, db=db)
    artifact = next(iter(db.artifacts.values()))
    assert artifact["status"] == "removed"
    assert all(item["status"] == "archived" for item in db.knowledge_items)
    assert all(item["valid_to"] is not None for item in db.knowledge_items)
    assert db.build_states[artifact["source_ref"]]["build_status"] == "removed"
    assert stats.reconciled_removed_files == 1


def test_reappearing_file_returns_artifact_to_active(tmp_path: Path, monkeypatch: pytest.MonkeyPatch) -> None:
    docs = tmp_path / "docs"
    docs.mkdir()
    path = docs / "a.md"
    path.write_text("# Title\nPresent", encoding="utf-8")
    _config, db, _stats = _run(tmp_path, docs, monkeypatch)
    path.unlink()
    _config, db, _stats = _run(tmp_path, docs, monkeypatch, db=db)
    path.write_text("# Title\nReturned", encoding="utf-8")
    _config, db, _stats = _run(tmp_path, docs, monkeypatch, db=db)
    artifact = next(iter(db.artifacts.values()))
    assert artifact["status"] == "active"
    assert db.build_states[artifact["source_ref"]]["build_status"] == "succeeded"
    assert any(item["status"] == "active" for item in db.knowledge_items)


def test_out_of_scope_artifact_is_not_archived(tmp_path: Path, monkeypatch: pytest.MonkeyPatch) -> None:
    docs = tmp_path / "docs"
    docs.mkdir()
    (docs / "a.md").write_text("# Title\nPresent", encoding="utf-8")
    _config, db, _stats = _run(tmp_path, docs, monkeypatch)
    outside_path = (tmp_path / "outside" / "b.md").resolve()
    outside_path.parent.mkdir(exist_ok=True)
    db.artifacts[outside_path.as_posix()] = {
        "source_artifact_id": "outside-id",
        "source_ref": outside_path.as_posix(),
        "source_path": outside_path.as_posix(),
        "artifact_hash": "outside-hash",
        "status": "active",
        "removed_at": None,
        "last_seen_at": datetime.now(),
    }
    db.build_states[outside_path.as_posix()] = {"artifact_hash": "outside-hash", "build_status": "succeeded"}
    _config, db, _stats = _run(tmp_path, docs, monkeypatch, db=db)
    assert db.artifacts[outside_path.as_posix()]["status"] == "active"


def test_relative_absolute_root_reconciliation_is_cwd_independent(tmp_path: Path, monkeypatch: pytest.MonkeyPatch) -> None:
    docs = tmp_path / "docs"
    docs.mkdir()
    _write_runtime_config(tmp_path, docs)
    config_path = tmp_path / "config" / "builder_config.jsonc"
    config = load_config(tmp_path, override=str(config_path))
    roots_a = [normalize_fs_path(path) for path in config.knowledge_base.input_roots]
    monkeypatch.chdir(tmp_path.parent)
    config_b = load_config(Path(__file__).resolve().parents[1], override=str(config_path))
    roots_b = [normalize_fs_path(path) for path in config_b.knowledge_base.input_roots]
    assert roots_a == roots_b


def test_end_to_end_sync_smoke(tmp_path: Path, monkeypatch: pytest.MonkeyPatch) -> None:
    docs = tmp_path / "docs"
    docs.mkdir()
    (docs / "a.md").write_text("# A\nMarkdown source", encoding="utf-8")
    (docs / "b.txt").write_text("Text source", encoding="utf-8")
    (docs / "b.txt.meta.json").write_text(json.dumps({"labels": ["migration_note"]}), encoding="utf-8")
    _config, db, _stats = _run(tmp_path, docs, monkeypatch)
    (docs / "a.md").write_text("# A\nMarkdown source updated", encoding="utf-8")
    (docs / "b.txt").unlink()
    (docs / "b.txt.meta.json").unlink()
    (docs / "c.md").write_text("# C\nNew source", encoding="utf-8")
    _config, db, stats = _run(tmp_path, docs, monkeypatch, db=db)
    active_refs = {item["source_ref"] for item in db.knowledge_items if item["status"] == "active"}
    assert (docs / "a.md").resolve().as_posix() in active_refs
    assert (docs / "c.md").resolve().as_posix() in active_refs
    removed_artifact = db.artifacts[(docs / "b.txt").resolve().as_posix()]
    assert removed_artifact["status"] == "removed"
    assert stats.reconciled_removed_files == 1
