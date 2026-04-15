using System.Globalization;
using System.Text;
using System.Text.Json;
using HarnessMcp.Contracts;
using HarnessMcp.Core;
using Npgsql;
using NpgsqlTypes;

namespace HarnessMcp.Infrastructure.Postgres;

public sealed class PostgresKnowledgeRepository : IKnowledgeRepository
{
    private readonly AppConfig _config;
    private readonly DatabaseConfig _db;
    private readonly RetrievalConfig _retrieval;
    private readonly MonitoringConfig _monitoring;
    private readonly NpgsqlDataSource _dataSource;

    public PostgresKnowledgeRepository(AppConfig config, NpgsqlDataSource dataSource)
    {
        _config = config;
        _db = config.Database;
        _retrieval = config.Retrieval;
        _monitoring = config.Monitoring;
        _dataSource = dataSource;
    }

    public async ValueTask<IReadOnlyList<KnowledgeCandidateDto>> SearchLexicalAsync(
        SearchKnowledgeRequest request,
        CancellationToken cancellationToken)
    {
        var preferredProfileType = ToProfileType(request.QueryKind);
        var lexicalLimit = Math.Min(_retrieval.LexicalCandidateCount, request.TopK > 0 ? request.TopK * 5 : _retrieval.LexicalCandidateCount);

        await using var conn = await _dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();

        cmd.CommandText = SqlTextLibrary.SqlLexicalCandidates(_db.SearchSchema);
        cmd.Parameters.AddWithValue("queryText", request.QueryText);
        cmd.Parameters.AddWithValue("minAuthority", (int)request.MinimumAuthority);
        cmd.Parameters.AddWithValue("status", ToKnowledgeStatusDb(request.Status));
        cmd.Parameters.AddWithValue("preferredProfileType", preferredProfileType);
        VectorParameterFormatter.AddTextArrayParameter(cmd, "retrievalClasses", request.RetrievalClasses.Select(ToRetrievalClassDb).ToArray());
        VectorParameterFormatter.AddTextArrayParameter(cmd, "domains", request.Scopes.Domains.ToArray());
        VectorParameterFormatter.AddTextArrayParameter(cmd, "modules", request.Scopes.Modules.ToArray());
        VectorParameterFormatter.AddTextArrayParameter(cmd, "features", request.Scopes.Features.ToArray());
        VectorParameterFormatter.AddTextArrayParameter(cmd, "layers", request.Scopes.Layers.ToArray());
        VectorParameterFormatter.AddTextArrayParameter(cmd, "concerns", request.Scopes.Concerns.ToArray());
        VectorParameterFormatter.AddTextArrayParameter(cmd, "repos", request.Scopes.Repos.ToArray());
        VectorParameterFormatter.AddTextArrayParameter(cmd, "services", request.Scopes.Services.ToArray());
        VectorParameterFormatter.AddTextArrayParameter(cmd, "symbols", request.Scopes.Symbols.ToArray());
        cmd.Parameters.AddWithValue("limit", lexicalLimit);

        var rows = new List<PostgresRowMappers.LexicalRow>();
        await using (var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
        {
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                rows.Add(PostgresRowMappers.MapLexicalRow(reader));
            }
        }

        // Hydrate scopes/labels/tags and optionally evidence.
        var candidateIds = rows.Select(r => r.Id).ToArray();
        var scopes = request.Scopes is null || IsAllEmpty(request.Scopes) ? await LoadScopesAsync(conn, candidateIds, cancellationToken).ConfigureAwait(false) :
            await LoadScopesAsync(conn, candidateIds, cancellationToken).ConfigureAwait(false);

        var labelsTags = await LoadLabelsAndTagsAsync(conn, candidateIds, cancellationToken).ConfigureAwait(false);

        IReadOnlyDictionary<Guid, IReadOnlyList<EvidenceDto>> evidenceByItem = Array.Empty<Guid>().Length == 0
            ? new Dictionary<Guid, IReadOnlyList<EvidenceDto>>()
            : request.IncludeEvidence
                ? await LoadEvidenceAsync(conn, candidateIds, cancellationToken).ConfigureAwait(false)
                : new Dictionary<Guid, IReadOnlyList<EvidenceDto>>();

        var candidates = new List<KnowledgeCandidateDto>(rows.Count);
        foreach (var r in rows)
        {
            var status = r.Status;
            var details = request.IncludeRawDetails ? r.Details : null;
            evidenceByItem.TryGetValue(r.Id, out var evidence);
            scopes.TryGetValue(r.Id, out var scopeDto);
            labelsTags.TryGetValue(r.Id, out var lt);
            var lexicalScore = r.LexicalScore + RecencyBoostUtc(r.UpdatedAtUtc);

            candidates.Add(new KnowledgeCandidateDto(
                r.Id,
                r.RetrievalClass,
                r.Title,
                r.Summary,
                details,
                SemanticScore: 0,
                LexicalScore: lexicalScore,
                ScopeScore: 0,
                AuthorityScore: 0,
                CaseShapeScore: 0,
                FinalScore: 0,
                Authority: (AuthorityLevel)r.AuthorityLevel,
                Status: status,
                Scopes: scopeDto ?? ScopeDtos.Empty,
                Labels: lt?.Labels ?? new List<string>(),
                Tags: lt?.Tags ?? new List<string>(),
                Evidence: evidence ?? Array.Empty<EvidenceDto>(),
                SupportedByChunks: Array.Empty<string>(),
                SupportedByQueryKinds: [preferredProfileType]));
        }

        return candidates
            .OrderByDescending(x => x.LexicalScore)
            .ToList();
    }

