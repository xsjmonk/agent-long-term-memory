using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Text.Json;
using HarnessMcp.Contracts;
using HarnessMcp.Core;
using HarnessMcp.Infrastructure.Postgres;
using Xunit;

namespace HarnessMcp.Tests.Unit;

public sealed class UnitTest1
{
    private sealed class ZeroCaseShapes : ICaseShapeScoreProvider
    {
        public double ComputeScore(SearchKnowledgeRequest request, Guid knowledgeItemId) => 0;
    }

    [Fact]
    public void AuthorityPolicy_AllowsEqualOrHigher()
    {
        var p = new AuthorityPolicy();
        Assert.True(p.IsAllowed(AuthorityLevel.Approved, AuthorityLevel.Reviewed));
        Assert.True(p.IsAllowed(AuthorityLevel.Reviewed, AuthorityLevel.Reviewed));
        Assert.False(p.IsAllowed(AuthorityLevel.Draft, AuthorityLevel.Reviewed));
    }

    [Fact]
    public void SupersessionPolicy_HidesSupersededAndArchived()
    {
        var p = new SupersessionPolicy();
        Assert.False(p.IsVisible(KnowledgeStatus.Superseded, Guid.NewGuid()));
        Assert.False(p.IsVisible(KnowledgeStatus.Archived, null));
        Assert.False(p.IsVisible(KnowledgeStatus.Active, Guid.NewGuid()));
        Assert.True(p.IsVisible(KnowledgeStatus.Active, null));
    }

    [Fact]
    public void ScopeNormalizer_TrimsAndDedupes_CaseInsensitive()
    {
        var norm = new ScopeNormalizer();
        var input = new ScopeFilterDto(
            Domains: new[] { " MingPan ", "mingpan" },
            Modules: Array.Empty<string>(),
            Features: Array.Empty<string>(),
            Layers: Array.Empty<string>(),
            Concerns: Array.Empty<string>(),
            Repos: Array.Empty<string>(),
            Services: Array.Empty<string>(),
            Symbols: Array.Empty<string>());

        var output = norm.Normalize(input);
        Assert.Single(output.Domains);
        Assert.Equal("MingPan", output.Domains[0]);
    }

    [Fact]
    public void RequestValidator_RejectsEmptyQueryText()
    {
        var cfg = new RetrievalConfig { MaxTopK = 10, MaxQueryTextLength = 10 };
        var v = new RequestValidator(cfg);

        Assert.Throws<ValidationException>(() =>
            v.Validate(new SearchKnowledgeRequest(
                SchemaVersion: "1.0",
                RequestId: "r1",
                QueryText: " ",
                QueryKind: QueryKind.CoreTask,
                Scopes: ScopeDtos.Empty,
                RetrievalClasses: Array.Empty<RetrievalClass>(),
                MinimumAuthority: AuthorityLevel.Draft,
                Status: KnowledgeStatus.Active,
                TopK: 1,
                IncludeEvidence: false,
                IncludeRawDetails: false)));
    }

    [Fact]
    public void ChunkQueryPlanner_MapsChunkTypeToQueryKindAndRetrievalClasses()
    {
        var planner = new ChunkQueryPlanner();
        var request = new RetrieveMemoryByChunksRequest(
            SchemaVersion: "1.0",
            RequestId: "req",
            TaskId: "task",
            RequirementIntent: new RequirementIntentDto(
                TaskType: "core",
                Domain: null,
                Module: null,
                Feature: null,
                HardConstraints: Array.Empty<string>(),
                RiskSignals: Array.Empty<string>()),
            RetrievalChunks: Array.Empty<RetrievalChunkDto>(),
            SearchProfile: new ChunkSearchProfileDto(
                ActiveOnly: true,
                MinimumAuthority: AuthorityLevel.Reviewed,
                MaxItemsPerChunk: 3,
                RequireTypeSeparation: false));

        var chunk = new RetrievalChunkDto(
            ChunkId: "c1",
            ChunkType: ChunkType.Constraint,
            Text: "txt",
            StructuredScopes: ScopeDtos.Empty,
            TaskShape: null);

        var built = planner.BuildSearchRequest(request, chunk, "suf");
        Assert.Equal("req:suf", built.RequestId);
        Assert.Equal(QueryKind.Constraint, built.QueryKind);
        Assert.Equal(KnowledgeStatus.Active, built.Status);
        Assert.InRange(built.RetrievalClasses.Count, 1, 10);
    }

    [Fact]
    public void HybridRankingService_RanksSemanticHigherWhenAuthorityMatches()
    {
        var ranking = new HybridRankingService(new AuthorityPolicy(), new ZeroCaseShapes());
        var scopes = ScopeDtos.Empty;
        var a = Candidate(Guid.NewGuid(), RetrievalClass.Decision, semanticScore: 1.0, lexicalScore: 0.0, AuthorityLevel.Approved, KnowledgeStatus.Active, scopes);
        var b = Candidate(Guid.NewGuid(), RetrievalClass.Decision, semanticScore: 0.9, lexicalScore: 0.0, AuthorityLevel.Draft, KnowledgeStatus.Active, scopes);

        var request = new SearchKnowledgeRequest(
            SchemaVersion: "1.0",
            RequestId: "r",
            QueryText: "q",
            QueryKind: QueryKind.CoreTask,
            Scopes: scopes,
            RetrievalClasses: Array.Empty<RetrievalClass>(),
            MinimumAuthority: AuthorityLevel.Draft,
            Status: KnowledgeStatus.Active,
            TopK: 2,
            IncludeEvidence: false,
            IncludeRawDetails: false);

        var ranked = ranking.Rank(
            lexical: new[] { a with { LexicalScore = 0.1 } },
            semantic: new[] { a, b },
            request);

        Assert.Equal(2, ranked.Count);
        Assert.Equal(a.KnowledgeItemId, ranked[0].KnowledgeItemId);
    }

    private sealed class FakeCaseShapeProvider(Dictionary<Guid, double> map) : ICaseShapeScoreProvider
    {
        public double ComputeScore(SearchKnowledgeRequest request, Guid knowledgeItemId) =>
            map.TryGetValue(knowledgeItemId, out var s) ? s : 0d;
    }

    [Fact]
    public void HybridRankingService_SimilarCase_UsesCaseShapeScoreProvider()
    {
        var aId = Guid.NewGuid();
        var bId = Guid.NewGuid();

        var caseShapes = new FakeCaseShapeProvider(new Dictionary<Guid, double>
        {
            [aId] = 0.9,
            [bId] = 0.1
        });

        var ranking = new HybridRankingService(new AuthorityPolicy(), caseShapes);

        var scopes = ScopeDtos.Empty;
        var a = Candidate(aId, RetrievalClass.SimilarCase, semanticScore: 0.0, lexicalScore: 1.0, AuthorityLevel.Approved, KnowledgeStatus.Active, scopes);
        var b = Candidate(bId, RetrievalClass.SimilarCase, semanticScore: 0.0, lexicalScore: 1.0, AuthorityLevel.Approved, KnowledgeStatus.Active, scopes);

        var request = new SearchKnowledgeRequest(
            SchemaVersion: "1.0",
            RequestId: "r",
            QueryText: "{\"taskType\":\"x\",\"featureShape\":\"y\",\"engineChangeAllowed\":false,\"likelyLayers\":[],\"riskSignals\":[],\"complexity\":null}",
            QueryKind: QueryKind.SimilarCase,
            Scopes: scopes,
            RetrievalClasses: new[] { RetrievalClass.SimilarCase },
            MinimumAuthority: AuthorityLevel.Draft,
            Status: KnowledgeStatus.Active,
            TopK: 2,
            IncludeEvidence: false,
            IncludeRawDetails: false);

        var ranked = ranking.Rank(
            lexical: new[] { a, b },
            semantic: Array.Empty<KnowledgeCandidateDto>(),
            request);

        Assert.Equal(aId, ranked[0].KnowledgeItemId);
    }

