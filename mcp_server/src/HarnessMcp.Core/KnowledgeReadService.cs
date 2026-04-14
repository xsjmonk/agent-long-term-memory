using HarnessMcp.Contracts;

namespace HarnessMcp.Core;

public sealed class KnowledgeReadService(IRequestValidator validator, IKnowledgeRepository repository)
    : IKnowledgeReadService
{
    public async ValueTask<GetKnowledgeItemResponse> GetKnowledgeItemAsync(
        GetKnowledgeItemRequest request,
        CancellationToken cancellationToken)
    {
        validator.Validate(request);
        return await repository.GetKnowledgeItemAsync(request, cancellationToken).ConfigureAwait(false);
    }
}
