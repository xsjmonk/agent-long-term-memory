using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using HarnessMcp.Contracts;
using HarnessMcp.Core;
using Xunit;

namespace HarnessMcp.Tests.Integration;

public sealed class EmbeddingSemanticPathTests
{
    private sealed class NoOpValidator : IRequestValidator
    {
        public void Validate(SearchKnowledgeRequest request) { }
        public void Validate(RetrieveMemoryByChunksRequest request) { }
        public void Validate(MergeRetrievalResultsRequest request) { }
        public void Validate(BuildMemoryContextPackRequest request) { }
        public void Validate(GetKnowledgeItemRequest request) { }
        public void Validate(GetRelatedKnowledgeRequest request) { }
    }

    private sealed class PassThroughScopeNormalizer : IScopeNormalizer
    {
        public ScopeFilterDto Normalize(ScopeFilterDto scopes) => scopes;
    }

    private sealed class FakeRanking : IHybridRankingService
    {
        public IReadOnlyList<KnowledgeCandidateDto> Rank(
            IReadOnlyList<KnowledgeCandidateDto> lexical,
            IReadOnlyList<KnowledgeCandidateDto> semantic,
            SearchKnowledgeRequest request)
            => semantic.Count > 0 ? semantic : lexical;
    }

    private sealed class FakeRepository : IKnowledgeRepository
    {
        public int LexicalCallCount { get; private set; }
        public int SemanticCallCount { get; private set; }

        public ValueTask<IReadOnlyList<KnowledgeCandidateDto>> SearchLexicalAsync(
            SearchKnowledgeRequest request,
            CancellationToken cancellationToken)
        {
            LexicalCallCount++;
            var item = MakeCandidate(request.QueryKind, RetrievalClass.Decision);
            return ValueTask.FromResult<IReadOnlyList<KnowledgeCandidateDto>>([item]);
        }

        public ValueTask<IReadOnlyList<KnowledgeCandidateDto>> SearchSemanticAsync(
            SearchKnowledgeRequest request,
            ReadOnlyMemory<float> embedding,
            CancellationToken cancellationToken)
        {
            SemanticCallCount++;
            var item = MakeCandidate(request.QueryKind, RetrievalClass.Decision) with { SemanticScore = 1.0 };
            return ValueTask.FromResult<IReadOnlyList<KnowledgeCandidateDto>>([item]);
        }

        public ValueTask<GetKnowledgeItemResponse> GetKnowledgeItemAsync(GetKnowledgeItemRequest request, CancellationToken cancellationToken) =>
            throw new NotImplementedException();

        public ValueTask<GetRelatedKnowledgeResponse> GetRelatedKnowledgeAsync(GetRelatedKnowledgeRequest request, CancellationToken cancellationToken) =>
            throw new NotImplementedException();

        private static KnowledgeCandidateDto MakeCandidate(QueryKind kind, RetrievalClass retrievalClass) =>
            new(
                KnowledgeItemId: Guid.NewGuid(),
                RetrievalClass: retrievalClass,
                Title: "t",
                Summary: "s",
                Details: null,
                SemanticScore: 0,
                LexicalScore: 1,
                ScopeScore: 0,
                AuthorityScore: 0,
                CaseShapeScore: 0,
                FinalScore: 0,
                Authority: AuthorityLevel.Draft,
                Status: KnowledgeStatus.Active,
                Scopes: ScopeDtos.Empty,
                Labels: Array.Empty<string>(),
                Tags: Array.Empty<string>(),
                Evidence: Array.Empty<EvidenceDto>(),
                SupportedByChunks: Array.Empty<string>(),
                SupportedByQueryKinds: Array.Empty<string>());
    }

    private sealed class FakeEmbeddingService : IQueryEmbeddingService
    {
        private readonly QueryEmbeddingResult _result;
        public FakeEmbeddingService(QueryEmbeddingResult result) => _result = result;

        public ValueTask<QueryEmbeddingResult> EmbedAsync(SearchKnowledgeRequest request, CancellationToken cancellationToken) =>
            ValueTask.FromResult(_result);
    }

