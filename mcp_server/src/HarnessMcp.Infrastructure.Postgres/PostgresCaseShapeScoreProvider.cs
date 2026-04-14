using System.Text.Json;
using HarnessMcp.Contracts;
using HarnessMcp.Core;
using Npgsql;

namespace HarnessMcp.Infrastructure.Postgres;

/// <summary>
/// Computes SimilarCase structural scores backed by PostgreSQL `case_shapes`.
/// </summary>
public sealed class PostgresCaseShapeScoreProvider(NpgsqlDataSource dataSource, string schema) : ICaseShapeScoreProvider
{
    public double ComputeScore(SearchKnowledgeRequest request, Guid knowledgeItemId)
    {
        if (request.QueryKind != QueryKind.SimilarCase)
            return 0d;

        SimilarCaseShapeDto? requestedShape;
        try
        {
            // For SimilarCase, Core serializes `chunk.TaskShape` into `QueryText`.
            requestedShape = JsonSerializer.Deserialize(
                request.QueryText,
                AppJsonSerializerContext.Default.SimilarCaseShapeDto);
        }
        catch
        {
            return 0d;
        }

        if (requestedShape is null)
            return 0d;

        using var conn = dataSource.CreateConnection();
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"""
            SELECT
              task_type,
              feature_shape,
              engine_change_allowed,
              likely_layers,
              risk_signals,
              complexity
            FROM {schema}.case_shapes
            WHERE knowledge_item_id = @id
            LIMIT 1
            """;

        cmd.Parameters.AddWithValue("id", knowledgeItemId);

        using var reader = cmd.ExecuteReader();
        if (!reader.Read())
            return 0d;

        var dbShape = new SimilarCaseShapeDto(
            TaskType: reader.IsDBNull(0) ? string.Empty : reader.GetString(0),
            FeatureShape: reader.IsDBNull(1) ? string.Empty : reader.GetString(1),
            EngineChangeAllowed: reader.IsDBNull(2) ? false : reader.GetBoolean(2),
            LikelyLayers: ReadStringArray(reader, 3),
            RiskSignals: ReadStringArray(reader, 4),
            Complexity: reader.IsDBNull(5) ? null : reader.GetString(5));

        // Normalized 0.0 - 1.0 by contract through CaseShapeMatcher.
        return CaseShapeMatcher.Match(requestedShape, dbShape);
    }

    private static IReadOnlyList<string> ReadStringArray(NpgsqlDataReader reader, int idx)
    {
        if (reader.IsDBNull(idx))
            return Array.Empty<string>();

        // likely_layers / risk_signals are jsonb arrays. Read as string and parse.
        var json = reader.GetString(idx);
        if (string.IsNullOrWhiteSpace(json))
            return Array.Empty<string>();

        try
        {
            return JsonSerializer.Deserialize<string[]>(json) ?? Array.Empty<string>();
        }
        catch
        {
            return Array.Empty<string>();
        }
    }
}

