from __future__ import annotations

import json
from pathlib import Path

import pytest

from agent_embedding_builder import main
from app.config_loader import _strip_jsonc, load_config, resolve_config_path
from app.path_utils import (
    is_config_absolute_path,
    is_unc_absolute,
    is_windows_drive_absolute,
    normalize_fs_path,
    normalize_path_string,
)

EXPECTED_INTERNAL_DEFAULT_SECTIONS = {
    "embedding",
    "category_governance",
    "runtime",
    "metadata_internal",
    "builder_internal",
    "llm_internal",
    "segmentation_internal",
    "classification_internal",
    "retrieval_profiles_internal",
    "rebuild_internal",
    "category_internal",
    "embedding_internal",
}


def _write_operator_fixture(tmp_path: Path, config_text: str) -> None:
    (tmp_path / "config").mkdir(exist_ok=True)
    root = Path(__file__).resolve().parents[1]
    for name in ["builder_defaults.json", "builder_config.schema.json"]:
        (tmp_path / "config" / name).write_text((root / "config" / name).read_text(encoding="utf-8"), encoding="utf-8")
    (tmp_path / "config" / "builder_config.jsonc").write_text(config_text, encoding="utf-8")


def test_canonical_config_load() -> None:
    root = Path(__file__).resolve().parents[1]
    config = load_config(root)
    assert config.knowledge_base.input_roots
    assert Path(config.category_governance.ontology_path).is_absolute()
    assert config.llm.context_length == 8192
    assert config.database.host


def test_builder_defaults_contains_only_internal_sections() -> None:
    root = Path(__file__).resolve().parents[1]
    payload = json.loads(_strip_jsonc((root / "config" / "builder_defaults.json").read_text(encoding="utf-8")))
    assert set(payload) == EXPECTED_INTERNAL_DEFAULT_SECTIONS


def test_operator_jsonc_loads_with_comments(tmp_path: Path) -> None:
    _write_operator_fixture(
        tmp_path,
        '{\n// operator config\n"database":{"driver":"postgresql","host":"h","port":5432,"database":"d","username":"u","password":"p","schema":"public","ssl_mode":"prefer"},'
        '"knowledge_base":{"input_roots":["docs"],"include_extensions":[".md"],"exclude_dirs":[],"exclude_globs":[],"follow_symlinks":false},'
        '"llm":{"provider":"llama_cpp","local_model_path":"m.gguf","download_if_missing":false,"download_source":{"type":"huggingface","repo_id":"r","filename":"f","local_dir":"l"}}}',
    )
    config = load_config(tmp_path)
    assert config.database.host == "h"
    assert config.embedding.model_name == "BAAI/bge-small-en-v1.5"
    assert config.metadata_internal["sidecar_suffix"] == ".meta.json"


def test_legacy_config_normalization(tmp_path: Path, capsys: pytest.CaptureFixture[str]) -> None:
    legacy = {
        "database": {"host": "h", "port": 1, "database": "d", "username": "u", "password": "p"},
        "source_inputs": {"directories": ["docs"], "include_extensions": [".md"], "exclude_globs": []},
        "llm": {"provider": "llama_cpp", "model_path": "m.gguf", "download_if_missing": False, "download": {"repo_id": "r", "filename": "f", "local_dir": "l"}},
        "embeddings": {"model_name_or_path": "emb", "local_model_dir": "emb", "download_if_missing": False},
        "builder": {"builder_version": "1", "classifier_version": "1", "normalization_version": "1"},
        "segmentation": {},
        "classification": {"enable_llm": False, "enable_rule_based": True, "enable_heading_rules": True, "labels": ["x"]},
        "retrieval_profiles": {},
        "incremental": {}
    }
    (tmp_path / "legacy.json").write_text(json.dumps(legacy), encoding="utf-8")
    config = load_config(tmp_path, override=str(tmp_path / "legacy.json"))
    assert config.knowledge_base.input_roots == [str((tmp_path / "docs").resolve()).replace("\\", "/")]
    assert config.embedding.model_name == "emb"
    assert config.llm.local_model_path == str((tmp_path / "m.gguf").resolve()).replace("\\", "/")
    assert "deprecated legacy config format" in capsys.readouterr().out


def test_default_config_path_behavior(monkeypatch: pytest.MonkeyPatch) -> None:
    root = Path(__file__).resolve().parents[1]
    monkeypatch.delenv("EMBEDDING_BUILDER_CONFIG", raising=False)
    assert resolve_config_path(root) == root / "config" / "builder_config.jsonc"


