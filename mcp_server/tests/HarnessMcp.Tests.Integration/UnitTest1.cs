using System.Text;
using System.Linq;
using System.Text.Json;
using HarnessMcp.Contracts;
using HarnessMcp.Core;
using HarnessMcp.Infrastructure.Postgres;
using HarnessMcp.Host.Aot;
using Npgsql;
using Xunit;

namespace HarnessMcp.Tests.Integration;

public sealed class UnitTest1
{
    private sealed class MemoryPostgresFixture : IAsyncLifetime
    {
        private readonly string _schemaSqlPath;
        private readonly string _indexesSqlPath;
        private readonly string _startScriptPath;

        public bool IsAvailable { get; private set; }

        public AppConfig Config { get; }
        public NpgsqlConnection Connection { get; private set; } = null!;

        public MemoryPostgresFixture()
        {
            var repoRoot = FindUpwards(AppContext.BaseDirectory, "mcp_server_design.md");
            _schemaSqlPath = Path.Combine(repoRoot, "agent_embedding_builder", "sql", "schema.sql");
            _indexesSqlPath = Path.Combine(repoRoot, "agent_embedding_builder", "sql", "indexes.sql");
            _startScriptPath = Path.Combine(repoRoot, "start-memory-postgres.ps1");

            Config = new AppConfig
            {
                Database = new DatabaseConfig
                {
                    Host = "localhost",
                    Port = 54329,
                    Database = "memorydb",
                    Username = "user",
                    Password = "password",
                    SearchSchema = "public",
                    CommandTimeoutSeconds = 30
                },
                Retrieval = new RetrievalConfig
                {
                    LexicalCandidateCount = 50,
                    SemanticCandidateCount = 50,
                    MaxTopK = 20,
                    DefaultTopK = 5,
                    MinimumAuthority = AuthorityLevel.Reviewed
                },
                Monitoring = new MonitoringConfig
                {
                    MaxPayloadPreviewChars = 4000
                },
                Embedding = new EmbeddingConfig { QueryEmbeddingProvider = "NoOp" },
                Server = new ServerConfig { TransportMode = TransportMode.Http }
            };
        }

        public async Task InitializeAsync()
        {
            try
            {
                // Boot a single reusable docker container (pgvector-backed).
                if (!File.Exists(_startScriptPath))
                    return;

                var start = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "powershell",
                    Arguments =
                        $"-NoProfile -ExecutionPolicy Bypass -File \"{_startScriptPath}\"",
                    RedirectStandardError = true,
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var proc = System.Diagnostics.Process.Start(start);
                if (proc is null)
                    return;

                using var timeoutCts = new CancellationTokenSource(TimeSpan.FromMinutes(2));
                await proc.WaitForExitAsync(timeoutCts.Token).ConfigureAwait(false);
                IsAvailable = proc.ExitCode == 0;
            }
            catch
            {
                IsAvailable = false;
            }

            if (!IsAvailable)
                return;

            var connString =
                $"Host={Config.Database.Host};Port={Config.Database.Port};Database={Config.Database.Database};Username={Config.Database.Username};Password={Config.Database.Password}";

            Connection = new NpgsqlConnection(connString);
            await Connection.OpenAsync().ConfigureAwait(false);

            await ExecuteSqlFileAsync(Connection, _schemaSqlPath).ConfigureAwait(false);
            await ExecuteSqlFileAsync(Connection, _indexesSqlPath).ConfigureAwait(false);

            await SeedAsync().ConfigureAwait(false);
        }

        public Task DisposeAsync()
        {
            try { Connection.Dispose(); } catch { /* ignore */ }
            return Task.CompletedTask;
        }

        private async Task ExecuteSqlFileAsync(NpgsqlConnection conn, string path)
        {
            var text = await File.ReadAllTextAsync(path, Encoding.UTF8).ConfigureAwait(false);
            var statements = text.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            foreach (var raw in statements)
            {
                var stmt = raw.Trim();
                if (string.IsNullOrWhiteSpace(stmt))
                    continue;
                await using var cmd = conn.CreateCommand();
                cmd.CommandText = stmt;
                await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
            }
        }

        private async Task SeedAsync()
        {
            // Keep seed deterministic by wiping only the knowledge-related tables.
            const string wipe = """
                TRUNCATE TABLE
                  case_shapes,
                  knowledge_embeddings,
                  knowledge_item_segments,
                  knowledge_labels,
                  knowledge_scopes,
                  knowledge_tags,
                  knowledge_relations,
                  retrieval_profiles,
                  knowledge_items,
                  source_segments,
                  source_artifacts
                RESTART IDENTITY CASCADE;
                """;

            await using (var wipeCmd = Connection.CreateCommand())
            {
                wipeCmd.CommandText = wipe;
                await wipeCmd.ExecuteNonQueryAsync().ConfigureAwait(false);
            }

            var sourceArtifactId = Guid.NewGuid();
            var sourceSegmentId = Guid.NewGuid();
            var ingestionRunId = (Guid?)null;

            var headingPath = JsonSerializer.Serialize(new[] { "domain", "module", "feature" });
            var segmentRawText = "example segment raw text for lexical search";

            var vecLiteral = MakeVectorLiteral(384, 0f);
            var vecAltLiteral = MakeVectorLiteral(384, 1f);

            var tOld = new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero);
            var tNew = new DateTimeOffset(2025, 12, 31, 0, 0, 0, TimeSpan.Zero);