    public async ValueTask<IReadOnlyList<KnowledgeCandidateDto>> SearchSemanticAsync(
        SearchKnowledgeRequest request,
        ReadOnlyMemory<float> embedding,
        CancellationToken cancellationToken)
    {
        var preferredEmbeddingRole = ToProfileType(request.QueryKind);
        var semanticLimit = Math.Min(_retrieval.SemanticCandidateCount, request.TopK > 0 ? request.TopK * 5 : _retrieval.SemanticCandidateCount);

        var vectorLiteral = VectorParameterFormatter.FormatVectorLiteral(embedding);
        await using var conn = await _dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();

        cmd.CommandText = SqlTextLibrary.SqlSemanticCandidates(_db.SearchSchema);
        cmd.Parameters.AddWithValue("preferredRole", preferredEmbeddingRole);
        cmd.Parameters.AddWithValue("fallbackRole", "normalized_retrieval_text");
        cmd.Parameters.AddWithValue("minAuthority", (int)request.MinimumAuthority);
        cmd.Parameters.AddWithValue("status", ToKnowledgeStatusDb(request.Status));
        VectorParameterFormatter.AddTextArrayParameter(cmd, "retrievalClasses", request.RetrievalClasses.Select(ToRetrievalClassDb).ToArray());
        VectorParameterFormatter.AddTextArrayParameter(cmd, "domains", request.Scopes.Domains.ToArray());
        VectorParameterFormatter.AddTextArrayParameter(cmd, "modules", request.Scopes.Modules.ToArray());
        VectorParameterFormatter.AddTextArrayParameter(cmd, "features", request.Scopes.Features.ToArray());
        VectorParameterFormatter.AddTextArrayParameter(cmd, "layers", request.Scopes.Layers.ToArray());
        VectorParameterFormatter.AddTextArrayParameter(cmd, "concerns", request.Scopes.Concerns.ToArray());
        VectorParameterFormatter.AddTextArrayParameter(cmd, "repos", request.Scopes.Repos.ToArray());
        VectorParameterFormatter.AddTextArrayParameter(cmd, "services", request.Scopes.Services.ToArray());
        VectorParameterFormatter.AddTextArrayParameter(cmd, "symbols", request.Scopes.Symbols.ToArray());
        cmd.Parameters.AddWithValue("embeddingVector", vectorLiteral);
        cmd.Parameters.AddWithValue("limit", semanticLimit);

        var rows = new List<PostgresRowMappers.SemanticRow>();
        await using (var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
        {
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                rows.Add(PostgresRowMappers.MapSemanticRow(reader));
            }
        }

        var candidateIds = rows.Select(r => r.Id).ToArray();
        var scopes = await LoadScopesAsync(conn, candidateIds, cancellationToken).ConfigureAwait(false);
        var labelsTags = await LoadLabelsAndTagsAsync(conn, candidateIds, cancellationToken).ConfigureAwait(false);

        IReadOnlyDictionary<Guid, IReadOnlyList<EvidenceDto>> evidenceByItem =
            request.IncludeEvidence
                ? await LoadEvidenceAsync(conn, candidateIds, cancellationToken).ConfigureAwait(false)
                : new Dictionary<Guid, IReadOnlyList<EvidenceDto>>();

        var candidates = new List<KnowledgeCandidateDto>(rows.Count);
        foreach (var r in rows)
        {
            scopes.TryGetValue(r.Id, out var scopeDto);
            labelsTags.TryGetValue(r.Id, out var lt);
            evidenceByItem.TryGetValue(r.Id, out var evidence);

            var details = request.IncludeRawDetails ? r.Details : null;
            var semanticScore = r.SemanticScore + RecencyBoostUtc(r.UpdatedAtUtc);
            candidates.Add(new KnowledgeCandidateDto(
                r.Id,
                r.RetrievalClass,
                r.Title,
                r.Summary,
                details,
                SemanticScore: semanticScore,
                LexicalScore: 0,
                ScopeScore: 0,
                AuthorityScore: 0,
                CaseShapeScore: 0,
                FinalScore: 0,
                Authority: (AuthorityLevel)r.AuthorityLevel,
                Status: r.Status,
                Scopes: scopeDto ?? ScopeDtos.Empty,
                Labels: lt?.Labels ?? new List<string>(),
                Tags: lt?.Tags ?? new List<string>(),
                Evidence: evidence ?? Array.Empty<EvidenceDto>(),
                SupportedByChunks: Array.Empty<string>(),
                SupportedByQueryKinds: [preferredEmbeddingRole]));
        }

        return candidates
            .OrderByDescending(x => x.SemanticScore)
            .ToList();
    }