def test_config_folder_artifact_existence() -> None:
    root = Path(__file__).resolve().parents[1]
    assert (root / "config" / "builder_config.jsonc").exists()
    assert (root / "config" / "builder_config.example.jsonc").exists()
    assert (root / "config" / "builder_defaults.json").exists()
    assert (root / "config" / "builder_config.schema.json").exists()
    assert (root / "config" / "category_ontology.json").exists()
    assert (root / "config" / "category_ontology.schema.json").exists()
    assert not (root / "builder_config.json.example").exists()


def test_documentation_references_new_config_paths() -> None:
    root = Path(__file__).resolve().parents[1]
    text = (root / "docs" / "configuration_guide.md").read_text(encoding="utf-8")
    assert "config/builder_config.jsonc" in text
    assert "config/builder_defaults.json" in text
    assert "config file's directory" in text
    assert "forward slashes" in text
    assert "recursive" in text
    assert "not intended for normal users" in text


def test_invalid_operator_config_fails_validation(tmp_path: Path) -> None:
    _write_operator_fixture(tmp_path, '{"database":{"driver":"sqlite"}}')
    with pytest.raises(ValueError, match="schema validation failed"):
        load_config(tmp_path)


def test_config_paths_normalize_relative_and_absolute(monkeypatch: pytest.MonkeyPatch, tmp_path: Path) -> None:
    root = Path(__file__).resolve().parents[1]
    relative_cfg = load_config(root)
    assert Path(relative_cfg.llm.local_model_path).is_absolute()
    assert Path(relative_cfg.embedding.local_model_path).is_absolute()
    assert all(Path(path).is_absolute() for path in relative_cfg.knowledge_base.input_roots)
    assert Path(relative_cfg.category_governance.ontology_path).is_absolute()
    assert Path(relative_cfg.category_governance.schema_path).is_absolute()
    # Canonical string format: slash-normalized and never backslash-containing.
    assert "\\" not in relative_cfg.llm.local_model_path
    assert "\\" not in relative_cfg.embedding.local_model_path
    assert all("\\" not in p for p in relative_cfg.knowledge_base.input_roots)
    assert "\\" not in relative_cfg.category_governance.ontology_path
    assert "\\" not in relative_cfg.category_governance.schema_path
    abs_dir = tmp_path / "absdocs"
    abs_dir.mkdir()
    (tmp_path / "config").mkdir()
    for name in ["builder_defaults.json", "builder_config.schema.json"]:
        (tmp_path / "config" / name).write_text((root / "config" / name).read_text(encoding="utf-8"), encoding="utf-8")
    cfg = (
        "{"
        f'"database":{{"driver":"postgresql","host":"h","port":1,"database":"d","username":"u","password":"p","schema":"public","ssl_mode":"prefer"}},'
        f'"knowledge_base":{{"input_roots":["./docs","{abs_dir.as_posix()}"],"include_extensions":[".md"],"exclude_dirs":[],"exclude_globs":[],"follow_symlinks":false}},'
        f'"llm":{{"provider":"llama_cpp","local_model_path":"./models/m.gguf","download_if_missing":false,"download_source":{{"type":"huggingface","repo_id":"r","filename":"f","local_dir":"./models"}}}}'
        "}"
    )
    (tmp_path / "config" / "builder_config.jsonc").write_text(cfg, encoding="utf-8")
    config = load_config(tmp_path)
    assert Path(config.knowledge_base.input_roots[0]).is_absolute()
    assert Path(config.knowledge_base.input_roots[1]) == abs_dir.resolve()
    assert Path(config.llm.local_model_path) == (tmp_path / "config" / "models" / "m.gguf").resolve()
    assert Path(config.embedding.local_model_path) == (tmp_path / "config" / "models" / "embeddings" / "bge-small-en-v1.5").resolve()
    assert Path(config.category_governance.ontology_path) == (tmp_path / "config" / "category_ontology.json").resolve()
    assert Path(config.category_governance.schema_path) == (tmp_path / "config" / "category_ontology.schema.json").resolve()
    assert "\\" not in config.llm.local_model_path
    assert "\\" not in config.embedding.local_model_path
    assert all("\\" not in p for p in config.knowledge_base.input_roots)
    assert "\\" not in config.category_governance.ontology_path
    assert "\\" not in config.category_governance.schema_path


