using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using HarnessMcp.AgentClient.Transport;
using HarnessMcp.AgentClient.Planning;
using HarnessMcp.AgentClient.Support;
using HarnessMcp.Contracts;

namespace HarnessMcp.AgentClient.Tests;

internal sealed class FakePlanningModelClient : IPlanningModelClient
{
    private readonly Queue<string> _jsonOutputs;
    public List<(string SystemPrompt, string UserPrompt)> Calls { get; } = new();

    public FakePlanningModelClient(IEnumerable<string> jsonOutputs)
    {
        _jsonOutputs = new Queue<string>(jsonOutputs);
    }

    public Task<string> CompleteJsonAsync(string systemPrompt, string userPrompt, CancellationToken cancellationToken)
    {
        Calls.Add((systemPrompt, userPrompt));
        if (_jsonOutputs.Count == 0)
            throw new InvalidOperationException("FakePlanningModelClient: no more queued outputs.");
        return Task.FromResult(_jsonOutputs.Dequeue());
    }
}

internal sealed class FakeMcpToolClient : IMcpToolClient
{
    public ServerInfoResponse ServerInfo { get; set; } = FakeServerInfo();

    public RetrieveMemoryByChunksResponse Retrieved { get; set; } = FakeRetrieved();
    public MergeRetrievalResultsResponse Merged { get; set; } = FakeMerged();
    public BuildMemoryContextPackResponse ContextPack { get; set; } = FakeContextPack();

    private readonly Dictionary<Guid, GetKnowledgeItemResponse> _knowledgeItems = new();

    public List<string> ToolCallOrder { get; } = new();
    public List<SearchKnowledgeRequest> SearchCalls { get; } = new();
    public List<Guid> KnowledgeItemCalls { get; } = new();

    public void SeedKnowledgeItem(Guid id, RetrievalClass cls, string title)
    {
        _knowledgeItems[id] = new GetKnowledgeItemResponse(
            SchemaVersion: "1.0",
            Kind: "get_knowledge_item",
            RequestId: "seed",
            Item: new KnowledgeCandidateDto(
                KnowledgeItemId: id,
                RetrievalClass: cls,
                Title: title,
                Summary: "s",
                Details: null,
                SemanticScore: 0,
                LexicalScore: 0,
                ScopeScore: 0,
                AuthorityScore: 0,
                CaseShapeScore: 0,
                FinalScore: 1,
                Authority: AuthorityLevel.Reviewed,
                Status: KnowledgeStatus.Active,
                Scopes: new ScopeFilterDto(
                    Domains: Array.Empty<string>(),
                    Modules: Array.Empty<string>(),
                    Features: Array.Empty<string>(),
                    Layers: Array.Empty<string>(),
                    Concerns: Array.Empty<string>(),
                    Repos: Array.Empty<string>(),
                    Services: Array.Empty<string>(),
                    Symbols: Array.Empty<string>()),
                Labels: Array.Empty<string>(),
                Tags: Array.Empty<string>(),
                Evidence: Array.Empty<EvidenceDto>(),
                SupportedByChunks: Array.Empty<string>(),
                SupportedByQueryKinds: Array.Empty<string>()),
            Segments: Array.Empty<KnowledgeSegmentDto>(),
            Relations: Array.Empty<RelatedKnowledgeDto>());
    }

    public Task<ServerInfoResponse> GetServerInfoAsync(CancellationToken cancellationToken)
    {
        ToolCallOrder.Add("get_server_info");
        return Task.FromResult(ServerInfo);
    }

    public Task<RetrieveMemoryByChunksResponse> RetrieveMemoryByChunksAsync(RetrieveMemoryByChunksRequest request, CancellationToken cancellationToken)
    {
        ToolCallOrder.Add("retrieve_memory_by_chunks");
        return Task.FromResult(Retrieved);
    }

    public Task<MergeRetrievalResultsResponse> MergeRetrievalResultsAsync(MergeRetrievalResultsRequest request, CancellationToken cancellationToken)
    {
        ToolCallOrder.Add("merge_retrieval_results");
        return Task.FromResult(Merged);
    }