    [Fact]
    public void RetrievalMergeService_OrdersByRecencyProxy_WhenFinalScoresEqual()
    {
        var validatorCfg = new RetrievalConfig
        {
            MaxTopK = 10,
            DefaultTopK = 5,
            MinimumAuthority = AuthorityLevel.Draft,
            MaxQueryTextLength = 50,
            MaxChunkTextLength = 50
        };
        var svc = new RetrievalMergeService(new RequestValidator(validatorCfg));

        var older = Candidate(Guid.NewGuid(), RetrievalClass.Decision, semanticScore: 0.4, lexicalScore: 0.1, AuthorityLevel.Approved, KnowledgeStatus.Active, ScopeDtos.Empty) with { FinalScore = 0.9 };
        var newer = Candidate(Guid.NewGuid(), RetrievalClass.Decision, semanticScore: 0.6, lexicalScore: 0.4, AuthorityLevel.Approved, KnowledgeStatus.Active, ScopeDtos.Empty) with { FinalScore = 0.9 };

        var retrieved = new RetrieveMemoryByChunksResponse(
            SchemaVersion: "1.0",
            Kind: "retrieve_memory_by_chunks",
            RequestId: "req",
            TaskId: "task",
            ChunkResults: new[]
            {
                new ChunkRetrievalResultDto(
                    ChunkId: "c1",
                    ChunkType: ChunkType.CoreTask,
                    Results: new ChunkBucketDto(
                        Decisions: new[] { older, newer },
                        BestPractices: Array.Empty<KnowledgeCandidateDto>(),
                        Antipatterns: Array.Empty<KnowledgeCandidateDto>(),
                        SimilarCases: Array.Empty<KnowledgeCandidateDto>(),
                        Constraints: Array.Empty<KnowledgeCandidateDto>(),
                        References: Array.Empty<KnowledgeCandidateDto>(),
                        Structures: Array.Empty<KnowledgeCandidateDto>()),
                    Diagnostics: new SearchKnowledgeDiagnosticsDto(
                        LexicalCandidateCount: 0,
                        VectorCandidateCount: 0,
                        MergedCandidateCount: 0,
                        FinalCandidateCount: 0,
                        ElapsedMs: 0,
                        QueryEmbeddingModel: "NoOp",
                        EmbeddingRoleUsed: "none"))
            },
            Notes: Array.Empty<string>(),
            ElapsedMs: 0);

        var req = new MergeRetrievalResultsRequest(
            SchemaVersion: "1.0",
            RequestId: "m1",
            TaskId: "task",
            Retrieved: retrieved);

        var resp = svc.MergeRetrievalResultsAsync(req, CancellationToken.None).Result;
        Assert.Equal(newer.KnowledgeItemId, resp.Decisions[0].Item.KnowledgeItemId);
    }

    [Fact]
    public void CaseShapeMatcher_ComplexityNearCountsAsMatch()
    {
        var a = new SimilarCaseShapeDto(
            TaskType: "t",
            FeatureShape: "f",
            EngineChangeAllowed: false,
            LikelyLayers: Array.Empty<string>(),
            RiskSignals: Array.Empty<string>(),
            Complexity: "medium");

        var b = new SimilarCaseShapeDto(
            TaskType: "t",
            FeatureShape: "f",
            EngineChangeAllowed: false,
            LikelyLayers: Array.Empty<string>(),
            RiskSignals: Array.Empty<string>(),
            Complexity: "medium_plus");

        var score = CaseShapeMatcher.Match(a, b);
        Assert.True(score > 0d);
    }

    [Fact]
    public void UiTrimPolicy_TrimsWhenLongerThan64Chars()
    {
        var policy = new UiTrimPolicy(new MonitoringConfig { MaxPayloadPreviewChars = 10 });
        var input = new string('x', 100);
        var trimmed = policy.Trim(input);
        Assert.Equal(65, trimmed.Length); // 64 chars + ellipsis
    }

    private sealed class FakeMonitorBuffer : IMonitorEventBuffer
    {
        private readonly IReadOnlyList<MonitorEventDto> _events;
        public FakeMonitorBuffer(IReadOnlyList<MonitorEventDto> events, long lastSequence)
        {
            _events = events;
            LastSequence = lastSequence;
        }
        public IReadOnlyList<MonitorEventDto> Snapshot() => _events;
        public long LastSequence { get; }
    }

    [Fact]
    public void MonitoringSnapshotService_ProjectsAndTrimsWarningsAndOutputs()
    {
        var started = DateTimeOffset.UtcNow;
        var cfg = new AppConfig
        {
            Server = new ServerConfig
            {
                EnableMonitoringUi = true,
                Environment = "Test",
                TransportMode = TransportMode.Http
            },
            Database = new DatabaseConfig { Host = "localhost" },
            Monitoring = new MonitoringConfig { MaxRenderedRows = 10, MaxPayloadPreviewChars = 10 },
            Embedding = new EmbeddingConfig { QueryEmbeddingProvider = "NoOp", Model = "m" }
        };

        var appInfo = new FakeAppInfoProvider();
        var trim = new UiTrimPolicy(cfg.Monitoring);
        var projector = new UiEventProjector(trim);

        var longText = new string('a', 200);
        var events = new[]
        {
            new MonitorEventDto(1, started, MonitorEventKind.Warning, null, "tool", null, "Warning", longText, longText),
            new MonitorEventDto(2, started, MonitorEventKind.RequestSuccess, "req", "tool2", "task", "Information", longText, longText)
        };

        var buffer = new FakeMonitorBuffer(events, lastSequence: 2);
        var svc = new MonitoringSnapshotService(cfg, appInfo, buffer, projector, startedAtUtc: started);

        var snap = svc.GetSnapshotAsync(CancellationToken.None).Result;
        Assert.Single(snap.RecentWarnings);
        Assert.Equal(65, snap.RecentWarnings[0].Summary.Length);
        Assert.NotNull(snap.RecentWarnings[0].PayloadPreviewJson);
        Assert.Equal(65, snap.RecentWarnings[0].PayloadPreviewJson!.Length);

        Assert.Single(snap.RecentOutputs);
    }

