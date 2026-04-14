from __future__ import annotations

from pathlib import Path

from app.path_utils import normalize_fs_path


def source_ref_for_path(path: Path) -> str:
    return normalize_fs_path(path).as_posix()


def source_type_for_extension(extension: str) -> str:
    return "markdown" if extension == ".md" else "text"