            await using var tx = await Connection.BeginTransactionAsync().ConfigureAwait(false);

            // source_artifacts
            await using (var cmd = Connection.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = """
                    INSERT INTO source_artifacts (id, source_type, source_ref, source_path, status, created_at, updated_at)
                    VALUES (@id, 'builder', @ref, '/fake/source', 'active', @createdAt, @updatedAt);
                    """;
                cmd.Parameters.AddWithValue("id", sourceArtifactId);
                cmd.Parameters.AddWithValue("ref", "unit-test-artifact");
                cmd.Parameters.AddWithValue("createdAt", tOld);
                cmd.Parameters.AddWithValue("updatedAt", tOld);
                await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
            }

            // source_segments
            await using (var cmd = Connection.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = """
                    INSERT INTO source_segments
                      (id, source_artifact_id, heading_path, start_offset, end_offset, start_line, end_line, span_level, segment_hash, raw_text)
                    VALUES
                      (@id, @artifactId, @headingPath::jsonb, 0, 10, 1, 1, 'span', 'seg-hash', @raw_text);
                    """;
                cmd.Parameters.AddWithValue("id", sourceSegmentId);
                cmd.Parameters.AddWithValue("artifactId", sourceArtifactId);
                cmd.Parameters.AddWithValue("headingPath", headingPath);
                cmd.Parameters.AddWithValue("raw_text", segmentRawText);
                await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
            }

            Guid InsertKnowledgeItem(
                string retrievalClassDb,
                string title,
                string summary,
                string normalized,
                DateTimeOffset updatedAtUtc,
                string layerOrConcernType,
                string layerOrConcernValue,
                bool isCoreHydrationTarget,
                bool embedVecAlt)
            {
                var id = Guid.NewGuid();

                return InsertKnowledgeItemAsync(id, retrievalClassDb, title, summary, normalized, updatedAtUtc, layerOrConcernType, layerOrConcernValue, isCoreHydrationTarget, embedVecAlt).GetAwaiter().GetResult();
            }

            async Task<Guid> InsertKnowledgeItemAsync(
                Guid id,
                string retrievalClassDb,
                string title,
                string summary,
                string normalized,
                DateTimeOffset updatedAtUtc,
                string layerOrConcernType,
                string layerOrConcernValue,
                bool isCoreHydrationTarget,
                bool embedVecAlt)
            {
                // knowledge_items
                await using (var cmd = Connection.CreateCommand())
                {
                    cmd.Transaction = tx;
                    cmd.CommandText = """
                        INSERT INTO knowledge_items
                          (id, retrieval_class, title, summary, details, normalized_retrieval_text, span_level,
                           authority_level, authority_label, status, confidence, domain, module, feature,
                           source_type, parent_item_id, created_at, updated_at, ingestion_run_id, source_artifact_id)
                        VALUES
                          (@id, @retrievalClass, @title, @summary, NULL, @normalized, 'span',
                           @authority_level, 'Approved', 'active', NULL, 'unit-domain', 'unit-module', 'unit-feature',
                           'builder', NULL, @createdAt, @updatedAt, @ingestion_run_id, @source_artifact_id);
                        """;
                    cmd.Parameters.AddWithValue("id", id);
                    cmd.Parameters.AddWithValue("retrievalClass", retrievalClassDb);
                    cmd.Parameters.AddWithValue("title", title);
                    cmd.Parameters.AddWithValue("summary", summary);
                    cmd.Parameters.AddWithValue("normalized", normalized);
                    cmd.Parameters.AddWithValue("authority_level", (int)AuthorityLevel.Approved);
                    cmd.Parameters.AddWithValue("createdAt", updatedAtUtc);
                    cmd.Parameters.AddWithValue("updatedAt", updatedAtUtc);
                    cmd.Parameters.AddWithValue("ingestion_run_id", ingestionRunId ?? (object)DBNull.Value);
                    cmd.Parameters.AddWithValue("source_artifact_id", sourceArtifactId);
                    await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
                }

                // knowledge_item_segments
                await using (var cmd = Connection.CreateCommand())
                {
                    cmd.Transaction = tx;
                    cmd.CommandText = """
                        INSERT INTO knowledge_item_segments (knowledge_item_id, source_segment_id, role)
                        VALUES (@knowledgeItemId, @sourceSegmentId, 'primary_origin');
                        """;
                    cmd.Parameters.AddWithValue("knowledgeItemId", id);
                    cmd.Parameters.AddWithValue("sourceSegmentId", sourceSegmentId);
                    await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
                }

                // knowledge_scopes (minimum set used by SQL early filtering)
                await InsertScopesAsync(id, layerOrConcernType, layerOrConcernValue).ConfigureAwait(false);

                // labels/tags for GetKnowledgeItem test
                if (isCoreHydrationTarget)
                {
                    await using (var cmd = Connection.CreateCommand())
                    {
                        cmd.Transaction = tx;
                        cmd.CommandText = """
                            INSERT INTO knowledge_labels
                              (id, knowledge_item_id, label, label_role, confidence, source_method, created_at)
                            VALUES
                              (@id, @knowledgeItemId, 'unit-label', 'builder', NULL, 'builder', @now);
                            """;
                        cmd.Parameters.AddWithValue("id", Guid.NewGuid());
                        cmd.Parameters.AddWithValue("knowledgeItemId", id);
                        cmd.Parameters.AddWithValue("now", updatedAtUtc);
                        await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
                    }

                    await using (var cmd = Connection.CreateCommand())
                    {
                        cmd.Transaction = tx;
                        cmd.CommandText = """
                            INSERT INTO knowledge_tags (id, knowledge_item_id, tag, tag_source)
                            VALUES (@t1, @knowledgeItemId, 'unit-tag', 'builder');
                            """;
                        cmd.Parameters.AddWithValue("t1", Guid.NewGuid());
                        cmd.Parameters.AddWithValue("knowledgeItemId", id);
                        await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
                    }
                }

                // knowledge_embeddings (fallback path is always normalized_retrieval_text)
                await using (var cmd = Connection.CreateCommand())
                {
                    cmd.Transaction = tx;
                    cmd.CommandText = """
                        INSERT INTO knowledge_embeddings
                          (id, knowledge_item_id, profile_id, embedding_role, embedding_text, embedding, model_name, model_version, created_at)
                        VALUES
                          (@id1, @knowledgeItemId, NULL, 'normalized_retrieval_text', 'normalized', @vec::vector, 'test-embed', 'v1', @now);
                        """;
                    cmd.Parameters.AddWithValue("id1", Guid.NewGuid());
                    cmd.Parameters.AddWithValue("knowledgeItemId", id);
                    cmd.Parameters.AddWithValue("vec", embedVecAlt ? vecAltLiteral : vecLiteral);
                    cmd.Parameters.AddWithValue("now", updatedAtUtc);
                    await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
                }

                return id;
            }

