using HarnessMcp.Contracts;

namespace HarnessMcp.Core;

public sealed class SupersessionPolicy : ISupersessionPolicy
{
    public bool IsVisible(KnowledgeStatus status, Guid? supersededBy)
    {
        if (status == KnowledgeStatus.Superseded)
            return false;
        if (supersededBy is not null && supersededBy != Guid.Empty)
            return false;
        return status != KnowledgeStatus.Archived;
    }
}
