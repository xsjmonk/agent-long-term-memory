CREATE EXTENSION IF NOT EXISTS vector;

CREATE TABLE IF NOT EXISTS source_artifacts (
  id uuid PRIMARY KEY,
  source_type text NOT NULL,
  source_ref text NOT NULL UNIQUE,
  source_path text,
  status text NOT NULL DEFAULT 'active',
  removed_at timestamptz,
  last_seen_at timestamptz,
  repo_name text,
  branch_name text,
  commit_sha text,
  title text,
  artifact_hash text,
  observed_at timestamptz,
  created_at timestamptz NOT NULL,
  updated_at timestamptz NOT NULL
);

CREATE TABLE IF NOT EXISTS ingestion_runs (
  id uuid PRIMARY KEY,
  builder_version text NOT NULL,
  classifier_version text,
  embedding_model text NOT NULL,
  embedding_model_version text,
  normalization_version text NOT NULL,
  ontology_version text NOT NULL,
  schema_compatibility_version text NOT NULL,
  started_at timestamptz NOT NULL,
  finished_at timestamptz,
  status text NOT NULL,
  notes jsonb
);

CREATE TABLE IF NOT EXISTS knowledge_items (
  id uuid PRIMARY KEY,
  retrieval_class text NOT NULL,
  title text,
  summary text,
  details text,
  normalized_retrieval_text text NOT NULL,
  span_level text NOT NULL,
  authority_level integer NOT NULL,
  authority_label text NOT NULL,
  status text NOT NULL,
  confidence numeric,
  domain text,
  module text,
  feature text,
  source_type text,
  parent_item_id uuid REFERENCES knowledge_items(id) ON DELETE SET NULL,
  valid_from timestamptz,
  valid_to timestamptz,
  superseded_by uuid REFERENCES knowledge_items(id) ON DELETE SET NULL,
  created_at timestamptz NOT NULL,
  updated_at timestamptz NOT NULL,
  ingestion_run_id uuid REFERENCES ingestion_runs(id) ON DELETE SET NULL,
  source_artifact_id uuid REFERENCES source_artifacts(id) ON DELETE CASCADE
);

CREATE TABLE IF NOT EXISTS source_segments (
  id uuid PRIMARY KEY,
  source_artifact_id uuid NOT NULL REFERENCES source_artifacts(id) ON DELETE CASCADE,
  heading_path jsonb,
  start_offset integer,
  end_offset integer,
  start_line integer,
  end_line integer,
  span_level text NOT NULL,
  segment_hash text,
  raw_text text
);

CREATE TABLE IF NOT EXISTS knowledge_item_segments (
  knowledge_item_id uuid NOT NULL REFERENCES knowledge_items(id) ON DELETE CASCADE,
  source_segment_id uuid NOT NULL REFERENCES source_segments(id) ON DELETE CASCADE,
  role text NOT NULL,
  PRIMARY KEY (knowledge_item_id, source_segment_id, role)
);

CREATE TABLE IF NOT EXISTS knowledge_labels (
  id uuid PRIMARY KEY,
  knowledge_item_id uuid NOT NULL REFERENCES knowledge_items(id) ON DELETE CASCADE,
  label text NOT NULL,
  label_role text NOT NULL,
  confidence numeric,
  source_method text NOT NULL,
  created_at timestamptz NOT NULL
);

CREATE TABLE IF NOT EXISTS knowledge_scopes (
  id uuid PRIMARY KEY,
  knowledge_item_id uuid NOT NULL REFERENCES knowledge_items(id) ON DELETE CASCADE,
  scope_type text NOT NULL,
  scope_value text NOT NULL,
  weight numeric
);

CREATE TABLE IF NOT EXISTS knowledge_tags (
  id uuid PRIMARY KEY,
  knowledge_item_id uuid NOT NULL REFERENCES knowledge_items(id) ON DELETE CASCADE,
  tag text NOT NULL,
  tag_source text
);

CREATE TABLE IF NOT EXISTS knowledge_relations (
  id uuid PRIMARY KEY,
  from_item_id uuid NOT NULL REFERENCES knowledge_items(id) ON DELETE CASCADE,
  to_item_id uuid NOT NULL REFERENCES knowledge_items(id) ON DELETE CASCADE,
  relation_type text NOT NULL,
  strength numeric,
  created_at timestamptz NOT NULL
);

CREATE TABLE IF NOT EXISTS case_shapes (
  id uuid PRIMARY KEY,
  knowledge_item_id uuid NOT NULL REFERENCES knowledge_items(id) ON DELETE CASCADE,
  task_type text,
  feature_shape text,
  engine_change_allowed boolean,
  likely_layers jsonb,
  risk_signals jsonb,
  complexity text
);

CREATE TABLE IF NOT EXISTS retrieval_profiles (
  id uuid PRIMARY KEY,
  knowledge_item_id uuid NOT NULL REFERENCES knowledge_items(id) ON DELETE CASCADE,
  profile_type text NOT NULL,
  profile_text text NOT NULL,
  created_at timestamptz NOT NULL
);

CREATE TABLE IF NOT EXISTS knowledge_embeddings (
  id uuid PRIMARY KEY,
  knowledge_item_id uuid NOT NULL REFERENCES knowledge_items(id) ON DELETE CASCADE,
  profile_id uuid REFERENCES retrieval_profiles(id) ON DELETE CASCADE,
  embedding_role text NOT NULL,
  embedding_text text NOT NULL,
  embedding vector(384),
  model_name text NOT NULL,
  model_version text,
  created_at timestamptz NOT NULL
);

CREATE TABLE IF NOT EXISTS artifact_build_states (
  source_artifact_id uuid PRIMARY KEY REFERENCES source_artifacts(id) ON DELETE CASCADE,
  last_ingestion_run_id uuid REFERENCES ingestion_runs(id) ON DELETE SET NULL,
  artifact_hash text NOT NULL,
  builder_version text NOT NULL,
  classifier_version text,
  embedding_model_version text,
  normalization_version text NOT NULL,
  ontology_version text NOT NULL,
  schema_compatibility_version text NOT NULL,
  build_status text NOT NULL,
  failure_details jsonb,
  last_built_at timestamptz,
  last_reconciled_at timestamptz,
  notes jsonb
);