    private sealed class FakeMetadataInspector : IEmbeddingMetadataInspector
    {
        private readonly StoredEmbeddingMetadata? _stored;
        public FakeMetadataInspector(StoredEmbeddingMetadata? stored) => _stored = stored;

        public ValueTask<StoredEmbeddingMetadata?> GetMetadataForRoleAsync(QueryKind queryKind, CancellationToken cancellationToken) =>
            ValueTask.FromResult(_stored);
    }

    private static RetrieveMemoryByChunksResponse RunChunkRetrieval(
        KnowledgeSearchService searchService,
        RetrieveMemoryByChunksRequest retrieveRequest)
    {
        var planner = new ChunkQueryPlanner();
        var svc = new ChunkRetrievalService(new NoOpValidator(), planner, searchService);
        return svc.RetrieveMemoryByChunksAsync(retrieveRequest, CancellationToken.None).Result;
    }

    [Fact]
    public void ScenarioA_SemanticActiveButDegradedByTruncation()
    {
        var config = new EmbeddingConfig
        {
            QueryEmbeddingProvider = "LocalHttp",
            Model = "cfg-model",
            RequireCompatibilityCheck = true,
            AllowLexicalFallbackOnSemanticIncompatibility = true,
            AllowHashingFallback = false
        };

        var repo = new FakeRepository();
        var embeddingResult = new QueryEmbeddingResult(
            Vector: new float[] { 0f, 1f },
            Provider: "builder",
            ModelName: "builder-model",
            ModelVersion: null,
            NormalizeEmbeddings: false,
            Dimension: 2,
            FallbackMode: false,
            TextProcessingId: "tp",
            VectorSpaceId: "vs",
            InputCharCount: 1,
            EffectiveTextCharCount: 1,
            Truncated: true,
            Warnings: Array.Empty<string>());

        var metadata = new StoredEmbeddingMetadata(
            ModelName: "builder-model",
            ModelVersion: null,
            Dimension: 2,
            NormalizeEmbeddings: null,
            HasRows: true,
            SelectedEmbeddingRole: "CoreTask");

        var search = new KnowledgeSearchService(
            validator: new NoOpValidator(),
            scopeNormalizer: new PassThroughScopeNormalizer(),
            repository: repo,
            embeddingService: new FakeEmbeddingService(embeddingResult),
            ranking: new FakeRanking(),
            embeddingConfig: config,
            metadataInspector: new FakeMetadataInspector(metadata),
            compatibilityChecker: new EmbeddingCompatibilityChecker());

        var req = new RetrieveMemoryByChunksRequest(
            SchemaVersion: "1.0",
            RequestId: "task",
            TaskId: "t",
            RequirementIntent: new RequirementIntentDto(
                TaskType: "x",
                Domain: null,
                Module: null,
                Feature: null,
                HardConstraints: Array.Empty<string>(),
                RiskSignals: Array.Empty<string>()),
            RetrievalChunks: [
                new RetrievalChunkDto(
                    ChunkId: "c1",
                    ChunkType: ChunkType.CoreTask,
                    Text: "q1",
                    StructuredScopes: null,
                    TaskShape: null)
            ],
            SearchProfile: new ChunkSearchProfileDto(
                ActiveOnly: true,
                MinimumAuthority: AuthorityLevel.Draft,
                MaxItemsPerChunk: 1,
                RequireTypeSeparation: false));

        var resp = RunChunkRetrieval(search, req);
        Assert.Single(resp.ChunkResults);
        // Semantic should have been used => diagnostics should show actual builder model name.
        Assert.Equal(1, repo.SemanticCallCount);
        Assert.Equal("semantic-active:degraded:builder-model:text-truncated-before-embedding", resp.ChunkResults[0].Diagnostics.QueryEmbeddingModel);
        Assert.Equal("CoreTask|degraded", resp.ChunkResults[0].Diagnostics.EmbeddingRoleUsed);
        Assert.Contains("chunk:c1 semantic active with degraded quality: text truncated before embedding", resp.Notes);
    }

