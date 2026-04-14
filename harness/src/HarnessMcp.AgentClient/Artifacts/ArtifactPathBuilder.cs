namespace HarnessMcp.AgentClient.Artifacts;

public sealed class ArtifactPathBuilder
{
    public string SessionJson(string outputDir) => Path.Combine(outputDir, "00-session.json");
    public string RawTaskText(string outputDir) => Path.Combine(outputDir, "01-raw-task.txt");
    public string RequirementIntentJson(string outputDir) => Path.Combine(outputDir, "02-requirement-intent.json");
    public string RetrievalChunksJson(string outputDir) => Path.Combine(outputDir, "03-retrieval-chunks.json");
    public string ChunkQualityReportJson(string outputDir) => Path.Combine(outputDir, "04-chunk-quality-report.json");
    public string RetrievedMemoryByChunksJson(string outputDir) => Path.Combine(outputDir, "05-retrieve-memory-by-chunks.json");
    public string MergedRetrievalResultsJson(string outputDir) => Path.Combine(outputDir, "06-merge-retrieval-results.json");
    public string BuildMemoryContextPackJson(string outputDir) => Path.Combine(outputDir, "07-build-memory-context-pack.json");
    public string PlanningMemorySummaryMd(string outputDir) => Path.Combine(outputDir, "08-planning-memory-summary.md");
    public string ExecutionPlanJson(string outputDir) => Path.Combine(outputDir, "09-execution-plan.json");
    public string ExecutionPlanMd(string outputDir) => Path.Combine(outputDir, "10-execution-plan.md");
    public string WorkerPacketMd(string outputDir) => Path.Combine(outputDir, "11-worker-packet.md");
    public string HarnessRunManifestJson(string outputDir) => Path.Combine(outputDir, "12-harness-run-manifest.json");
}

