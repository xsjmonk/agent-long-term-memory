from __future__ import annotations

import json
from contextlib import contextmanager
from datetime import UTC, datetime
from pathlib import Path
from typing import Any
from uuid import UUID

from app.models import AppConfig, IngestionRunContext, SelectedSegment, SourceFile, new_uuid
from app.path_utils import normalize_fs_path, path_is_under_roots


def utc_now() -> datetime:
    return datetime.now(UTC)


class Database:
    def __init__(self, config: AppConfig, connection_factory=None) -> None:
        self.config = config
        self.connection_factory = connection_factory or self._default_connect

    def _default_connect(self):
        import psycopg

        return psycopg.connect(
            host=self.config.database.host,
            port=self.config.database.port,
            dbname=self.config.database.database,
            user=self.config.database.username,
            password=self.config.database.password,
            sslmode=self.config.database.ssl_mode,
        )

    @contextmanager
    def transaction(self):
        conn = self.connection_factory()
        try:
            yield conn
            conn.commit()
        except Exception:
            conn.rollback()
            raise
        finally:
            conn.close()

    def create_ingestion_run(self, embedding_model_name: str, embedding_model_version: str | None) -> IngestionRunContext:
        run_id = new_uuid()
        started_at = utc_now()
        with self.transaction() as conn:
            with conn.cursor() as cur:
                cur.execute(
                    """
                    insert into ingestion_runs (
                        id, builder_version, classifier_version, embedding_model,
                        embedding_model_version, normalization_version, ontology_version,
                        schema_compatibility_version, started_at, status
                    ) values (%s, %s, %s, %s, %s, %s, %s, %s, %s, %s)
                    """,
                    (
                        run_id,
                        self.config.builder.builder_version,
                        self.config.builder.classifier_version,
                        embedding_model_name,
                        embedding_model_version,
                        self.config.builder.normalization_version,
                        getattr(self.config, "_ontology_version", "unknown"),
                        self.config.builder.schema_compatibility_version,
                        started_at,
                        "running",
                    ),
                )
        return IngestionRunContext(run_id, started_at, embedding_model_name, embedding_model_version, getattr(self.config, "_ontology_version", "unknown"))

    def finalize_ingestion_run(self, run_id: UUID, status: str, notes: dict[str, Any]) -> None:
        with self.transaction() as conn:
            with conn.cursor() as cur:
                cur.execute(
                    "update ingestion_runs set finished_at = %s, status = %s, notes = %s where id = %s",
                    (utc_now(), status, json.dumps(notes), run_id),
                )

    def get_last_build_state(self, source_ref: str) -> dict[str, str | None] | None:
        with self.transaction() as conn:
            with conn.cursor() as cur:
                cur.execute(
                    """
                    select abs.artifact_hash, abs.builder_version, abs.classifier_version,
                           abs.embedding_model_version, abs.normalization_version,
                           abs.ontology_version, abs.schema_compatibility_version, abs.build_status
                    from artifact_build_states abs
                    join source_artifacts sa on sa.id = abs.source_artifact_id
                    where sa.source_ref = %s
                    """,
                    (source_ref,),
                )
                row = cur.fetchone()
        if not row:
            return None
        return {
            "artifact_hash": row[0],
            "builder_version": row[1],
            "classifier_version": row[2],
            "embedding_model_version": row[3],
            "normalization_version": row[4],
            "ontology_version": row[5],
            "schema_compatibility_version": row[6],
            "build_status": row[7],
        }

    def upsert_source_artifact(self, conn, source_file: SourceFile, first_segment: SelectedSegment | None = None) -> UUID:
        source_artifact_id = new_uuid()
        metadata = first_segment.candidate.metadata if first_segment else None
        artifact_title = (
            metadata.title if metadata and metadata.title else first_segment.candidate.heading_path[0] if first_segment and first_segment.candidate.heading_path else source_file.path.stem
        )
        artifact_source_type = metadata.source_type if metadata and metadata.source_type else ("markdown" if source_file.extension == ".md" else "text")
        with conn.cursor() as cur:
            cur.execute(
                """
                insert into source_artifacts (
                    id, source_type, source_ref, source_path, title, artifact_hash, observed_at, created_at, updated_at
                    , status, removed_at, last_seen_at
                ) values (%s, %s, %s, %s, %s, %s, %s, %s, %s, %s, %s, %s)
                on conflict (source_ref) do update
                set source_type = excluded.source_type,
                    source_path = excluded.source_path,
                    title = excluded.title,
                    status = 'active',
                    removed_at = null,
                    last_seen_at = excluded.last_seen_at,
                    artifact_hash = excluded.artifact_hash,
                    observed_at = excluded.observed_at,
                    updated_at = excluded.updated_at
                returning id
                """,
                (
                    source_artifact_id,
                    artifact_source_type,
                    source_file.path.as_posix(),
                    source_file.path.as_posix(),
                    artifact_title,
                    source_file.content_hash,
                    source_file.modified_at,
                    utc_now(),
                    utc_now(),
                    "active",
                    None,
                    utc_now(),
                ),
            )
            existing = cur.fetchone()
        return existing[0]

    def record_artifact_seen(self, source_file: SourceFile) -> UUID:
        with self.transaction() as conn:
            return self.upsert_source_artifact(conn, source_file)

    def reactivate_existing_artifact_if_returned(self, source_file: SourceFile) -> UUID:
        return self.record_artifact_seen(source_file)

    def list_known_artifacts_for_roots(self, input_roots: list[str]) -> list[dict[str, str | UUID | None]]:
        with self.transaction() as conn:
            with conn.cursor() as cur:
                cur.execute(
                    """
                    select id, source_ref, source_path, status, artifact_hash
                    from source_artifacts
                    """
                )
                rows = cur.fetchall() or []
        artifacts: list[dict[str, str | UUID | None]] = []
        for row in rows:
            source_path = row[2]
            if not source_path or not path_is_under_roots(source_path, input_roots):
                continue
            artifacts.append(
                {
                    "source_artifact_id": row[0],
                    "source_ref": row[1],
                    "source_path": normalize_fs_path(source_path).as_posix(),
                    "status": row[3],
                    "artifact_hash": row[4],
                }
            )
        return artifacts

    def replace_artifact_knowledge(
        self,
        run_ctx: IngestionRunContext,
        source_file: SourceFile,
        segments: list[SelectedSegment],
        embeddings: dict[str, list[list[float]]],
    ) -> dict[str, UUID | int]:
        with self.transaction() as conn:
            source_artifact_id = self.upsert_source_artifact(conn, source_file, segments[0] if segments else None)
            replaced_at = utc_now()
            with conn.cursor() as cur:
                cur.execute(
                    """
                    update knowledge_items
                    set status = 'superseded', valid_to = %s, updated_at = %s
                    where source_artifact_id = %s and status = 'active'
                    """,
                    (replaced_at, replaced_at, source_artifact_id),
                )
                superseded_count = cur.rowcount or 0
                for index, item in enumerate(segments):
                    item_id = new_uuid()
                    source_segment_id = new_uuid()
                    metadata = item.candidate.metadata
                    cur.execute(
                        """
                        insert into source_segments (
                            id, source_artifact_id, heading_path, start_line, end_line, span_level, segment_hash, raw_text
                        ) values (%s, %s, %s::jsonb, %s, %s, %s, %s, %s)
                        """,
                        (
                            source_segment_id,
                            source_artifact_id,
                            json.dumps(item.candidate.heading_path),
                            item.candidate.start_line,
                            item.candidate.end_line,
                            item.candidate.span_level,
                            item.candidate.segment_hash,
                            item.candidate.text if self.config.builder.write_raw_text_to_db else None,
                        ),
                    )
                    cur.execute(
                        """
                        insert into knowledge_items (
                            id, retrieval_class, title, summary, details, normalized_retrieval_text, span_level,
                            authority_level, authority_label, status, confidence, domain, module, feature,
                            source_type, parent_item_id, created_at, updated_at, ingestion_run_id, source_artifact_id
                        ) values (%s, %s, %s, %s, %s, %s, %s, %s, %s, %s, %s, %s, %s, %s, %s, %s, %s, %s, %s, %s)
                        """,
                        (
                            item_id,
                            item.retrieval_class,
                            metadata.title or item.candidate.heading_path[-1],
                            item.summary,
                            item.candidate.text if self.config.builder.write_raw_text_to_db else None,
                            item.normalized_retrieval_text,
                            item.candidate.span_level,
                            3,
                            metadata.authority_label or self.config.builder.default_authority_label,
                            metadata.status or self.config.builder.default_status,
                            item.confidence,
                            metadata.domain or (item.candidate.heading_path[0].lower().replace(" ", "-") if item.candidate.heading_path else None),
                            metadata.module or (item.candidate.heading_path[1].lower().replace(" ", "-") if len(item.candidate.heading_path) > 1 else None),
                            metadata.feature or (item.candidate.heading_path[-1].lower().replace(" ", "-") if item.candidate.heading_path else None),
                            metadata.source_type or item.candidate.source_type,
                            item.parent_item_id,
                            utc_now(),
                            utc_now(),
                            run_ctx.run_id,
                            source_artifact_id,
                        ),
                    )
                    cur.execute(
                        "insert into knowledge_item_segments (knowledge_item_id, source_segment_id, role) values (%s, %s, %s)",
                        (item_id, source_segment_id, "primary_origin"),
                    )
                    for label in item.labels:
                        cur.execute(
                            """
                            insert into knowledge_labels (id, knowledge_item_id, label, label_role, confidence, source_method, created_at)
                            values (%s, %s, %s, %s, %s, %s, %s)
                            """,
                            (new_uuid(), item_id, label.label, label.label_role, label.confidence, label.source_method, utc_now()),
                        )
                    for scope_type, scope_value, weight in item.scopes:
                        cur.execute(
                            "insert into knowledge_scopes (id, knowledge_item_id, scope_type, scope_value, weight) values (%s, %s, %s, %s, %s)",
                            (new_uuid(), item_id, scope_type, scope_value, weight),
                        )
                    for tag in item.tags:
                        cur.execute(
                            "insert into knowledge_tags (id, knowledge_item_id, tag, tag_source) values (%s, %s, %s, %s)",
                            (new_uuid(), item_id, tag, "builder"),
                        )
                    profile_id_map: dict[str, UUID] = {}
                    for profile_type, profile_text in item.retrieval_profiles:
                        profile_id = new_uuid()
                        profile_id_map[profile_type] = profile_id
                        cur.execute(
                            "insert into retrieval_profiles (id, knowledge_item_id, profile_type, profile_text, created_at) values (%s, %s, %s, %s, %s)",
                            (profile_id, item_id, profile_type, profile_text, utc_now()),
                        )
                    vector = embeddings["normalized_retrieval_text"][index]
                    cur.execute(
                        """
                        insert into knowledge_embeddings (
                            id, knowledge_item_id, profile_id, embedding_role, embedding_text, embedding, model_name, model_version, created_at
                        ) values (%s, %s, %s, %s, %s, %s, %s, %s, %s)
                        """,
                        (
                            new_uuid(),
                            item_id,
                            None,
                            "normalized_retrieval_text",
                            item.normalized_retrieval_text,
                            vector,
                            run_ctx.embedding_model_name,
                            run_ctx.embedding_model_version,
                            utc_now(),
                        ),
                    )
                    if item.summary and index < len(embeddings.get("summary", [])):
                        cur.execute(
                            """
                            insert into knowledge_embeddings (
                                id, knowledge_item_id, profile_id, embedding_role, embedding_text, embedding, model_name, model_version, created_at
                            ) values (%s, %s, %s, %s, %s, %s, %s, %s, %s)
                            """,
                            (new_uuid(), item_id, None, "summary", item.summary, embeddings["summary"][index], run_ctx.embedding_model_name, run_ctx.embedding_model_version, utc_now()),
                        )
                    for profile_row in embeddings.get("profile_rows", []):
                        if profile_row["item_index"] != index:
                            continue
                        profile_type = profile_row["profile_type"]
                        profile_text = profile_row["profile_text"]
                        profile_id = profile_id_map.get(profile_type)
                        if profile_id:
                            cur.execute(
                                """
                                insert into knowledge_embeddings (
                                    id, knowledge_item_id, profile_id, embedding_role, embedding_text, embedding, model_name, model_version, created_at
                                ) values (%s, %s, %s, %s, %s, %s, %s, %s, %s)
                                """,
                                (new_uuid(), item_id, profile_id, profile_type, profile_text, profile_row["vector"], run_ctx.embedding_model_name, run_ctx.embedding_model_version, utc_now()),
                            )
                    if item.case_shape:
                        cur.execute(
                            """
                            insert into case_shapes (
                                id, knowledge_item_id, task_type, feature_shape, engine_change_allowed, likely_layers, risk_signals, complexity
                            ) values (%s, %s, %s, %s, %s, %s::jsonb, %s::jsonb, %s)
                            """,
                            (
                                new_uuid(),
                                item_id,
                                item.case_shape.get("task_type"),
                                item.case_shape.get("feature_shape"),
                                item.case_shape.get("engine_change_allowed"),
                                json.dumps(item.case_shape.get("likely_layers", [])),
                                json.dumps(item.case_shape.get("risk_signals", [])),
                                item.case_shape.get("complexity"),
                            ),
                        )
            self._update_build_state(conn, source_artifact_id, source_file.content_hash, run_ctx)
            return {
                "source_artifact_id": source_artifact_id,
                "superseded_count": superseded_count,
                "created_count": len(segments),
            }

    def verify_required_schema(self) -> dict[str, Any]:
        """
        Verify that the configured schema contains all required core tables.
        Fails fast if any required table is missing.
        """

        required_tables = {
            "knowledge_items",
            "knowledge_labels",
            "knowledge_scopes",
            "knowledge_tags",
            "source_artifacts",
            "source_segments",
            "knowledge_item_segments",
            "retrieval_profiles",
            "knowledge_embeddings",
            "ingestion_runs",
            "artifact_build_states",
        }

        schema = self.config.database.db_schema
        with self.transaction() as conn:
            with conn.cursor() as cur:
                cur.execute("select current_database(), current_user, current_schema()")
                row = cur.fetchone()
                if not row:
                    raise RuntimeError("Database schema verification failed: could not query current_database/current_user/current_schema().")

                current_database, current_user, current_schema = row[0], row[1], row[2]

                cur.execute(
                    """
                    select table_name
                    from information_schema.tables
                    where table_schema = %s
                    """,
                    (schema,),
                )
                present = cur.fetchall() or []
                present_tables = {r[0] for r in present if r and len(r) > 0}

        missing = sorted(required_tables - present_tables)
        result = {
            "database_name": str(current_database),
            "current_user": str(current_user),
            "current_schema": str(current_schema),
            "missing_tables": missing,
            "present_tables_count": len(present_tables),
            "host": str(self.config.database.host),
            "port": int(self.config.database.port),
            "schema": str(schema),
        }
        if missing:
            missing_txt = ", ".join(missing)
            raise RuntimeError(
                "Database schema verification failed: "
                f"host={result['host']} port={result['port']} database={result['database_name']} schema={result['schema']} "
                f"missing_tables=[{missing_txt}]"
            )
        return result

    def verify_artifact_visibility(self, source_artifact_id: UUID) -> dict[str, int]:
        """
        Post-write verification: count rows that should be visible for the given artifact.
        """

        schema = self.config.database.db_schema
        with self.transaction() as conn:
            with conn.cursor() as cur:
                cur.execute(
                    f"select count(*) from {schema}.knowledge_items where source_artifact_id = %s and status = 'active'",
                    (source_artifact_id,),
                )
                active_items = int(cur.fetchone()[0])

                cur.execute(
                    """
                    select
                      (select count(*)
                       from knowledge_labels kl
                       join knowledge_items ki on ki.id = kl.knowledge_item_id
                       where ki.source_artifact_id = %s and ki.status = 'active') as label_count,
                      (select count(*)
                       from knowledge_scopes ks
                       join knowledge_items ki on ki.id = ks.knowledge_item_id
                       where ki.source_artifact_id = %s and ki.status = 'active') as scope_count,
                      (select count(*)
                       from knowledge_tags kt
                       join knowledge_items ki on ki.id = kt.knowledge_item_id
                       where ki.source_artifact_id = %s and ki.status = 'active') as tag_count,
                      (select count(*)
                       from retrieval_profiles rp
                       join knowledge_items ki on ki.id = rp.knowledge_item_id
                       where ki.source_artifact_id = %s and ki.status = 'active') as profile_count,
                      (select count(*)
                       from knowledge_embeddings ke
                       join knowledge_items ki on ki.id = ke.knowledge_item_id
                       where ki.source_artifact_id = %s and ki.status = 'active') as embedding_count
                    """,
                    (source_artifact_id, source_artifact_id, source_artifact_id, source_artifact_id, source_artifact_id),
                )
                label_count, scope_count, tag_count, profile_count, embedding_count = (cur.fetchone() or (0, 0, 0, 0, 0))

                cur.execute(
                    f"""
                    select count(distinct ss.id)
                    from source_segments ss
                    join knowledge_item_segments kis on kis.source_segment_id = ss.id
                    join knowledge_items ki on ki.id = kis.knowledge_item_id
                    where ki.source_artifact_id = %s and ki.status = 'active'
                    """,
                    (source_artifact_id,),
                )
                source_segment_count = int(cur.fetchone()[0])

        return {
            "active_items": active_items,
            "labels": int(label_count),
            "scopes": int(scope_count),
            "tags": int(tag_count),
            "profiles": int(profile_count),
            "embeddings": int(embedding_count),
            "source_segments": source_segment_count,
            # Compatibility keys for pipeline checks.
            "visible_items": active_items,
            "visible_embeddings": int(embedding_count),
        }

    def get_database_totals(self) -> dict[str, Any]:
        """
        End-of-run DB totals for operator visibility.
        """

        schema = self.config.database.db_schema
        with self.transaction() as conn:
            with conn.cursor() as cur:
                cur.execute("select current_database()")
                current_database = cur.fetchone()[0]

                cur.execute("select count(*) from source_artifacts")
                source_artifacts_count = int(cur.fetchone()[0])

                cur.execute(f"select count(*) from {schema}.knowledge_items where status = 'active'")
                active_items_count = int(cur.fetchone()[0])

                cur.execute("select count(*) from knowledge_embeddings")
                embeddings_count = int(cur.fetchone()[0])

                cur.execute("select count(*) from ingestion_runs")
                ingestion_runs_count = int(cur.fetchone()[0])

        return {
            "current_database": str(current_database),
            "source_artifacts": source_artifacts_count,
            "active_items": active_items_count,
            "embeddings": embeddings_count,
            "ingestion_runs": ingestion_runs_count,
        }

    def replace_artifact_knowledge_stream(
        self,
        run_ctx: IngestionRunContext,
        source_file: SourceFile,
        segments: list[SelectedSegment],
        embed_item_fn: Any,
    ) -> dict[str, UUID | int]:
        """
        Memory-friendly variant: calls embed_item_fn(index, item) to get vectors for the
        current item only, then inserts immediately within the same transaction.

        embed_item_fn must return a dict with:
          - normalized_vector: list[float]
          - summary_vector: list[float] | None
          - profile_vectors: dict[str, list[float]] mapping profile_type -> vector
        """

        with self.transaction() as conn:
            source_artifact_id = self.upsert_source_artifact(conn, source_file, segments[0] if segments else None)
            replaced_at = utc_now()
            with conn.cursor() as cur:
                cur.execute(
                    """
                    update knowledge_items
                    set status = 'superseded', valid_to = %s, updated_at = %s
                    where source_artifact_id = %s and status = 'active'
                    """,
                    (replaced_at, replaced_at, source_artifact_id),
                )
                superseded_count = cur.rowcount or 0

                for index, item in enumerate(segments):
                    vectors = embed_item_fn(index, item)
                    item_id = new_uuid()
                    source_segment_id = new_uuid()
                    metadata = item.candidate.metadata

                    cur.execute(
                        """
                        insert into source_segments (
                            id, source_artifact_id, heading_path, start_line, end_line, span_level, segment_hash, raw_text
                        ) values (%s, %s, %s::jsonb, %s, %s, %s, %s, %s)
                        """,
                        (
                            source_segment_id,
                            source_artifact_id,
                            json.dumps(item.candidate.heading_path),
                            item.candidate.start_line,
                            item.candidate.end_line,
                            item.candidate.span_level,
                            item.candidate.segment_hash,
                            item.candidate.text if self.config.builder.write_raw_text_to_db else None,
                        ),
                    )
                    cur.execute(
                        """
                        insert into knowledge_items (
                            id, retrieval_class, title, summary, details, normalized_retrieval_text, span_level,
                            authority_level, authority_label, status, confidence, domain, module, feature,
                            source_type, parent_item_id, created_at, updated_at, ingestion_run_id, source_artifact_id
                        ) values (%s, %s, %s, %s, %s, %s, %s, %s, %s, %s, %s, %s, %s, %s, %s, %s, %s, %s, %s, %s)
                        """,
                        (
                            item_id,
                            item.retrieval_class,
                            metadata.title or item.candidate.heading_path[-1],
                            item.summary,
                            item.candidate.text if self.config.builder.write_raw_text_to_db else None,
                            item.normalized_retrieval_text,
                            item.candidate.span_level,
                            3,
                            metadata.authority_label or self.config.builder.default_authority_label,
                            metadata.status or self.config.builder.default_status,
                            item.confidence,
                            metadata.domain or (item.candidate.heading_path[0].lower().replace(" ", "-") if item.candidate.heading_path else None),
                            metadata.module or (item.candidate.heading_path[1].lower().replace(" ", "-") if len(item.candidate.heading_path) > 1 else None),
                            metadata.feature or (item.candidate.heading_path[-1].lower().replace(" ", "-") if item.candidate.heading_path else None),
                            metadata.source_type or item.candidate.source_type,
                            item.parent_item_id,
                            utc_now(),
                            utc_now(),
                            run_ctx.run_id,
                            source_artifact_id,
                        ),
                    )
                    cur.execute(
                        "insert into knowledge_item_segments (knowledge_item_id, source_segment_id, role) values (%s, %s, %s)",
                        (item_id, source_segment_id, "primary_origin"),
                    )
                    for label in item.labels:
                        cur.execute(
                            """
                            insert into knowledge_labels (id, knowledge_item_id, label, label_role, confidence, source_method, created_at)
                            values (%s, %s, %s, %s, %s, %s, %s)
                            """,
                            (new_uuid(), item_id, label.label, label.label_role, label.confidence, label.source_method, utc_now()),
                        )
                    for scope_type, scope_value, weight in item.scopes:
                        cur.execute(
                            "insert into knowledge_scopes (id, knowledge_item_id, scope_type, scope_value, weight) values (%s, %s, %s, %s, %s)",
                            (new_uuid(), item_id, scope_type, scope_value, weight),
                        )
                    for tag in item.tags:
                        cur.execute(
                            "insert into knowledge_tags (id, knowledge_item_id, tag, tag_source) values (%s, %s, %s, %s)",
                            (new_uuid(), item_id, tag, "builder"),
                        )

                    profile_id_map: dict[str, UUID] = {}
                    for profile_type, profile_text in item.retrieval_profiles:
                        profile_id = new_uuid()
                        profile_id_map[profile_type] = profile_id
                        cur.execute(
                            "insert into retrieval_profiles (id, knowledge_item_id, profile_type, profile_text, created_at) values (%s, %s, %s, %s, %s)",
                            (profile_id, item_id, profile_type, profile_text, utc_now()),
                        )

                    cur.execute(
                        """
                        insert into knowledge_embeddings (
                            id, knowledge_item_id, profile_id, embedding_role, embedding_text, embedding, model_name, model_version, created_at
                        ) values (%s, %s, %s, %s, %s, %s, %s, %s, %s)
                        """,
                        (
                            new_uuid(),
                            item_id,
                            None,
                            "normalized_retrieval_text",
                            item.normalized_retrieval_text,
                            vectors["normalized_vector"],
                            run_ctx.embedding_model_name,
                            run_ctx.embedding_model_version,
                            utc_now(),
                        ),
                    )
                    if vectors.get("summary_vector") is not None:
                        cur.execute(
                            """
                            insert into knowledge_embeddings (
                                id, knowledge_item_id, profile_id, embedding_role, embedding_text, embedding, model_name, model_version, created_at
                            ) values (%s, %s, %s, %s, %s, %s, %s, %s, %s)
                            """,
                            (new_uuid(), item_id, None, "summary", item.summary, vectors["summary_vector"], run_ctx.embedding_model_name, run_ctx.embedding_model_version, utc_now()),
                        )

                    for profile_type, profile_text in item.retrieval_profiles:
                        if profile_type == "summary":
                            continue
                        if profile_type == "default" and profile_text == item.normalized_retrieval_text:
                            continue
                        vec = vectors.get("profile_vectors", {}).get(profile_type)
                        if vec is None:
                            continue
                        profile_id = profile_id_map.get(profile_type)
                        if not profile_id:
                            continue
                        cur.execute(
                            """
                            insert into knowledge_embeddings (
                                id, knowledge_item_id, profile_id, embedding_role, embedding_text, embedding, model_name, model_version, created_at
                            ) values (%s, %s, %s, %s, %s, %s, %s, %s, %s)
                            """,
                            (
                                new_uuid(),
                                item_id,
                                profile_id,
                                profile_type,
                                profile_text,
                                vec,
                                run_ctx.embedding_model_name,
                                run_ctx.embedding_model_version,
                                utc_now(),
                            ),
                        )

                    if item.case_shape:
                        cur.execute(
                            """
                            insert into case_shapes (
                                id, knowledge_item_id, task_type, feature_shape, engine_change_allowed, likely_layers, risk_signals, complexity
                            ) values (%s, %s, %s, %s, %s, %s::jsonb, %s::jsonb, %s)
                            """,
                            (
                                new_uuid(),
                                item_id,
                                item.case_shape.get("task_type"),
                                item.case_shape.get("feature_shape"),
                                item.case_shape.get("engine_change_allowed"),
                                json.dumps(item.case_shape.get("likely_layers", [])),
                                json.dumps(item.case_shape.get("risk_signals", [])),
                                item.case_shape.get("complexity"),
                            ),
                        )

            self._update_build_state(conn, source_artifact_id, source_file.content_hash, run_ctx)
            return {
                "source_artifact_id": source_artifact_id,
                "superseded_count": superseded_count,
                "created_count": len(segments),
            }

    def _update_build_state(self, conn, source_artifact_id: UUID, artifact_hash: str, run_ctx: IngestionRunContext) -> None:
        with conn.cursor() as cur:
            cur.execute(
                """
                insert into artifact_build_states (
                    source_artifact_id, last_ingestion_run_id, artifact_hash, builder_version,
                    classifier_version, embedding_model_version, normalization_version, ontology_version,
                    schema_compatibility_version, build_status, failure_details, last_built_at, last_reconciled_at, notes
                ) values (%s, %s, %s, %s, %s, %s, %s, %s, %s, %s, %s, %s, %s, %s)
                on conflict (source_artifact_id) do update
                set last_ingestion_run_id = excluded.last_ingestion_run_id,
                    artifact_hash = excluded.artifact_hash,
                    builder_version = excluded.builder_version,
                    classifier_version = excluded.classifier_version,
                    embedding_model_version = excluded.embedding_model_version,
                    normalization_version = excluded.normalization_version,
                    ontology_version = excluded.ontology_version,
                    schema_compatibility_version = excluded.schema_compatibility_version,
                    build_status = excluded.build_status,
                    failure_details = excluded.failure_details,
                    last_built_at = excluded.last_built_at,
                    last_reconciled_at = excluded.last_reconciled_at,
                    notes = excluded.notes
                """,
                (
                    source_artifact_id,
                    run_ctx.run_id,
                    artifact_hash,
                    self.config.builder.builder_version,
                    self.config.builder.classifier_version,
                    run_ctx.embedding_model_version,
                    self.config.builder.normalization_version,
                    run_ctx.ontology_version,
                    self.config.builder.schema_compatibility_version,
                    "succeeded",
                    None,
                    utc_now(),
                    None,
                    None,
                ),
            )

    def record_artifact_failure(self, source_ref: str, artifact_hash: str, error: Exception, run_ctx: IngestionRunContext) -> None:
        payload = {
            "error_type": type(error).__name__,
            "message": str(error),
            "source_ref": source_ref,
            "failed_at": utc_now().isoformat(),
        }
        with self.transaction() as conn:
            with conn.cursor() as cur:
                cur.execute("select id from source_artifacts where source_ref = %s", (source_ref,))
                row = cur.fetchone()
                source_artifact_id = row[0] if row else new_uuid()
                if not row:
                    cur.execute(
                        """
                        insert into source_artifacts (id, source_type, source_ref, source_path, title, artifact_hash, created_at, updated_at, status, removed_at, last_seen_at)
                        values (%s, %s, %s, %s, %s, %s, %s, %s, %s, %s, %s)
                        """,
                        (source_artifact_id, "text" if source_ref.endswith(".txt") else "markdown", source_ref, source_ref, Path(source_ref).stem, artifact_hash, utc_now(), utc_now(), "active", None, utc_now()),
                    )
                cur.execute(
                    """
                    insert into artifact_build_states (
                        source_artifact_id, last_ingestion_run_id, artifact_hash, builder_version,
                        classifier_version, embedding_model_version, normalization_version, ontology_version,
                        schema_compatibility_version, build_status, failure_details, last_built_at, last_reconciled_at, notes
                    ) values (%s, %s, %s, %s, %s, %s, %s, %s, %s, %s, %s::jsonb, %s, %s, %s::jsonb)
                    on conflict (source_artifact_id) do update
                    set last_ingestion_run_id = excluded.last_ingestion_run_id,
                        build_status = excluded.build_status,
                        failure_details = excluded.failure_details,
                        last_built_at = excluded.last_built_at,
                        last_reconciled_at = excluded.last_reconciled_at,
                        notes = excluded.notes
                    """,
                    (
                        source_artifact_id,
                        run_ctx.run_id,
                        artifact_hash,
                        self.config.builder.builder_version,
                        self.config.builder.classifier_version,
                        run_ctx.embedding_model_version,
                        self.config.builder.normalization_version,
                        run_ctx.ontology_version,
                        self.config.builder.schema_compatibility_version,
                        "failed",
                        json.dumps(payload),
                        utc_now(),
                        None,
                        None,
                    ),
                )

    def archive_removed_artifact(self, source_artifact_id: UUID, run_ctx: IngestionRunContext) -> int:
        archived_at = utc_now()
        with self.transaction() as conn:
            with conn.cursor() as cur:
                cur.execute(
                    """
                    update source_artifacts
                    set status = 'removed', removed_at = %s, updated_at = %s
                    where id = %s
                    """,
                    (archived_at, archived_at, source_artifact_id),
                )
                cur.execute(
                    """
                    update knowledge_items
                    set status = 'archived', valid_to = %s, updated_at = %s
                    where source_artifact_id = %s and status = 'active'
                    """,
                    (archived_at, archived_at, source_artifact_id),
                )
                archived_count = cur.rowcount or 0
                cur.execute(
                    """
                    select artifact_hash from artifact_build_states where source_artifact_id = %s
                    """,
                    (source_artifact_id,),
                )
                row = cur.fetchone()
                artifact_hash = row[0] if row else ""
                cur.execute(
                    """
                    insert into artifact_build_states (
                        source_artifact_id, last_ingestion_run_id, artifact_hash, builder_version,
                        classifier_version, embedding_model_version, normalization_version, ontology_version,
                        schema_compatibility_version, build_status, failure_details, last_built_at, last_reconciled_at, notes
                    ) values (%s, %s, %s, %s, %s, %s, %s, %s, %s, %s, %s, %s, %s, %s::jsonb)
                    on conflict (source_artifact_id) do update
                    set last_ingestion_run_id = excluded.last_ingestion_run_id,
                        artifact_hash = excluded.artifact_hash,
                        builder_version = excluded.builder_version,
                        classifier_version = excluded.classifier_version,
                        embedding_model_version = excluded.embedding_model_version,
                        normalization_version = excluded.normalization_version,
                        ontology_version = excluded.ontology_version,
                        schema_compatibility_version = excluded.schema_compatibility_version,
                        build_status = excluded.build_status,
                        failure_details = excluded.failure_details,
                        last_reconciled_at = excluded.last_reconciled_at,
                        notes = excluded.notes
                    """,
                    (
                        source_artifact_id,
                        run_ctx.run_id,
                        artifact_hash,
                        self.config.builder.builder_version,
                        self.config.builder.classifier_version,
                        run_ctx.embedding_model_version,
                        self.config.builder.normalization_version,
                        run_ctx.ontology_version,
                        self.config.builder.schema_compatibility_version,
                        "removed",
                        None,
                        None,
                        archived_at,
                        json.dumps({"archived_items": archived_count}),
                    ),
                )
        return archived_count
