namespace HarnessMcp.Infrastructure.Postgres;

public static class SqlTextLibrary
{
    public static string SqlLexicalCandidates(string schema) => $"""
WITH q AS (
  SELECT plainto_tsquery('simple', @queryText) AS tsq
),
filtered AS (
  SELECT
    i.id,
    i.retrieval_class,
    i.title,
    i.summary,
    i.details,
    i.authority_level,
    i.status,
    i.updated_at
  FROM {schema}.knowledge_items i
  WHERE i.status = @status
    AND i.superseded_by IS NULL
    AND i.authority_level >= @minAuthority
    AND (cardinality(@retrievalClasses::text[]) = 0 OR i.retrieval_class = ANY(@retrievalClasses))
    AND (
      (cardinality(@domains::text[]) = 0 AND cardinality(@modules::text[]) = 0 AND cardinality(@features::text[]) = 0 AND
       cardinality(@layers::text[]) = 0 AND cardinality(@concerns::text[]) = 0 AND cardinality(@repos::text[]) = 0 AND
       cardinality(@services::text[]) = 0 AND cardinality(@symbols::text[]) = 0)
      OR EXISTS (
        SELECT 1
        FROM {schema}.knowledge_scopes ks
        WHERE ks.knowledge_item_id = i.id
          AND (
            (cardinality(@domains::text[]) > 0  AND ks.scope_type = 'domain'  AND ks.scope_value = ANY(@domains)) OR
            (cardinality(@modules::text[]) > 0  AND ks.scope_type = 'module'  AND ks.scope_value = ANY(@modules)) OR
            (cardinality(@features::text[]) > 0 AND ks.scope_type = 'feature' AND ks.scope_value = ANY(@features)) OR
            (cardinality(@layers::text[]) > 0   AND ks.scope_type = 'layer'   AND ks.scope_value = ANY(@layers)) OR
            (cardinality(@concerns::text[]) > 0 AND ks.scope_type = 'concern' AND ks.scope_value = ANY(@concerns)) OR
            (cardinality(@repos::text[]) > 0    AND ks.scope_type = 'repo'    AND ks.scope_value = ANY(@repos)) OR
            (cardinality(@services::text[]) > 0 AND ks.scope_type = 'service' AND ks.scope_value = ANY(@services)) OR
            (cardinality(@symbols::text[]) > 0 AND ks.scope_type = 'symbol' AND ks.scope_value = ANY(@symbols))
          )
      )
    )
),
scored AS (
  SELECT
    f.id,
    f.retrieval_class,
    f.title,
    f.summary,
    f.details,
    GREATEST(
      COALESCE(ts_rank_cd(to_tsvector('simple', rp.profile_text), q.tsq), 0),
      COALESCE(ts_rank_cd(to_tsvector('simple', i.normalized_retrieval_text), q.tsq), 0),
      COALESCE(ts_rank_cd(to_tsvector('simple', f.title), q.tsq), 0),
      COALESCE(ts_rank_cd(to_tsvector('simple', f.summary), q.tsq), 0)
    ) AS lexical_score,
    f.authority_level,
    f.status,
    f.updated_at
  FROM filtered f
  JOIN {schema}.knowledge_items i ON i.id = f.id
  LEFT JOIN {schema}.retrieval_profiles rp
    ON rp.knowledge_item_id = f.id
   AND rp.profile_type = @preferredProfileType
  CROSS JOIN q
)
SELECT
  s.id,
  s.retrieval_class,
  s.title,
  s.summary,
  s.details,
  s.lexical_score,
  s.authority_level,
  s.status,
  s.updated_at
FROM scored s
WHERE s.lexical_score > 0
ORDER BY s.lexical_score DESC, s.updated_at DESC
LIMIT @limit
""";

    public static string SqlSemanticCandidates(string schema) => $"""
WITH filtered AS (
  SELECT
    i.id,
    i.retrieval_class,
    i.title,
    i.summary,
    i.details,
    i.authority_level,
    i.status,
    i.updated_at,
    ke.embedding_role,
    ke.embedding <=> @embeddingVector::vector AS distance
  FROM {schema}.knowledge_embeddings ke
  JOIN {schema}.knowledge_items i ON i.id = ke.knowledge_item_id
  WHERE i.status = @status
    AND i.superseded_by IS NULL
    AND i.authority_level >= @minAuthority
    AND (ke.embedding_role = @preferredRole OR ke.embedding_role = @fallbackRole)
    AND (cardinality(@retrievalClasses::text[]) = 0 OR i.retrieval_class = ANY(@retrievalClasses))
    AND (
      (cardinality(@domains::text[]) = 0 AND cardinality(@modules::text[]) = 0 AND cardinality(@features::text[]) = 0 AND
       cardinality(@layers::text[]) = 0 AND cardinality(@concerns::text[]) = 0 AND cardinality(@repos::text[]) = 0 AND
       cardinality(@services::text[]) = 0 AND cardinality(@symbols::text[]) = 0)
      OR EXISTS (
        SELECT 1
        FROM {schema}.knowledge_scopes ks
        WHERE ks.knowledge_item_id = i.id
          AND (
            (cardinality(@domains::text[]) > 0  AND ks.scope_type = 'domain'  AND ks.scope_value = ANY(@domains)) OR
            (cardinality(@modules::text[]) > 0  AND ks.scope_type = 'module'  AND ks.scope_value = ANY(@modules)) OR
            (cardinality(@features::text[]) > 0 AND ks.scope_type = 'feature' AND ks.scope_value = ANY(@features)) OR
            (cardinality(@layers::text[]) > 0   AND ks.scope_type = 'layer'   AND ks.scope_value = ANY(@layers)) OR
            (cardinality(@concerns::text[]) > 0 AND ks.scope_type = 'concern' AND ks.scope_value = ANY(@concerns)) OR
            (cardinality(@repos::text[]) > 0    AND ks.scope_type = 'repo'    AND ks.scope_value = ANY(@repos)) OR
            (cardinality(@services::text[]) > 0 AND ks.scope_type = 'service' AND ks.scope_value = ANY(@services)) OR
            (cardinality(@symbols::text[]) > 0 AND ks.scope_type = 'symbol' AND ks.scope_value = ANY(@symbols))
          )
      )
    )
),
ranked AS (
  SELECT
    *,
    ROW_NUMBER() OVER (
      PARTITION BY id
      ORDER BY (embedding_role = @preferredRole) DESC, distance ASC
    ) AS rn
  FROM filtered
)
SELECT
  id,
  retrieval_class,
  title,
  summary,
  details,
  (GREATEST(1e-6, 1.0 / (1.0 + COALESCE(distance, 1e9))))::double precision AS semantic_score,
  authority_level,
  status,
  updated_at
FROM ranked
WHERE rn = 1
ORDER BY semantic_score DESC, updated_at DESC
LIMIT @limit
""";

