from __future__ import annotations

from app.models import RetrievalProfilesConfig, SelectedSegment


def build_retrieval_profiles(item: SelectedSegment, config: RetrievalProfilesConfig) -> list[tuple[str, str]]:
    profiles: list[tuple[str, str]] = []
    if config.generate_summary and item.summary:
        profiles.append(("summary", item.summary))
    if config.generate_default_profile:
        profiles.append(("default", item.normalized_retrieval_text))
    if config.generate_route_profiles:
        labels = {label.label for label in item.labels}
        for route in config.route_types:
            if route == "core_task" and item.retrieval_class in {"reference", "best_practice", "implementation_note", "decision"}:
                profiles.append((route, item.normalized_retrieval_text))
            elif route == "pattern" and item.retrieval_class in {"best_practice", "implementation_note"}:
                profiles.append((route, item.normalized_retrieval_text))
            elif route == "risk" and "anti_pattern" in labels:
                profiles.append((route, item.normalized_retrieval_text))
            elif route == "constraint" and item.retrieval_class == "constraint":
                profiles.append((route, item.normalized_retrieval_text))
            elif route == "similar_case" and (item.retrieval_class == "similar_case" or {"project_history", "incident", "commit_context"} & labels):
                profiles.append((route, item.normalized_retrieval_text))
    seen: set[tuple[str, str]] = set()
    result: list[tuple[str, str]] = []
    for profile in profiles:
        if profile not in seen:
            seen.add(profile)
            result.append(profile)
    return result