            async Task InsertScopesAsync(Guid knowledgeItemId, string type, string value)
            {
                // Provide domain+module baseline and one dimension (layer or concern) used by tests.
                await using var cmd = Connection.CreateCommand();
                cmd.Transaction = tx;
                cmd.CommandText = """
                    INSERT INTO knowledge_scopes (id, knowledge_item_id, scope_type, scope_value, weight)
                    VALUES
                      (@s1, @knowledgeItemId, 'domain', 'unit-domain', 1.0),
                      (@s2, @knowledgeItemId, 'module', 'unit-module', 1.0),
                      (@s3, @knowledgeItemId, @type, @value, 1.0);
                    """;
                cmd.Parameters.AddWithValue("s1", Guid.NewGuid());
                cmd.Parameters.AddWithValue("s2", Guid.NewGuid());
                cmd.Parameters.AddWithValue("s3", Guid.NewGuid());
                cmd.Parameters.AddWithValue("knowledgeItemId", knowledgeItemId);
                cmd.Parameters.AddWithValue("type", type);
                cmd.Parameters.AddWithValue("value", value);
                await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
            }

            // Scenario/query strings used in tests.
            const string CORE_QUERY = "year switching for yearly weighted card";
            const string CONSTRAINT_QUERY = "engine logic must not change";
            const string RISK_QUERY = "avoid recurrence of previous placement inconsistency caused by ui inference";
            const string PATTERN_QUERY = "ajax refresh with explicit loading state and no full reload";

            // Similar-case task shape JSON includes these values as tsquery tokens.
            const string SIM_TASK_TYPE = "ui-change";
            const string SIM_FEATURE_SHAPE = "card-refresh";
            const string SIM_COMPLEXITY = "medium";
            const string SIM_ENGINE_FLAG_TOKEN = "false";

            var coreDecisionId = await InsertKnowledgeItemAsync(
                Guid.NewGuid(),
                retrievalClassDb: "decision",
                title: "CoreDecision: Year switching",
                summary: "Decision for core feature target",
                normalized: $"{CORE_QUERY} decision core",
                updatedAtUtc: tNew,
                layerOrConcernType: "layer",
                layerOrConcernValue: "ui",
                isCoreHydrationTarget: true,
                embedVecAlt: false);

            var coreBestPracticeId = await InsertKnowledgeItemAsync(
                Guid.NewGuid(),
                retrievalClassDb: "best_practice",
                title: "CoreBestPractice: AJAX loading state",
                summary: "Best practice for the feature target",
                normalized: $"{CORE_QUERY} ajax loading state",
                updatedAtUtc: tNew,
                layerOrConcernType: "layer",
                layerOrConcernValue: "ui",
                isCoreHydrationTarget: false,
                embedVecAlt: false);

            var sharedDecisionId = await InsertKnowledgeItemAsync(
                Guid.NewGuid(),
                retrievalClassDb: "decision",
                title: "SharedDecision: Year+AJAX refresh",
                summary: "Shared decision supported by core and pattern",
                normalized: $"{CORE_QUERY} {PATTERN_QUERY}",
                updatedAtUtc: tNew,
                layerOrConcernType: "layer",
                layerOrConcernValue: "ui",
                isCoreHydrationTarget: false,
                embedVecAlt: false);