    public Task<BuildMemoryContextPackResponse> BuildMemoryContextPackAsync(BuildMemoryContextPackRequest request, CancellationToken cancellationToken)
    {
        ToolCallOrder.Add("build_memory_context_pack");
        return Task.FromResult(ContextPack);
    }

    public Task<SearchKnowledgeResponse> SearchKnowledgeAsync(SearchKnowledgeRequest request, CancellationToken cancellationToken)
    {
        ToolCallOrder.Add("search_knowledge");
        SearchCalls.Add(request);

        // Create a predictable candidate list from QueryText and QueryKind.
        var candidates = Enumerable.Range(0, request.TopK)
            .Select(i =>
            {
                var id = GuidUtility.CreateStableGuid($"{request.QueryKind}:{request.QueryText}:{i}");
                var retrievalClass = request.QueryKind switch
                {
                    QueryKind.SimilarCase => RetrievalClass.SimilarCase,
                    QueryKind.Constraint => RetrievalClass.Constraint,
                    QueryKind.Risk => RetrievalClass.Antipattern,
                    _ => RetrievalClass.Decision
                };
                return new KnowledgeCandidateDto(
                    KnowledgeItemId: id,
                    RetrievalClass: retrievalClass,
                    Title: $"{request.QueryKind} item {i}",
                    Summary: "s",
                    Details: null,
                    SemanticScore: 0,
                    LexicalScore: 0,
                    ScopeScore: 0,
                    AuthorityScore: 0,
                    CaseShapeScore: 0,
                    FinalScore: 10 - i,
                    Authority: request.MinimumAuthority,
                    Status: request.Status,
                    Scopes: new ScopeFilterDto(
                        Domains: Array.Empty<string>(),
                        Modules: Array.Empty<string>(),
                        Features: Array.Empty<string>(),
                        Layers: Array.Empty<string>(),
                        Concerns: Array.Empty<string>(),
                        Repos: Array.Empty<string>(),
                        Services: Array.Empty<string>(),
                        Symbols: Array.Empty<string>()),
                    Labels: Array.Empty<string>(),
                    Tags: Array.Empty<string>(),
                    Evidence: Array.Empty<EvidenceDto>(),
                    SupportedByChunks: Array.Empty<string>(),
                    SupportedByQueryKinds: Array.Empty<string>());
            })
            .ToList();

        var resp = new SearchKnowledgeResponse(
            SchemaVersion: "1.0",
            Kind: "search_knowledge",
            RequestId: request.RequestId,
            Candidates: candidates,
            Diagnostics: new SearchKnowledgeDiagnosticsDto(
                LexicalCandidateCount: 0,
                VectorCandidateCount: 0,
                MergedCandidateCount: 0,
                FinalCandidateCount: candidates.Count,
                ElapsedMs: 1,
                QueryEmbeddingModel: "fake",
                EmbeddingRoleUsed: "fake"));

        // Seed knowledge items so hydration can return them.
        foreach (var c in candidates)
            SeedKnowledgeItem(c.KnowledgeItemId, c.RetrievalClass, c.Title);

        return Task.FromResult(resp);
    }

    public Task<GetKnowledgeItemResponse> GetKnowledgeItemAsync(GetKnowledgeItemRequest request, CancellationToken cancellationToken)
    {
        ToolCallOrder.Add("get_knowledge_item");
        KnowledgeItemCalls.Add(request.KnowledgeItemId);

        if (!_knowledgeItems.TryGetValue(request.KnowledgeItemId, out var item))
            SeedKnowledgeItem(request.KnowledgeItemId, RetrievalClass.Decision, "seed");

        return Task.FromResult(_knowledgeItems[request.KnowledgeItemId]);
    }

    public Task<GetRelatedKnowledgeResponse> GetRelatedKnowledgeAsync(GetRelatedKnowledgeRequest request, CancellationToken cancellationToken)
        => throw new NotImplementedException();

