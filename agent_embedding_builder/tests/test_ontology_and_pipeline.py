from __future__ import annotations

import json
import sys
import types
from pathlib import Path

import pytest

from app.category_ontology import load_category_ontology
from app.config_loader import load_config
from app.pipeline import run_pipeline


def _base_ontology() -> dict:
    root = Path(__file__).resolve().parents[1]
    return json.loads((root / "config" / "category_ontology.json").read_text(encoding="utf-8"))


def test_ontology_loading_and_deprecated_rewrite(tmp_path: Path) -> None:
    root = Path(__file__).resolve().parents[1]
    ontology_path = tmp_path / "ontology.json"
    schema_path = root / "config" / "category_ontology.schema.json"
    ontology = _base_ontology()
    ontology["labels"].append(
        {
            "name": "old_label",
            "description": "old",
            "may_be_primary": True,
            "default_retrieval_class": "reference",
            "aliases": [],
            "parents": [],
            "children": [],
            "confidence_floor": 0.55,
            "scope_hints": [],
            "positive_lexical_hints": [],
            "negative_lexical_hints": [],
            "llm_guidance": "old",
            "deprecated": True,
            "replacement": "reference"
        }
    )
    ontology_path.write_text(json.dumps(ontology), encoding="utf-8")
    loaded = load_category_ontology(ontology_path, schema_path)
    assert loaded.normalize_label("best-practice", False)[0] == "best_practice"
    assert loaded.normalize_label("old_label", False)[0] == "reference"
    bad = tmp_path / "bad.json"
    bad.write_text(json.dumps({"version": "1"}), encoding="utf-8")
    with pytest.raises(Exception):
        load_category_ontology(bad, schema_path)


@pytest.mark.parametrize(
    ("mutator", "expected"),
    [
        (lambda payload: payload.__setitem__("labels", {}), "labels"),
        (lambda payload: payload["labels"][0].__setitem__("aliases", "oops"), "aliases"),
        (lambda payload: payload["labels"][0].pop("llm_guidance"), "llm_guidance"),
        (lambda payload: payload["retrieval_class_resolution"].__setitem__("special_rules", {}), "special_rules"),
    ],
)
def test_ontology_json_schema_failure_cases(tmp_path: Path, mutator, expected: str) -> None:
    root = Path(__file__).resolve().parents[1]
    schema_path = root / "config" / "category_ontology.schema.json"
    ontology = _base_ontology()
    mutator(ontology)
    ontology_path = tmp_path / "ontology.json"
    ontology_path.write_text(json.dumps(ontology), encoding="utf-8")
    with pytest.raises(ValueError, match=expected):
        load_category_ontology(ontology_path, schema_path)


def test_ontology_rejects_alias_collisions(tmp_path: Path) -> None:
    root = Path(__file__).resolve().parents[1]
    schema_path = root / "config" / "category_ontology.schema.json"
    ontology = _base_ontology()
    ontology["labels"][1]["aliases"].append("best-practice")
    path = tmp_path / "ontology.json"
    path.write_text(json.dumps(ontology), encoding="utf-8")
    with pytest.raises(ValueError, match="alias collision"):
        load_category_ontology(path, schema_path)


def test_ontology_rejects_invalid_parent_reference(tmp_path: Path) -> None:
    root = Path(__file__).resolve().parents[1]
    schema_path = root / "config" / "category_ontology.schema.json"
    ontology = _base_ontology()
    ontology["labels"][0]["parents"] = ["missing_label"]
    path = tmp_path / "ontology.json"
    path.write_text(json.dumps(ontology), encoding="utf-8")
    with pytest.raises(ValueError, match="unknown parent"):
        load_category_ontology(path, schema_path)


def test_ontology_rejects_invalid_replacement_target(tmp_path: Path) -> None:
    root = Path(__file__).resolve().parents[1]
    schema_path = root / "config" / "category_ontology.schema.json"
    ontology = _base_ontology()
    ontology["labels"][0]["deprecated"] = True
    ontology["labels"][0]["replacement"] = "missing_label"
    path = tmp_path / "ontology.json"
    path.write_text(json.dumps(ontology), encoding="utf-8")
    with pytest.raises(ValueError, match="replacement target"):
        load_category_ontology(path, schema_path)


