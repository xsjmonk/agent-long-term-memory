from __future__ import annotations

import os
from datetime import datetime
from pathlib import Path

from app.hash_state import sha256_file
from app.models import KnowledgeBaseConfig, SourceFile
#


def _is_excluded(path: Path, exclude_dirs: list[str], exclude_globs: list[str]) -> bool:
    path_text = path.as_posix()
    return any(part in exclude_dirs for part in path.parts) or any(
        path.match(pattern) or path.name.endswith(pattern.replace("*", "")) or path_text.endswith(pattern.replace("*", ""))
        for pattern in exclude_globs
    )


def discover_source_files(root: Path, config: KnowledgeBaseConfig, max_file_size_bytes: int) -> list[SourceFile]:
    results: list[SourceFile] = []
    seen_visible_paths: set[str] = set()
    extensions = {ext.lower() for ext in config.include_extensions}
    for directory in config.input_roots:
        # `knowledge_base.input_roots` must already be canonical absolute strings.
        base = Path(directory)
        if not base.exists():
            continue
        for dirpath, dirnames, filenames in os.walk(base, topdown=True, followlinks=config.follow_symlinks):
            dirpath_path = Path(dirpath)
            filtered_dirnames: list[str] = []
            for dirname in sorted(dirnames):
                candidate_dir = dirpath_path / dirname
                relative_dir = candidate_dir.relative_to(base)
                if dirname in config.exclude_dirs:
                    continue
                if any(part in config.exclude_dirs for part in relative_dir.parts):
                    continue
                if _is_excluded(relative_dir, config.exclude_dirs, config.exclude_globs):
                    continue
                if not config.follow_symlinks and candidate_dir.is_symlink():
                    continue
                filtered_dirnames.append(dirname)
            dirnames[:] = filtered_dirnames
            for filename in sorted(filenames):
                visible_path = dirpath_path / filename
                relative_path = visible_path.relative_to(base)
                if not config.follow_symlinks and visible_path.is_symlink():
                    continue
                if any(part in config.exclude_dirs for part in relative_path.parts):
                    continue
                if visible_path.suffix.lower() not in extensions:
                    continue
                if _is_excluded(relative_path, config.exclude_dirs, config.exclude_globs):
                    continue
                visible_key = visible_path.as_posix()
                if visible_key in seen_visible_paths:
                    continue
                seen_visible_paths.add(visible_key)
                stat = visible_path.stat()
                if stat.st_size > max_file_size_bytes:
                    continue
                results.append(
                    SourceFile(
                        path=visible_path,
                        extension=visible_path.suffix.lower(),
                        content_hash=sha256_file(visible_path),
                        size_bytes=stat.st_size,
                        modified_at=datetime.fromtimestamp(stat.st_mtime),
                    )
                )
    return sorted(results, key=lambda item: item.path.as_posix())