def test_operator_config_tolerates_windows_backslash_paths_for_supported_fields(tmp_path: Path) -> None:
    _write_operator_fixture(
        tmp_path,
        '{'
        '"database":{"driver":"postgresql","host":"h","port":5432,"database":"d","username":"u","password":"p","schema":"public","ssl_mode":"prefer"},'
        '"knowledge_base":{"input_roots":["C:\\Docs\\Repo\\github\\pipesim\\pip-pipesim\\Stingray\\.ai\\docs\\","..\\notes\\"],"include_extensions":[".md"],"exclude_dirs":[],"exclude_globs":[],"follow_symlinks":false},'
        '"llm":{"provider":"llama_cpp","local_model_path":"C:\\Models\\builder\\model.gguf","download_if_missing":false,"download_source":{"type":"huggingface","repo_id":"r","filename":"f","local_dir":"C:\\Models\\builder\\"}}}',
    )
    config = load_config(tmp_path)
    assert config.knowledge_base.input_roots[0] == "C:/Docs/Repo/github/pipesim/pip-pipesim/Stingray/.ai/docs"
    assert config.knowledge_base.input_roots[1] == (tmp_path / "notes").resolve().as_posix()
    assert config.llm.local_model_path == "C:/Models/builder/model.gguf"
    assert config.llm.download_source.local_dir == "C:/Models/builder"
    assert config.embedding.local_model_path == (tmp_path / "config" / "models" / "embeddings" / "bge-small-en-v1.5").resolve().as_posix()
    assert config.category_governance.ontology_path == (tmp_path / "config" / "category_ontology.json").resolve().as_posix()


def test_windows_absolute_paths_are_not_rebased_under_config_dir(tmp_path: Path) -> None:
    _write_operator_fixture(
        tmp_path,
        '{'
        '"database":{"driver":"postgresql","host":"h","port":5432,"database":"d","username":"u","password":"p","schema":"public","ssl_mode":"prefer"},'
        '"knowledge_base":{"input_roots":["C:/Docs/Repo/github/pipesim/pip-pipesim/Stingray/.ai/docs/","C:\\Docs\\Repo\\github\\pipesim\\pip-pipesim\\Stingray\\.ai\\docs\\"],"include_extensions":[".md"],"exclude_dirs":[],"exclude_globs":[],"follow_symlinks":false},'
        '"llm":{"provider":"llama_cpp","local_model_path":"C:/Models/builder/model.gguf","download_if_missing":false,"download_source":{"type":"huggingface","repo_id":"r","filename":"f","local_dir":"C:/Models/builder/"}}}',
    )
    config = load_config(tmp_path)
    assert config.knowledge_base.input_roots[0] == "C:/Docs/Repo/github/pipesim/pip-pipesim/Stingray/.ai/docs"
    assert config.knowledge_base.input_roots[1] == "C:/Docs/Repo/github/pipesim/pip-pipesim/Stingray/.ai/docs"
    assert config.llm.local_model_path == "C:/Models/builder/model.gguf"
    assert config.llm.download_source.local_dir == "C:/Models/builder"
    assert not config.knowledge_base.input_roots[0].startswith((tmp_path / "config").resolve().as_posix())


def test_config_override_and_env_paths_resolve_from_config_dir(monkeypatch: pytest.MonkeyPatch, tmp_path: Path) -> None:
    root = tmp_path
    (root / "config").mkdir()
    (root / "rel").mkdir()
    for name in ["builder_defaults.json", "builder_config.schema.json"]:
        (root / "config" / name).write_text((Path(__file__).resolve().parents[1] / "config" / name).read_text(encoding="utf-8"), encoding="utf-8")
    (root / "rel" / "custom.jsonc").write_text(
        '{"database":{"driver":"postgresql","host":"h","port":1,"database":"d","username":"u","password":"p","schema":"public","ssl_mode":"prefer"},'
        '"knowledge_base":{"input_roots":["./docs"],"include_extensions":[".md"],"exclude_dirs":[],"exclude_globs":[],"follow_symlinks":false},'
        '"llm":{"provider":"llama_cpp","local_model_path":"./m.gguf","download_if_missing":false,"download_source":{"type":"huggingface","repo_id":"r","filename":"f","local_dir":"./models"}}}',
        encoding="utf-8",
    )
    assert resolve_config_path(root, override="rel/custom.jsonc") == (root / "rel" / "custom.jsonc").resolve()
    monkeypatch.setenv("EMBEDDING_BUILDER_CONFIG", "rel/custom.jsonc")
    assert resolve_config_path(root) == (root / "rel" / "custom.jsonc").resolve()
    monkeypatch.setenv("EMBEDDING_BUILDER_CONFIG", str((root / "rel" / "custom.jsonc").resolve()))
    assert resolve_config_path(root) == (root / "rel" / "custom.jsonc").resolve()
    config = load_config(root, override="rel/custom.jsonc")
    assert Path(config.knowledge_base.input_roots[0]) == (root / "rel" / "docs").resolve()
    assert Path(config.llm.local_model_path) == (root / "rel" / "m.gguf").resolve()


