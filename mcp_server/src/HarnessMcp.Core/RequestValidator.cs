using HarnessMcp.Contracts;

namespace HarnessMcp.Core;

public sealed class RequestValidator(RetrievalConfig retrieval) : IRequestValidator
{
    private readonly RetrievalConfig _retrieval = retrieval;

    public void Validate(SearchKnowledgeRequest request)
    {
        RequireId(request.RequestId);
        if (string.IsNullOrWhiteSpace(request.QueryText))
            throw new ValidationException("QueryText is required.");
        RequireTopK(request.TopK);
        if (request.QueryText.Length > _retrieval.MaxQueryTextLength)
            throw new ValidationException("QueryText exceeds MaxQueryTextLength.");
    }

    public void Validate(RetrieveMemoryByChunksRequest request)
    {
        RequireId(request.RequestId);
        RequireId(request.TaskId);
        if (request.RetrievalChunks is null || request.RetrievalChunks.Count == 0)
            throw new ValidationException("RetrievalChunks must not be empty.");

        foreach (var chunk in request.RetrievalChunks)
        {
            if (string.IsNullOrWhiteSpace(chunk.ChunkId))
                throw new ValidationException("ChunkId is required.");

            if (chunk.ChunkType == ChunkType.SimilarCase)
            {
                if (chunk.TaskShape is null)
                    throw new ValidationException($"Chunk {chunk.ChunkId} similar_case requires TaskShape.");
                if (string.IsNullOrWhiteSpace(chunk.Text))
                    throw new ValidationException($"Chunk {chunk.ChunkId} similar_case requires Text.");
                if (chunk.Text.Length > _retrieval.MaxChunkTextLength)
                    throw new ValidationException($"Chunk {chunk.ChunkId} text exceeds MaxChunkTextLength.");
            }
            else
            {
                if (string.IsNullOrWhiteSpace(chunk.Text))
                    throw new ValidationException($"Chunk {chunk.ChunkId} requires Text.");
                if (chunk.Text.Length > _retrieval.MaxChunkTextLength)
                    throw new ValidationException($"Chunk {chunk.ChunkId} text exceeds MaxChunkTextLength.");
            }
        }
    }

    public void Validate(MergeRetrievalResultsRequest request)
    {
        RequireId(request.RequestId);
        RequireId(request.TaskId);
        if (request.Retrieved is null)
            throw new ValidationException("Retrieved is required.");
        if (!string.Equals(request.TaskId, request.Retrieved.TaskId, StringComparison.Ordinal))
            throw new ValidationException("TaskId mismatch with Retrieved.");
    }

    public void Validate(BuildMemoryContextPackRequest request)
    {
        RequireId(request.RequestId);
        RequireId(request.TaskId);
        if (request.Retrieved is null || request.Merged is null)
            throw new ValidationException("Retrieved and Merged are required.");
        if (!string.Equals(request.TaskId, request.Retrieved.TaskId, StringComparison.Ordinal))
            throw new ValidationException("TaskId mismatch with Retrieved.");
        if (!string.Equals(request.TaskId, request.Merged.TaskId, StringComparison.Ordinal))
            throw new ValidationException("TaskId mismatch with Merged.");
    }

    public void Validate(GetKnowledgeItemRequest request)
    {
        RequireId(request.RequestId);
        if (request.KnowledgeItemId == Guid.Empty)
            throw new ValidationException("KnowledgeItemId is required.");
    }

    public void Validate(GetRelatedKnowledgeRequest request)
    {
        RequireId(request.RequestId);
        if (request.KnowledgeItemId == Guid.Empty)
            throw new ValidationException("KnowledgeItemId is required.");
        if (request.RelationTypes is null || request.RelationTypes.Count == 0)
            throw new ValidationException("RelationTypes must not be empty.");
        RequireTopK(request.TopK);
    }

    private void RequireId(string? id)
    {
        if (string.IsNullOrWhiteSpace(id))
            throw new ValidationException("RequestId/TaskId is required.");
    }

    private void RequireTopK(int topK)
    {
        if (topK <= 0)
            throw new ValidationException("TopK must be positive.");
        if (topK > _retrieval.MaxTopK)
            throw new ValidationException("TopK exceeds MaxTopK.");
    }
}