    public static string SqlGetKnowledgeItemBase(string schema) => $"""
SELECT
  i.id,
  i.retrieval_class,
  i.title,
  i.summary,
  i.details,
  i.authority_level,
  i.status,
  i.authority_label
FROM {schema}.knowledge_items i
WHERE i.id = @id
  AND i.superseded_by IS NULL
  AND i.status <> 'superseded'
  AND i.status <> 'archived'
LIMIT 1
""";

    public static string SqlGetRelatedKnowledge(string schema) => $"""
SELECT
  rel.to_item_id,
  rel.relation_type,
  to_i.title,
  to_i.summary,
  to_i.retrieval_class,
  to_i.authority_level,
  rel.strength
FROM {schema}.knowledge_relations rel
JOIN {schema}.knowledge_items to_i ON to_i.id = rel.to_item_id
WHERE rel.from_item_id = @id
  AND rel.relation_type = ANY(@relationTypes)
  AND to_i.superseded_by IS NULL
  AND to_i.status <> 'superseded'
  AND to_i.status <> 'archived'
ORDER BY
  COALESCE(rel.strength, 0) DESC,
  to_i.authority_level DESC,
  to_i.updated_at DESC
LIMIT @limit
""";

    public static string SqlLoadScopes(string schema) => $"""
SELECT
  ks.knowledge_item_id,
  ks.scope_type,
  ks.scope_value
FROM {schema}.knowledge_scopes ks
WHERE ks.knowledge_item_id = ANY(@ids)
ORDER BY ks.scope_type ASC
""";

    public static string SqlLoadLabelsAndTags(string schema) => $"""
SELECT
  k.id AS knowledge_item_id,
  lbl.label,
  tg.tag
FROM {schema}.knowledge_items k
LEFT JOIN {schema}.knowledge_labels lbl
  ON lbl.knowledge_item_id = k.id
LEFT JOIN {schema}.knowledge_tags tg
  ON tg.knowledge_item_id = k.id
WHERE k.id = ANY(@ids)
""";

    public static string SqlLoadEvidence(string schema) => $"""
WITH ranked AS (
  SELECT
    kis.knowledge_item_id,
    sa.id AS source_artifact_id,
    sa.source_path,
    ss.heading_path::text AS heading_path_json,
    ss.raw_text,
    ss.start_line,
    ss.end_line,
    ROW_NUMBER() OVER (
      PARTITION BY kis.knowledge_item_id
      ORDER BY ss.start_line NULLS LAST, ss.id
    ) AS rn
  FROM {schema}.knowledge_item_segments kis
  JOIN {schema}.source_segments ss ON ss.id = kis.source_segment_id
  JOIN {schema}.source_artifacts sa ON sa.id = ss.source_artifact_id
  WHERE kis.knowledge_item_id = ANY(@ids)
)
SELECT
  knowledge_item_id,
  source_artifact_id,
  source_path,
  heading_path_json,
  raw_text,
  start_line,
  end_line
FROM ranked
WHERE rn <= @maxPerItem
ORDER BY knowledge_item_id, rn
""";

    public static string SqlLoadSegments(string schema) => $"""
SELECT
  ss.id AS source_segment_id,
  ss.span_level,
  ss.heading_path::text AS heading_path_json,
  ss.start_line,
  ss.end_line,
  ss.start_offset,
  ss.end_offset,
  kis.role,
  sa.source_path
FROM {schema}.knowledge_item_segments kis
JOIN {schema}.source_segments ss ON ss.id = kis.source_segment_id
LEFT JOIN {schema}.source_artifacts sa ON sa.id = ss.source_artifact_id
WHERE kis.knowledge_item_id = @id
ORDER BY kis.role ASC, ss.start_line NULLS LAST
""";

    public static string SqlLoadRelations(string schema) => $"""
SELECT
  rel.to_item_id,
  rel.relation_type,
  to_i.title,
  to_i.summary,
  to_i.retrieval_class,
  to_i.authority_level,
  rel.strength
FROM {schema}.knowledge_relations rel
JOIN {schema}.knowledge_items to_i ON to_i.id = rel.to_item_id
WHERE rel.from_item_id = @id
  AND to_i.superseded_by IS NULL
  AND to_i.status <> 'superseded'
  AND to_i.status <> 'archived'
ORDER BY
  COALESCE(rel.strength, 0) DESC,
  to_i.authority_level DESC,
  to_i.updated_at DESC
LIMIT @limit
""";
}

