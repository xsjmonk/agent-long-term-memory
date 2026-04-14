from __future__ import annotations

import os
import re
import copy
from pathlib import Path, PureWindowsPath


_WINDOWS_DRIVE_ABSOLUTE_RE = re.compile(r"^[A-Za-z]:[\\/]")


def is_windows_drive_absolute(path_str: str) -> bool:
    return bool(_WINDOWS_DRIVE_ABSOLUTE_RE.match(path_str))


def is_unc_absolute(path_str: str) -> bool:
    return path_str.startswith("\\\\") or path_str.startswith("//")


def is_config_absolute_path(path_str: str | Path) -> bool:
    text = str(path_str)
    return Path(text).is_absolute() or is_windows_drive_absolute(text) or is_unc_absolute(text)


def normalize_path_separators(path_str: str) -> str:
    text = path_str.replace("\\", "/")
    if is_unc_absolute(path_str):
        text = "//" + re.sub(r"/+", "/", text.lstrip("/"))
    else:
        text = re.sub(r"/+", "/", text)
    if len(text) > 1 and text.endswith("/"):
        if is_windows_drive_absolute(text) and len(text) == 3:
            return text
        if text.startswith("//") and text.count("/") == 3:
            return text
        return text.rstrip("/")
    return text


def _absolute_path_object(path_str: str) -> Path | PureWindowsPath:
    normalized = normalize_path_separators(path_str)
    if os.name == "nt":
        return Path(normalized)
    return PureWindowsPath(normalized)


def resolve_config_path_value(base_dir: Path, value: str | Path) -> Path | PureWindowsPath:
    expanded = os.path.expandvars(os.path.expanduser(str(value)))
    if is_windows_drive_absolute(expanded) or is_unc_absolute(expanded):
        return _absolute_path_object(expanded)
    raw = Path(expanded)
    if raw.is_absolute():
        return raw.resolve(strict=False)
    return (base_dir / raw).resolve(strict=False)


def normalize_fs_path(path: str | Path, *, base_dir: Path | None = None) -> Path | PureWindowsPath:
    expanded = os.path.expandvars(os.path.expanduser(str(path)))
    if base_dir is not None:
        return resolve_config_path_value(base_dir, expanded)
    if is_windows_drive_absolute(expanded) or is_unc_absolute(expanded):
        return _absolute_path_object(expanded)
    return Path(expanded).resolve(strict=False)


def resolve_path_from_config_dir(config_dir: Path, value: str | Path) -> Path | PureWindowsPath:
    return normalize_fs_path(value, base_dir=config_dir)


def resolve_paths_from_config_dir(config_dir: Path, values: list[str]) -> list[Path | PureWindowsPath]:
    return [resolve_path_from_config_dir(config_dir, value) for value in values]


def canonicalize_config_path_to_string(config_dir: Path, value: str | Path) -> str:
    """
    Convert an operator-facing config path to a plain absolute, slash-normalized string.
    """
    resolved = resolve_path_from_config_dir(config_dir, value)
    return normalize_path_string(resolved)


def normalize_path_string(path: Path | PureWindowsPath | str) -> str:
    text = str(path)
    if isinstance(path, (Path, PureWindowsPath)):
        text = path.as_posix()
    else:
        text = normalize_path_separators(text)
    return normalize_path_separators(text)


def path_is_under_roots(path: str | Path, roots: list[str | Path]) -> bool:
    normalized_path = normalize_path_string(normalize_fs_path(path))
    for root in roots:
        normalized_root = normalize_path_string(normalize_fs_path(root))
        if normalized_path == normalized_root or normalized_path.startswith(f"{normalized_root}/"):
            return True
    return False


def canonicalize_config_paths(config_dir: Path, data: dict) -> dict:
    """
    Return a deep-copied config payload where all operator-facing path fields
    are absolute normalized strings resolved relative to config_dir when needed.
    """

    # Deep copy to ensure we never mutate caller-provided payload.
    payload = copy.deepcopy(data)

    def _canon_one(value: str | Path) -> str:
        resolved = resolve_path_from_config_dir(config_dir, value)
        return normalize_path_string(resolved)

    # knowledge_base.input_roots[] (list[str])
    kb = payload.get("knowledge_base")
    if isinstance(kb, dict) and isinstance(kb.get("input_roots"), list):
        kb["input_roots"] = [_canon_one(v) for v in kb["input_roots"]]

    # llm.local_model_path + llm.download_source.local_dir
    llm = payload.get("llm")
    if isinstance(llm, dict):
        if "local_model_path" in llm:
            llm["local_model_path"] = _canon_one(llm["local_model_path"])
        dsrc = llm.get("download_source")
        if isinstance(dsrc, dict) and "local_dir" in dsrc:
            dsrc["local_dir"] = _canon_one(dsrc["local_dir"])

    # embedding.local_model_path
    emb = payload.get("embedding")
    if isinstance(emb, dict) and "local_model_path" in emb:
        emb["local_model_path"] = _canon_one(emb["local_model_path"])

    # category_governance.ontology_path + schema_path
    cg = payload.get("category_governance")
    if isinstance(cg, dict):
        if "ontology_path" in cg:
            cg["ontology_path"] = _canon_one(cg["ontology_path"])
        if "schema_path" in cg:
            cg["schema_path"] = _canon_one(cg["schema_path"])

    return payload
