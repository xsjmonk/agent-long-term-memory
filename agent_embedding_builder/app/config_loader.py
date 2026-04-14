from __future__ import annotations

import json
import os
from pathlib import Path

from jsonschema import Draft202012Validator
from jsonschema.exceptions import best_match
from pydantic import ValidationError

from app.config_jsonc_preprocessor import preprocess_operator_jsonc
from app.models import AppConfig
from app.path_utils import canonicalize_config_paths, resolve_path_from_config_dir

OPERATOR_SECTIONS = {
    "database",
    "knowledge_base",
    "llm",
}

OPTIONAL_OPERATOR_SECTIONS = {
    "embedding",
    "classification",
    "api",
}

INTERNAL_SECTIONS = {
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


def resolve_config_path(root: Path, override: str | None = None) -> Path:
    checked: list[Path] = []
    if override:
        path = resolve_path_from_config_dir(root, override)
        checked.append(path)
        if path.exists():
            return path
    if os.environ.get("EMBEDDING_BUILDER_CONFIG"):
        env_path = resolve_path_from_config_dir(root, os.environ["EMBEDDING_BUILDER_CONFIG"])
        checked.append(env_path)
        if env_path.exists():
            return env_path
    for candidate in [root / "config" / "builder_config.jsonc", root / "config" / "builder_config.json"]:
        checked.append(candidate)
        if candidate.exists():
            return candidate
    raise FileNotFoundError("No config file found. Checked:\n" + "\n".join(f"- {path}" for path in checked))


def _strip_jsonc(text: str) -> str:
    result: list[str] = []
    in_string = False
    escape = False
    i = 0
    while i < len(text):
        ch = text[i]
        nxt = text[i + 1] if i + 1 < len(text) else ""
        if in_string:
            result.append(ch)
            if escape:
                escape = False
            elif ch == "\\":
                escape = True
            elif ch == '"':
                in_string = False
            i += 1
            continue
        if ch == '"':
            in_string = True
            result.append(ch)
            i += 1
            continue
        if ch == "/" and nxt == "/":
            i += 2
            while i < len(text) and text[i] != "\n":
                i += 1
            continue
        if ch == "/" and nxt == "*":
            i += 2
            while i + 1 < len(text) and not (text[i] == "*" and text[i + 1] == "/"):
                i += 1
            i += 2
            continue
        result.append(ch)
        i += 1
    return "".join(result)


def _deep_merge(base: dict, overlay: dict) -> dict:
    merged = dict(base)
    for key, value in overlay.items():
        if isinstance(value, dict) and isinstance(merged.get(key), dict):
            merged[key] = _deep_merge(merged[key], value)
        else:
            merged[key] = value
    return merged


def _validate_operator_config(data: dict, schema: dict) -> None:
    validator = Draft202012Validator(schema)
    error = best_match(validator.iter_errors(data))
    if error is None:
        return
    path = ".".join(str(part) for part in error.absolute_path)
    location = f" at `{path}`" if path else ""
    raise ValueError(f"Operator config schema validation failed{location}: {error.message}")


def _build_canonical_runtime_config(merged: dict) -> dict:
    classification_override = merged.get("classification", {})
    return {
        "database": merged["database"],
        "knowledge_base": merged["knowledge_base"],
        "llm": {
            **merged["llm"],
            **merged["llm_internal"],
        },
        "embedding": {
            **merged["embedding"],
            **merged["embedding_internal"],
        },
        "category_governance": {
            **merged["category_governance"],
        },
        "builder": {
            **merged["builder_internal"],
            "mode": merged["runtime"]["mode"],
        },
        "segmentation": merged["segmentation_internal"],
        "classification": _deep_merge(merged["classification_internal"], classification_override),
        "retrieval_profiles": merged["retrieval_profiles_internal"],
        "rebuild": merged["rebuild_internal"],
        "logging": {
            "level": merged["runtime"]["log_level"],
            "json_logs": merged["runtime"]["json_logs"],
        },
        # Optional operator config. Defaults live in `AppConfig`.
        "api": merged.get("api", {}),
        "sync": merged["runtime"].get(
            "sync",
            {
                "reconcile_removed_files": True,
                "archive_removed_artifacts": True,
            },
        ),
        "metadata_internal": merged["metadata_internal"],
        "builder_internal": merged["builder_internal"],
        "llm_internal": merged["llm_internal"],
        "embedding_internal": merged["embedding_internal"],
        "segmentation_internal": merged["segmentation_internal"],
        "classification_internal": merged["classification_internal"],
        "retrieval_profiles_internal": merged["retrieval_profiles_internal"],
        "rebuild_internal": merged["rebuild_internal"],
        "category_internal": merged["category_internal"],
    }


def _validate_defaults_payload(defaults: dict) -> None:
    unexpected = sorted(set(defaults) - INTERNAL_SECTIONS)
    if unexpected:
        raise ValueError(
            "builder_defaults.json may only contain internal sections. "
            f"Unexpected top-level keys: {', '.join(unexpected)}"
        )


def _merge_operator_with_internal_defaults(defaults: dict, operator_data: dict) -> dict:
    _validate_defaults_payload(defaults)
    missing_operator_sections = sorted(OPERATOR_SECTIONS - set(operator_data))
    if missing_operator_sections:
        raise ValueError(
            "Operator config is missing required top-level sections: "
            + ", ".join(missing_operator_sections)
        )

    merged: dict = {section: defaults.get(section, {}) for section in INTERNAL_SECTIONS}
    for section in OPERATOR_SECTIONS:
        merged[section] = operator_data[section]

    # Optional operator sections overlay defaults without replacing them entirely.
    for section in OPTIONAL_OPERATOR_SECTIONS:
        if section not in operator_data:
            continue
        if section == "embedding":
            merged[section] = _deep_merge(merged.get(section, {}), operator_data[section])
        elif section == "classification":
            merged[section] = _deep_merge({}, operator_data[section])
        elif section == "api":
            merged[section] = operator_data[section]
    return merged


def _normalize_legacy_config(data: dict) -> dict:
    result = dict(data)
    legacy_detected = False
    if "source_inputs" in result and "knowledge_base" not in result:
        legacy_detected = True
        source_inputs = result.pop("source_inputs")
        result["knowledge_base"] = {
            "input_roots": source_inputs.get("directories", []),
            "include_extensions": source_inputs.get("include_extensions", [".md", ".txt"]),
            "exclude_dirs": [".git", "node_modules", "__pycache__", ".venv"],
            "exclude_globs": source_inputs.get("exclude_globs", []),
            "follow_symlinks": source_inputs.get("follow_symlinks", False),
        }
    if "embeddings" in result and "embedding" not in result:
        legacy_detected = True
        embeddings = result.pop("embeddings")
        result["embedding"] = {
            "provider": embeddings.get("provider", "sentence_transformers"),
            "model_name": embeddings.get("model_name_or_path", embeddings.get("model_name", "")),
            "local_model_path": embeddings.get("local_model_dir", embeddings.get("local_model_path", "")),
            "download_if_missing": embeddings.get("download_if_missing", True),
            "batch_size": embeddings.get("batch_size", 32),
            "normalize_embeddings": embeddings.get("normalize_embeddings", True),
        }
    if "incremental" in result and "rebuild" not in result:
        legacy_detected = True
        result["rebuild"] = result.pop("incremental")
    if "llm" in result:
        llm = dict(result["llm"])
        if "model_path" in llm and "local_model_path" not in llm:
            legacy_detected = True
            llm["local_model_path"] = llm.pop("model_path")
        if "download" in llm and "download_source" not in llm:
            legacy_detected = True
            llm["download_source"] = {"type": "huggingface", **llm.pop("download")}
        if "context_window" in llm and "context_length" not in llm:
            legacy_detected = True
            llm["context_length"] = llm.pop("context_window")
        result["llm"] = llm
    result.setdefault(
        "category_governance",
        {
            "ontology_path": "./config/category_ontology.json",
            "schema_path": "./config/category_ontology.schema.json",
            "allow_unknown_labels": False,
        },
    )
    result.setdefault("logging", {"level": result.get("builder", {}).get("log_level", "INFO"), "json_logs": False})
    result.setdefault("sync", {"reconcile_removed_files": True, "archive_removed_artifacts": True})
    if "builder" in result and "schema_compatibility_version" not in result["builder"]:
        result["builder"]["schema_compatibility_version"] = "1.0.0"
    if legacy_detected:
        print("Warning: deprecated legacy config format loaded; migrate to config/builder_config.jsonc")
    return result


def load_config(root: Path, override: str | None = None) -> AppConfig:
    path = resolve_config_path(root, override=override)
    config_dir = path.parent
    if path.suffix == ".jsonc":
        defaults_path = root / "config" / "builder_defaults.json"
        schema_path = root / "config" / "builder_config.schema.json"
        defaults = json.loads(_strip_jsonc(defaults_path.read_text(encoding="utf-8")))
        operator_text = preprocess_operator_jsonc(path.read_text(encoding="utf-8"))
        operator_data = json.loads(_strip_jsonc(operator_text))
        operator_schema = json.loads(schema_path.read_text(encoding="utf-8"))
        _validate_operator_config(operator_data, operator_schema)
        data = _build_canonical_runtime_config(_merge_operator_with_internal_defaults(defaults, operator_data))
    else:
        data = _normalize_legacy_config(json.loads(path.read_text(encoding="utf-8")))
    # Canonicalize operator-facing config paths once, relative to active config file directory.
    data = canonicalize_config_paths(config_dir, data)

    # Normalize and validate operator-facing embedding.force_device override.
    force_device = data.get("embedding", {}).get("force_device", "")
    if force_device is None:
        force_device = ""
    if isinstance(force_device, str):
        force_device = force_device.strip().lower()
    allowed = {"", "cpu", "cuda", "mps"}
    if force_device not in allowed:
        raise ValueError(
            f"Invalid config value `embedding.force_device={force_device!r}`. Allowed values are: '', 'cpu', 'cuda', 'mps'."
        )
    data["embedding"]["force_device"] = force_device
    try:
        return AppConfig.model_validate(data)
    except ValidationError as exc:
        raise ValueError(f"Invalid config file `{path}`:\n{exc}") from exc
