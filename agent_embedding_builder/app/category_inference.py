from __future__ import annotations

import json
import logging

from app.category_ontology import CategoryOntology
from app.category_rules import dedupe_labels, infer_labels_from_rules
from app.models import CandidateSegment, ClassificationConfig, LLMInferenceResult, LabelEvidence

logger = logging.getLogger(__name__)


LLM_PROMPT = """You are classifying a retrieval candidate from a long-term memory builder.
Return JSON only with keys: labels, normalized_retrieval_text, summary, rationale.
Each label must include: label, label_role, confidence, source_method, explanation.

Allowed labels:
{labels}

Heading path:
{heading_path}

Text:
{text}
"""


def infer_categories(
    candidate: CandidateSegment,
    config: ClassificationConfig,
    local_llm,
    ontology: CategoryOntology,
    allow_unknown_labels: bool,
    default_confidence_floor: float,
    allow_llm: bool,
) -> tuple[list[LabelEvidence], LLMInferenceResult, list[str]]:
    labels: list[LabelEvidence] = []
    warnings: list[str] = []
    for label in candidate.metadata.labels:
        normalized, warning = ontology.normalize_label(label, allow_unknown_labels)
        if warning:
            warnings.append(warning)
        if normalized:
            labels.append(LabelEvidence(label=normalized, label_role="secondary", confidence=0.9, source_method="metadata", explanation="Source metadata label."))
    if config.enable_rule_based or config.enable_heading_rules:
        labels.extend(infer_labels_from_rules(candidate, ontology, config.enable_heading_rules, config.enable_rule_based))

    llm_result = LLMInferenceResult(
        normalized_retrieval_text=candidate.text.strip(),
        summary=candidate.text.strip().split("\n")[0][:240],
    )
    if config.enable_llm and local_llm is not None and allow_llm:
        prompt = LLM_PROMPT.format(
            labels=json.dumps(sorted(ontology.labels.keys())),
            heading_path=" > ".join(candidate.heading_path),
            text=candidate.text[:2000],
        )
        try:
            llm_result = local_llm.infer(prompt)
            labels.extend(llm_result.labels)
        except Exception as exc:
            marker = f"local_llm_fallback:{type(exc).__name__}"
            warnings.append(marker)
            logger.debug("Local LLM classification fallback used marker=%s error=%s", marker, exc)

    normalized_labels: list[LabelEvidence] = []
    for item in labels:
        normalized, warning = ontology.normalize_label(item.label, allow_unknown_labels)
        if warning:
            warnings.append(warning)
        if normalized:
            item.label = normalized
            normalized_labels.append(item)
    deduped = dedupe_labels(normalized_labels)
    if not deduped:
        deduped = [
            LabelEvidence(
                label="reference",
                label_role="primary",
                confidence=default_confidence_floor,
                source_method="fallback",
                explanation="Fallback when no stronger classification signal exists.",
            )
        ]
    for item in deduped:
        threshold = max(config.min_primary_label_confidence, ontology.confidence_floor_for(item.label, default_confidence_floor))
        if item.confidence >= threshold:
            item.label_role = "primary"
    if not llm_result.normalized_retrieval_text:
        llm_result.normalized_retrieval_text = candidate.text.strip()
    return deduped, llm_result, warnings