def test_relative_paths_resolve_against_active_config_dir_not_cwd(monkeypatch: pytest.MonkeyPatch, tmp_path: Path) -> None:
    root = tmp_path
    (root / "config").mkdir()
    for name in ["builder_defaults.json", "builder_config.schema.json"]:
        (root / "config" / name).write_text((Path(__file__).resolve().parents[1] / "config" / name).read_text(encoding="utf-8"), encoding="utf-8")

    nested_cfg_dir = tmp_path / "nested_cfg"
    nested_cfg_dir.mkdir()
    (nested_cfg_dir / "docs").mkdir(parents=True)
    (nested_cfg_dir / "models").mkdir(parents=True)
    (nested_cfg_dir / "config").mkdir(parents=True)
    (nested_cfg_dir / "models" / "m.gguf").write_text("x", encoding="utf-8")
    # Defaults use `./category_ontology.json` and `./category_ontology.schema.json`.
    (nested_cfg_dir / "category_ontology.json").write_text("{}", encoding="utf-8")
    (nested_cfg_dir / "category_ontology.schema.json").write_text("{}", encoding="utf-8")

    (tmp_path / "does_not_matter").mkdir()
    monkeypatch.chdir(tmp_path / "does_not_matter")

    cfg_text = (
        "{"
        f'"database":{{"driver":"postgresql","host":"h","port":1,"database":"d","username":"u","password":"p","schema":"public","ssl_mode":"prefer"}},'
        f'"knowledge_base":{{"input_roots":["./docs"],"include_extensions":[".md"],"exclude_dirs":[],"exclude_globs":[],"follow_symlinks":false}},'
        f'"llm":{{"provider":"llama_cpp","local_model_path":"./models/m.gguf","download_if_missing":false,"download_source":{{"type":"huggingface","repo_id":"r","filename":"f","local_dir":"./models"}}}}'
        "}"
    )
    nested_cfg_path = nested_cfg_dir / "builder_config.jsonc"
    nested_cfg_path.write_text(cfg_text, encoding="utf-8")

    cfg = load_config(root, override=str(nested_cfg_path))
    assert Path(cfg.knowledge_base.input_roots[0]) == (nested_cfg_dir / "docs").resolve()
    assert Path(cfg.llm.local_model_path) == (nested_cfg_dir / "models" / "m.gguf").resolve()
    assert Path(cfg.embedding.local_model_path) == (nested_cfg_dir / "models" / "embeddings" / "bge-small-en-v1.5").resolve()
    assert Path(cfg.category_governance.ontology_path) == (nested_cfg_dir / "category_ontology.json").resolve()
    assert Path(cfg.category_governance.schema_path) == (nested_cfg_dir / "category_ontology.schema.json").resolve()

    assert "\\" not in cfg.llm.local_model_path
    assert "\\" not in cfg.embedding.local_model_path
    assert all("\\" not in p for p in cfg.knowledge_base.input_roots)


