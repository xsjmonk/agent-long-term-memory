using HarnessMcp.Contracts;
using HarnessMcp.Core;

namespace HarnessMcp.Infrastructure.Postgres;

public sealed class NoOpCaseShapeScoreProvider : ICaseShapeScoreProvider
{
    public double ComputeScore(Guid knowledgeItemId, SimilarCaseShapeDto? requestedShape) => 0;
}