    private const double RecencyWeight = 1e-3;

    private static double RecencyBoostUtc(DateTimeOffset updatedAtUtc)
    {
        // Normalized to 0..1 via age-days; higher score => newer items.
        var ageDays = (DateTimeOffset.UtcNow - updatedAtUtc).TotalDays;
        var recency = 1.0 / (1.0 + Math.Max(0d, ageDays));
        return RecencyWeight * recency;
    }

    public async ValueTask<GetKnowledgeItemResponse> GetKnowledgeItemAsync(
        GetKnowledgeItemRequest request,
        CancellationToken cancellationToken)
    {
        await using var conn = await _dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = SqlTextLibrary.SqlGetKnowledgeItemBase(_db.SearchSchema);
        cmd.Parameters.AddWithValue("id", request.KnowledgeItemId);

        PostgresRowMappers.KnowledgeBaseRow row = default;
        var found = false;
        await using (var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
        {
            if (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                row = PostgresRowMappers.MapKnowledgeBaseRow(reader);
                found = true;
            }
        }

        if (!found)
            throw new KeyNotFoundException($"Knowledge item {request.KnowledgeItemId} not found.");

        var scopes = request.IncludeScopes
            ? await LoadScopesAsync(conn, new[] { request.KnowledgeItemId }, cancellationToken).ConfigureAwait(false)
            : new Dictionary<Guid, ScopeFilterDto>();
        var labelsTags = (request.IncludeLabels || request.IncludeTags)
            ? await LoadLabelsAndTagsAsync(conn, new[] { request.KnowledgeItemId }, cancellationToken).ConfigureAwait(false)
            : new Dictionary<Guid, LabelsAndTags>();

        var segments = request.IncludeSegments
            ? await LoadSegmentsAsync(conn, request.KnowledgeItemId, cancellationToken).ConfigureAwait(false)
            : (IReadOnlyList<KnowledgeSegmentDto>)Array.Empty<KnowledgeSegmentDto>();

        var relations = request.IncludeRelations
            ? await LoadRelationsAsync(conn, request.KnowledgeItemId, cancellationToken).ConfigureAwait(false)
            : (IReadOnlyList<RelatedKnowledgeDto>)Array.Empty<RelatedKnowledgeDto>();

        scopes.TryGetValue(request.KnowledgeItemId, out var scopeDto);
        labelsTags.TryGetValue(request.KnowledgeItemId, out var lt);

        var item = new KnowledgeCandidateDto(
            row.Id,
            row.RetrievalClass,
            row.Title,
            row.Summary,
            row.Details,
            SemanticScore: 0,
            LexicalScore: 0,
            ScopeScore: 0,
            AuthorityScore: 0,
            CaseShapeScore: 0,
            FinalScore: 0,
            Authority: (AuthorityLevel)row.AuthorityLevel,
            Status: row.Status,
            Scopes: scopeDto ?? ScopeDtos.Empty,
            Labels: lt?.Labels ?? new List<string>(),
            Tags: lt?.Tags ?? new List<string>(),
            Evidence: Array.Empty<EvidenceDto>(),
            SupportedByChunks: Array.Empty<string>(),
            SupportedByQueryKinds: Array.Empty<string>());

        return new GetKnowledgeItemResponse(
            request.SchemaVersion,
            "get_knowledge_item",
            request.RequestId,
            item,
            segments,
            relations);
    }

    public async ValueTask<GetRelatedKnowledgeResponse> GetRelatedKnowledgeAsync(
        GetRelatedKnowledgeRequest request,
        CancellationToken cancellationToken)
    {
        await using var conn = await _dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = SqlTextLibrary.SqlGetRelatedKnowledge(_db.SearchSchema);

        cmd.Parameters.AddWithValue("id", request.KnowledgeItemId);
        VectorParameterFormatter.AddTextArrayParameter(cmd, "relationTypes", request.RelationTypes.Select(ToRelationTypeDb).ToArray());
        cmd.Parameters.AddWithValue("limit", request.TopK);

        var items = new List<RelatedKnowledgeDto>();
        await using (var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
        {
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                items.Add(new RelatedKnowledgeDto(
                    reader.GetGuid(0),
                    ParseRelationType(reader.GetString(1)),
                    reader.GetString(2),
                    reader.GetString(3),
                    ParseRetrievalClass(reader.GetString(4)),
                    (AuthorityLevel)reader.GetInt32(5),
                    reader.IsDBNull(6) ? 0d : reader.GetDouble(6)));
            }
        }

        return new GetRelatedKnowledgeResponse(
            request.SchemaVersion,
            "get_related_knowledge",
            request.RequestId,
            request.KnowledgeItemId,
            items);
    }

    private static bool IsAllEmpty(ScopeFilterDto scopes) =>
        scopes.Domains.Count == 0 &&
        scopes.Modules.Count == 0 &&
        scopes.Features.Count == 0 &&
        scopes.Layers.Count == 0 &&
        scopes.Concerns.Count == 0 &&
        scopes.Repos.Count == 0 &&
        scopes.Services.Count == 0 &&
        scopes.Symbols.Count == 0;

    private static string ToProfileType(QueryKind kind) => kind switch
    {
        QueryKind.CoreTask => "core_task",
        QueryKind.Constraint => "constraint",
        QueryKind.Risk => "risk",
        QueryKind.Pattern => "pattern",
        QueryKind.SimilarCase => "similar_case",
        QueryKind.Summary => "summary",
        QueryKind.Details => "details",
        _ => kind.ToString()
    };

    private static string ToRetrievalClassDb(RetrievalClass c) => c switch
    {
        RetrievalClass.Decision => "decision",
        RetrievalClass.BestPractice => "best_practice",
        RetrievalClass.Antipattern => "antipattern",
        RetrievalClass.SimilarCase => "similar_case",
        RetrievalClass.Constraint => "constraint",
        RetrievalClass.Reference => "reference",
        RetrievalClass.Structure => "structure",
        _ => c.ToString()
    };

    private static RetrievalClass ParseRetrievalClass(string dbValue) => dbValue.ToLowerInvariant() switch
    {
        "decision" => RetrievalClass.Decision,
        "best_practice" => RetrievalClass.BestPractice,
        "antipattern" => RetrievalClass.Antipattern,
        "similar_case" => RetrievalClass.SimilarCase,
        "constraint" => RetrievalClass.Constraint,
        "reference" => RetrievalClass.Reference,
        "structure" => RetrievalClass.Structure,
        _ => RetrievalClass.Decision
    };

    private static string ToRelationTypeDb(RelationType t) => t.ToString();

    private static RelationType ParseRelationType(string s) =>
        Enum.TryParse<RelationType>(s, ignoreCase: true, out var r) ? r : RelationType.Related;

    private static void AddTextArrayParameter(NpgsqlCommand cmd, string name, string[] values)
    {
        var p = cmd.Parameters.Add(name, NpgsqlDbType.Array | NpgsqlDbType.Text);
        p.Value = values;
    }

    private static string FormatVectorLiteral(ReadOnlyMemory<float> embedding)
    {
        if (embedding.IsEmpty) return "[]";
        var arr = embedding.ToArray();
        var sb = new StringBuilder(arr.Length * 8);
        sb.Append('[');
        for (var i = 0; i < arr.Length; i++)
        {
            if (i > 0) sb.Append(',');
            sb.Append(arr[i].ToString("G9", CultureInfo.InvariantCulture));
        }
        sb.Append(']');
        return sb.ToString();
    }

    private string EvidenceSnippet(string? raw)
    {
        if (string.IsNullOrEmpty(raw))
            return "";
        var max = Math.Max(64, _monitoring.MaxPayloadPreviewChars);
        if (raw.Length <= max)
            return raw;
        // Keep a prefix slice; do not attempt JSON-aware trimming.
        return raw.Substring(0, max) + "…";
    }

    private async ValueTask<IReadOnlyDictionary<Guid, ScopeFilterDto>> LoadScopesAsync(
        NpgsqlConnection conn,
        Guid[] candidateIds,
        CancellationToken cancellationToken)
    {
        if (candidateIds.Length == 0)
            return new Dictionary<Guid, ScopeFilterDto>();

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = SqlTextLibrary.SqlLoadScopes(_db.SearchSchema);
        cmd.Parameters.AddWithValue("ids", candidateIds);

        var result = candidateIds.ToDictionary(id => id, _ => ScopeDtos.Empty);
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var buckets = new Dictionary<Guid, ScopeBucket>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var id = reader.GetGuid(0);
            var type = reader.GetString(1);
            var value = reader.GetString(2);
            if (!buckets.TryGetValue(id, out var b))
                buckets[id] = b = new ScopeBucket();
            b.Add(type, value);
        }

        foreach (var (id, b) in buckets)
            result[id] = b.ToDto();

        return result;
    }