            var constraintId = await InsertKnowledgeItemAsync(
                Guid.NewGuid(),
                retrievalClassDb: "constraint",
                title: "Constraint: Engine logic must not change",
                summary: "Hard boundary / do-not-change rule",
                normalized: CONSTRAINT_QUERY,
                updatedAtUtc: tNew,
                layerOrConcernType: "layer",
                layerOrConcernValue: "engine",
                isCoreHydrationTarget: false,
                embedVecAlt: false);

            var decisionForConstraintId = await InsertKnowledgeItemAsync(
                Guid.NewGuid(),
                retrievalClassDb: "decision",
                title: "Decision: Engine logic must not change",
                summary: "Decision variant for the constraint chunk",
                normalized: CONSTRAINT_QUERY,
                updatedAtUtc: tNew,
                layerOrConcernType: "layer",
                layerOrConcernValue: "ui",
                isCoreHydrationTarget: false,
                embedVecAlt: false);

            var decisionScopeMismatchId = await InsertKnowledgeItemAsync(
                Guid.NewGuid(),
                retrievalClassDb: "decision",
                title: "Decision scope mismatch (engine layer)",
                summary: "Same lexical tokens but different layer scope",
                normalized: CONSTRAINT_QUERY,
                updatedAtUtc: tNew,
                layerOrConcernType: "layer",
                layerOrConcernValue: "engine",
                isCoreHydrationTarget: false,
                embedVecAlt: false);

            // Antipattern variant so constraint chunks can return antipatterns as required.
            await InsertKnowledgeItemAsync(
                Guid.NewGuid(),
                retrievalClassDb: "antipattern",
                title: "Constraint antipattern: engine logic must not change",
                summary: "Do-not-change boundary expressed as an antipattern",
                normalized: CONSTRAINT_QUERY,
                updatedAtUtc: tNew,
                layerOrConcernType: "layer",
                layerOrConcernValue: "engine",
                isCoreHydrationTarget: false,
                embedVecAlt: false);

            var decisionZeroScoreId = await InsertKnowledgeItemAsync(
                Guid.NewGuid(),
                retrievalClassDb: "decision",
                title: "Decision zero-score junk",
                summary: "Does not contain the lexical query tokens",
                normalized: "completely unrelated persistence and authorization guidance",
                updatedAtUtc: tNew,
                layerOrConcernType: "layer",
                layerOrConcernValue: "ui",
                isCoreHydrationTarget: false,
                embedVecAlt: false);

            var bestPracticeForConstraintPhraseId = await InsertKnowledgeItemAsync(
                Guid.NewGuid(),
                retrievalClassDb: "best_practice",
                title: "BestPractice phrase (should be filtered out by retrieval class)",
                summary: "Matches constraint query text but wrong retrieval_class for Decision-only search",
                normalized: CONSTRAINT_QUERY,
                updatedAtUtc: tNew,
                layerOrConcernType: "layer",
                layerOrConcernValue: "ui",
                isCoreHydrationTarget: false,
                embedVecAlt: false);

            var riskAntiPatternId = await InsertKnowledgeItemAsync(
                Guid.NewGuid(),
                retrievalClassDb: "antipattern",
                title: "Risk anti-pattern: placement inconsistency",
                summary: "Historical defect / fragility cue",
                normalized: RISK_QUERY,
                updatedAtUtc: tNew,
                layerOrConcernType: "concern",
                layerOrConcernValue: "placement",
                isCoreHydrationTarget: false,
                embedVecAlt: false);

            var patternBestPracticeId = await InsertKnowledgeItemAsync(
                Guid.NewGuid(),
                retrievalClassDb: "best_practice",
                title: "Pattern: AJAX refresh guidance",
                summary: "Pattern-level implementation guidance",
                normalized: PATTERN_QUERY,
                updatedAtUtc: tNew,
                layerOrConcernType: "layer",
                layerOrConcernValue: "ui",
                isCoreHydrationTarget: false,
                embedVecAlt: false);

            var similarCorrectId = await InsertKnowledgeItemAsync(
                Guid.NewGuid(),
                retrievalClassDb: "similar_case",
                title: "SimilarCase: UI-only card refresh no engine change",
                summary: "Structurally similar historical case",
                normalized: $"taskType {SIM_TASK_TYPE} featureShape {SIM_FEATURE_SHAPE} engineChangeAllowed {SIM_ENGINE_FLAG_TOKEN} likelyLayers ui api riskSignals placement consistency async refresh complexity {SIM_COMPLEXITY} {RISK_QUERY}",
                updatedAtUtc: tNew,
                layerOrConcernType: "layer",
                layerOrConcernValue: "ui",
                isCoreHydrationTarget: false,
                embedVecAlt: false);

            var similarWrongId = await InsertKnowledgeItemAsync(
                Guid.NewGuid(),
                retrievalClassDb: "similar_case",
                title: "SimilarCase WRONG: card refresh mismatched structure",
                summary: "Card refresh no engine change wrong shape in case_shapes",
                normalized: $"taskType {SIM_TASK_TYPE} featureShape {SIM_FEATURE_SHAPE} featureShape {SIM_FEATURE_SHAPE} featureShape {SIM_FEATURE_SHAPE} featureShape {SIM_FEATURE_SHAPE} featureShape {SIM_FEATURE_SHAPE} engineChangeAllowed {SIM_ENGINE_FLAG_TOKEN} likelyLayers ui api riskSignals placement consistency async refresh complexity {SIM_COMPLEXITY} {RISK_QUERY}",
                updatedAtUtc: tNew,
                layerOrConcernType: "layer",
                layerOrConcernValue: "ui",
                isCoreHydrationTarget: false,
                embedVecAlt: false);