def test_ontology_rejects_invalid_label_mapping(tmp_path: Path) -> None:
    root = Path(__file__).resolve().parents[1]
    schema_path = root / "config" / "category_ontology.schema.json"
    ontology = _base_ontology()
    ontology["retrieval_class_resolution"]["label_to_retrieval_class"]["missing_label"] = "reference"
    path = tmp_path / "ontology.json"
    path.write_text(json.dumps(ontology), encoding="utf-8")
    with pytest.raises(ValueError, match="unknown label"):
        load_category_ontology(path, schema_path)


def test_ontology_rejects_invalid_special_rule_reference(tmp_path: Path) -> None:
    root = Path(__file__).resolve().parents[1]
    schema_path = root / "config" / "category_ontology.schema.json"
    ontology = _base_ontology()
    ontology["retrieval_class_resolution"]["special_rules"][0] = {
        "if_labels": ["missing_label"],
        "then_retrieval_class": "not_a_class",
    }
    path = tmp_path / "ontology.json"
    path.write_text(json.dumps(ontology), encoding="utf-8")
    with pytest.raises(ValueError, match="unknown label|invalid retrieval class"):
        load_category_ontology(path, schema_path)


def test_end_to_end_smoke(tmp_path: Path, monkeypatch: pytest.MonkeyPatch) -> None:
    root = Path(__file__).resolve().parents[1]
    docs = tmp_path / "docs"
    notes = tmp_path / "notes"
    docs.mkdir()
    notes.mkdir()
    (docs / "a.md").write_text(
        "---\ntitle: Preferred Title\ndomain: dm\nmodule: mm\nfeature: ff\nlabels:\n  - anti-pattern\ntags:\n  - mdtag\nauthority_label: approved\nstatus: active\n---\n# Anti-Patterns\nDo not infer UI state.\n",
        encoding="utf-8",
    )
    (notes / "b.txt").write_text("Migration:\n\nUpgrade without changing engine.", encoding="utf-8")
    (notes / "b.txt.meta.json").write_text(json.dumps({"labels": ["migration_note"], "tags": ["txttag"], "source_type": "text"}), encoding="utf-8")
    (tmp_path / "config").mkdir()
    for name in ["builder_defaults.json", "builder_config.schema.json"]:
        (tmp_path / "config" / name).write_text((root / "config" / name).read_text(encoding="utf-8"), encoding="utf-8")
    (tmp_path / "config" / "category_ontology.json").write_text((root / "config" / "category_ontology.json").read_text(encoding="utf-8"), encoding="utf-8")
    (tmp_path / "config" / "category_ontology.schema.json").write_text((root / "config" / "category_ontology.schema.json").read_text(encoding="utf-8"), encoding="utf-8")
    (tmp_path / "m.gguf").write_text("x", encoding="utf-8")
    (tmp_path / "config" / "builder_config.jsonc").write_text(
        "{"
        '"database":{"driver":"postgresql","host":"127.0.0.1","port":5432,"database":"memory_db","username":"postgres","password":"change_me","schema":"public","ssl_mode":"prefer"},'
        f'"knowledge_base":{{"input_roots":["{docs.as_posix()}","{notes.as_posix()}"],"include_extensions":[".md",".txt"],"exclude_dirs":[".git"],"exclude_globs":[],"follow_symlinks":false}},'
        f'"llm":{{"provider":"llama_cpp","local_model_path":"{(tmp_path / "m.gguf").as_posix()}","download_if_missing":false,"download_source":{{"type":"huggingface","repo_id":"r","filename":"f","local_dir":"l"}}}}'
        "}",
        encoding="utf-8",
    )

    class FakeEmbeddings:
        def __init__(self, config):
            pass
        def model_metadata(self):
            return ("fake-emb", "1")
        def embed(self, texts):
            return [[0.1, 0.2] for _ in texts]

    class FakeLLM:
        def __init__(self, config):
            pass
        def infer(self, prompt):
            from app.models import LLMInferenceResult, LabelEvidence
            return LLMInferenceResult(labels=[LabelEvidence(label="best_practice", confidence=0.8, source_method="llm_inference")], normalized_retrieval_text="normalized", summary="summary", tags=["llm-tag"])

    class FakeDB:
        def __init__(self, config):
            self.saved = []
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
        def create_ingestion_run(self, embedding_model_name, embedding_model_version):
            from app.models import IngestionRunContext, new_uuid
            from datetime import datetime
            return IngestionRunContext(new_uuid(), datetime.now(), embedding_model_name, embedding_model_version, "1.0.0")
        def get_last_build_state(self, source_ref):
            return None
        def replace_artifact_knowledge(self, run_ctx, source_file, selected, embeddings):
            import uuid
            self.saved.append((source_file, selected, embeddings))
            created_count = len(selected)
            source_artifact_id = str(uuid.uuid4())
            self._visibility_by_artifact[source_artifact_id] = {
                "active_items": created_count,
                "labels": created_count,
                "scopes": 0,
                "tags": 0,
                "profiles": len(embeddings.get("profile_rows", [])) if embeddings else 0,
                "embeddings": len(embeddings.get("profile_rows", [])) + created_count,
                "source_segments": created_count,
                "visible_items": created_count,
                "visible_embeddings": len(embeddings.get("profile_rows", [])) + created_count,
            }
            return {
                "source_artifact_id": source_artifact_id,
                "superseded_count": 0,
                "created_count": created_count,
            }
        def record_artifact_failure(self, source_ref, artifact_hash, error, run_ctx):
            raise AssertionError("no failure expected")
        def finalize_ingestion_run(self, run_id, status, notes):
            self.status = status
            self.notes = notes
        def verify_required_schema(self):
            return self._schema_info
        def verify_artifact_visibility(self, source_artifact_id):
            return self._visibility_by_artifact.get(str(source_artifact_id), {"visible_items": 1, "visible_embeddings": 1, "labels": 1, "profiles": 1, "embeddings": 1, "active_items": 1})
        def get_database_totals(self):
            return {"current_database": self._schema_info["database_name"], "source_artifacts": 1, "active_items": 1, "embeddings": 1, "ingestion_runs": 1}

    import app.pipeline as pipeline_mod
    monkeypatch.setattr(pipeline_mod, "EmbeddingProvider", FakeEmbeddings)
    monkeypatch.setattr(pipeline_mod, "LocalLLM", FakeLLM)
    fake_db = FakeDB(load_config(tmp_path))
    stats = run_pipeline(tmp_path, load_config(tmp_path), database=fake_db)
    assert stats.created_items > 0
    selected = fake_db.saved[0][1][0]
    assert selected.retrieval_profiles
    assert selected.retrieval_class in {"antipattern", "historical_note", "best_practice"}
    assert selected.candidate.metadata.title == "Preferred Title"
    assert selected.candidate.metadata.domain == "dm"
    profile_rows = fake_db.saved[0][2]["profile_rows"]
    assert any(row["profile_type"] == "risk" for row in profile_rows)


