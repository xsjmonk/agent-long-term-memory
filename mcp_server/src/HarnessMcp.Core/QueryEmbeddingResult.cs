using System;

namespace HarnessMcp.Core;

public sealed record QueryEmbeddingResult(
    ReadOnlyMemory<float> Vector,
    string Provider,
    string ModelName,
    string? ModelVersion,
    bool NormalizeEmbeddings,
    int Dimension,
    bool FallbackMode,
    string TextProcessingId,
    string VectorSpaceId,
    int InputCharCount,
    int EffectiveTextCharCount,
    bool Truncated,
    IReadOnlyList<string> Warnings);

