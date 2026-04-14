using HarnessMcp.Contracts;

namespace HarnessMcp.Core;

public sealed class RelatedKnowledgeService(IRequestValidator validator, IKnowledgeRepository repository)
    : IRelatedKnowledgeService
{
    public async ValueTask<GetRelatedKnowledgeResponse> GetRelatedKnowledgeAsync(
        GetRelatedKnowledgeRequest request,
        CancellationToken cancellationToken)
    {
        validator.Validate(request);
        var resp = await repository.GetRelatedKnowledgeAsync(request, cancellationToken).ConfigureAwait(false);
        var items = resp.Items
            .OrderByDescending(x => x.RelationStrength)
            .ThenByDescending(x => x.Authority)
            .Take(request.TopK)
            .ToList();
        return resp with { Items = items };
    }
}