    private async ValueTask<IReadOnlyDictionary<Guid, LabelsAndTags>> LoadLabelsAndTagsAsync(
        NpgsqlConnection conn,
        Guid[] candidateIds,
        CancellationToken cancellationToken)
    {
        if (candidateIds.Length == 0)
            return new Dictionary<Guid, LabelsAndTags>();

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = SqlTextLibrary.SqlLoadLabelsAndTags(_db.SearchSchema);
        cmd.Parameters.AddWithValue("ids", candidateIds);

        var labels = new Dictionary<Guid, List<string>>();
        var tags = new Dictionary<Guid, List<string>>();
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var id = reader.GetGuid(0);
            var label = reader.IsDBNull(1) ? null : reader.GetString(1);
            var tag = reader.IsDBNull(2) ? null : reader.GetString(2);

            if (label is not null)
            {
                if (!labels.TryGetValue(id, out var l))
                    labels[id] = l = new List<string>();
                l.Add(label);
            }
            if (tag is not null)
            {
                if (!tags.TryGetValue(id, out var t))
                    tags[id] = t = new List<string>();
                t.Add(tag);
            }
        }

        var result = new Dictionary<Guid, LabelsAndTags>();
        foreach (var id in candidateIds)
        {
            result[id] = new LabelsAndTags(
                labels.TryGetValue(id, out var l) ? l : new List<string>(),
                tags.TryGetValue(id, out var t) ? t : new List<string>());
        }
        return result;
    }

    private async ValueTask<IReadOnlyDictionary<Guid, IReadOnlyList<EvidenceDto>>> LoadEvidenceAsync(
        NpgsqlConnection conn,
        Guid[] candidateIds,
        CancellationToken cancellationToken)
    {
        if (candidateIds.Length == 0)
            return new Dictionary<Guid, IReadOnlyList<EvidenceDto>>();

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = SqlTextLibrary.SqlLoadEvidence(_db.SearchSchema);
        cmd.Parameters.AddWithValue("ids", candidateIds);
        cmd.Parameters.AddWithValue("maxPerItem", 3);

        var buckets = new Dictionary<Guid, List<EvidenceDto>>();
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var knowledgeItemId = reader.GetGuid(0);
            var sourceArtifactId = reader.GetGuid(1);
            var sourcePath = reader.IsDBNull(2) ? null : reader.GetString(2);
            var headingPathJson = reader.IsDBNull(3) ? "[]" : reader.GetString(3);
            var headingPath = TryReadStringArray(headingPathJson);
            var snippetRaw = reader.IsDBNull(4) ? "" : reader.GetString(4);
            var snippet = EvidenceSnippetReader.Read(snippetRaw, _monitoring.MaxPayloadPreviewChars);
            int? startLine = reader.IsDBNull(5) ? null : reader.GetInt32(5);
            int? endLine = reader.IsDBNull(6) ? null : reader.GetInt32(6);

            if (!buckets.TryGetValue(knowledgeItemId, out var list))
                buckets[knowledgeItemId] = list = new List<EvidenceDto>();

            list.Add(new EvidenceDto(
                sourceArtifactId,
                sourcePath,
                headingPath,
                snippet,
                startLine,
                endLine));
        }

        return buckets.ToDictionary(k => k.Key, v => (IReadOnlyList<EvidenceDto>)v.Value);
    }

    private async ValueTask<IReadOnlyList<KnowledgeSegmentDto>> LoadSegmentsAsync(
        NpgsqlConnection conn,
        Guid knowledgeItemId,
        CancellationToken cancellationToken)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = SqlTextLibrary.SqlLoadSegments(_db.SearchSchema);
        cmd.Parameters.AddWithValue("id", knowledgeItemId);

        var segments = new List<KnowledgeSegmentDto>();
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var segId = reader.GetGuid(0);
            var spanLevel = reader.GetString(1);
            var headingPathJson = reader.IsDBNull(2) ? "[]" : reader.GetString(2);
            var headingPath = TryReadStringArray(headingPathJson);
            int? startLine = reader.IsDBNull(3) ? null : reader.GetInt32(3);
            int? endLine = reader.IsDBNull(4) ? null : reader.GetInt32(4);
            int? startOffset = reader.IsDBNull(5) ? null : reader.GetInt32(5);
            int? endOffset = reader.IsDBNull(6) ? null : reader.GetInt32(6);
            var role = reader.GetString(7);
            var sourcePath = reader.IsDBNull(8) ? null : reader.GetString(8);

            segments.Add(new KnowledgeSegmentDto(
                segId,
                spanLevel,
                headingPath,
                startLine,
                endLine,
                startOffset,
                endOffset,
                role,
                sourcePath));
        }

        return segments;
    }

    private async ValueTask<IReadOnlyList<RelatedKnowledgeDto>> LoadRelationsAsync(
        NpgsqlConnection conn,
        Guid knowledgeItemId,
        CancellationToken cancellationToken)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = SqlTextLibrary.SqlLoadRelations(_db.SearchSchema);
        cmd.Parameters.AddWithValue("id", knowledgeItemId);
        cmd.Parameters.AddWithValue("limit", 1000);

        var items = new List<RelatedKnowledgeDto>();
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            items.Add(new RelatedKnowledgeDto(
                reader.GetGuid(0),
                ParseRelationType(reader.GetString(1)),
                reader.GetString(2),
                reader.GetString(3),
                ParseRetrievalClass(reader.GetString(4)),
                (AuthorityLevel)reader.GetInt32(5),
                reader.IsDBNull(6) ? 0d : reader.GetDouble(6)));
        }
        return items;
    }

    private static string SqlLexicalCandidates(string schema) => $"""
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
    i.status
  FROM {schema}.knowledge_items i
  WHERE i.status = @status
    AND i.superseded_by IS NULL
    AND i.authority_level >= @minAuthority
    -- RetrievalClass filtering is applied later in C# ranking.
)
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
  f.status
FROM filtered f
JOIN {schema}.knowledge_items i ON i.id = f.id
LEFT JOIN {schema}.retrieval_profiles rp
  ON rp.knowledge_item_id = f.id
 AND rp.profile_type = @preferredProfileType
CROSS JOIN q
WHERE GREATEST(
    COALESCE(ts_rank_cd(to_tsvector('simple', rp.profile_text), q.tsq), 0),
    COALESCE(ts_rank_cd(to_tsvector('simple', i.normalized_retrieval_text), q.tsq), 0),
    COALESCE(ts_rank_cd(to_tsvector('simple', f.title), q.tsq), 0),
    COALESCE(ts_rank_cd(to_tsvector('simple', f.summary), q.tsq), 0)
  ) >= 0
ORDER BY lexical_score DESC
LIMIT @limit
""";

    private static string SqlSemanticCandidates(string schema) => $"""
WITH filtered AS (
  SELECT
    i.id,
    i.retrieval_class,
    i.title,
    i.summary,
    i.details,
    i.authority_level,
    i.status,
    ke.embedding_role,
    ke.embedding <=> @embeddingVector::vector AS distance
  FROM {schema}.knowledge_embeddings ke
  JOIN {schema}.knowledge_items i ON i.id = ke.knowledge_item_id
  WHERE i.status = @status
    AND i.superseded_by IS NULL
    AND i.authority_level >= @minAuthority
    AND (ke.embedding_role = @preferredRole OR ke.embedding_role = @fallbackRole)
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
  status
FROM ranked
WHERE rn = 1
ORDER BY semantic_score DESC
LIMIT @limit
""";

    private static string SqlGetKnowledgeItemBase(string schema) => $"""
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

    private static string SqlGetRelatedKnowledge(string schema) => $"""
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

    private static string SqlLoadScopes(string schema) => $"""
SELECT
  ks.knowledge_item_id,
  ks.scope_type,
  ks.scope_value
FROM {schema}.knowledge_scopes ks
WHERE ks.knowledge_item_id = ANY(@ids)
ORDER BY ks.scope_type ASC
""";

    private static string SqlLoadLabelsAndTags(string schema) => $"""
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

    private static string SqlLoadEvidence(string schema) => $"""
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

    private static string SqlLoadSegments(string schema) => $"""
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

    private static string SqlLoadRelations(string schema) => $"""
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

    private static string ToKnowledgeStatusDb(KnowledgeStatus status) => status switch
    {
        KnowledgeStatus.Active => "active",
        KnowledgeStatus.Deprecated => "deprecated",
        KnowledgeStatus.Superseded => "superseded",
        KnowledgeStatus.Archived => "archived",
        _ => "active"
    };

    private static KnowledgeStatus ParseKnowledgeStatus(string dbValue) =>
        dbValue.ToLowerInvariant() switch
        {
            "active" => KnowledgeStatus.Active,
            "deprecated" => KnowledgeStatus.Deprecated,
            "superseded" => KnowledgeStatus.Superseded,
            "archived" => KnowledgeStatus.Archived,
            _ => KnowledgeStatus.Active
        };

    private static string[] TryReadStringArray(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return Array.Empty<string>();
        try
        {
            return JsonSerializer.Deserialize(json, InfrastructureJsonSerializerContext.Default.StringArray) ?? Array.Empty<string>();
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    private readonly record struct LexicalRow(
        Guid Id,
        RetrievalClass RetrievalClass,
        string Title,
        string Summary,
        string? Details,
        double LexicalScore,
        int AuthorityLevel,
        KnowledgeStatus Status);

    private readonly record struct SemanticRow(
        Guid Id,
        RetrievalClass RetrievalClass,
        string Title,
        string Summary,
        string? Details,
        double SemanticScore,
        int AuthorityLevel,
        KnowledgeStatus Status);

    private readonly record struct KnowledgeBaseRow(
        Guid Id,
        RetrievalClass RetrievalClass,
        string Title,
        string Summary,
        string? Details,
        int AuthorityLevel,
        KnowledgeStatus Status,
        string AuthorityLabel);

    private sealed class LabelsAndTags
    {
        public List<string> Labels { get; }
        public List<string> Tags { get; }

        public LabelsAndTags(List<string> labels, List<string> tags)
        {
            Labels = labels;
            Tags = tags;
        }
    }

    private sealed class ScopeBucket
    {
        public readonly List<string> Domains = new();
        public readonly List<string> Modules = new();
        public readonly List<string> Features = new();
        public readonly List<string> Layers = new();
        public readonly List<string> Concerns = new();
        public readonly List<string> Repos = new();
        public readonly List<string> Services = new();
        public readonly List<string> Symbols = new();

        public void Add(string type, string value)
        {
            var t = type.ToLowerInvariant();
            switch (t)
            {
                case "domain": Domains.Add(value); break;
                case "module": Modules.Add(value); break;
                case "feature": Features.Add(value); break;
                case "layer": Layers.Add(value); break;
                case "concern": Concerns.Add(value); break;
                case "repo": Repos.Add(value); break;
                case "service": Services.Add(value); break;
                case "symbol": Symbols.Add(value); break;
            }
        }

        public ScopeFilterDto ToDto() => new(
            Domains.Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            Modules.Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            Features.Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            Layers.Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            Concerns.Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            Repos.Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            Services.Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            Symbols.Distinct(StringComparer.OrdinalIgnoreCase).ToList());
    }
}
