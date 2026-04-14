from __future__ import annotations

import logging
from pathlib import Path

from app.build_stats import BuildStats
from app.category_ontology import load_category_ontology
from app.category_inference import infer_categories
from app.class_resolution import resolve_retrieval_class
from app.database import Database
from app.embedding_provider import EmbeddingProvider
from app.file_discovery import discover_source_files
from app.hash_state import rebuild_signature, should_rebuild
from app.local_llm import LocalLLM
from app.markdown_parser import parse_markdown
from app.models import AppConfig, LLMInferenceResult, SelectedSegment, SourceMetadata
from app.metadata_loader import load_sidecar_metadata, merge_metadata
from app.retrieval_profile_builder import build_retrieval_profiles
from app.scope_tag_extractor import extract_scopes_and_tags
from app.segmenter import generate_candidates, select_segments_with_duplicate_control
from app.source_registry import source_ref_for_path
from app.text_parser import parse_text

logger = logging.getLogger(__name__)


class LazyLocalLLM:
    """
    Lazy wrapper to defer GGUF model loading until the first actual inference call.
    """

    def __init__(self, config) -> None:
        self.config = config
        self._runtime: LocalLLM | None = None

    def infer(self, prompt: str, retries: int = 2) -> LLMInferenceResult:
        if self._runtime is None:
            self._runtime = LocalLLM(self.config)
        # Some test doubles may implement infer(prompt) without the retries argument.
        try:
            return self._runtime.infer(prompt, retries=retries)
        except TypeError:
            return self._runtime.infer(prompt)


def infer_case_shape(item: SelectedSegment) -> dict | None:
    labels = {label.label for label in item.labels}
    if item.retrieval_class != "similar_case" and not labels.intersection({"project_history", "incident", "migration_note", "commit_context", "similar_case"}):
        return None
    text = item.normalized_retrieval_text.lower()
    layer_scopes = [value for scope_type, value, _ in item.scopes if scope_type == "layer"]
    if any(token in text for token in ["bug", "fix", "incident", "outage", "failure"]):
        task_type = "bugfix"
    elif any(token in text for token in ["migrate", "migration", "upgrade"]):
        task_type = "migration"
    elif "refactor" in text:
        task_type = "refactor"
    elif "ui" in layer_scopes and any(token in text for token in ["change", "selector", "refresh", "component"]):
        task_type = "ui-change"
    elif "api" in layer_scopes and any(token in text for token in ["endpoint", "request", "response"]):
        task_type = "api-change"
    else:
        task_type = "general-change"
    feature_shape = "reference"
    for candidate in ["card-refresh", "selector-add", "async-load", "migration", "incident-response"]:
        if candidate in "-".join(item.tags) or candidate.replace("-", " ") in text:
            feature_shape = candidate
            break
    engine_change_allowed = False if any(token in text for token in ["without changing engine", "must not change engine", "no engine change"]) else None
    risk_signals = []
    if "anti_pattern" in labels:
        risk_signals.append("risk")
    if "incident" in labels:
        risk_signals.append("incident")
    if "migration_note" in labels:
        risk_signals.append("migration")
    if item.retrieval_class == "constraint":
        risk_signals.append("constraint")
    complexity = "high" if len(text) > 4000 or ({"ui", "api"} <= set(layer_scopes) and len(labels) >= 3) else "medium" if len(text) > 1500 or len(labels) >= 2 else "low"
    return {"task_type": task_type, "feature_shape": feature_shape, "engine_change_allowed": engine_change_allowed, "likely_layers": sorted(set(layer_scopes)), "risk_signals": sorted(set(risk_signals)), "complexity": complexity}