            // Risk chunks filter by `concern`; add a matching concern scope so similar cases can participate in risk retrieval.
            await InsertScopesAsync(similarCorrectId, "concern", "placement").ConfigureAwait(false);
            await InsertScopesAsync(similarWrongId, "concern", "placement").ConfigureAwait(false);

            // case_shapes for SimilarCase provider
            async Task InsertCaseShapeAsync(Guid knowledgeItemId, string taskType, string featureShape, bool engineChangeAllowed, string[] likelyLayers, string[] riskSignals, string? complexity)
            {
                var likelyJson = JsonSerializer.Serialize(likelyLayers);
                var riskJson = JsonSerializer.Serialize(riskSignals);
                await using var cmd = Connection.CreateCommand();
                cmd.Transaction = tx;
                cmd.CommandText = """
                    INSERT INTO case_shapes
                      (id, knowledge_item_id, task_type, feature_shape, engine_change_allowed, likely_layers, risk_signals, complexity)
                    VALUES
                      (@id, @knowledgeItemId, @taskType, @featureShape, @engineChangeAllowed, @likely::jsonb, @risk::jsonb, @complexity);
                    """;
                cmd.Parameters.AddWithValue("id", Guid.NewGuid());
                cmd.Parameters.AddWithValue("knowledgeItemId", knowledgeItemId);
                cmd.Parameters.AddWithValue("taskType", taskType);
                cmd.Parameters.AddWithValue("featureShape", featureShape);
                cmd.Parameters.AddWithValue("engineChangeAllowed", engineChangeAllowed);
                cmd.Parameters.AddWithValue("likely", likelyJson);
                cmd.Parameters.AddWithValue("risk", riskJson);
                cmd.Parameters.AddWithValue("complexity", (object?)complexity ?? DBNull.Value);
                await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
            }

            await InsertCaseShapeAsync(
                similarCorrectId,
                taskType: SIM_TASK_TYPE,
                featureShape: SIM_FEATURE_SHAPE,
                engineChangeAllowed: false,
                likelyLayers: new[] { "ui", "api" },
                riskSignals: new[] { "placement consistency", "async refresh" },
                complexity: SIM_COMPLEXITY).ConfigureAwait(false);

            await InsertCaseShapeAsync(
                similarWrongId,
                taskType: SIM_TASK_TYPE,
                featureShape: "card-refresh-MISMATCH",
                engineChangeAllowed: true,
                likelyLayers: new[] { "ui", "api" },
                riskSignals: new[] { "placement consistency", "async refresh" },
                complexity: SIM_COMPLEXITY).ConfigureAwait(false);

            await tx.CommitAsync().ConfigureAwait(false);
        }

        private static string MakeVectorLiteral(int dims, float value)
        {
            var sb = new StringBuilder();
            sb.Append('[');
            for (var i = 0; i < dims; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append(value.ToString("G9", System.Globalization.CultureInfo.InvariantCulture));
            }
            sb.Append(']');
            return sb.ToString();
        }

