CREATE INDEX IF NOT EXISTS idx_knowledge_items_class_status_authority
  ON knowledge_items (retrieval_class, status, authority_level);
CREATE INDEX IF NOT EXISTS idx_knowledge_items_domain_module_feature
  ON knowledge_items (domain, module, feature);
CREATE INDEX IF NOT EXISTS idx_knowledge_items_source_type_status
  ON knowledge_items (source_type, status);
CREATE INDEX IF NOT EXISTS idx_knowledge_items_parent_item_id
  ON knowledge_items (parent_item_id);
CREATE INDEX IF NOT EXISTS idx_knowledge_items_ingestion_run_id
  ON knowledge_items (ingestion_run_id);

CREATE INDEX IF NOT EXISTS idx_knowledge_labels_label_role
  ON knowledge_labels (label, label_role);
CREATE INDEX IF NOT EXISTS idx_knowledge_labels_item
  ON knowledge_labels (knowledge_item_id);

CREATE INDEX IF NOT EXISTS idx_knowledge_scopes_type_value
  ON knowledge_scopes (scope_type, scope_value);
CREATE INDEX IF NOT EXISTS idx_knowledge_tags_tag
  ON knowledge_tags (tag);
CREATE INDEX IF NOT EXISTS idx_source_artifacts_type_ref
  ON source_artifacts (source_type, source_ref);
CREATE INDEX IF NOT EXISTS idx_source_artifacts_status_path
  ON source_artifacts (status, source_path);
CREATE INDEX IF NOT EXISTS idx_source_artifacts_hash
  ON source_artifacts (artifact_hash);
CREATE INDEX IF NOT EXISTS idx_source_segments_artifact
  ON source_segments (source_artifact_id);
CREATE INDEX IF NOT EXISTS idx_source_segments_hash
  ON source_segments (segment_hash);
CREATE INDEX IF NOT EXISTS idx_case_shapes_task_feature
  ON case_shapes (task_type, feature_shape);
CREATE INDEX IF NOT EXISTS idx_retrieval_profiles_item_type
  ON retrieval_profiles (knowledge_item_id, profile_type);
CREATE INDEX IF NOT EXISTS idx_embeddings_item_role
  ON knowledge_embeddings (knowledge_item_id, embedding_role);
CREATE INDEX IF NOT EXISTS idx_ingestion_runs_status
  ON ingestion_runs (status);
CREATE INDEX IF NOT EXISTS idx_artifact_build_states_status
  ON artifact_build_states (build_status);
