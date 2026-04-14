using HarnessMcp.Contracts;
using Npgsql;

namespace HarnessMcp.Infrastructure.Postgres;

public static class PostgresRowMappers
{
    public readonly record struct LexicalRow(
        Guid Id,
        RetrievalClass RetrievalClass,
        string Title,
        string Summary,
        string? Details,
        double LexicalScore,
        int AuthorityLevel,
        KnowledgeStatus Status,
        DateTimeOffset UpdatedAtUtc);

    public readonly record struct SemanticRow(
        Guid Id,
        RetrievalClass RetrievalClass,
        string Title,
        string Summary,
        string? Details,
        double SemanticScore,
        int AuthorityLevel,
        KnowledgeStatus Status,
        DateTimeOffset UpdatedAtUtc);

    public readonly record struct KnowledgeBaseRow(
        Guid Id,
        RetrievalClass RetrievalClass,
        string Title,
        string Summary,
        string? Details,
        int AuthorityLevel,
        KnowledgeStatus Status,
        string AuthorityLabel);

    public static LexicalRow MapLexicalRow(NpgsqlDataReader reader) =>
        new(
            reader.GetGuid(0),
            ParseRetrievalClass(reader.GetString(1)),
            reader.GetString(2),
            reader.GetString(3),
            reader.IsDBNull(4) ? null : reader.GetString(4),
            reader.GetFloat(5),
            reader.GetInt32(6),
            ParseKnowledgeStatus(reader.GetString(7)),
            reader.GetFieldValue<DateTimeOffset>(8));

    public static SemanticRow MapSemanticRow(NpgsqlDataReader reader) =>
        new(
            reader.GetGuid(0),
            ParseRetrievalClass(reader.GetString(1)),
            reader.GetString(2),
            reader.GetString(3),
            reader.IsDBNull(4) ? null : reader.GetString(4),
            reader.GetDouble(5),
            reader.GetInt32(6),
            ParseKnowledgeStatus(reader.GetString(7)),
            reader.GetFieldValue<DateTimeOffset>(8));

    public static KnowledgeBaseRow MapKnowledgeBaseRow(NpgsqlDataReader reader) =>
        new(
            reader.GetGuid(0),
            ParseRetrievalClass(reader.GetString(1)),
            reader.GetString(2),
            reader.GetString(3),
            reader.IsDBNull(4) ? null : reader.GetString(4),
            reader.GetInt32(5),
            ParseKnowledgeStatus(reader.GetString(6)),
            reader.GetString(7));

    public static RetrievalClass ParseRetrievalClass(string dbValue) =>
        dbValue.ToLowerInvariant() switch
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

    public static KnowledgeStatus ParseKnowledgeStatus(string dbValue) =>
        dbValue.ToLowerInvariant() switch
        {
            "active" => KnowledgeStatus.Active,
            "deprecated" => KnowledgeStatus.Deprecated,
            "superseded" => KnowledgeStatus.Superseded,
            "archived" => KnowledgeStatus.Archived,
            _ => KnowledgeStatus.Active
        };
}