    private static ServerInfoResponse FakeServerInfo() =>
        new ServerInfoResponse(
            SchemaVersion: "1.0",
            Kind: "server_info",
            ServerName: "HarnessMcp",
            ServerVersion: "1.0.0",
            ProtocolMode: "http+mcp",
            Features: new FeatureFlagsDto(
                RetrieveMemoryByChunks: true,
                MergeRetrievalResults: true,
                BuildMemoryContextPack: true,
                SearchKnowledge: true,
                GetKnowledgeItem: true,
                GetRelatedKnowledge: true,
                HttpTransport: true,
                StdioTransport: false,
                WriteOperations: false,
                MonitoringUi: false,
                RealtimeTracking: false),
            SchemaSet: new SchemaSetDto(
                RetrieveMemoryByChunks: "1.0",
                MergeRetrievalResults: "1.0",
                BuildMemoryContextPack: "1.0",
                SearchKnowledge: "1.0",
                GetKnowledgeItem: "1.0",
                GetRelatedKnowledge: "1.0",
                GetServerInfo: "1.0"));

    private static RetrieveMemoryByChunksResponse FakeRetrieved() =>
        new RetrieveMemoryByChunksResponse(
            SchemaVersion: "1.0",
            Kind: "retrieve_memory_by_chunks",
            RequestId: "r",
            TaskId: "t",
            ChunkResults: Array.Empty<ChunkRetrievalResultDto>(),
            Notes: Array.Empty<string>(),
            ElapsedMs: 1);

    private static MergeRetrievalResultsResponse FakeMerged() =>
        new MergeRetrievalResultsResponse(
            SchemaVersion: "1.0",
            Kind: "merge_retrieval_results",
            RequestId: "r",
            TaskId: "t",
            Decisions: Array.Empty<MergedKnowledgeItemDto>(),
            Constraints: Array.Empty<MergedKnowledgeItemDto>(),
            BestPractices: Array.Empty<MergedKnowledgeItemDto>(),
            AntiPatterns: Array.Empty<MergedKnowledgeItemDto>(),
            SimilarCases: Array.Empty<MergedKnowledgeItemDto>(),
            References: Array.Empty<MergedKnowledgeItemDto>(),
            Structures: Array.Empty<MergedKnowledgeItemDto>(),
            Warnings: Array.Empty<string>(),
            ElapsedMs: 1);

    private static BuildMemoryContextPackResponse FakeContextPack() =>
        new BuildMemoryContextPackResponse(
            SchemaVersion: "1.0",
            Kind: "build_memory_context_pack",
            RequestId: "r",
            TaskId: "t",
            ContextPack: new ContextPackSectionDto(
                Decisions: Array.Empty<MergedKnowledgeItemDto>(),
                Constraints: Array.Empty<MergedKnowledgeItemDto>(),
                BestPractices: Array.Empty<MergedKnowledgeItemDto>(),
                AntiPatterns: Array.Empty<MergedKnowledgeItemDto>(),
                SimilarCases: Array.Empty<MergedKnowledgeItemDto>(),
                References: Array.Empty<MergedKnowledgeItemDto>(),
                Structures: Array.Empty<MergedKnowledgeItemDto>()),
            Diagnostics: new ContextPackDiagnosticsDto(
                ChunksProcessed: 0,
                DistinctKnowledgeItems: 0,
                RetrievalElapsedMs: 0,
                MergeElapsedMs: 0,
                AssemblyElapsedMs: 0,
                Warnings: Array.Empty<string>()));
}

internal static class GuidUtility
{
    public static Guid CreateStableGuid(string input)
    {
        var bytes = System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(input));
        var g = new byte[16];
        Array.Copy(bytes, g, 16);
        return new Guid(g);
    }
}

internal sealed class FakeHttpMessageHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _responder;

    public FakeHttpMessageHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> responder)
    {
        _responder = responder;
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        => _responder(request, cancellationToken);
}