def test_embedding_provider_prefers_gpu_when_torch_cuda_is_available(tmp_path: Path, monkeypatch: pytest.MonkeyPatch) -> None:
    fake_torch = types.SimpleNamespace(
        cuda=types.SimpleNamespace(is_available=lambda: True),
        backends=types.SimpleNamespace(mps=types.SimpleNamespace(is_available=lambda: False)),
    )
    monkeypatch.setitem(sys.modules, "torch", fake_torch)

    calls: list[str] = []

    class FakeSentenceTransformer:
        def __init__(self, model_name_or_path, device=None):
            calls.append(device)

        def save(self, path):
            Path(path).mkdir(parents=True, exist_ok=True)

    monkeypatch.setitem(sys.modules, "sentence_transformers", types.SimpleNamespace(SentenceTransformer=FakeSentenceTransformer))

    from app.embedding_provider import EmbeddingProvider
    from app.models import EmbeddingConfig

    provider = EmbeddingProvider(
        EmbeddingConfig(
            provider="sentence_transformers",
            model_name="emb",
            local_model_path=str(tmp_path / "emb"),
            download_if_missing=True,
            batch_size=8,
            normalize_embeddings=True,
        )
    )
    provider._load()
    assert calls == ["cuda", "cuda"]


def test_mixed_profile_embeddings_generated_per_item(tmp_path: Path, monkeypatch: pytest.MonkeyPatch) -> None:
    root = Path(__file__).resolve().parents[1]
    docs = tmp_path / "docs"
    docs.mkdir()
    (docs / "a.md").write_text("---\nlabels:\n  - anti-pattern\n---\n# Risks\nDo not do this.", encoding="utf-8")
    (docs / "b.md").write_text("---\nlabels:\n  - feature_description\n---\n# Overview\nThis feature exists.", encoding="utf-8")
    (tmp_path / "config").mkdir()
    for name in ["builder_defaults.json", "builder_config.schema.json", "category_ontology.json", "category_ontology.schema.json"]:
        (tmp_path / "config" / name).write_text((root / "config" / name).read_text(encoding="utf-8"), encoding="utf-8")
    (tmp_path / "m.gguf").write_text("x", encoding="utf-8")
    (tmp_path / "config" / "builder_config.jsonc").write_text(
        "{"
        f'"database":{{"driver":"postgresql","host":"127.0.0.1","port":5432,"database":"memory_db","username":"postgres","password":"change_me","schema":"public","ssl_mode":"prefer"}},'
        f'"knowledge_base":{{"input_roots":["{docs.as_posix()}"],"include_extensions":[".md"],"exclude_dirs":[],"exclude_globs":[],"follow_symlinks":false}},'
        f'"llm":{{"provider":"llama_cpp","local_model_path":"{(tmp_path / "m.gguf").as_posix()}","download_if_missing":false,"download_source":{{"type":"huggingface","repo_id":"r","filename":"f","local_dir":"l"}}}}'
        "}",
        encoding="utf-8",
    )

    class FakeEmbeddings:
        def __init__(self, config): pass
        def model_metadata(self): return ("fake-emb", "1")
        def embed(self, texts): return [[float(len(text)), 0.0] for text in texts]

    class FakeLLM:
        def __init__(self, config): pass
        def infer(self, prompt):
            from app.models import LLMInferenceResult
            return LLMInferenceResult(normalized_retrieval_text="normalized", summary="summary")

    class FakeDB:
        def __init__(self, config):
            self.saved = []
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
        def create_ingestion_run(self, embedding_model_name, embedding_model_version):
            from app.models import IngestionRunContext, new_uuid
            from datetime import datetime
            return IngestionRunContext(new_uuid(), datetime.now(), embedding_model_name, embedding_model_version, "1.0.0")
        def get_last_build_state(self, source_ref): return None
        def replace_artifact_knowledge(self, run_ctx, source_file, selected, embeddings):
            import uuid
            self.saved.append((selected, embeddings))
            created_count = len(selected)
            source_artifact_id = str(uuid.uuid4())
            self._visibility_by_artifact[source_artifact_id] = {
                "active_items": created_count,
                "labels": created_count,
                "scopes": 0,
                "tags": 0,
                "profiles": len(embeddings.get("profile_rows", [])) if embeddings else 0,
                "embeddings": len(embeddings.get("profile_rows", [])) + created_count,
                "source_segments": created_count,
                "visible_items": created_count,
                "visible_embeddings": len(embeddings.get("profile_rows", [])) + created_count,
            }
            return {"source_artifact_id": source_artifact_id, "superseded_count": 0, "created_count": created_count}
        def record_artifact_failure(self, source_ref, artifact_hash, error, run_ctx): raise AssertionError("no failure expected")
        def finalize_ingestion_run(self, run_id, status, notes): self.status = status
        def verify_required_schema(self): return self._schema_info
        def verify_artifact_visibility(self, source_artifact_id): return self._visibility_by_artifact.get(str(source_artifact_id), {"visible_items": 1, "visible_embeddings": 1, "labels": 1, "profiles": 1, "embeddings": 1, "active_items": 1})
        def get_database_totals(self): return {"current_database": self._schema_info["database_name"], "source_artifacts": 1, "active_items": 1, "embeddings": 1, "ingestion_runs": 1}

    import app.pipeline as pipeline_mod
    monkeypatch.setattr(pipeline_mod, "EmbeddingProvider", FakeEmbeddings)
    monkeypatch.setattr(pipeline_mod, "LocalLLM", FakeLLM)
    fake_db = FakeDB(load_config(tmp_path))
    run_pipeline(tmp_path, load_config(tmp_path), database=fake_db)
    rows = [row for _, embeddings in fake_db.saved for row in embeddings["profile_rows"]]
    assert any(row["profile_type"] == "risk" for row in rows)
    assert not any(row["profile_type"] == "default" for row in rows)