    private sealed class FakeAppInfoProvider : IAppInfoProvider
    {
        public ServerInfoResponse GetServerInfo() =>
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
                    MonitoringUi: true,
                    RealtimeTracking: false),
                SchemaSet: new SchemaSetDto(
                    RetrieveMemoryByChunks: "1.0",
                    MergeRetrievalResults: "1.0",
                    BuildMemoryContextPack: "1.0",
                    SearchKnowledge: "1.0",
                    GetKnowledgeItem: "1.0",
                    GetRelatedKnowledge: "1.0",
                    GetServerInfo: "1.0"));
    }

    private static KnowledgeCandidateDto Candidate(
        Guid id,
        RetrievalClass retrievalClass,
        double semanticScore,
        double lexicalScore,
        AuthorityLevel authority,
        KnowledgeStatus status,
        ScopeFilterDto scopes) =>
        new KnowledgeCandidateDto(
            KnowledgeItemId: id,
            RetrievalClass: retrievalClass,
            Title: "t",
            Summary: "s",
            Details: null,
            SemanticScore: semanticScore,
            LexicalScore: lexicalScore,
            ScopeScore: 0,
            AuthorityScore: 0,
            CaseShapeScore: 0,
            FinalScore: 0,
            Authority: authority,
            Status: status,
            Scopes: scopes,
            Labels: Array.Empty<string>(),
            Tags: Array.Empty<string>(),
            Evidence: Array.Empty<EvidenceDto>(),
            SupportedByChunks: Array.Empty<string>(),
            SupportedByQueryKinds: Array.Empty<string>());

    [Fact]
    public void MonitorRingBuffer_AssignsIncreasingSequences_AndSupportsSnapshot()
    {
        var ring = new MonitorRingBuffer(3);
        var e1 = ring.Append(new MonitorEventDto(0, DateTimeOffset.UtcNow, MonitorEventKind.Log, null, "c", null, "Info", "m1", null));
        var e2 = ring.Append(new MonitorEventDto(0, DateTimeOffset.UtcNow, MonitorEventKind.Log, null, "c", null, "Info", "m2", null));

        Assert.True(e2.Sequence > e1.Sequence);
        var snap = ring.Snapshot();
        Assert.True(snap.Count >= 2);
        Assert.Equal(e2.Sequence, snap[^1].Sequence);
    }

    private sealed class FakeEmbedMessageHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> responder) : HttpMessageHandler
    {
        public HttpRequestMessage? LastRequest { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            LastRequest = request;
            return await responder(request, cancellationToken).ConfigureAwait(false);
        }
    }

    [Fact]
    public async Task LocalHttpQueryEmbeddingService_SendsEmbedQueryEnvelopeAndParsesResponse()
    {
        var request = new SearchKnowledgeRequest(
            SchemaVersion: "ignored",
            RequestId: "req:suf",
            QueryText: "hello world",
            QueryKind: QueryKind.CoreTask,
            Scopes: ScopeDtos.Empty,
            RetrievalClasses: Array.Empty<RetrievalClass>(),
            MinimumAuthority: AuthorityLevel.Draft,
            Status: KnowledgeStatus.Active,
            TopK: 3,
            IncludeEvidence: false,
            IncludeRawDetails: false);

        var handler = new FakeEmbedMessageHandler(async (req, ct) =>
        {
            var body = await req.Content!.ReadAsStringAsync(ct).ConfigureAwait(false);
            using var doc = JsonDocument.Parse(body);
            Assert.Equal("1.1", doc.RootElement.GetProperty("schema_version").GetString());
            Assert.Equal("req:suf", doc.RootElement.GetProperty("request_id").GetString());
                Assert.Equal("req", doc.RootElement.GetProperty("task_id").GetString());
            Assert.Equal("mcp", doc.RootElement.GetProperty("caller").GetString());
            Assert.Equal("harness_retrieval", doc.RootElement.GetProperty("purpose").GetString());
            var items = doc.RootElement.GetProperty("items");
            Assert.Equal(1, items.GetArrayLength());
            var item = items[0];
            Assert.Equal("req:suf", item.GetProperty("item_id").GetString());
                Assert.Equal("CoreTask", item.GetProperty("query_kind").GetString());
                Assert.Equal("CoreTask", item.GetProperty("retrieval_role_hint").GetString());
            Assert.Equal("hello world", item.GetProperty("text").GetString());
                Assert.Equal("suf", item.GetProperty("chunk_id").GetString());
                Assert.Equal("core_task", item.GetProperty("chunk_type").GetString());
            Assert.True(item.GetProperty("structured_scopes").ValueKind == JsonValueKind.Null);
            Assert.True(item.GetProperty("task_shape").ValueKind == JsonValueKind.Null);

            var responseJson = """
            {
              "schema_version": "1.1",
              "request_id": "req:suf",
              "task_id": null,
              "provider": "sentence_transformers",
              "model_name": "m",
              "model_version": null,
              "normalize_embeddings": true,
              "dimension": 3,
              "fallback_mode": false,
              "text_processing_id": "tp",
              "vector_space_id": "vs",
              "items": [
                {
                  "item_id": "req:suf",
                  "chunk_id": null,
                  "chunk_type": null,
                  "query_kind": "CoreTask",
                  "retrieval_role_hint": "CoreTask",
                  "vector": [0.1, 0.2, 0.3],
                  "input_char_count": 11,
                  "effective_text_char_count": 11,
                  "truncated": true,
                  "warnings": ["item-warn"]
                }
              ],
              "warnings": ["top-warn"]
            }
            """;

            var resp = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseJson, Encoding.UTF8, "application/json")
            };
            return resp;
        });

        var http = new HttpClient(handler);
        var cfg = new EmbeddingConfig
        {
            QueryEmbeddingProvider = "LocalHttp",
            Endpoint = "http://test/embed-query",
            Model = "m",
            TimeoutSeconds = 30
        };

        var svc = new LocalHttpQueryEmbeddingService(cfg, http);
        var result = await svc.EmbedAsync(request, CancellationToken.None).ConfigureAwait(false);

        Assert.Equal(3, result.Vector.Length);
        Assert.Equal("sentence_transformers", result.Provider);
        Assert.Equal("m", result.ModelName);
        Assert.False(result.FallbackMode);
        Assert.Equal(3, result.Dimension);
        Assert.Equal("tp", result.TextProcessingId);
        Assert.Equal("vs", result.VectorSpaceId);
        Assert.Equal(11, result.InputCharCount);
        Assert.Equal(11, result.EffectiveTextCharCount);
        Assert.True(result.Truncated);
        Assert.Equal(2, result.Warnings.Count);
        Assert.Equal("top-warn", result.Warnings[0]);
        Assert.Equal("item-warn", result.Warnings[1]);
    }

    [Fact]
    public async Task LocalHttpQueryEmbeddingService_MapsStructuredScopesIntoStructuredScopesObject()
    {
        var scopes = new ScopeFilterDto(
            Domains: Array.Empty<string>(),
            Modules: Array.Empty<string>(),
            Features: Array.Empty<string>(),
            Layers: new[] { "engine" },
            Concerns: Array.Empty<string>(),
            Repos: Array.Empty<string>(),
            Services: Array.Empty<string>(),
            Symbols: Array.Empty<string>());

        var request = new SearchKnowledgeRequest(
            SchemaVersion: "ignored",
            RequestId: "req:suf",
            QueryText: "hello world",
            QueryKind: QueryKind.CoreTask,
            Scopes: scopes,
            RetrievalClasses: Array.Empty<RetrievalClass>(),
            MinimumAuthority: AuthorityLevel.Draft,
            Status: KnowledgeStatus.Active,
            TopK: 3,
            IncludeEvidence: false,
            IncludeRawDetails: false);

        var handler = new FakeEmbedMessageHandler(async (req, ct) =>
        {
            var body = await req.Content!.ReadAsStringAsync(ct).ConfigureAwait(false);
            using var doc = JsonDocument.Parse(body);
            var item = doc.RootElement.GetProperty("items")[0];
            Assert.True(item.GetProperty("structured_scopes").ValueKind == JsonValueKind.Object);
            Assert.Equal(1, item.GetProperty("structured_scopes").GetProperty("Layers").GetArrayLength());
            Assert.Equal("engine", item.GetProperty("structured_scopes").GetProperty("Layers")[0].GetString());

            var responseJson = """
            {
              "schema_version": "1.1",
              "request_id": "req:suf",
              "task_id": null,
              "provider": "sentence_transformers",
              "model_name": "m",
              "model_version": null,
              "normalize_embeddings": true,
              "dimension": 1,
              "fallback_mode": false,
              "text_processing_id": "tp",
              "vector_space_id": "vs",
              "items": [
                {
                  "item_id": "req:suf",
                  "chunk_id": null,
                  "chunk_type": null,
                  "query_kind": "CoreTask",
                  "retrieval_role_hint": "CoreTask",
                  "vector": [0.1],
                  "input_char_count": 1,
                  "effective_text_char_count": 1,
                  "truncated": false,
                  "warnings": []
                }
              ],
              "warnings": []
            }
            """;

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseJson, Encoding.UTF8, "application/json")
            };
        });

        var http = new HttpClient(handler);
        var cfg = new EmbeddingConfig
        {
            QueryEmbeddingProvider = "LocalHttp",
            Endpoint = "http://test/embed-query",
            Model = "m",
            TimeoutSeconds = 30
        };

        var svc = new LocalHttpQueryEmbeddingService(cfg, http);
        var result = await svc.EmbedAsync(request, CancellationToken.None).ConfigureAwait(false);
        Assert.Equal(1, result.Dimension);
    }

    [Fact]
    public async Task LocalHttpQueryEmbeddingService_FailsOnWrongItemCount()
    {
        var request = new SearchKnowledgeRequest(
            SchemaVersion: "ignored",
            RequestId: "req:1",
            QueryText: "q",
            QueryKind: QueryKind.CoreTask,
            Scopes: ScopeDtos.Empty,
            RetrievalClasses: Array.Empty<RetrievalClass>(),
            MinimumAuthority: AuthorityLevel.Draft,
            Status: KnowledgeStatus.Active,
            TopK: 3,
            IncludeEvidence: false,
            IncludeRawDetails: false);

        var handler = new FakeEmbedMessageHandler((req, ct) =>
        {
            var responseJson = """
            {
              "schema_version": "1.1",
              "request_id": "req:1",
              "task_id": null,
              "provider": "p",
              "model_name": "m",
              "model_version": null,
              "normalize_embeddings": true,
              "dimension": 1,
              "fallback_mode": false,
              "text_processing_id": "tp",
              "vector_space_id": "vs",
              "items": [],
              "warnings": []
            }
            """;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseJson, Encoding.UTF8, "application/json")
            });
        });

        var http = new HttpClient(handler);
        var cfg = new EmbeddingConfig { Endpoint = "http://test/embed-query", TimeoutSeconds = 30, QueryEmbeddingProvider = "LocalHttp", Model = "m" };
        var svc = new LocalHttpQueryEmbeddingService(cfg, http);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            svc.EmbedAsync(request, CancellationToken.None).AsTask());
    }

    [Fact]
    public async Task LocalHttpQueryEmbeddingService_FailsOnEchoedItemIdMismatch()
    {
        var request = new SearchKnowledgeRequest(
            SchemaVersion: "ignored",
            RequestId: "req:1",
            QueryText: "q",
            QueryKind: QueryKind.CoreTask,
            Scopes: ScopeDtos.Empty,
            RetrievalClasses: Array.Empty<RetrievalClass>(),
            MinimumAuthority: AuthorityLevel.Draft,
            Status: KnowledgeStatus.Active,
            TopK: 3,
            IncludeEvidence: false,
            IncludeRawDetails: false);

        var handler = new FakeEmbedMessageHandler((req, ct) =>
        {
            var responseJson = """
            {
              "schema_version": "1.1",
              "request_id": "req:1",
              "task_id": null,
              "provider": "p",
              "model_name": "m",
              "model_version": null,
              "normalize_embeddings": true,
              "dimension": 1,
              "fallback_mode": false,
              "text_processing_id": "tp",
              "vector_space_id": "vs",
              "items": [
                {
                  "item_id": "wrong",
                  "chunk_id": null,
                  "chunk_type": null,
                  "query_kind": "CoreTask",
                  "retrieval_role_hint": "CoreTask",
                  "vector": [0.0],
                  "input_char_count": 1,
                  "effective_text_char_count": 1,
                  "truncated": false,
                  "warnings": []
                }
              ],
              "warnings": []
            }
            """;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseJson, Encoding.UTF8, "application/json")
            });
        });

        var http = new HttpClient(handler);
        var cfg = new EmbeddingConfig { Endpoint = "http://test/embed-query", TimeoutSeconds = 30, QueryEmbeddingProvider = "LocalHttp", Model = "m" };
        var svc = new LocalHttpQueryEmbeddingService(cfg, http);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            svc.EmbedAsync(request, CancellationToken.None).AsTask());
    }

    [Fact]
    public async Task LocalHttpQueryEmbeddingService_FailsOnDimensionMismatch()
    {
        var request = new SearchKnowledgeRequest(
            SchemaVersion: "ignored",
            RequestId: "req:1",
            QueryText: "q",
            QueryKind: QueryKind.CoreTask,
            Scopes: ScopeDtos.Empty,
            RetrievalClasses: Array.Empty<RetrievalClass>(),
            MinimumAuthority: AuthorityLevel.Draft,
            Status: KnowledgeStatus.Active,
            TopK: 3,
            IncludeEvidence: false,
            IncludeRawDetails: false);

        var handler = new FakeEmbedMessageHandler((req, ct) =>
        {
            var responseJson = """
            {
              "schema_version": "1.1",
              "request_id": "req:1",
              "task_id": null,
              "provider": "p",
              "model_name": "m",
              "model_version": null,
              "normalize_embeddings": true,
              "dimension": 3,
              "fallback_mode": false,
              "text_processing_id": "tp",
              "vector_space_id": "vs",
              "items": [
                {
                  "item_id": "req:1",
                  "chunk_id": null,
                  "chunk_type": null,
                  "query_kind": "CoreTask",
                  "retrieval_role_hint": "CoreTask",
                  "vector": [0.0, 0.0],
                  "input_char_count": 1,
                  "effective_text_char_count": 1,
                  "truncated": false,
                  "warnings": []
                }
              ],
              "warnings": []
            }
            """;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseJson, Encoding.UTF8, "application/json")
            });
        });

        var http = new HttpClient(handler);
        var cfg = new EmbeddingConfig { Endpoint = "http://test/embed-query", TimeoutSeconds = 30, QueryEmbeddingProvider = "LocalHttp", Model = "m" };
        var svc = new LocalHttpQueryEmbeddingService(cfg, http);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            svc.EmbedAsync(request, CancellationToken.None).AsTask());
    }

    [Fact]
    public void EmbeddingCompatibilityChecker_AcceptsCompatibleMetadata()
    {
        var checker = new EmbeddingCompatibilityChecker();
        var cfg = new EmbeddingConfig { RequireCompatibilityCheck = true, AllowHashingFallback = false, AllowLexicalFallbackOnSemanticIncompatibility = true };
        var query = new QueryEmbeddingResult(
            Vector: new float[] { 0f, 1f },
            Provider: "p",
            ModelName: "m",
            ModelVersion: null,
            NormalizeEmbeddings: false,
            Dimension: 2,
            FallbackMode: false,
            TextProcessingId: "tp",
            VectorSpaceId: "vs",
            InputCharCount: 1,
            EffectiveTextCharCount: 1,
            Truncated: false,
            Warnings: Array.Empty<string>());
        var stored = new StoredEmbeddingMetadata(
            ModelName: "m",
            ModelVersion: null,
            Dimension: 2,
            NormalizeEmbeddings: null,
            HasRows: true,
            SelectedEmbeddingRole: "CoreTask");

        var res = checker.Check(query, stored, cfg);
        Assert.True(res.IsCompatible);
    }

    [Fact]
    public void EmbeddingCompatibilityChecker_RejectsDimensionMismatch()
    {
        var checker = new EmbeddingCompatibilityChecker();
        var cfg = new EmbeddingConfig { RequireCompatibilityCheck = true, AllowHashingFallback = true, AllowLexicalFallbackOnSemanticIncompatibility = true };
        var query = new QueryEmbeddingResult(
            Vector: new float[] { 0f, 1f },
            Provider: "p",
            ModelName: "m",
            ModelVersion: null,
            NormalizeEmbeddings: false,
            Dimension: 2,
            FallbackMode: false,
            TextProcessingId: "tp",
            VectorSpaceId: "vs",
            InputCharCount: 1,
            EffectiveTextCharCount: 1,
            Truncated: false,
            Warnings: Array.Empty<string>());
        var stored = new StoredEmbeddingMetadata(
            ModelName: "m",
            ModelVersion: null,
            Dimension: 3,
            NormalizeEmbeddings: null,
            HasRows: true,
            SelectedEmbeddingRole: "CoreTask");

        var res = checker.Check(query, stored, cfg);
        Assert.False(res.IsCompatible);
    }

    [Fact]
    public void EmbeddingCompatibilityChecker_RejectsModelMismatch()
    {
        var checker = new EmbeddingCompatibilityChecker();
        var cfg = new EmbeddingConfig { RequireCompatibilityCheck = true, AllowHashingFallback = true, AllowLexicalFallbackOnSemanticIncompatibility = true };
        var query = new QueryEmbeddingResult(
            Vector: new float[] { 0f, 1f },
            Provider: "p",
            ModelName: "m",
            ModelVersion: null,
            NormalizeEmbeddings: false,
            Dimension: 2,
            FallbackMode: false,
            TextProcessingId: "tp",
            VectorSpaceId: "vs",
            InputCharCount: 1,
            EffectiveTextCharCount: 1,
            Truncated: false,
            Warnings: Array.Empty<string>());
        var stored = new StoredEmbeddingMetadata(
            ModelName: "other",
            ModelVersion: null,
            Dimension: 2,
            NormalizeEmbeddings: null,
            HasRows: true,
            SelectedEmbeddingRole: "CoreTask");

        var res = checker.Check(query, stored, cfg);
        Assert.False(res.IsCompatible);
    }

    [Fact]
    public void EmbeddingCompatibilityChecker_RejectsHashingFallbackWhenDisallowed()
    {
        var checker = new EmbeddingCompatibilityChecker();
        var cfg = new EmbeddingConfig { RequireCompatibilityCheck = true, AllowHashingFallback = false, AllowLexicalFallbackOnSemanticIncompatibility = true };
        var query = new QueryEmbeddingResult(
            Vector: new float[] { 0f, 1f },
            Provider: "p",
            ModelName: "m",
            ModelVersion: null,
            NormalizeEmbeddings: false,
            Dimension: 2,
            FallbackMode: true,
            TextProcessingId: "tp",
            VectorSpaceId: "vs",
            InputCharCount: 1,
            EffectiveTextCharCount: 1,
            Truncated: false,
            Warnings: Array.Empty<string>());
        var stored = new StoredEmbeddingMetadata(
            ModelName: "m",
            ModelVersion: null,
            Dimension: 2,
            NormalizeEmbeddings: null,
            HasRows: true,
            SelectedEmbeddingRole: "CoreTask");

        var res = checker.Check(query, stored, cfg);
        Assert.False(res.IsCompatible);
    }

    [Fact]
    public void EmbeddingCompatibilityChecker_RejectsMissingStoredMetadataWhenRequired()
    {
        var checker = new EmbeddingCompatibilityChecker();
        var cfg = new EmbeddingConfig { RequireCompatibilityCheck = true, AllowHashingFallback = true, AllowLexicalFallbackOnSemanticIncompatibility = true };
        var query = new QueryEmbeddingResult(
            Vector: new float[] { 0f, 1f },
            Provider: "p",
            ModelName: "m",
            ModelVersion: null,
            NormalizeEmbeddings: false,
            Dimension: 2,
            FallbackMode: false,
            TextProcessingId: "tp",
            VectorSpaceId: "vs",
            InputCharCount: 1,
            EffectiveTextCharCount: 1,
            Truncated: false,
            Warnings: Array.Empty<string>());
        var stored = new StoredEmbeddingMetadata(
            ModelName: "m",
            ModelVersion: null,
            Dimension: 0,
            NormalizeEmbeddings: null,
            HasRows: false,
            SelectedEmbeddingRole: "CoreTask");

        var res = checker.Check(query, stored, cfg);
        Assert.False(res.IsCompatible);
    }

    [Fact]
    public void EmbeddingCompatibilityChecker_AllowsMissingStoredMetadataWhenDisabled()
    {
        var checker = new EmbeddingCompatibilityChecker();
        var cfg = new EmbeddingConfig { RequireCompatibilityCheck = false, AllowHashingFallback = true, AllowLexicalFallbackOnSemanticIncompatibility = true };
        var query = new QueryEmbeddingResult(
            Vector: new float[] { 0f, 1f },
            Provider: "p",
            ModelName: "m",
            ModelVersion: null,
            NormalizeEmbeddings: false,
            Dimension: 2,
            FallbackMode: false,
            TextProcessingId: "tp",
            VectorSpaceId: "vs",
            InputCharCount: 1,
            EffectiveTextCharCount: 1,
            Truncated: false,
            Warnings: Array.Empty<string>());
        var stored = new StoredEmbeddingMetadata(
            ModelName: "m",
            ModelVersion: null,
            Dimension: 0,
            NormalizeEmbeddings: null,
            HasRows: false,
            SelectedEmbeddingRole: "CoreTask");

        var res = checker.Check(query, stored, cfg);
        Assert.True(res.IsCompatible);
    }

    [Fact]
    public void EmbeddingCompatibilityChecker_MarksDegradedWhenTruncated()
    {
        var checker = new EmbeddingCompatibilityChecker();
        var cfg = new EmbeddingConfig
        {
            RequireCompatibilityCheck = true,
            AllowHashingFallback = true,
            AllowLexicalFallbackOnSemanticIncompatibility = true
        };

        var query = new QueryEmbeddingResult(
            Vector: new float[] { 0f, 1f },
            Provider: "p",
            ModelName: "m",
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

        var stored = new StoredEmbeddingMetadata(
            ModelName: "m",
            ModelVersion: null,
            Dimension: 2,
            NormalizeEmbeddings: null,
            HasRows: true,
            SelectedEmbeddingRole: "CoreTask");

        var res = checker.Check(query, stored, cfg);
        Assert.True(res.IsCompatible);
        Assert.True(res.SemanticQualityDegraded);
        Assert.Contains("text-truncated-before-embedding", res.DegradationSignals);
    }

    [Fact]
    public void EmbeddingCompatibilityChecker_MarksDegradedWhenBuilderWarningsPresent()
    {
        var checker = new EmbeddingCompatibilityChecker();
        var cfg = new EmbeddingConfig
        {
            RequireCompatibilityCheck = true,
            AllowHashingFallback = true,
            AllowLexicalFallbackOnSemanticIncompatibility = true
        };

        var query = new QueryEmbeddingResult(
            Vector: new float[] { 0f, 1f },
            Provider: "p",
            ModelName: "m",
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

        var stored = new StoredEmbeddingMetadata(
            ModelName: "m",
            ModelVersion: null,
            Dimension: 2,
            NormalizeEmbeddings: null,
            HasRows: true,
            SelectedEmbeddingRole: "CoreTask");

        var res = checker.Check(query, stored, cfg);
        Assert.True(res.IsCompatible);
        Assert.True(res.SemanticQualityDegraded);
        Assert.Contains("builder-warning:hashing-fallback-active", res.DegradationSignals);
    }

    [Fact]
    public void EmbeddingCompatibilityChecker_TextProcessingMismatch_DegradeOrIncompatibleBasedOnConfig()
    {
        var checker = new EmbeddingCompatibilityChecker();
        var stored = new StoredEmbeddingMetadata(
            ModelName: "m",
            ModelVersion: null,
            Dimension: 2,
            NormalizeEmbeddings: null,
            HasRows: true,
            SelectedEmbeddingRole: "CoreTask");

        var baseQuery = new QueryEmbeddingResult(
            Vector: new float[] { 0f, 1f },
            Provider: "p",
            ModelName: "m",
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

        var cfgDegrade = new EmbeddingConfig
        {
            RequireCompatibilityCheck = true,
            AllowHashingFallback = true,
            AllowLexicalFallbackOnSemanticIncompatibility = true,
            ExpectedTextProcessingId = "tp-expected",
            TreatTextProcessingMismatchAsIncompatible = false
        };

        var resDegrade = checker.Check(baseQuery, stored, cfgDegrade);
        Assert.True(resDegrade.IsCompatible);
        Assert.True(resDegrade.SemanticQualityDegraded);
        Assert.Contains("text-processing-mismatch", resDegrade.DegradationSignals);

        var cfgIncompatible = new EmbeddingConfig
        {
            RequireCompatibilityCheck = cfgDegrade.RequireCompatibilityCheck,
            AllowHashingFallback = cfgDegrade.AllowHashingFallback,
            AllowLexicalFallbackOnSemanticIncompatibility = cfgDegrade.AllowLexicalFallbackOnSemanticIncompatibility,
            ExpectedTextProcessingId = cfgDegrade.ExpectedTextProcessingId,
            TreatTextProcessingMismatchAsIncompatible = true,
            ExpectedVectorSpaceId = cfgDegrade.ExpectedVectorSpaceId,
            TreatVectorSpaceMismatchAsIncompatible = cfgDegrade.TreatVectorSpaceMismatchAsIncompatible
        };
        var resIncompatible = checker.Check(baseQuery, stored, cfgIncompatible);
        Assert.False(resIncompatible.IsCompatible);
        Assert.False(resIncompatible.SemanticQualityDegraded);
        Assert.Contains("incompatible:text-processing-mismatch", resIncompatible.Reason);
    }

    [Fact]
    public void EmbeddingCompatibilityChecker_VectorSpaceMismatch_DegradeOrIncompatibleBasedOnConfig()
    {
        var checker = new EmbeddingCompatibilityChecker();
        var stored = new StoredEmbeddingMetadata(
            ModelName: "m",
            ModelVersion: null,
            Dimension: 2,
            NormalizeEmbeddings: null,
            HasRows: true,
            SelectedEmbeddingRole: "CoreTask");

        var baseQuery = new QueryEmbeddingResult(
            Vector: new float[] { 0f, 1f },
            Provider: "p",
            ModelName: "m",
            ModelVersion: null,
            NormalizeEmbeddings: false,
            Dimension: 2,
            FallbackMode: false,
            TextProcessingId: "tp",
            VectorSpaceId: "vs-mismatch",
            InputCharCount: 1,
            EffectiveTextCharCount: 1,
            Truncated: false,
            Warnings: Array.Empty<string>());

        var cfgDegrade = new EmbeddingConfig
        {
            RequireCompatibilityCheck = true,
            AllowHashingFallback = true,
            AllowLexicalFallbackOnSemanticIncompatibility = true,
            ExpectedVectorSpaceId = "vs-expected",
            TreatVectorSpaceMismatchAsIncompatible = false
        };

        var resDegrade = checker.Check(baseQuery, stored, cfgDegrade);
        Assert.True(resDegrade.IsCompatible);
        Assert.True(resDegrade.SemanticQualityDegraded);
        Assert.Contains("vector-space-mismatch", resDegrade.DegradationSignals);

        var cfgIncompatible = new EmbeddingConfig
        {
            RequireCompatibilityCheck = cfgDegrade.RequireCompatibilityCheck,
            AllowHashingFallback = cfgDegrade.AllowHashingFallback,
            AllowLexicalFallbackOnSemanticIncompatibility = cfgDegrade.AllowLexicalFallbackOnSemanticIncompatibility,
            ExpectedVectorSpaceId = cfgDegrade.ExpectedVectorSpaceId,
            TreatVectorSpaceMismatchAsIncompatible = true,
            ExpectedTextProcessingId = cfgDegrade.ExpectedTextProcessingId,
            TreatTextProcessingMismatchAsIncompatible = cfgDegrade.TreatTextProcessingMismatchAsIncompatible,
            QueryEmbeddingProvider = cfgDegrade.QueryEmbeddingProvider,
            Endpoint = cfgDegrade.Endpoint,
            Model = cfgDegrade.Model,
            TimeoutSeconds = cfgDegrade.TimeoutSeconds,
            // Keep only the single AllowLexicalFallbackOnSemanticIncompatibility assignment above.
        };
        var resIncompatible = checker.Check(baseQuery, stored, cfgIncompatible);
        Assert.False(resIncompatible.IsCompatible);
        Assert.False(resIncompatible.SemanticQualityDegraded);
        Assert.Contains("incompatible:vector-space-mismatch", resIncompatible.Reason);
    }

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

    private sealed class FakeRepository : IKnowledgeRepository
    {
        public int SemanticCallCount { get; private set; }

        public ValueTask<IReadOnlyList<KnowledgeCandidateDto>> SearchLexicalAsync(
            SearchKnowledgeRequest request,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult<IReadOnlyList<KnowledgeCandidateDto>>([
                new KnowledgeCandidateDto(
                    KnowledgeItemId: Guid.NewGuid(),
                    RetrievalClass: RetrievalClass.Decision,
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
                    SupportedByQueryKinds: Array.Empty<string>())
            ]);

        public ValueTask<IReadOnlyList<KnowledgeCandidateDto>> SearchSemanticAsync(
            SearchKnowledgeRequest request,
            ReadOnlyMemory<float> embedding,
            CancellationToken cancellationToken)
        {
            SemanticCallCount++;
            return ValueTask.FromResult<IReadOnlyList<KnowledgeCandidateDto>>([
                new KnowledgeCandidateDto(
                    KnowledgeItemId: Guid.NewGuid(),
                    RetrievalClass: RetrievalClass.Decision,
                    Title: "t2",
                    Summary: "s2",
                    Details: null,
                    SemanticScore: 1,
                    LexicalScore: 0,
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
                    SupportedByQueryKinds: Array.Empty<string>())
            ]);
        }

        public ValueTask<GetKnowledgeItemResponse> GetKnowledgeItemAsync(GetKnowledgeItemRequest request, CancellationToken cancellationToken) =>
            throw new NotImplementedException();

        public ValueTask<GetRelatedKnowledgeResponse> GetRelatedKnowledgeAsync(GetRelatedKnowledgeRequest request, CancellationToken cancellationToken) =>
            throw new NotImplementedException();
    }

    private sealed class FakeRanking : IHybridRankingService
    {
        public IReadOnlyList<KnowledgeCandidateDto> Rank(
            IReadOnlyList<KnowledgeCandidateDto> lexical,
            IReadOnlyList<KnowledgeCandidateDto> semantic,
            SearchKnowledgeRequest request)
        {
            var all = new List<KnowledgeCandidateDto>();
            all.AddRange(semantic.Count > 0 ? semantic : lexical);
            return all;
        }
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
        private readonly StoredEmbeddingMetadata? _meta;
        public FakeMetadataInspector(StoredEmbeddingMetadata? meta) => _meta = meta;
        public ValueTask<StoredEmbeddingMetadata?> GetMetadataForRoleAsync(QueryKind queryKind, CancellationToken cancellationToken) =>
            ValueTask.FromResult(_meta);
    }

    [Fact]
    public async Task KnowledgeSearchService_UsesSemanticWhenCompatible()
    {
        var embeddingCfg = new EmbeddingConfig
        {
            QueryEmbeddingProvider = "LocalHttp",
            Model = "ignored",
            RequireCompatibilityCheck = true,
            AllowLexicalFallbackOnSemanticIncompatibility = true,
            AllowHashingFallback = false
        };

        var repo = new FakeRepository();
        var embeddingResult = new QueryEmbeddingResult(
            Vector: new float[] { 0f, 1f },
            Provider: "p",
            ModelName: "m",
            ModelVersion: null,
            NormalizeEmbeddings: false,
            Dimension: 2,
            FallbackMode: false,
            TextProcessingId: "tp",
            VectorSpaceId: "vs",
            InputCharCount: 1,
            EffectiveTextCharCount: 1,
            Truncated: false,
            Warnings: Array.Empty<string>());

        var inspector = new FakeMetadataInspector(new StoredEmbeddingMetadata(
            ModelName: "m",
            ModelVersion: null,
            Dimension: 2,
            NormalizeEmbeddings: null,
            HasRows: true,
            SelectedEmbeddingRole: "CoreTask"));

        var checker = new EmbeddingCompatibilityChecker();

        var svc = new KnowledgeSearchService(
            validator: new NoOpValidator(),
            scopeNormalizer: new PassThroughScopeNormalizer(),
            repository: repo,
            embeddingService: new FakeEmbeddingService(embeddingResult),
            ranking: new FakeRanking(),
            embeddingConfig: embeddingCfg,
            metadataInspector: inspector,
            compatibilityChecker: checker);

        var req = new SearchKnowledgeRequest(
            SchemaVersion: "1.0",
            RequestId: "r",
            QueryText: "q",
            QueryKind: QueryKind.CoreTask,
            Scopes: ScopeDtos.Empty,
            RetrievalClasses: Array.Empty<RetrievalClass>(),
            MinimumAuthority: AuthorityLevel.Draft,
            Status: KnowledgeStatus.Active,
            TopK: 1,
            IncludeEvidence: false,
            IncludeRawDetails: false);

        var resp = await svc.SearchKnowledgeAsync(req, CancellationToken.None).ConfigureAwait(false);
        Assert.Equal(1, repo.SemanticCallCount);
        Assert.Equal("m", resp.Diagnostics.QueryEmbeddingModel);
        Assert.Equal("CoreTask", resp.Diagnostics.EmbeddingRoleUsed);
    }

    [Fact]
    public async Task KnowledgeSearchService_SemanticActiveButDegraded_StillRunsSemanticAndSurfacesDegradation()
    {
        var embeddingCfg = new EmbeddingConfig
        {
            QueryEmbeddingProvider = "LocalHttp",
            Model = "ignored",
            RequireCompatibilityCheck = true,
            AllowLexicalFallbackOnSemanticIncompatibility = true,
            AllowHashingFallback = false
        };

        var repo = new FakeRepository();
        var embeddingResult = new QueryEmbeddingResult(
            Vector: new float[] { 0f, 1f },
            Provider: "p",
            ModelName: "m",
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

        var inspector = new FakeMetadataInspector(new StoredEmbeddingMetadata(
            ModelName: "m",
            ModelVersion: null,
            Dimension: 2,
            NormalizeEmbeddings: null,
            HasRows: true,
            SelectedEmbeddingRole: "CoreTask"));

        var checker = new EmbeddingCompatibilityChecker();

        var svc = new KnowledgeSearchService(
            validator: new NoOpValidator(),
            scopeNormalizer: new PassThroughScopeNormalizer(),
            repository: repo,
            embeddingService: new FakeEmbeddingService(embeddingResult),
            ranking: new FakeRanking(),
            embeddingConfig: embeddingCfg,
            metadataInspector: inspector,
            compatibilityChecker: checker);

        var req = new SearchKnowledgeRequest(
            SchemaVersion: "1.0",
            RequestId: "r",
            QueryText: "q",
            QueryKind: QueryKind.CoreTask,
            Scopes: ScopeDtos.Empty,
            RetrievalClasses: Array.Empty<RetrievalClass>(),
            MinimumAuthority: AuthorityLevel.Draft,
            Status: KnowledgeStatus.Active,
            TopK: 1,
            IncludeEvidence: false,
            IncludeRawDetails: false);

        var resp = await svc.SearchKnowledgeAsync(req, CancellationToken.None).ConfigureAwait(false);
        Assert.Equal(1, repo.SemanticCallCount);
        Assert.Equal("semantic-active:degraded:m:text-truncated-before-embedding", resp.Diagnostics.QueryEmbeddingModel);
        Assert.Equal("CoreTask|degraded", resp.Diagnostics.EmbeddingRoleUsed);
    }

    [Fact]
    public async Task KnowledgeSearchService_DegradesToLexicalOnlyWhenIncompatibleAndFallbackAllowed()
    {
        var embeddingCfg = new EmbeddingConfig
        {
            QueryEmbeddingProvider = "LocalHttp",
            Model = "ignored",
            RequireCompatibilityCheck = true,
            AllowLexicalFallbackOnSemanticIncompatibility = true,
            AllowHashingFallback = false
        };

        var repo = new FakeRepository();
        var embeddingResult = new QueryEmbeddingResult(
            Vector: new float[] { 0f, 1f },
            Provider: "p",
            ModelName: "m",
            ModelVersion: null,
            NormalizeEmbeddings: false,
            Dimension: 2,
            FallbackMode: false,
            TextProcessingId: "tp",
            VectorSpaceId: "vs",
            InputCharCount: 1,
            EffectiveTextCharCount: 1,
            Truncated: false,
            Warnings: Array.Empty<string>());

        var inspector = new FakeMetadataInspector(new StoredEmbeddingMetadata(
            ModelName: "other",
            ModelVersion: null,
            Dimension: 2,
            NormalizeEmbeddings: null,
            HasRows: true,
            SelectedEmbeddingRole: "CoreTask"));

        var checker = new EmbeddingCompatibilityChecker();

        var svc = new KnowledgeSearchService(
            validator: new NoOpValidator(),
            scopeNormalizer: new PassThroughScopeNormalizer(),
            repository: repo,
            embeddingService: new FakeEmbeddingService(embeddingResult),
            ranking: new FakeRanking(),
            embeddingConfig: embeddingCfg,
            metadataInspector: inspector,
            compatibilityChecker: checker);

        var req = new SearchKnowledgeRequest(
            SchemaVersion: "1.0",
            RequestId: "r",
            QueryText: "q",
            QueryKind: QueryKind.CoreTask,
            Scopes: ScopeDtos.Empty,
            RetrievalClasses: Array.Empty<RetrievalClass>(),
            MinimumAuthority: AuthorityLevel.Draft,
            Status: KnowledgeStatus.Active,
            TopK: 1,
            IncludeEvidence: false,
            IncludeRawDetails: false);

        var resp = await svc.SearchKnowledgeAsync(req, CancellationToken.None).ConfigureAwait(false);
        Assert.Equal(0, repo.SemanticCallCount);
        Assert.Equal("lexical-only:fallback:model-mismatch", resp.Diagnostics.QueryEmbeddingModel);
        Assert.Equal("lexical-only", resp.Diagnostics.EmbeddingRoleUsed);
    }

    [Fact]
    public async Task KnowledgeSearchService_ThrowsWhenIncompatibleAndFallbackDisallowed()
    {
        var embeddingCfg = new EmbeddingConfig
        {
            QueryEmbeddingProvider = "LocalHttp",
            Model = "ignored",
            RequireCompatibilityCheck = true,
            AllowLexicalFallbackOnSemanticIncompatibility = false,
            AllowHashingFallback = false
        };

        var repo = new FakeRepository();
        var embeddingResult = new QueryEmbeddingResult(
            Vector: new float[] { 0f, 1f },
            Provider: "p",
            ModelName: "m",
            ModelVersion: null,
            NormalizeEmbeddings: false,
            Dimension: 2,
            FallbackMode: false,
            TextProcessingId: "tp",
            VectorSpaceId: "vs",
            InputCharCount: 1,
            EffectiveTextCharCount: 1,
            Truncated: false,
            Warnings: Array.Empty<string>());

        var inspector = new FakeMetadataInspector(new StoredEmbeddingMetadata(
            ModelName: "other",
            ModelVersion: null,
            Dimension: 2,
            NormalizeEmbeddings: null,
            HasRows: true,
            SelectedEmbeddingRole: "CoreTask"));

        var checker = new EmbeddingCompatibilityChecker();

        var svc = new KnowledgeSearchService(
            validator: new NoOpValidator(),
            scopeNormalizer: new PassThroughScopeNormalizer(),
            repository: repo,
            embeddingService: new FakeEmbeddingService(embeddingResult),
            ranking: new FakeRanking(),
            embeddingConfig: embeddingCfg,
            metadataInspector: inspector,
            compatibilityChecker: checker);

        var req = new SearchKnowledgeRequest(
            SchemaVersion: "1.0",
            RequestId: "r",
            QueryText: "q",
            QueryKind: QueryKind.CoreTask,
            Scopes: ScopeDtos.Empty,
            RetrievalClasses: Array.Empty<RetrievalClass>(),
            MinimumAuthority: AuthorityLevel.Draft,
            Status: KnowledgeStatus.Active,
            TopK: 1,
            IncludeEvidence: false,
            IncludeRawDetails: false);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            svc.SearchKnowledgeAsync(req, CancellationToken.None).AsTask());
    }

    [Fact]
    public async Task ChunkRetrievalService_AppendsNoteWhenChunkSemanticDegrades()
    {
        var validator = new NoOpValidator();
        var planner = new ChunkQueryPlanner();
        var search = new SemanticDiagnosticsSearchService();
        var svc = new ChunkRetrievalService(validator, planner, search);

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
            RetrievalChunks: new[]
            {
                new RetrievalChunkDto(
                    ChunkId: "c1",
                    ChunkType: ChunkType.CoreTask,
                    Text: "q1",
                    StructuredScopes: null,
                    TaskShape: null),
                new RetrievalChunkDto(
                    ChunkId: "c2",
                    ChunkType: ChunkType.Constraint,
                    Text: "q2",
                    StructuredScopes: null,
                    TaskShape: null)
            },
            SearchProfile: new ChunkSearchProfileDto(
                ActiveOnly: true,
                MinimumAuthority: AuthorityLevel.Draft,
                MaxItemsPerChunk: 1,
                RequireTypeSeparation: false));

        var resp = await svc.RetrieveMemoryByChunksAsync(req, CancellationToken.None).ConfigureAwait(false);
        Assert.Contains("chunk:c1 semantic degraded to lexical-only: model mismatch", resp.Notes);
        Assert.DoesNotContain("chunk:c2", resp.Notes);
    }

    [Fact]
    public async Task ChunkRetrievalService_AppendsNoteWhenChunkSemanticIsActiveButDegradedQuality()
    {
        var validator = new NoOpValidator();
        var planner = new ChunkQueryPlanner();
        var search = new SemanticDegradedDiagnosticsSearchService();
        var svc = new ChunkRetrievalService(validator, planner, search);

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
            RetrievalChunks: new[]
            {
                new RetrievalChunkDto(
                    ChunkId: "c1",
                    ChunkType: ChunkType.CoreTask,
                    Text: "q1",
                    StructuredScopes: null,
                    TaskShape: null),
                new RetrievalChunkDto(
                    ChunkId: "c2",
                    ChunkType: ChunkType.Constraint,
                    Text: "q2",
                    StructuredScopes: null,
                    TaskShape: null)
            },
            SearchProfile: new ChunkSearchProfileDto(
                ActiveOnly: true,
                MinimumAuthority: AuthorityLevel.Draft,
                MaxItemsPerChunk: 1,
                RequireTypeSeparation: false));

        var resp = await svc.RetrieveMemoryByChunksAsync(req, CancellationToken.None).ConfigureAwait(false);
        Assert.Contains("chunk:c1 semantic active with degraded quality: text truncated before embedding", resp.Notes);
        Assert.DoesNotContain("chunk:c2 semantic active with degraded quality", resp.Notes);
    }

    private sealed class SemanticDiagnosticsSearchService : IKnowledgeSearchService
    {
        public ValueTask<SearchKnowledgeResponse> SearchKnowledgeAsync(SearchKnowledgeRequest request, CancellationToken cancellationToken)
        {
            // Use RequestId suffix to select a chunk id deterministically.
            if (request.RequestId.Contains(":c1", StringComparison.Ordinal))
            {
                var diag = new SearchKnowledgeDiagnosticsDto(
                    LexicalCandidateCount: 0,
                    VectorCandidateCount: 0,
                    MergedCandidateCount: 0,
                    FinalCandidateCount: 0,
                    ElapsedMs: 0,
                    QueryEmbeddingModel: "lexical-only:fallback:model-mismatch",
                    EmbeddingRoleUsed: "lexical-only");

                return ValueTask.FromResult(new SearchKnowledgeResponse(
                    SchemaVersion: request.SchemaVersion,
                    Kind: "search_knowledge",
                    RequestId: request.RequestId,
                    Candidates: Array.Empty<KnowledgeCandidateDto>(),
                    Diagnostics: diag));
            }

            var diag2 = new SearchKnowledgeDiagnosticsDto(
                LexicalCandidateCount: 0,
                VectorCandidateCount: 1,
                MergedCandidateCount: 1,
                FinalCandidateCount: 1,
                ElapsedMs: 0,
                QueryEmbeddingModel: "builder-model",
                EmbeddingRoleUsed: "CoreTask");

            return ValueTask.FromResult(new SearchKnowledgeResponse(
                SchemaVersion: request.SchemaVersion,
                Kind: "search_knowledge",
                RequestId: request.RequestId,
                Candidates: Array.Empty<KnowledgeCandidateDto>(),
                Diagnostics: diag2));
        }
    }

    private sealed class SemanticDegradedDiagnosticsSearchService : IKnowledgeSearchService
    {
        public ValueTask<SearchKnowledgeResponse> SearchKnowledgeAsync(SearchKnowledgeRequest request, CancellationToken cancellationToken)
        {
            if (request.RequestId.Contains(":c1", StringComparison.Ordinal))
            {
                var diag = new SearchKnowledgeDiagnosticsDto(
                    LexicalCandidateCount: 0,
                    VectorCandidateCount: 1,
                    MergedCandidateCount: 1,
                    FinalCandidateCount: 1,
                    ElapsedMs: 0,
                    QueryEmbeddingModel: "semantic-active:degraded:m:text-truncated-before-embedding",
                    EmbeddingRoleUsed: "CoreTask|degraded");

                return ValueTask.FromResult(new SearchKnowledgeResponse(
                    SchemaVersion: request.SchemaVersion,
                    Kind: "search_knowledge",
                    RequestId: request.RequestId,
                    Candidates: Array.Empty<KnowledgeCandidateDto>(),
                    Diagnostics: diag));
            }

            var diag2 = new SearchKnowledgeDiagnosticsDto(
                LexicalCandidateCount: 0,
                VectorCandidateCount: 1,
                MergedCandidateCount: 1,
                FinalCandidateCount: 1,
                ElapsedMs: 0,
                QueryEmbeddingModel: "m",
                EmbeddingRoleUsed: "CoreTask");

            return ValueTask.FromResult(new SearchKnowledgeResponse(
                SchemaVersion: request.SchemaVersion,
                Kind: "search_knowledge",
                RequestId: request.RequestId,
                Candidates: Array.Empty<KnowledgeCandidateDto>(),
                Diagnostics: diag2));
        }
    }

}

