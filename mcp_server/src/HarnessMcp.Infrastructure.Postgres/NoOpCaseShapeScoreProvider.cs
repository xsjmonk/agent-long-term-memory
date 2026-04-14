using HarnessMcp.Contracts;
using HarnessMcp.Core;

namespace HarnessMcp.Infrastructure.Postgres;

public sealed class NoOpCaseShapeScoreProvider : ICaseShapeScoreProvider
{
    public double ComputeScore(SearchKnowledgeRequest request, Guid knowledgeItemId) => 0;
}