    [Fact]
    public void ScenarioB_SemanticActiveButDegradedByBuilderWarning()
    {
        var config = new EmbeddingConfig
        {
            QueryEmbeddingProvider = "LocalHttp",
            Model = "cfg-model",
            RequireCompatibilityCheck = true,
            AllowLexicalFallbackOnSemanticIncompatibility = true,
            AllowHashingFallback = false
        };

        var repo = new FakeRepository();
        var embeddingResult = new QueryEmbeddingResult(
            Vector: new float[] { 0f, 1f },
            Provider: "builder",
            ModelName: "builder-model",
            ModelVersion: null,
            NormalizeEmbeddings: false,
            Dimension: 2,
            FallbackMode: false,
            TextProcessingId: "tp",
            VectorSpaceId: "vs",
            InputCharCount: 1,
            EffectiveTextCharCount: 1,
            Truncated: false,
            Warnings: ["hashing fallback active"]);

        var metadata = new StoredEmbeddingMetadata(
            ModelName: "builder-model",
            ModelVersion: null,
            Dimension: 2,
            NormalizeEmbeddings: null,
            HasRows: true,
            SelectedEmbeddingRole: "CoreTask");

        var search = new KnowledgeSearchService(
            validator: new NoOpValidator(),
            scopeNormalizer: new PassThroughScopeNormalizer(),
            repository: repo,
            embeddingService: new FakeEmbeddingService(embeddingResult),
            ranking: new FakeRanking(),
            embeddingConfig: config,
            metadataInspector: new FakeMetadataInspector(metadata),
            compatibilityChecker: new EmbeddingCompatibilityChecker());

        var req = new RetrieveMemoryByChunksRequest(
            SchemaVersion: "1.0",
            RequestId: "task",
            TaskId: "t",
            RequirementIntent: new RequirementIntentDto(
                TaskType: "x",
                Domain: null,
                Module: null,
                Feature: null,
                HardConstraints: Array.Empty<string>(),
                RiskSignals: Array.Empty<string>()),
            RetrievalChunks: [
                new RetrievalChunkDto(
                    ChunkId: "c1",
                    ChunkType: ChunkType.CoreTask,
                    Text: "q1",
                    StructuredScopes: null,
                    TaskShape: null)
            ],
            SearchProfile: new ChunkSearchProfileDto(
                ActiveOnly: true,
                MinimumAuthority: AuthorityLevel.Draft,
                MaxItemsPerChunk: 1,
                RequireTypeSeparation: false));

        var resp = RunChunkRetrieval(search, req);
        Assert.Equal(1, repo.SemanticCallCount);
        Assert.Equal("semantic-active:degraded:builder-model:builder-warning:hashing-fallback-active", resp.ChunkResults[0].Diagnostics.QueryEmbeddingModel);
        Assert.Equal("CoreTask|degraded", resp.ChunkResults[0].Diagnostics.EmbeddingRoleUsed);
        Assert.Contains("chunk:c1 semantic active with degraded quality: builder warning hashing fallback active", resp.Notes);
    }

