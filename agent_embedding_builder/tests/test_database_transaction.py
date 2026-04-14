from __future__ import annotations

import json
from pathlib import Path

import pytest

from app.config_loader import load_config
from app.database import Database
from app.models import LabelEvidence, SelectedSegment, CandidateSegment, SourceMetadata
class FakeCursor:
    def __init__(self, fail: bool = False):
        self.fail = fail
        self.commands = []
        self.rowcount = 1

    def __enter__(self):
        return self

    def __exit__(self, exc_type, exc, tb):
        return False

    def execute(self, sql, params=None):
        self.commands.append((sql, params))
        if self.fail:
            raise RuntimeError("boom")

    def fetchone(self):
        return ["12345678-1234-5678-1234-567812345678"]

    def fetchall(self):
        return []


class FakeConnection:
    def __init__(self, fail: bool = False):
        self.fail = fail
        self.committed = False
        self.rolled_back = False
        self.closed = False
        self.cursor_obj = FakeCursor(fail=fail)

    def cursor(self):
        return self.cursor_obj

    def commit(self):
        self.committed = True

    def rollback(self):
        self.rolled_back = True

    def close(self):
        self.closed = True


def test_sql_write_path_transactional_rollback_on_failure(tmp_path: Path) -> None:
    (tmp_path / "config").mkdir()
    root = Path(__file__).resolve().parents[1]
    for name in ["builder_defaults.json", "builder_config.schema.json", "builder_config.jsonc"]:
        (tmp_path / "config" / name).write_text((root / "config" / name).read_text(encoding="utf-8"), encoding="utf-8")
    conn = FakeConnection(fail=True)
    db = Database(load_config(tmp_path), connection_factory=lambda: conn)
    with pytest.raises(RuntimeError):
        db.create_ingestion_run("model", None)
    assert conn.rolled_back


def test_database_run_and_failure_metadata(tmp_path: Path) -> None:
    root = Path(__file__).resolve().parents[1]
    config = load_config(root)
    setattr(config, "_ontology_version", "1.0.0")
    conn = FakeConnection()
    db = Database(config, connection_factory=lambda: conn)
    run = db.create_ingestion_run("model", "v1")
    executed = " ".join(command for command, _ in conn.cursor_obj.commands)
    assert "ontology_version" in executed
    db.record_artifact_failure("x.md", "hash", RuntimeError("bad"), run)
    executed = " ".join(command for command, _ in conn.cursor_obj.commands)
    assert "build_status" in executed
    assert "last_reconciled_at" in executed


def test_metadata_first_values_used_in_write(monkeypatch: pytest.MonkeyPatch) -> None:
    root = Path(__file__).resolve().parents[1]
    config = load_config(root)
    setattr(config, "_ontology_version", "1.0.0")
    conn = FakeConnection()
    db = Database(config, connection_factory=lambda: conn)
    run = db.create_ingestion_run("model", "v1")
    from datetime import datetime
    source_file = type("SF", (), {"extension": ".md", "path": root / "docs" / "x.md", "content_hash": "h", "modified_at": datetime.now()})()
    item = SelectedSegment(
        candidate=CandidateSegment(source_file.path, "markdown", "file", "raw", ["Heading"], 1, 1, metadata=SourceMetadata(title="Meta Title", domain="meta-domain", module="meta-module", feature="meta-feature", authority_label="approved", status="active", source_type="markdown", tags=["t"])),
        labels=[LabelEvidence(label="reference", label_role="primary", confidence=0.8, source_method="fallback")],
        retrieval_class="reference",
        normalized_retrieval_text="norm",
        summary="sum",
        scopes=[],
        tags=["t"],
        retrieval_profiles=[("default", "norm")],
        confidence=0.8,
        explanation_payload={},
    )
    db.replace_artifact_knowledge(run, source_file, [item], {"normalized_retrieval_text": [[0.1]], "profile_rows": []})
    params = [params for _, params in conn.cursor_obj.commands if params]
    flat = " ".join(str(p) for group in params for p in (group if isinstance(group, tuple) else [group]))
    assert "Meta Title" in flat
    assert "meta-domain" in flat
    assert "approved" in flat


def test_archive_removed_artifact_updates_lifecycle_fields() -> None:
    root = Path(__file__).resolve().parents[1]
    config = load_config(root)
    setattr(config, "_ontology_version", "1.0.0")
    conn = FakeConnection()
    db = Database(config, connection_factory=lambda: conn)
    run = db.create_ingestion_run("model", "v1")
    db.archive_removed_artifact("12345678-1234-5678-1234-567812345678", run)
    executed = " ".join(command for command, _ in conn.cursor_obj.commands)
    assert "status = 'removed'" in executed
    assert "status = 'archived'" in executed
    assert "build_status" in executed
