namespace HarnessMcp.Core;

public sealed record EmbeddingCompatibilityResult(
    bool IsCompatible,
    bool AllowLexicalFallback,
    bool SemanticQualityDegraded,
    string Reason,
    IReadOnlyList<string> DegradationSignals);

