using System.Threading.Tasks;
using HarnessMcp.Core;
using HarnessMcp.Contracts;
using Npgsql;

namespace HarnessMcp.Infrastructure.Postgres;

public sealed class EmbeddingMetadataInspector : IEmbeddingMetadataInspector
{
    private readonly AppConfig _config;
    private readonly NpgsqlDataSource _dataSource;

    public EmbeddingMetadataInspector(AppConfig config, NpgsqlDataSource dataSource)
    {
        _config = config;
        _dataSource = dataSource;
    }

    public async ValueTask<StoredEmbeddingMetadata?> GetMetadataForRoleAsync(
        QueryKind queryKind,
        CancellationToken cancellationToken)
    {
        var schema = _config.Database.SearchSchema;

        var primaryRole = queryKind.ToString();
        var fallbackRole = "normalized_retrieval_text";

        var primary = await TryLoadAsync(schema, primaryRole, cancellationToken).ConfigureAwait(false);
        if (primary is not null)
            return primary with { SelectedEmbeddingRole = primaryRole, HasRows = true };

        var fallback = await TryLoadAsync(schema, fallbackRole, cancellationToken).ConfigureAwait(false);
        if (fallback is not null)
            return fallback with { SelectedEmbeddingRole = fallbackRole, HasRows = true };

        return new StoredEmbeddingMetadata(
            ModelName: primaryRole,
            ModelVersion: null,
            Dimension: 0,
            NormalizeEmbeddings: null,
            HasRows: false,
            SelectedEmbeddingRole: primaryRole);
    }

    private async ValueTask<StoredEmbeddingMetadata?> TryLoadAsync(
        string schema,
        string embeddingRole,
        CancellationToken cancellationToken)
    {
        await using var conn = await _dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();

        cmd.CommandText = $"""
            SELECT
              ke.model_name,
              ke.model_version,
              vector_dims(ke.embedding) AS dimension
            FROM {schema}.knowledge_embeddings ke
            WHERE ke.embedding_role = @role
            LIMIT 1
            """;

        cmd.Parameters.AddWithValue("role", embeddingRole);

        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            return null;

        var modelName = reader.GetString(0);
        var modelVersion = reader.IsDBNull(1) ? null : reader.GetString(1);
        var dimension = reader.GetInt32(2);

        // knowledge_embeddings in schema does not persist normalization/text-processing identity.
        return new StoredEmbeddingMetadata(
            ModelName: modelName,
            ModelVersion: modelVersion,
            Dimension: dimension,
            NormalizeEmbeddings: null,
            HasRows: true,
            SelectedEmbeddingRole: embeddingRole);
    }
}