def test_absolute_windows_paths_are_not_rebased_into_config_dir(monkeypatch: pytest.MonkeyPatch, tmp_path: Path) -> None:
    root = tmp_path
    (root / "config").mkdir()
    for name in ["builder_defaults.json", "builder_config.schema.json"]:
        (root / "config" / name).write_text((Path(__file__).resolve().parents[1] / "config" / name).read_text(encoding="utf-8"), encoding="utf-8")

    nested_cfg_dir = tmp_path / "nested_cfg"
    nested_cfg_dir.mkdir()
    (nested_cfg_dir / "docs").mkdir()
    (nested_cfg_dir / "models").mkdir()

    cfg_text = (
        "{"
        f'"database":{{"driver":"postgresql","host":"h","port":1,"database":"d","username":"u","password":"p","schema":"public","ssl_mode":"prefer"}},'
        f'"knowledge_base":{{"input_roots":["C:/Docs/Repo/docs","C:\\Docs\\Repo\\docs2"],"include_extensions":[".md"],"exclude_dirs":[],"exclude_globs":[],"follow_symlinks":false}},'
        f'"llm":{{"provider":"llama_cpp","local_model_path":"C:\\Docs\\Repo\\models\\qwen.gguf","download_if_missing":false,"download_source":{{"type":"huggingface","repo_id":"r","filename":"f","local_dir":"C:/Docs/Repo/models/"}}}}'
        "}"
    )
    nested_cfg_path = nested_cfg_dir / "builder_config.jsonc"
    nested_cfg_path.write_text(cfg_text, encoding="utf-8")

    cfg = load_config(root, override=str(nested_cfg_path))
    assert cfg.knowledge_base.input_roots[0] == "C:/Docs/Repo/docs"
    assert cfg.knowledge_base.input_roots[1] == "C:/Docs/Repo/docs2"
    assert cfg.llm.local_model_path == "C:/Docs/Repo/models/qwen.gguf"
    assert cfg.llm.download_source.local_dir == "C:/Docs/Repo/models"
    assert not cfg.knowledge_base.input_roots[0].startswith((nested_cfg_dir).resolve().as_posix())


def test_unc_paths_are_not_rebased_into_config_dir(tmp_path: Path) -> None:
    root = tmp_path
    (root / "config").mkdir()
    for name in ["builder_defaults.json", "builder_config.schema.json"]:
        (root / "config" / name).write_text((Path(__file__).resolve().parents[1] / "config" / name).read_text(encoding="utf-8"), encoding="utf-8")

    nested_cfg_dir = tmp_path / "nested_cfg"
    nested_cfg_dir.mkdir()

    cfg_text = (
        "{"
        f'"database":{{"driver":"postgresql","host":"h","port":1,"database":"d","username":"u","password":"p","schema":"public","ssl_mode":"prefer"}},'
        f'"knowledge_base":{{"input_roots":["//server/share/docs"],"include_extensions":[".md"],"exclude_dirs":[],"exclude_globs":[],"follow_symlinks":false}},'
        f'"llm":{{"provider":"llama_cpp","local_model_path":"C:/Docs/Repo/models/qwen.gguf","download_if_missing":false,"download_source":{{"type":"huggingface","repo_id":"r","filename":"f","local_dir":"C:/Docs/Repo/models"}}}}'
        "}"
    )
    nested_cfg_path = nested_cfg_dir / "builder_config.jsonc"
    nested_cfg_path.write_text(cfg_text, encoding="utf-8")

    cfg = load_config(root, override=str(nested_cfg_path))
    assert cfg.knowledge_base.input_roots[0] == "//server/share/docs"
    assert not cfg.knowledge_base.input_roots[0].startswith(nested_cfg_dir.resolve().as_posix())


def test_windows_absolute_override_paths_are_detected_as_absolute() -> None:
    assert is_windows_drive_absolute("C:/custom-config/builder_config.jsonc")
    assert is_windows_drive_absolute(r"C:\custom-config\builder_config.jsonc")
    assert is_unc_absolute(r"\\server\share\builder_config.jsonc")
    assert is_unc_absolute("//server/share/builder_config.jsonc")
    assert is_config_absolute_path("C:/custom-config/builder_config.jsonc")
    assert is_config_absolute_path(r"C:\custom-config\builder_config.jsonc")
    assert is_config_absolute_path(r"\\server\share\builder_config.jsonc")
    assert normalize_path_string(normalize_fs_path("C:/custom-config/builder_config.jsonc", base_dir=Path("/tmp"))) == "C:/custom-config/builder_config.jsonc"
    assert normalize_path_string(normalize_fs_path(r"C:\custom-config\builder_config.jsonc", base_dir=Path("/tmp"))) == "C:/custom-config/builder_config.jsonc"


