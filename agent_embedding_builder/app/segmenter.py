from __future__ import annotations

from collections import defaultdict

from app.hash_state import segment_hash
from app.models import CandidateSegment, ParsedDocument, SegmentationConfig, SourceMetadata


def _group_paragraphs(paragraphs: list[str], max_chars: int) -> list[str]:
    groups: list[str] = []
    current: list[str] = []
    current_len = 0
    for paragraph in paragraphs:
        if current and current_len + len(paragraph) > max_chars:
            groups.append("\n\n".join(current))
            current = []
            current_len = 0
        current.append(paragraph)
        current_len += len(paragraph)
    if current:
        groups.append("\n\n".join(current))
    return groups


def generate_candidates(document: ParsedDocument, config: SegmentationConfig) -> list[CandidateSegment]:
    candidates: list[CandidateSegment] = []
    base_metadata = SourceMetadata.model_validate(document.metadata or {})
    if config.allow_file_level_items:
        candidates.append(
            CandidateSegment(
                source_path=document.path,
                source_type=document.source_type,
                span_level="file",
                text=document.raw_text.strip(),
                heading_path=[document.title],
                start_line=1,
                end_line=len(document.raw_text.splitlines()) or 1,
                metadata=base_metadata,
            )
        )

    by_level: dict[int, list] = defaultdict(list)
    for section in document.sections:
        by_level[section.level].append(section)
        if section.level == 1 and config.allow_section_level_items:
            candidates.append(
                CandidateSegment(
                    source_path=document.path,
                    source_type=document.source_type,
                    span_level="section",
                    text=section.body,
                    heading_path=section.heading_path,
                    start_line=section.start_line,
                    end_line=section.end_line,
                    parent_key=document.title,
                    metadata=base_metadata,
                )
            )
        elif section.level >= 2 and config.allow_subsection_level_items:
            candidates.append(
                CandidateSegment(
                    source_path=document.path,
                    source_type=document.source_type,
                    span_level="subsection",
                    text=section.body,
                    heading_path=section.heading_path,
                    start_line=section.start_line,
                    end_line=section.end_line,
                    parent_key=">".join(section.heading_path[:-1]) or document.title,
                    metadata=base_metadata,
                )
            )

    if config.allow_paragraph_group_items:
        paragraph_groups = _group_paragraphs(document.paragraphs, config.max_paragraph_group_chars)
        parent_paths = [section.heading_path for section in document.sections] or [[document.title]]
        for idx, group in enumerate(paragraph_groups, start=1):
            inherited_path = parent_paths[min(idx - 1, len(parent_paths) - 1)]
            candidates.append(
                CandidateSegment(
                    source_path=document.path,
                    source_type=document.source_type,
                    span_level="paragraph_group",
                    text=group,
                    heading_path=inherited_path + [f"paragraph-group-{idx}"],
                    start_line=None,
                    end_line=None,
                    parent_key=">".join(inherited_path),
                    metadata=base_metadata,
                )
            )

    final_candidates = [candidate for candidate in candidates if candidate.text.strip()]
    for candidate in final_candidates:
        candidate.segment_hash = segment_hash(candidate)
    return final_candidates


def select_segments_with_duplicate_control(candidates: list[CandidateSegment], specificity_scores: dict[str, float]) -> list[CandidateSegment]:
    selected: list[CandidateSegment] = []
    level_order = {"file": 0, "section": 1, "subsection": 2, "paragraph_group": 3, "synthetic": 4}
    for candidate in sorted(candidates, key=lambda item: (level_order[item.span_level], -len(item.text))):
        overlapping = [
            existing
            for existing in selected
            if existing.parent_key == candidate.parent_key or existing.heading_path[:-1] == candidate.heading_path[:-1]
        ]
        if any(existing.text == candidate.text and specificity_scores.get(candidate.segment_hash, 0.0) <= specificity_scores.get(existing.segment_hash, 0.0) for existing in overlapping):
            continue
        selected.append(candidate)
    return selected
