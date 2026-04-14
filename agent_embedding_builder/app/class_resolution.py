from __future__ import annotations

from app.category_ontology import CategoryOntology
from app.models import LabelEvidence, RetrievalClass


def resolve_retrieval_class(labels: list[LabelEvidence], ontology: CategoryOntology) -> RetrievalClass:
    label_set = {label.label for label in labels}
    fallback = "reference"
    for rule in ontology.special_rules:
        if not rule["if_labels"]:
            fallback = rule["then_retrieval_class"]
            continue
        if label_set.intersection(set(rule["if_labels"])):
            return rule["then_retrieval_class"]
    ordered = sorted(labels, key=lambda item: item.confidence, reverse=True)
    for label in ordered:
        if label.label in ontology.label_to_retrieval_class:
            return ontology.label_to_retrieval_class[label.label]
    return fallback
