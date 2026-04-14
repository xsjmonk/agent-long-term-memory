from __future__ import annotations

import json
from pathlib import Path

from app.models import SourceMetadata


def load_sidecar_metadata(path: Path, sidecar_suffix: str = ".meta.json") -> SourceMetadata:
    sidecar = Path(str(path) + sidecar_suffix)
    if not sidecar.exists():
        return SourceMetadata()
    return SourceMetadata.model_validate(json.loads(sidecar.read_text(encoding="utf-8")))


def merge_metadata(primary: SourceMetadata, secondary: SourceMetadata) -> SourceMetadata:
    data = secondary.model_dump()
    for key, value in primary.model_dump().items():
        if value not in (None, [], ""):
            data[key] = value
    return SourceMetadata.model_validate(data)
