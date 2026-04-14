from __future__ import annotations

import re

from app.category_ontology import CategoryOntology
from app.models import CandidateSegment, LabelEvidence


LAYER_HINTS = {
    "ui": ["ui", "frontend", "screen", "component"],
    "api": ["api", "endpoint", "request", "response"],
    "database": ["database", "sql", "postgres", "table"],
    "engine": ["engine", "rule", "core logic"],
}


def extract_scopes_and_tags(candidate: CandidateSegment, labels: list[LabelEvidence], ontology: CategoryOntology, llm_tags: list[str] | None = None) -> tuple[list[tuple[str, str, float | None]], list[str]]:
    text = candidate.text.lower()
    scopes: list[tuple[str, str, float | None]] = []
    tags: set[str] = set(candidate.metadata.tags)
    metadata = candidate.metadata
    if metadata.domain:
        scopes.append(("domain", metadata.domain, 1.0))
    elif candidate.heading_path:
        scopes.append(("domain", candidate.heading_path[0].lower().replace(" ", "-"), 0.6))
    if metadata.module:
        scopes.append(("module", metadata.module, 1.0))
    elif len(candidate.heading_path) > 1:
        scopes.append(("module", candidate.heading_path[1].lower().replace(" ", "-"), 0.5))
    if metadata.feature:
        scopes.append(("feature", metadata.feature, 1.0))
    for part in candidate.heading_path:
        normalized = re.sub(r"[^a-z0-9]+", "-", part.lower()).strip("-")
        if normalized:
            tags.add(normalized)
    for layer, hints in LAYER_HINTS.items():
        if any(hint in text or hint in " ".join(tags) for hint in hints):
            scopes.append(("layer", layer, 1.0))
            tags.add(layer)
    for label in labels:
        tags.add(label.label)
        for scope_hint in ontology.labels.get(label.label, {}).get("scope_hints", []):
            scopes.append((scope_hint, metadata.module or metadata.feature or label.label, 0.4))
    if any(label.label == "anti_pattern" for label in labels):
        tags.add("risk")
    for tag in llm_tags or []:
        tags.add(tag)
    unique_scopes = []
    seen = set()
    for scope in scopes:
        if (scope[0], scope[1]) not in seen and scope[1]:
            seen.add((scope[0], scope[1]))
            unique_scopes.append(scope)
    return unique_scopes, sorted(tag for tag in tags if tag)