        private static string FindUpwards(string startDir, string targetFile)
        {
            var dir = new DirectoryInfo(startDir);
            while (dir is not null)
            {
                var candidate = Path.Combine(dir.FullName, targetFile);
                if (File.Exists(candidate))
                    return dir.FullName;
                dir = dir.Parent;
            }
            throw new InvalidOperationException($"Unable to find {targetFile} upwards from {startDir}.");
        }
    }

    private readonly MemoryPostgresFixture _fixture = new();

    [Fact]
    public async Task SearchLexical_ExcludesZeroScoreAndFiltersRetrievalClassAndScope()
    {
        await _fixture.InitializeAsync().ConfigureAwait(false);
        try
        {
            if (!_fixture.IsAvailable)
                return;

            var repo = new PostgresKnowledgeRepository(_fixture.Config, NpgsqlDataSourceFactory.Create(_fixture.Config.Database));

            const string query = "engine logic must not change";
            var req = new SearchKnowledgeRequest(
                SchemaVersion: "1.0",
                RequestId: "r1",
                QueryText: query,
                QueryKind: QueryKind.Constraint,
                Scopes: new ScopeFilterDto(
                    Domains: Array.Empty<string>(),
                    Modules: Array.Empty<string>(),
                    Features: Array.Empty<string>(),
                    Layers: new[] { "ui" },
                    Concerns: Array.Empty<string>(),
                    Repos: Array.Empty<string>(),
                    Services: Array.Empty<string>(),
                    Symbols: Array.Empty<string>()),
                RetrievalClasses: new[] { RetrievalClass.Decision },
                MinimumAuthority: AuthorityLevel.Reviewed,
                Status: KnowledgeStatus.Active,
                TopK: 5,
                IncludeEvidence: false,
                IncludeRawDetails: false);

            var result = await repo.SearchLexicalAsync(req, CancellationToken.None).ConfigureAwait(false);
            Assert.NotNull(result);
            Assert.NotEmpty(result);

            Assert.Contains(result, x => x.Title == "Decision: Engine logic must not change");
            Assert.DoesNotContain(result, x => x.Title == "Decision zero-score junk");
            Assert.DoesNotContain(result, x => x.Title == "BestPractice phrase (should be filtered out by retrieval class)");
            Assert.DoesNotContain(result, x => x.Title == "Decision scope mismatch (engine layer)");

            Assert.All(result, x => Assert.True(x.LexicalScore > 0));
        }
        finally
        {
            // We don't dispose fixture per-test to keep speed; xUnit will call DisposeAsync at process end.
        }
    }

    [Fact]
    public async Task SearchSemantic_FiltersRetrievalClassAndScopeInSql()
    {
        await _fixture.InitializeAsync().ConfigureAwait(false);
        try
        {
            if (!_fixture.IsAvailable)
                return;

            var repo = new PostgresKnowledgeRepository(_fixture.Config, NpgsqlDataSourceFactory.Create(_fixture.Config.Database));

            const string queryText = "ignored-in-semantic";
            var req = new SearchKnowledgeRequest(
                SchemaVersion: "1.0",
                RequestId: "r2",
                QueryText: queryText,
                QueryKind: QueryKind.CoreTask,
                Scopes: new ScopeFilterDto(
                    Domains: Array.Empty<string>(),
                    Modules: Array.Empty<string>(),
                    Features: Array.Empty<string>(),
                    Layers: new[] { "ui" },
                    Concerns: Array.Empty<string>(),
                    Repos: Array.Empty<string>(),
                    Services: Array.Empty<string>(),
                    Symbols: Array.Empty<string>()),
                RetrievalClasses: new[] { RetrievalClass.Decision },
                MinimumAuthority: AuthorityLevel.Reviewed,
                Status: KnowledgeStatus.Active,
                TopK: 5,
                IncludeEvidence: false,
                IncludeRawDetails: false);

            // For semantic search we provide a vector identical to the seed.
            var zeroVec = new float[384];
            var embedding = new ReadOnlyMemory<float>(zeroVec);

            var result = await repo.SearchSemanticAsync(req, embedding, CancellationToken.None).ConfigureAwait(false);
            Assert.NotNull(result);
            Assert.NotEmpty(result);

            Assert.All(result, x => Assert.Equal(RetrievalClass.Decision, x.RetrievalClass));
            Assert.All(result, x =>
                Assert.True(x.Scopes.Layers.Any(l => string.Equals(l, "ui", StringComparison.OrdinalIgnoreCase))));
        }
        finally { }
    }

    [Fact]
    public async Task SearchKnowledge_SimilarCase_RanksByCaseShapeProvider()
    {
        await _fixture.InitializeAsync().ConfigureAwait(false);
        try
        {
            if (!_fixture.IsAvailable)
                return;

            var repo = new PostgresKnowledgeRepository(_fixture.Config, NpgsqlDataSourceFactory.Create(_fixture.Config.Database));
            var dataSource = NpgsqlDataSourceFactory.Create(_fixture.Config.Database);

            const string queryKindLayerUi = "ui";

            var shape = new SimilarCaseShapeDto(
                TaskType: "ui-change",
                FeatureShape: "card-refresh",
                EngineChangeAllowed: false,
                LikelyLayers: new[] { "ui", "api" },
                RiskSignals: new[] { "placement consistency", "async refresh" },
                Complexity: "medium");

            var queryText = JsonSerializer.Serialize(shape, AppJsonSerializerContext.Default.SimilarCaseShapeDto);

            var req = new SearchKnowledgeRequest(
                SchemaVersion: "1.0",
                RequestId: "sim",
                QueryText: queryText,
                QueryKind: QueryKind.SimilarCase,
                Scopes: new ScopeFilterDto(
                    Domains: Array.Empty<string>(),
                    Modules: Array.Empty<string>(),
                    Features: Array.Empty<string>(),
                    Layers: new[] { queryKindLayerUi, "api" },
                    Concerns: Array.Empty<string>(),
                    Repos: Array.Empty<string>(),
                    Services: Array.Empty<string>(),
                    Symbols: Array.Empty<string>()),
                RetrievalClasses: new[] { RetrievalClass.SimilarCase },
                MinimumAuthority: AuthorityLevel.Reviewed,
                Status: KnowledgeStatus.Active,
                TopK: 2,
                IncludeEvidence: false,
                IncludeRawDetails: false);

            var lexical = await repo.SearchLexicalAsync(req, CancellationToken.None).ConfigureAwait(false);

            var rankedReal = new HybridRankingService(
                    new AuthorityPolicy(),
                    new PostgresCaseShapeScoreProvider(dataSource, _fixture.Config.Database.SearchSchema))
                .Rank(lexical, Array.Empty<KnowledgeCandidateDto>(), req);

            Assert.NotEmpty(rankedReal);
            Assert.Equal("SimilarCase: UI-only card refresh no engine change", rankedReal[0].Title);

            var rankedNoOp = new HybridRankingService(
                    new AuthorityPolicy(),
                    new NoOpCaseShapeScoreProvider())
                .Rank(lexical, Array.Empty<KnowledgeCandidateDto>(), req);

            Assert.NotEmpty(rankedNoOp);
        }
        finally { }
    }

    [Fact]
    public async Task HarnessFlowAccuracy_EndToEnd_ScenariosAtoF()
    {
        await _fixture.InitializeAsync().ConfigureAwait(false);
        try
        {
            if (!_fixture.IsAvailable)
                return;

            var app = CompositionRoot.Build(_fixture.Config);

            var CORE_QUERY = "year switching for yearly weighted card";
            var CONSTRAINT_QUERY = "engine logic must not change";
            var RISK_QUERY = "avoid recurrence of previous placement inconsistency caused by ui inference";
            var PATTERN_QUERY = "ajax refresh with explicit loading state and no full reload";

            var shape = new SimilarCaseShapeDto(
                TaskType: "ui-change",
                FeatureShape: "card-refresh",
                EngineChangeAllowed: false,
                LikelyLayers: new[] { "ui", "api" },
                RiskSignals: new[] { "placement consistency", "async refresh" },
                Complexity: "medium");

            var requirementIntent = new RequirementIntentDto(
                TaskType: "ui-change",
                Domain: "unit-domain",
                Module: "unit-module",
                Feature: "unit-feature",
                HardConstraints: new[] { "engine must not change" },
                RiskSignals: new[] { "placement inconsistency" });

            var layersUi = new[] { "ui" };
            var layersUiApi = new[] { "ui", "api" };
            var scopesUi = new ScopeFilterDto(
                Domains: Array.Empty<string>(),
                Modules: Array.Empty<string>(),
                Features: Array.Empty<string>(),
                Layers: layersUi,
                Concerns: Array.Empty<string>(),
                Repos: Array.Empty<string>(),
                Services: Array.Empty<string>(),
                Symbols: Array.Empty<string>());

            var scopesUiApi = new ScopeFilterDto(
                Domains: Array.Empty<string>(),
                Modules: Array.Empty<string>(),
                Features: Array.Empty<string>(),
                Layers: layersUiApi,
                Concerns: Array.Empty<string>(),
                Repos: Array.Empty<string>(),
                Services: Array.Empty<string>(),
                Symbols: Array.Empty<string>());

            var scopesEngine = new ScopeFilterDto(
                Domains: Array.Empty<string>(),
                Modules: Array.Empty<string>(),
                Features: Array.Empty<string>(),
                Layers: new[] { "engine" },
                Concerns: Array.Empty<string>(),
                Repos: Array.Empty<string>(),
                Services: Array.Empty<string>(),
                Symbols: Array.Empty<string>());

            var scopesPlacementConcern = new ScopeFilterDto(
                Domains: Array.Empty<string>(),
                Modules: Array.Empty<string>(),
                Features: Array.Empty<string>(),
                Layers: Array.Empty<string>(),
                Concerns: new[] { "placement" },
                Repos: Array.Empty<string>(),
                Services: Array.Empty<string>(),
                Symbols: Array.Empty<string>());

            var similarChunk = new RetrievalChunkDto(
                ChunkId: "c5",
                ChunkType: ChunkType.SimilarCase,
                Text: null,
                StructuredScopes: scopesUiApi,
                TaskShape: shape);

            var request = new RetrieveMemoryByChunksRequest(
                SchemaVersion: "1.0",
                RequestId: "r-flow",
                TaskId: "task-flow",
                RequirementIntent: requirementIntent,
                RetrievalChunks: new[]
                {
                    new RetrievalChunkDto(
                        ChunkId: "c1",
                        ChunkType: ChunkType.CoreTask,
                        Text: CORE_QUERY,
                        StructuredScopes: scopesUi,
                        TaskShape: null),
                    new RetrievalChunkDto(
                        ChunkId: "c2",
                        ChunkType: ChunkType.Constraint,
                        Text: CONSTRAINT_QUERY,
                        StructuredScopes: scopesEngine,
                        TaskShape: null),
                    new RetrievalChunkDto(
                        ChunkId: "c3",
                        ChunkType: ChunkType.Risk,
                        Text: RISK_QUERY,
                        StructuredScopes: scopesPlacementConcern,
                        TaskShape: null),
                    new RetrievalChunkDto(
                        ChunkId: "c4",
                        ChunkType: ChunkType.Pattern,
                        Text: PATTERN_QUERY,
                        StructuredScopes: scopesUi,
                        TaskShape: null),
                    similarChunk
                },
                SearchProfile: new ChunkSearchProfileDto(
                    ActiveOnly: true,
                    MinimumAuthority: AuthorityLevel.Reviewed,
                    MaxItemsPerChunk: 3,
                    RequireTypeSeparation: false));

            var retrieved = await app.KnowledgeQueryTools.RetrieveMemoryByChunks(request, CancellationToken.None).ConfigureAwait(false);
            Assert.NotEmpty(retrieved.ChunkResults);

            ChunkRetrievalResultDto Chunk(string id) => retrieved.ChunkResults.First(x => x.ChunkId == id);
            var c1 = Chunk("c1");
            var c2 = Chunk("c2");
            var c3 = Chunk("c3");
            var c4 = Chunk("c4");
            var c5 = Chunk("c5");

            Assert.Contains(c1.Results.Decisions, x => x.Title == "CoreDecision: Year switching");
            Assert.Contains(c1.Results.BestPractices, x => x.Title == "CoreBestPractice: AJAX loading state");

            Assert.Contains(c2.Results.Constraints, x => x.Title == "Constraint: Engine logic must not change");
            Assert.Contains(c2.Results.Antipatterns, x => x.Title == "Constraint antipattern: engine logic must not change");

            Assert.Contains(c3.Results.Antipatterns, x => x.Title == "Risk anti-pattern: placement inconsistency");
            Assert.Contains(c3.Results.SimilarCases, x => x.Title == "SimilarCase: UI-only card refresh no engine change");

            Assert.Contains(c4.Results.BestPractices, x => x.Title == "Pattern: AJAX refresh guidance");

            Assert.Contains(c5.Results.SimilarCases, x => x.Title == "SimilarCase: UI-only card refresh no engine change");
            Assert.Equal("SimilarCase: UI-only card refresh no engine change", c5.Results.SimilarCases[0].Title);

            // Trio: merge then assemble
            var mergeRequest = new MergeRetrievalResultsRequest(
                SchemaVersion: "1.0",
                RequestId: "m1",
                TaskId: "task-flow",
                Retrieved: retrieved);
            var merged = await app.KnowledgeQueryTools.MergeRetrievalResults(mergeRequest, CancellationToken.None).ConfigureAwait(false);

            var shared = merged.Decisions.FirstOrDefault(x => x.Item.Title == "SharedDecision: Year+AJAX refresh");
            Assert.NotNull(shared);
            Assert.Contains(shared!.SupportedByChunkIds, x => x == "c1");
            Assert.Contains(shared!.SupportedByChunkIds, x => x == "c4");

            var packRequest = new BuildMemoryContextPackRequest(
                SchemaVersion: "1.0",
                RequestId: "b1",
                TaskId: "task-flow",
                RequirementIntent: requirementIntent,
                Retrieved: retrieved,
                Merged: merged);
            var pack = await app.KnowledgeQueryTools.BuildMemoryContextPack(packRequest, CancellationToken.None).ConfigureAwait(false);

            Assert.Contains(pack.ContextPack.Decisions, x => x.Item.Title == "SharedDecision: Year+AJAX refresh");
            Assert.DoesNotContain(pack.Diagnostics.Warnings, w => w.Contains("empty decisions section", StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain(pack.Diagnostics.Warnings, w => w.Contains("empty constraints section", StringComparison.OrdinalIgnoreCase));
        }
        finally { }
    }

    [Fact]
    public async Task GetKnowledgeItem_LoadsSegmentsLabelsScopes()
    {
        await _fixture.InitializeAsync().ConfigureAwait(false);
        try
        {
            if (!_fixture.IsAvailable)
                return;

            var repo = new PostgresKnowledgeRepository(_fixture.Config, NpgsqlDataSourceFactory.Create(_fixture.Config.Database));

            var req = new GetKnowledgeItemRequest(
                SchemaVersion: "1.0",
                RequestId: "r3",
                KnowledgeItemId: FetchSeededKnowledgeItemId(_fixture.Connection),
                IncludeRelations: false,
                IncludeSegments: true,
                IncludeLabels: true,
                IncludeTags: true,
                IncludeScopes: true);

            var result = await repo.GetKnowledgeItemAsync(req, CancellationToken.None).ConfigureAwait(false);
            Assert.NotNull(result);
            Assert.NotEmpty(result.Segments);
            Assert.Contains("unit-label", result.Item.Labels, StringComparer.OrdinalIgnoreCase);
            Assert.Contains("unit-tag", result.Item.Tags, StringComparer.OrdinalIgnoreCase);
            Assert.Contains("unit-domain", result.Item.Scopes.Domains, StringComparer.OrdinalIgnoreCase);
        }
        finally { }
    }

    [Fact]
    public async Task GetRelatedKnowledge_ReturnsEmpty()
    {
        await _fixture.InitializeAsync().ConfigureAwait(false);
        try
        {
            if (!_fixture.IsAvailable)
                return;

            var repo = new PostgresKnowledgeRepository(_fixture.Config, NpgsqlDataSourceFactory.Create(_fixture.Config.Database));

            var id = FetchSeededKnowledgeItemId(_fixture.Connection);
            var req = new GetRelatedKnowledgeRequest(
                SchemaVersion: "1.0",
                RequestId: "r4",
                KnowledgeItemId: id,
                RelationTypes: new[] { RelationType.Related },
                TopK: 5);

            var result = await repo.GetRelatedKnowledgeAsync(req, CancellationToken.None).ConfigureAwait(false);
            Assert.NotNull(result);
            Assert.Empty(result.Items);
        }
        finally { }
    }

    private static Guid FetchSeededKnowledgeItemId(NpgsqlConnection conn)
    {
        // Pick the labeled core decision item seeded for GetKnowledgeItem assertions.
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT id FROM public.knowledge_items WHERE title = 'CoreDecision: Year switching' LIMIT 1";
        return cmd.ExecuteScalar() is Guid g ? g : Guid.Empty;
    }
}