    [Fact]
    public void ScenarioC_TextProcessingMismatch_DegradesOnly()
    {
        var config = new EmbeddingConfig
        {
            QueryEmbeddingProvider = "LocalHttp",
            Model = "cfg-model",
            RequireCompatibilityCheck = true,
            AllowLexicalFallbackOnSemanticIncompatibility = true,
            AllowHashingFallback = false,
            ExpectedTextProcessingId = "tp-expected",
            TreatTextProcessingMismatchAsIncompatible = false
        };

        var repo = new FakeRepository();
        var embeddingResult = new QueryEmbeddingResult(
            Vector: new float[] { 0f, 1f },
            Provider: "builder",
            ModelName: "builder-model",
            ModelVersion: null,
            NormalizeEmbeddings: false,
            Dimension: 2,
            FallbackMode: false,
            TextProcessingId: "tp-mismatch",
            VectorSpaceId: "vs",
            InputCharCount: 1,
            EffectiveTextCharCount: 1,
            Truncated: false,
            Warnings: Array.Empty<string>());

        var metadata = new StoredEmbeddingMetadata(
            ModelName: "builder-model",
            ModelVersion: null,
            Dimension: 2,
            NormalizeEmbeddings: null,
            HasRows: true,
            SelectedEmbeddingRole: "CoreTask");

        var search = new KnowledgeSearchService(
            validator: new NoOpValidator(),
            scopeNormalizer: new PassThroughScopeNormalizer(),
            repository: repo,
            embeddingService: new FakeEmbeddingService(embeddingResult),
            ranking: new FakeRanking(),
            embeddingConfig: config,
            metadataInspector: new FakeMetadataInspector(metadata),
            compatibilityChecker: new EmbeddingCompatibilityChecker());

        var req = new RetrieveMemoryByChunksRequest(
            SchemaVersion: "1.0",
            RequestId: "task",
            TaskId: "t",
            RequirementIntent: new RequirementIntentDto(
                TaskType: "x",
                Domain: null,
                Module: null,
                Feature: null,
                HardConstraints: Array.Empty<string>(),
                RiskSignals: Array.Empty<string>()),
            RetrievalChunks: [
                new RetrievalChunkDto(
                    ChunkId: "c1",
                    ChunkType: ChunkType.CoreTask,
                    Text: "q1",
                    StructuredScopes: null,
                    TaskShape: null)
            ],
            SearchProfile: new ChunkSearchProfileDto(
                ActiveOnly: true,
                MinimumAuthority: AuthorityLevel.Draft,
                MaxItemsPerChunk: 1,
                RequireTypeSeparation: false));

        var resp = RunChunkRetrieval(search, req);
        Assert.Equal(1, repo.SemanticCallCount);
        Assert.Equal("semantic-active:degraded:builder-model:text-processing-mismatch", resp.ChunkResults[0].Diagnostics.QueryEmbeddingModel);
        Assert.Equal("CoreTask|degraded", resp.ChunkResults[0].Diagnostics.EmbeddingRoleUsed);
        Assert.Contains("chunk:c1 semantic active with degraded quality: text-processing-mismatch", resp.Notes);
    }

    [Fact]
    public void ScenarioD_TextProcessingMismatch_TreatedAsIncompatible_FallsBackToLexical()
    {
        var config = new EmbeddingConfig
        {
            QueryEmbeddingProvider = "LocalHttp",
            Model = "cfg-model",
            RequireCompatibilityCheck = true,
            AllowLexicalFallbackOnSemanticIncompatibility = true,
            AllowHashingFallback = false,
            ExpectedTextProcessingId = "tp-expected",
            TreatTextProcessingMismatchAsIncompatible = true
        };

        var repo = new FakeRepository();
        var embeddingResult = new QueryEmbeddingResult(
            Vector: new float[] { 0f, 1f },
            Provider: "builder",
            ModelName: "builder-model",
            ModelVersion: null,
            NormalizeEmbeddings: false,
            Dimension: 2,
            FallbackMode: false,
            TextProcessingId: "tp-mismatch",
            VectorSpaceId: "vs",
            InputCharCount: 1,
            EffectiveTextCharCount: 1,
            Truncated: false,
            Warnings: Array.Empty<string>());

        var metadata = new StoredEmbeddingMetadata(
            ModelName: "builder-model",
            ModelVersion: null,
            Dimension: 2,
            NormalizeEmbeddings: null,
            HasRows: true,
            SelectedEmbeddingRole: "CoreTask");

        var search = new KnowledgeSearchService(
            validator: new NoOpValidator(),
            scopeNormalizer: new PassThroughScopeNormalizer(),
            repository: repo,
            embeddingService: new FakeEmbeddingService(embeddingResult),
            ranking: new FakeRanking(),
            embeddingConfig: config,
            metadataInspector: new FakeMetadataInspector(metadata),
            compatibilityChecker: new EmbeddingCompatibilityChecker());

        var req = new RetrieveMemoryByChunksRequest(
            SchemaVersion: "1.0",
            RequestId: "task",
            TaskId: "t",
            RequirementIntent: new RequirementIntentDto(
                TaskType: "x",
                Domain: null,
                Module: null,
                Feature: null,
                HardConstraints: Array.Empty<string>(),
                RiskSignals: Array.Empty<string>()),
            RetrievalChunks: [
                new RetrievalChunkDto(
                    ChunkId: "c1",
                    ChunkType: ChunkType.CoreTask,
                    Text: "q1",
                    StructuredScopes: null,
                    TaskShape: null)
            ],
            SearchProfile: new ChunkSearchProfileDto(
                ActiveOnly: true,
                MinimumAuthority: AuthorityLevel.Draft,
                MaxItemsPerChunk: 1,
                RequireTypeSeparation: false));

        var resp = RunChunkRetrieval(search, req);
        Assert.Equal(0, repo.SemanticCallCount);
        Assert.Equal("lexical-only:fallback:text-processing-mismatch", resp.ChunkResults[0].Diagnostics.QueryEmbeddingModel);
        Assert.Equal("lexical-only", resp.ChunkResults[0].Diagnostics.EmbeddingRoleUsed);
        Assert.Contains(resp.Notes, n => n.Contains("chunk:c1 semantic degraded to lexical-only"));
        Assert.Contains("text processing mismatch", string.Join("|", resp.Notes));
    }

