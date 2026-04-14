from __future__ import annotations

import json
import os
from pathlib import Path

import pytest

from app.category_ontology import load_category_ontology
from app.category_inference import infer_categories
from app.category_rules import infer_labels_from_rules
from app.config_loader import load_config
from app.file_discovery import discover_source_files
from app.markdown_parser import parse_markdown
from app.metadata_loader import load_sidecar_metadata, merge_metadata
from app.models import CandidateSegment, KnowledgeBaseConfig, SourceMetadata
from app.text_parser import parse_text


def test_file_discovery_finds_md_and_txt(tmp_path: Path) -> None:
    docs = tmp_path / "docs"
    docs.mkdir()
    (docs / "a.md").write_text("# Title\n\nBody", encoding="utf-8")
    (docs / "b.txt").write_text("Para 1\n\nPara 2", encoding="utf-8")
    config = KnowledgeBaseConfig(input_roots=[docs.as_posix()], include_extensions=[".md", ".txt"], exclude_dirs=[], exclude_globs=[], follow_symlinks=False)
    found = discover_source_files(tmp_path, config, max_file_size_bytes=1024 * 1024)
    assert [item.path.name for item in found] == ["a.md", "b.txt"]


def test_file_discovery_recurses_and_skips_excluded_dirs(tmp_path: Path) -> None:
    docs = tmp_path / "docs"
    deep = docs / "level1" / "level2" / "level3"
    deep.mkdir(parents=True)
    excluded = docs / "node_modules" / "inner"
    excluded.mkdir(parents=True)
    (docs / "root.md").write_text("root", encoding="utf-8")
    (deep / "nested.txt").write_text("nested", encoding="utf-8")
    (excluded / "ignored.md").write_text("ignored", encoding="utf-8")
    config = KnowledgeBaseConfig(
        input_roots=[docs.as_posix()],
        include_extensions=[".md", ".txt"],
        exclude_dirs=["node_modules"],
        exclude_globs=[],
        follow_symlinks=False,
    )
    found = discover_source_files(tmp_path, config, max_file_size_bytes=1024 * 1024)
    assert [item.path.relative_to(tmp_path).as_posix() for item in found] == [
        "docs/level1/level2/level3/nested.txt",
        "docs/root.md",
    ]


def test_file_discovery_respects_symlink_option(tmp_path: Path) -> None:
    docs = tmp_path / "docs"
    target = tmp_path / "target"
    docs.mkdir()
    target.mkdir()
    (target / "inside.md").write_text("hello", encoding="utf-8")
    file_link = docs / "linked-file.md"
    dir_link = docs / "linked-dir"
    try:
        os.symlink(target / "inside.md", file_link)
        os.symlink(target, dir_link, target_is_directory=True)
    except (OSError, NotImplementedError):
        pytest.skip("Symlink creation is unavailable on this platform")
    config_off = KnowledgeBaseConfig(
        input_roots=[docs.as_posix()],
        include_extensions=[".md"],
        exclude_dirs=[],
        exclude_globs=[],
        follow_symlinks=False,
    )
    config_on = KnowledgeBaseConfig(
        input_roots=[docs.as_posix()],
        include_extensions=[".md"],
        exclude_dirs=[],
        exclude_globs=[],
        follow_symlinks=True,
    )
    found_off = discover_source_files(tmp_path, config_off, max_file_size_bytes=1024 * 1024)
    found_on = discover_source_files(tmp_path, config_on, max_file_size_bytes=1024 * 1024)
    assert found_off == []
    assert sorted(item.path.relative_to(tmp_path).as_posix() for item in found_on) == [
        "docs/linked-dir/inside.md",
        "docs/linked-file.md",
    ]