def run_pipeline(root: Path, config: AppConfig, database: Database | None = None) -> BuildStats:
    stats = BuildStats()
    logger.info("Starting embedding builder run. mode=%s", config.builder.mode)

    ontology = load_category_ontology(Path(config.category_governance.ontology_path), Path(config.category_governance.schema_path))
    setattr(config, "_ontology_version", ontology.version)
    logger.info("Loaded ontology version %s from %s", ontology.version, config.category_governance.ontology_path)

    database = database or Database(config)

    # Startup DB verification (fail fast).
    db_schema_info = database.verify_required_schema()
    missing_tables_count = len(db_schema_info.get("missing_tables", []))
    logger.info(
        "Database target verified host=%s port=%s database=%s schema=%s current_user=%s current_database=%s missing_tables=%s",
        db_schema_info.get("host"),
        db_schema_info.get("port"),
        db_schema_info.get("database_name"),
        db_schema_info.get("schema"),
        db_schema_info.get("current_user"),
        db_schema_info.get("database_name"),
        missing_tables_count,
    )

    embedding_provider = EmbeddingProvider(config.embedding)
    embedding_model_name, embedding_model_version = embedding_provider.model_metadata()
    logger.info("Embedding provider ready. model=%s version=%s", embedding_model_name, embedding_model_version)

    # Lazy GGUF runtime wrapper (only instantiates on the first local_llm.infer call).
    llm = LazyLocalLLM(config.llm) if config.classification.enable_llm else None

    run_ctx = database.create_ingestion_run(embedding_model_name, embedding_model_version)
    logger.info("Created ingestion run %s", run_ctx.run_id)

    files = discover_source_files(root, config.knowledge_base, config.builder.max_file_size_bytes)
    logger.info("Discovered %s source file(s) across %s configured root(s).", len(files), len(config.knowledge_base.input_roots))

    current_files = {source_ref_for_path(source_file.path): source_file for source_file in files}
    list_known_artifacts = getattr(database, "list_known_artifacts_for_roots", lambda roots: [])
    record_seen = getattr(database, "record_artifact_seen", lambda source_file: None)
    archive_removed = getattr(database, "archive_removed_artifact", None)

    known_artifacts = {artifact["source_ref"]: artifact for artifact in list_known_artifacts(config.knowledge_base.input_roots)}
    signature = rebuild_signature(config, embedding_model_version)

    # LLM usage counters (memory cap & operator visibility).
    llm_attempted_segments = 0
    llm_fallback_segments = 0
    llm_skipped_by_rules = 0
    llm_skipped_by_budget = 0
    llm_budget_exhausted_logged = False
    max_llm_segments_per_run = int(config.classification.max_llm_segments_per_run)

    try:
        total_files = len(current_files)
        for index, (source_ref, source_file) in enumerate(current_files.items(), start=1):
            stats.processed_files += 1
            logger.info("[%s/%s] Inspecting source file: %s", index, total_files, source_file.path)

            last_state = database.get_last_build_state(source_ref)
            rebuild = config.builder.mode == "build-all" or should_rebuild(
                last_state, source_file.content_hash, signature, config.rebuild.enabled
            )
            if not rebuild:
                record_seen(source_file)
                stats.skipped_files += 1
                logger.info("[%s/%s] Skipping unchanged file: %s", index, total_files, source_file.path)
                continue

            try:
                document = parse_markdown(source_file.path) if source_file.extension == ".md" else parse_text(source_file.path)
                sidecar_metadata = (
                    load_sidecar_metadata(
                        source_file.path,
                        config.metadata_internal.get("sidecar_suffix", ".meta.json"),
                    )
                    if config.metadata_internal.get("enable_sidecar_metadata", True)
                    else SourceMetadata()
                )
                document.metadata = merge_metadata(
                    document.metadata if hasattr(document.metadata, "model_dump") else type(sidecar_metadata).model_validate(document.metadata or {}),
                    sidecar_metadata,
                ).model_dump()

                candidates = generate_candidates(document, config.segmentation)
                specificity_scores: dict[str, float] = {}
                selected: list[SelectedSegment] = []

                # Artifact-level LLM counters.
                artifact_llm_attempted = 0
                artifact_llm_fallbacks = 0
                artifact_llm_skipped_by_rules = 0
                artifact_llm_budget_exhausted = False

                default_confidence_floor = float(config.category_internal["default_confidence_floor"])
                for candidate in candidates:
                    # Rule-first classification.
                    rule_labels, rule_llm_result, rule_warnings = infer_categories(
                        candidate,
                        config.classification,
                        llm,
                        ontology,
                        config.category_governance.allow_unknown_labels,
                        default_confidence_floor,
                        allow_llm=False,
                    )

                    threshold_met = False
                    for label in rule_labels:
                        threshold = max(
                            config.classification.min_primary_label_confidence,
                            ontology.confidence_floor_for(label.label, default_confidence_floor),
                        )
                        if label.confidence >= threshold:
                            threshold_met = True
                            break

                    if threshold_met:
                        artifact_llm_skipped_by_rules += 1
                        llm_skipped_by_rules += 1
                        final_labels = rule_labels
                        final_llm_result = rule_llm_result
                        final_warnings = rule_warnings
                    else:
                        # Budget cap check.
                        if config.classification.enable_llm and llm is not None and llm_attempted_segments < max_llm_segments_per_run:
                            llm_attempted_segments += 1
                            artifact_llm_attempted += 1

                            labels, llm_result, warnings = infer_categories(
                                candidate,
                                config.classification,
                                llm,
                                ontology,
                                config.category_governance.allow_unknown_labels,
                                default_confidence_floor,
                                allow_llm=True,
                            )
                            final_labels = labels
                            final_llm_result = llm_result
                            final_warnings = warnings

                            if any(str(w).startswith("local_llm_fallback:") for w in warnings):
                                llm_fallback_segments += 1
                                artifact_llm_fallbacks += 1
                        else:
                            # Budget exhausted or LLM disabled: do rule-only.
                            if config.classification.enable_llm and llm is not None and llm_attempted_segments >= max_llm_segments_per_run:
                                llm_skipped_by_budget += 1
                                artifact_llm_budget_exhausted = True
                                if not llm_budget_exhausted_logged:
                                    logger.info("LLM budget exhausted: max_llm_segments_per_run=%s", max_llm_segments_per_run)
                                    llm_budget_exhausted_logged = True

                            final_labels = rule_labels
                            final_llm_result = rule_llm_result
                            final_warnings = rule_warnings

                    specificity_scores[candidate.segment_hash] = max((label.confidence for label in final_labels), default=0.0)
                    scopes, tags = extract_scopes_and_tags(candidate, final_labels, ontology, final_llm_result.tags)
                    retrieval_class = resolve_retrieval_class(final_labels, ontology)
                    selected.append(
                        SelectedSegment(
                            candidate=candidate,
                            labels=final_labels,
                            retrieval_class=retrieval_class,
                            normalized_retrieval_text=final_llm_result.normalized_retrieval_text or candidate.text,
                            summary=final_llm_result.summary,
                            scopes=scopes,
                            tags=tags,
                            retrieval_profiles=[],
                            confidence=max((label.confidence for label in final_labels), default=0.5),
                            explanation_payload={"rationale": final_llm_result.rationale, "warnings": final_warnings},
                        )
                    )

                filtered_candidates = select_segments_with_duplicate_control(candidates, specificity_scores)
                kept_hashes = {item.segment_hash for item in filtered_candidates}
                selected = [item for item in selected if item.candidate.segment_hash in kept_hashes]

                for item in selected:
                    item.retrieval_profiles = build_retrieval_profiles(item, config.retrieval_profiles)
                    item.case_shape = infer_case_shape(item) if config.builder.enable_case_shape_inference else None
                    for label in item.labels:
                        stats.labels[label.label] += 1
                    stats.retrieval_classes[item.retrieval_class] += 1

                logger.info(
                    "Artifact classification path=%s candidates=%s selected=%s llm_attempted=%s llm_fallbacks=%s llm_skipped_by_rules=%s llm_budget_exhausted=%s",
                    source_file.path.as_posix(),
                    len(candidates),
                    len(selected),
                    artifact_llm_attempted,
                    artifact_llm_fallbacks,
                    artifact_llm_skipped_by_rules,
                    str(artifact_llm_budget_exhausted).lower(),
                )

                item_count = len(selected)
                def embed_item(index_: int, item: SelectedSegment) -> dict[str, object]:
                    texts_to_embed: list[str] = [item.normalized_retrieval_text]
                    want_summary = bool(item.summary)
                    if want_summary:
                        texts_to_embed.append(item.summary or item.normalized_retrieval_text)

                    profile_types: list[str] = []
                    profile_texts: list[str] = []
                    for profile_type, profile_text in item.retrieval_profiles:
                        if profile_type == "summary":
                            continue
                        if profile_type == "default" and profile_text == item.normalized_retrieval_text:
                            continue
                        profile_types.append(profile_type)
                        profile_texts.append(profile_text)
                    if profile_texts:
                        texts_to_embed.extend(profile_texts)

                    vectors = embedding_provider.embed(texts_to_embed)

                    normalized_vector = vectors[0]
                    summary_vector = vectors[1] if want_summary else None

                    profile_vectors: dict[str, list[float]] = {}
                    if profile_texts:
                        profile_start = 1 + (1 if want_summary else 0)
                        for i, profile_type in enumerate(profile_types):
                            profile_vectors[profile_type] = vectors[profile_start + i]

                    return {
                        "normalized_vector": normalized_vector,
                        "summary_vector": summary_vector,
                        "profile_vectors": profile_vectors,
                    }

                replace_stream_fn = getattr(database, "replace_artifact_knowledge_stream", None)
                if replace_stream_fn is not None:
                    write_result = replace_stream_fn(run_ctx, source_file, selected, embed_item) or {}
                else:
                    # Compatibility fallback for test doubles without streaming API.
                    embeddings: dict[str, list[list[float]]] = {"normalized_retrieval_text": []}
                    if selected:
                        embeddings["normalized_retrieval_text"] = embedding_provider.embed([item.normalized_retrieval_text for item in selected])
                    if selected and any(item.summary for item in selected):
                        embeddings["summary"] = embedding_provider.embed([item.summary or item.normalized_retrieval_text for item in selected])

                    profile_text_items: list[tuple[int, str, str]] = []
                    profile_texts_to_embed: list[str] = []
                    for item_index, item in enumerate(selected):
                        for profile_type, profile_text in item.retrieval_profiles:
                            if profile_type == "summary":
                                continue
                            if profile_type == "default" and profile_text == item.normalized_retrieval_text:
                                continue
                            profile_text_items.append((item_index, profile_type, profile_text))
                            profile_texts_to_embed.append(profile_text)

                    vectors = embedding_provider.embed(profile_texts_to_embed) if profile_texts_to_embed else []
                    profile_embedding_rows: list[dict[str, list[float] | str | int]] = []
                    for (item_index, profile_type, profile_text), vector in zip(profile_text_items, vectors):
                        profile_embedding_rows.append(
                            {
                                "item_index": item_index,
                                "profile_type": profile_type,
                                "profile_text": profile_text,
                                "vector": vector,
                            }
                        )
                    embeddings["profile_rows"] = profile_embedding_rows

                    write_result = database.replace_artifact_knowledge(run_ctx, source_file, selected, embeddings) or {}

                source_artifact_id = write_result["source_artifact_id"]
                superseded_count = int(write_result.get("superseded_count", 0))
                created_count = int(write_result.get("created_count", item_count))

                visibility = database.verify_artifact_visibility(source_artifact_id)
                visible_items = int(visibility.get("visible_items", 0))
                visible_embeddings = int(visibility.get("visible_embeddings", 0))
                if visible_items == 0:
                    raise RuntimeError(f"Post-write verification failed: visible_items=0 for source_artifact_id={source_artifact_id}")

                stats.created_items += created_count
                stats.superseded_items += superseded_count

                logger.info(
                    "Stored artifact path=%s items=%s superseded=%s labels=%s profiles=%s embeddings=%s visible_items=%s visible_embeddings=%s",
                    source_file.path.as_posix(),
                    item_count,
                    superseded_count,
                    int(visibility.get("labels", 0)),
                    int(visibility.get("profiles", 0)),
                    int(visibility.get("embeddings", 0)),
                    visible_items,
                    visible_embeddings,
                )

            except Exception as exc:
                logger.exception("Artifact failed: %s", source_file.path)
                database.record_artifact_failure(source_ref, source_file.content_hash, exc, run_ctx)
                stats.failed_files += 1
                continue

        if config.sync.reconcile_removed_files and config.sync.archive_removed_artifacts and archive_removed:
            removed_refs = sorted(set(known_artifacts) - set(current_files))
            if removed_refs:
                logger.info("Reconciling %s removed artifact(s).", len(removed_refs))
            for source_ref in removed_refs:
                artifact = known_artifacts[source_ref]
                if artifact.get("status") == "removed":
                    continue
                archived_count = archive_removed(artifact["source_artifact_id"], run_ctx)
                stats.reconciled_removed_files += 1
                stats.archived_items += archived_count
                logger.info("Archived removed artifact %s with %s archived item(s).", artifact["source_path"], archived_count)

        final_status = "completed" if stats.failed_files == 0 else "partial"
        database.finalize_ingestion_run(run_ctx.run_id, final_status, stats.as_dict())

        logger.info(
            "Run completed status=%s processed=%s skipped=%s failed=%s created_items=%s superseded_items=%s archived_items=%s llm_attempted_segments=%s llm_fallbacks=%s llm_skipped_by_rules=%s llm_skipped_by_budget=%s",
            final_status,
            stats.processed_files,
            stats.skipped_files,
            stats.failed_files,
            stats.created_items,
            stats.superseded_items,
            stats.archived_items,
            llm_attempted_segments,
            llm_fallback_segments,
            llm_skipped_by_rules,
            llm_skipped_by_budget,
        )

        totals = database.get_database_totals()
        logger.info(
            "Database totals current_database=%s source_artifacts=%s active_items=%s embeddings=%s ingestion_runs=%s",
            totals.get("current_database"),
            totals.get("source_artifacts"),
            totals.get("active_items"),
            totals.get("embeddings"),
            totals.get("ingestion_runs"),
        )
        return stats

    except Exception:
        database.finalize_ingestion_run(run_ctx.run_id, "failed", stats.as_dict())
        logger.exception("Ingestion run %s failed.", run_ctx.run_id)
        raise
