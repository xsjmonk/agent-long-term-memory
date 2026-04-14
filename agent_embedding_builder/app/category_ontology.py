from __future__ import annotations

import json
from pathlib import Path
from typing import Any

from jsonschema import Draft202012Validator
from jsonschema.exceptions import best_match

ALLOWED_RETRIEVAL_CLASSES = {
    "constraint",
    "decision",
    "antipattern",
    "best_practice",
    "similar_case",
    "implementation_note",
    "historical_note",
    "example",
    "reference",
    "structure",
}


class CategoryOntology:
    def __init__(self, ontology: dict[str, Any], schema: dict[str, Any]) -> None:
        validate_ontology_payload(ontology, schema)
        self.raw = ontology
        self.version: str = ontology["version"]
        self.labels = {item["name"]: item for item in ontology["labels"]}
        self.alias_to_canonical: dict[str, str] = {}
        self.deprecated_rewrites: dict[str, str] = {}
        self.confidence_floors: dict[str, float] = {}
        self.label_metadata: dict[str, dict[str, Any]] = {}
        for label, item in self.labels.items():
            self.alias_to_canonical[label.lower()] = label
            for alias in item.get("aliases", []):
                self.alias_to_canonical[alias.lower()] = label
            self.confidence_floors[label] = float(item.get("confidence_floor", 0.55))
            self.label_metadata[label] = item
            if item.get("deprecated") and item.get("replacement"):
                self.deprecated_rewrites[label] = item["replacement"]
        self.label_to_retrieval_class = ontology["retrieval_class_resolution"]["label_to_retrieval_class"]
        self.priority_order = ontology["retrieval_class_resolution"]["priority_order"]
        self.special_rules = ontology["retrieval_class_resolution"]["special_rules"]

    def normalize_label(self, label: str, allow_unknown: bool) -> tuple[str | None, str | None]:
        mapped = self.alias_to_canonical.get(label.lower())
        if mapped is None:
            if allow_unknown:
                return label, None
            return None, f"Unknown label: {label}"
        label_def = self.labels[mapped]
        if label_def.get("deprecated"):
            replacement = label_def.get("replacement")
            if replacement:
                return replacement, f"Deprecated label `{mapped}` rewritten to `{replacement}`."
            return None, f"Deprecated label without replacement: {mapped}"
        return mapped, None

    def structural_matches(self, text: str) -> list[str]:
        lowered = text.lower()
        matches: list[str] = []
        for name, meta in self.labels.items():
            candidates = [name] + meta.get("aliases", []) + meta.get("positive_lexical_hints", [])
            if any(candidate.lower() in lowered for candidate in candidates):
                matches.append(name)
        return sorted(set(matches))

    def confidence_floor_for(self, label: str, default_floor: float) -> float:
        return max(default_floor, self.confidence_floors.get(label, default_floor))


def load_category_ontology(ontology_path: Path, schema_path: Path) -> CategoryOntology:
    ontology = json.loads(ontology_path.read_text(encoding="utf-8"))
    schema = json.loads(schema_path.read_text(encoding="utf-8"))
    return CategoryOntology(ontology, schema)


def validate_ontology_payload(ontology: dict[str, Any], schema: dict[str, Any]) -> None:
    validator = Draft202012Validator(schema)
    error = best_match(validator.iter_errors(ontology))
    if error is not None:
        path = ".".join(str(part) for part in error.absolute_path)
        location = f" at `{path}`" if path else ""
        raise ValueError(f"Ontology schema validation failed{location}: {error.message}")
    _validate_ontology_semantics(ontology)


def _validate_ontology_semantics(ontology: dict[str, Any]) -> None:
    labels = ontology["labels"]
    canonical_labels = {item["name"] for item in labels}
    alias_claims: dict[str, str] = {}
    for item in labels:
        name = item["name"]
        retrieval_class = item["default_retrieval_class"]
        if retrieval_class not in ALLOWED_RETRIEVAL_CLASSES:
            raise ValueError(f"Ontology label `{name}` has invalid default retrieval class `{retrieval_class}`")
        for relation in ("parents", "children"):
            for related in item.get(relation, []):
                if related not in canonical_labels:
                    raise ValueError(f"Ontology label `{name}` references unknown {relation[:-1]} `{related}`")
        replacement = item.get("replacement")
        if replacement is not None and replacement not in canonical_labels:
            raise ValueError(f"Ontology label `{name}` has invalid replacement target `{replacement}`")
        for alias in [name, *item.get("aliases", [])]:
            alias_key = alias.lower()
            existing = alias_claims.get(alias_key)
            if existing is not None and existing != name:
                raise ValueError(f"Ontology alias collision for `{alias}` between `{existing}` and `{name}`")
            alias_claims[alias_key] = name

    resolution = ontology["retrieval_class_resolution"]
    for label_name, retrieval_class in resolution["label_to_retrieval_class"].items():
        if label_name not in canonical_labels:
            raise ValueError(f"Retrieval-class mapping references unknown label `{label_name}`")
        if retrieval_class not in ALLOWED_RETRIEVAL_CLASSES:
            raise ValueError(f"Retrieval-class mapping for `{label_name}` uses invalid class `{retrieval_class}`")

    for retrieval_class in resolution["priority_order"]:
        if retrieval_class not in ALLOWED_RETRIEVAL_CLASSES:
            raise ValueError(f"Priority order contains invalid retrieval class `{retrieval_class}`")

    for idx, rule in enumerate(resolution["special_rules"]):
        if "if_labels" not in rule or "then_retrieval_class" not in rule:
            raise ValueError(f"Special rule #{idx} must contain `if_labels` and `then_retrieval_class`")
        for label_name in rule["if_labels"]:
            if label_name not in canonical_labels:
                raise ValueError(f"Special rule #{idx} references unknown label `{label_name}`")
        retrieval_class = rule["then_retrieval_class"]
        if retrieval_class not in ALLOWED_RETRIEVAL_CLASSES:
            raise ValueError(f"Special rule #{idx} uses invalid retrieval class `{retrieval_class}`")