def test_file_discovery_consumes_canonical_input_roots_recursively(tmp_path: Path) -> None:
    outer = tmp_path
    nested_cfg_dir = outer / "nested_cfg"
    nested_cfg_dir.mkdir()

    docs_root = nested_cfg_dir / "docs"
    docs_root.mkdir(parents=True)
    (docs_root / "a.md").write_text("A", encoding="utf-8")
    (docs_root / "sub").mkdir()
    (docs_root / "sub" / "b.txt").write_text("B", encoding="utf-8")
    (docs_root / "sub" / "deeper").mkdir()
    (docs_root / "sub" / "deeper" / "c.md").write_text("C", encoding="utf-8")

    # Write operator-required config schema/defaults for load_config.
    for name in ["builder_defaults.json", "builder_config.schema.json"]:
        (nested_cfg_dir / "config" / name).parent.mkdir(exist_ok=True)
        (nested_cfg_dir / "config" / name).write_text((Path(__file__).resolve().parents[1] / "config" / name).read_text(encoding="utf-8"), encoding="utf-8")

    cfg_text = (
        "{"
        f'"database":{{"driver":"postgresql","host":"h","port":1,"database":"d","username":"u","password":"p","schema":"public","ssl_mode":"prefer"}},'
        f'"knowledge_base":{{"input_roots":["./docs"],"include_extensions":[".md",".txt"],"exclude_dirs":[],"exclude_globs":[],"follow_symlinks":false}},'
        f'"llm":{{"provider":"llama_cpp","local_model_path":"./models/m.gguf","download_if_missing":false,"download_source":{{"type":"huggingface","repo_id":"r","filename":"f","local_dir":"./models"}}}}'
        "}"
    )
    (nested_cfg_dir / "models").mkdir()
    (nested_cfg_dir / "models" / "m.gguf").write_text("x", encoding="utf-8")
    nested_cfg_path = nested_cfg_dir / "builder_config.jsonc"
    nested_cfg_path.write_text(cfg_text, encoding="utf-8")

    # `load_config()` needs `root/config/*` defaults. Since we are overriding the config file
    # itself under `nested_cfg_dir`, pass `root=nested_cfg_dir` so defaults resolve correctly.
    cfg = load_config(nested_cfg_dir, override=str(nested_cfg_path))
    found = discover_source_files(Path("."), cfg.knowledge_base, max_file_size_bytes=1024 * 1024)
    paths = sorted(item.path.relative_to(docs_root).as_posix() for item in found)
    assert paths == ["a.md", "sub/b.txt", "sub/deeper/c.md"]


def test_markdown_heading_hierarchy_parsing(tmp_path: Path) -> None:
    path = tmp_path / "sample.md"
    path.write_text("---\ntitle: Sample\ndomain: alpha\nlabels:\n  - anti-pattern\n---\n# Parent\nIntro\n\n## Child\nMore text", encoding="utf-8")
    doc = parse_markdown(path)
    assert doc.sections[0].heading_path == ["Sample", "Parent"]
    assert doc.sections[1].heading_path == ["Sample", "Parent", "Child"]
    assert doc.metadata["domain"] == "alpha"


def test_txt_paragraph_grouping(tmp_path: Path) -> None:
    path = tmp_path / "notes.txt"
    path.write_text("Intro\n\nSetup:\n\nThis is a long paragraph block that should belong to Setup.\n\nOther", encoding="utf-8")
    doc = parse_text(path)
    assert "Setup" in [section.heading for section in doc.sections]


def test_category_rule_inference() -> None:
    root = Path(__file__).resolve().parents[1]
    ontology = load_category_ontology(root / "config" / "category_ontology.json", root / "config" / "category_ontology.schema.json")
    candidate = CandidateSegment(
        source_path=Path("x.md"),
        source_type="markdown",
        span_level="section",
        text="Do not infer logic in the UI. This is a common mistake.",
        heading_path=["Doc", "Anti-Patterns"],
        start_line=1,
        end_line=2,
    )
    labels = infer_labels_from_rules(candidate, ontology)
    assert {label.label for label in labels} >= {"anti_pattern"}


def test_front_matter_and_sidecar_handling(tmp_path: Path) -> None:
    md = tmp_path / "x.md"
    md.write_text("---\ndomain: dm\nmodule: mm\nfeature: ff\nlabels:\n - anti-pattern\ntags:\n - t1\nauthority_label: approved\nstatus: active\n---\n# Anti-Patterns\nAvoid this", encoding="utf-8")
    doc = parse_markdown(md)
    merged = merge_metadata(SourceMetadata.model_validate(doc.metadata), load_sidecar_metadata(md))
    assert merged.domain == "dm"
    assert merged.labels == ["anti-pattern"]

    txt = tmp_path / "y.txt"
    txt.write_text("plain text", encoding="utf-8")
    (tmp_path / "y.txt.meta.json").write_text(json.dumps({"domain": "d2", "labels": ["best-practice"], "tags": ["txt"]}), encoding="utf-8")
    sidecar = load_sidecar_metadata(txt)
    assert sidecar.domain == "d2"
    assert sidecar.labels == ["best-practice"]


def test_classification_fallback_and_primary_threshold() -> None:
    root = Path(__file__).resolve().parents[1]
    ontology = load_category_ontology(root / "config" / "category_ontology.json", root / "config" / "category_ontology.schema.json")
    candidate = CandidateSegment(Path("a.txt"), "text", "file", "neutral descriptive note", ["Doc"], 1, 1, metadata=SourceMetadata())
    labels, _, _ = infer_categories(candidate, load_config(root).classification, None, ontology, False, 0.55, allow_llm=False)
    assert labels[0].label == "reference"
    assert labels[0].label_role == "primary"
