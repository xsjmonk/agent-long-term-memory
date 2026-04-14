namespace HarnessMcp.Core;

public sealed record StoredEmbeddingMetadata(
    string ModelName,
    string? ModelVersion,
    int Dimension,
    bool? NormalizeEmbeddings,
    bool HasRows,
    string SelectedEmbeddingRole);