def test_absolute_path_preservation_and_working_directory_independence(monkeypatch: pytest.MonkeyPatch, tmp_path: Path) -> None:
    root = Path(__file__).resolve().parents[1]
    external = tmp_path / "external"
    external.mkdir()
    absolute_docs = tmp_path / "absolute-docs"
    absolute_docs.mkdir()
    absolute_model = tmp_path / "model.gguf"
    absolute_model.write_text("x", encoding="utf-8")
    for name in ["builder_defaults.json", "builder_config.schema.json"]:
        external.joinpath(name).write_text((root / "config" / name).read_text(encoding="utf-8"), encoding="utf-8")
    cfg = (
        "{"
        f'"database":{{"driver":"postgresql","host":"h","port":1,"database":"d","username":"u","password":"p","schema":"public","ssl_mode":"prefer"}},'
        f'"knowledge_base":{{"input_roots":["{absolute_docs.as_posix()}"],"include_extensions":[".md"],"exclude_dirs":[],"exclude_globs":[],"follow_symlinks":false}},'
        f'"llm":{{"provider":"llama_cpp","local_model_path":"{absolute_model.as_posix()}","download_if_missing":false,"download_source":{{"type":"huggingface","repo_id":"r","filename":"f","local_dir":"{tmp_path.as_posix()}"}}}}'
        "}"
    )
    config_path = external / "builder_config.jsonc"
    config_path.write_text(cfg, encoding="utf-8")
    monkeypatch.chdir(tmp_path)
    config = load_config(root, override=str(config_path))
    assert Path(config.knowledge_base.input_roots[0]) == absolute_docs.resolve()
    assert Path(config.llm.local_model_path) == absolute_model.resolve()


def test_forward_slash_windows_style_paths_and_trailing_separators_are_accepted(tmp_path: Path) -> None:
    _write_operator_fixture(
        tmp_path,
        '{'
        '"database":{"driver":"postgresql","host":"h","port":5432,"database":"d","username":"u","password":"p","schema":"public","ssl_mode":"prefer"},'
        '"knowledge_base":{"input_roots":["C:/Docs/Repo/github/pipesim/pip-pipesim/Stingray/.ai/docs/","../docs/"],"include_extensions":[".md"],"exclude_dirs":[],"exclude_globs":[],"follow_symlinks":false},'
        '"llm":{"provider":"llama_cpp","local_model_path":"C:/Models/builder/model.gguf","download_if_missing":false,"download_source":{"type":"huggingface","repo_id":"r","filename":"f","local_dir":"C:/Models/builder/"}}}',
    )
    config = load_config(tmp_path)
    assert config.knowledge_base.input_roots[0] == "C:/Docs/Repo/github/pipesim/pip-pipesim/Stingray/.ai/docs"
    assert config.knowledge_base.input_roots[1] == (tmp_path / "docs").resolve().as_posix()
    assert config.llm.download_source.local_dir == "C:/Models/builder"
    assert config.embedding.local_model_path == (tmp_path / "config" / "models" / "embeddings" / "bge-small-en-v1.5").resolve().as_posix()


def test_commented_config_mentions_relative_and_absolute_semantics() -> None:
    root = Path(__file__).resolve().parents[1]
    text = (root / "config" / "builder_config.jsonc").read_text(encoding="utf-8")
    assert "Absolute paths are allowed" in text
    assert "Relative paths are resolved relative to this config file's directory" in text
    assert "Prefer forward slashes on Windows" in text
    assert "Ordinary Windows backslash paths are also tolerated" in text
    assert "recursively across all subfolders" in text


def test_normal_user_only_edits_builder_config_jsonc(tmp_path: Path) -> None:
    root = Path(__file__).resolve().parents[1]
    (tmp_path / "config").mkdir()
    for name in [
        "builder_defaults.json",
        "builder_config.schema.json",
        "category_ontology.json",
        "category_ontology.schema.json",
    ]:
        (tmp_path / "config" / name).write_text((root / "config" / name).read_text(encoding="utf-8"), encoding="utf-8")
    builder_text = (
        "{"
        '"database":{"driver":"postgresql","host":"db.local","port":5432,"database":"memorydb","username":"user","password":"pw","schema":"public","ssl_mode":"prefer"},'
        '"knowledge_base":{"input_roots":["./docs"],"include_extensions":[".md",".txt"],"exclude_dirs":[".git"],"exclude_globs":[],"follow_symlinks":false},'
        '"llm":{"provider":"llama_cpp","local_model_path":"./models/model.gguf","download_if_missing":false,"download_source":{"type":"huggingface","repo_id":"repo","filename":"file.gguf","local_dir":"./staging"}}'
        "}"
    )
    (tmp_path / "config" / "builder_config.jsonc").write_text(builder_text, encoding="utf-8")
    config = load_config(tmp_path)
    assert config.database.host == "db.local"
    assert config.builder.mode == "build-changed"
    assert config.llm.download_source.local_dir == (tmp_path / "config" / "staging").resolve().as_posix()
    assert config.embedding.local_model_path == (tmp_path / "config" / "models" / "embeddings" / "bge-small-en-v1.5").resolve().as_posix()