    [Fact]
    public void ScenarioE_NoOpProvider_DisablesSemanticAndAddsChunkNote()
    {
        var config = new EmbeddingConfig
        {
            QueryEmbeddingProvider = "NoOp",
            Model = "cfg-model",
            RequireCompatibilityCheck = true,
            AllowLexicalFallbackOnSemanticIncompatibility = true,
            AllowHashingFallback = false
        };

        var repo = new FakeRepository();
        var search = new KnowledgeSearchService(
            validator: new NoOpValidator(),
            scopeNormalizer: new PassThroughScopeNormalizer(),
            repository: repo,
            embeddingService: new FakeEmbeddingService(new QueryEmbeddingResult(
                Vector: ReadOnlyMemory<float>.Empty,
                Provider: "noop",
                ModelName: "noop",
                ModelVersion: null,
                NormalizeEmbeddings: false,
                Dimension: 0,
                FallbackMode: false,
                TextProcessingId: "noop",
                VectorSpaceId: "noop",
                InputCharCount: 0,
                EffectiveTextCharCount: 0,
                Truncated: false,
                Warnings: Array.Empty<string>())),
            ranking: new FakeRanking(),
            embeddingConfig: config,
            metadataInspector: new FakeMetadataInspector(null),
            compatibilityChecker: new EmbeddingCompatibilityChecker());

        var req = new RetrieveMemoryByChunksRequest(
            SchemaVersion: "1.0",
            RequestId: "task",
            TaskId: "t",
            RequirementIntent: new RequirementIntentDto(
                TaskType: "x",
                Domain: null,
                Module: null,
                Feature: null,
                HardConstraints: Array.Empty<string>(),
                RiskSignals: Array.Empty<string>()),
            RetrievalChunks: [
                new RetrievalChunkDto(
                    ChunkId: "c1",
                    ChunkType: ChunkType.CoreTask,
                    Text: "q1",
                    StructuredScopes: null,
                    TaskShape: null)
            ],
            SearchProfile: new ChunkSearchProfileDto(
                ActiveOnly: true,
                MinimumAuthority: AuthorityLevel.Draft,
                MaxItemsPerChunk: 1,
                RequireTypeSeparation: false));

        var resp = RunChunkRetrieval(search, req);
        Assert.Equal(0, repo.SemanticCallCount);
        Assert.Equal("semantic-disabled:noop", resp.ChunkResults[0].Diagnostics.QueryEmbeddingModel);
        Assert.Equal("semantic-disabled", resp.ChunkResults[0].Diagnostics.EmbeddingRoleUsed);
        Assert.Contains("chunk:c1 semantic disabled: noop provider", resp.Notes);
    }
}

