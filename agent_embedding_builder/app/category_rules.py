from __future__ import annotations

import re

from app.category_ontology import CategoryOntology
from app.models import CandidateSegment, LabelEvidence


LEGACY_FALLBACK_RULES: dict[str, list[str]] = {
    "anti_pattern": [r"\bdo not\b", r"\bavoid\b", r"\bcommon mistake\b"],
    "best_practice": [r"\brecommended\b", r"\bshould\b"],
    "project_history": [r"\b20\d{2}\b", r"\bmilestone\b"],
    "implementation": [r"\bimplement\b", r"\bconfigure\b"],
    "constraint": [r"\bmust not\b", r"\bwithout changing\b"],
}


def infer_labels_from_rules(candidate: CandidateSegment, ontology: CategoryOntology, use_heading_rules: bool = True, use_legacy_fallback: bool = True) -> list[LabelEvidence]:
    text = candidate.text.lower()
    heading_text = " ".join(candidate.heading_path).lower()
    results: list[LabelEvidence] = []
    for label_name, meta in ontology.labels.items():
        positives = [value.lower() for value in meta.get("positive_lexical_hints", [])]
        negatives = [value.lower() for value in meta.get("negative_lexical_hints", [])]
        structural_terms = [label_name.lower()] + [alias.lower() for alias in meta.get("aliases", [])]
        if use_heading_rules and any(term in heading_text for term in structural_terms + positives):
            results.append(LabelEvidence(label=label_name, label_role="secondary", confidence=0.78, source_method="heading_rule", explanation=f"Heading/path matched ontology hints for {label_name}."))
        if any(term in text for term in positives) and not any(term in text for term in negatives):
            results.append(LabelEvidence(label=label_name, label_role="secondary", confidence=0.70, source_method="ontology_lexical", explanation=f"Text matched ontology lexical hints for {label_name}."))
    if use_legacy_fallback:
        for label, patterns in LEGACY_FALLBACK_RULES.items():
            if any(re.search(pattern, text) for pattern in patterns):
                results.append(LabelEvidence(label=label, label_role="secondary", confidence=0.62, source_method="rule_based", explanation=f"Matched legacy fallback rule for {label}."))    
    return dedupe_labels(results)


def dedupe_labels(labels: list[LabelEvidence]) -> list[LabelEvidence]:
    best: dict[str, LabelEvidence] = {}
    for item in labels:
        current = best.get(item.label)
        if current is None or item.confidence > current.confidence:
            best[item.label] = item
    return list(best.values())
