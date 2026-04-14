from __future__ import annotations

import hashlib
from pathlib import Path

from app.models import AppConfig, CandidateSegment


def sha256_text(text: str) -> str:
    return hashlib.sha256(text.encode("utf-8")).hexdigest()


def sha256_file(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as handle:
        for chunk in iter(lambda: handle.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def segment_hash(candidate: CandidateSegment) -> str:
    payload = "|".join(
        [
            str(candidate.source_path),
            candidate.span_level,
            ">".join(candidate.heading_path),
            str(candidate.start_line),
            str(candidate.end_line),
            candidate.text,
        ]
    )
    return sha256_text(payload)


def rebuild_signature(config: AppConfig, embedding_model_version: str | None) -> dict[str, str | None]:
    return {
        "builder_version": config.builder.builder_version,
        "classifier_version": config.builder.classifier_version,
        "embedding_model_version": embedding_model_version or config.embedding.model_name,
        "normalization_version": config.builder.normalization_version,
        "ontology_version": getattr(config, "_ontology_version", "unknown"),
        "schema_compatibility_version": config.builder.schema_compatibility_version,
    }


def should_rebuild(
    last_state: dict[str, str | None] | None,
    artifact_hash: str,
    signature: dict[str, str | None],
    enabled: bool,
) -> bool:
    if not enabled or not last_state:
        return True
    if last_state.get("build_status") != "succeeded":
        return True
    if last_state.get("artifact_hash") != artifact_hash:
        return True
    for key, value in signature.items():
        if last_state.get(key) != value:
            return True
    return False