@pytest.mark.parametrize(
    ("config_text", "expected"),
    [
        (
            '{"database":{"driver":"postgresql","host":"h","port":"5432","database":"d","username":"u","password":"p","schema":"public","ssl_mode":"prefer"},'
            '"knowledge_base":{"input_roots":["docs"],"include_extensions":[".md"],"exclude_dirs":[],"exclude_globs":[],"follow_symlinks":false},'
            '"llm":{"provider":"llama_cpp","local_model_path":"m.gguf","download_if_missing":false,"download_source":{"type":"huggingface","repo_id":"r","filename":"f","local_dir":"models"}}}',
            "database.port",
        ),
        (
            '{"database":{"driver":"postgresql","host":"h","port":5432,"database":"d","username":"u","password":"p","schema":"public","ssl_mode":"prefer"},'
            '"knowledge_base":{"input_roots":"docs","include_extensions":[".md"],"exclude_dirs":[],"exclude_globs":[],"follow_symlinks":false},'
            '"llm":{"provider":"llama_cpp","local_model_path":"m.gguf","download_if_missing":false,"download_source":{"type":"huggingface","repo_id":"r","filename":"f","local_dir":"models"}}}',
            "knowledge_base.input_roots",
        ),
        (
            '{"database":{"driver":"postgresql","host":"h","port":5432,"database":"d","username":"u","password":"p","schema":"public","ssl_mode":"prefer"},'
            '"knowledge_base":{"input_roots":["docs"],"include_extensions":[".md"],"exclude_dirs":[],"exclude_globs":[],"follow_symlinks":false},'
            '"llm":{"provider":"llama_cpp","local_model_path":"m.gguf","download_if_missing":false,"download_source":{"type":"huggingface","filename":"f","local_dir":"models"}}}',
            "llm.download_source",
        ),
        (
            '{"database":{"driver":"postgresql","host":"h","port":5432,"database":"d","username":"u","password":"p","schema":"public","ssl_mode":"prefer"},'
            '"knowledge_base":{"input_roots":["docs"],"include_extensions":[".md"],"exclude_dirs":[],"exclude_globs":[],"follow_symlinks":false},'
            '"llm":{"provider":"llama_cpp","local_model_path":"m.gguf","download_if_missing":false,"download_source":{"type":"huggingface","repo_id":"r","filename":"f","local_dir":"models"}},"runtime":{"mode":"build-all"}}',
            "Additional properties are not allowed",
        ),
        (
            '{"database":{"driver":"postgresql","host":"h","port":5432,"database":"d","username":"u","password":"p","schema":"public","ssl_mode":"prefer"},'
            '"knowledge_base":{"input_roots":["docs"],"include_extensions":[".md",".pdf"],"exclude_dirs":[],"exclude_globs":[],"follow_symlinks":false},'
            '"llm":{"provider":"llama_cpp","local_model_path":"m.gguf","download_if_missing":false,"download_source":{"type":"huggingface","repo_id":"r","filename":"f","local_dir":"models"}}}',
            "knowledge_base.include_extensions",
        ),
    ],
)
def test_operator_config_json_schema_failure_cases(tmp_path: Path, config_text: str, expected: str) -> None:
    _write_operator_fixture(tmp_path, config_text)
    with pytest.raises(ValueError, match=expected):
        load_config(tmp_path)


def test_entrypoint_no_arg_startup_behavior(monkeypatch: pytest.MonkeyPatch) -> None:
    root = Path(__file__).resolve().parents[1]
    monkeypatch.chdir(root)
    monkeypatch.setattr("sys.argv", ["agent_embedding_builder.py"])
    monkeypatch.setattr("agent_embedding_builder.run_pipeline", lambda root, config: type("Stats", (), {"as_dict": lambda self: {"ok": True}})())
    assert main() == 0

# NOTE:
# `Scripts/run_builder.ps1` must not be unit-tested (no launcher behavior tests).
# Validation and dependency/runtime rules should be enforced by code review and manual execution,
# not by string-matching tests against this script.
