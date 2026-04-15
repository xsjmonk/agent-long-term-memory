using System.Text.Json;

namespace HarnessMcp.Transport.Mcp;

public static class MonitorPayloadPreviewJson
{
    public static string SerializeRetrieveMemoryByChunks(int chunkCount, int totalItems, int notes, long elapsedMs) =>
        JsonSerializer.Serialize(new RetrieveMemoryByChunksSummaryDto
        {
            ChunkCount = chunkCount,
            TotalItems = totalItems,
            Notes = notes,
            ElapsedMs = elapsedMs
        }, TransportJsonSerializerContext.Default.RetrieveMemoryByChunksSummaryDto);

    public static string SerializeMergeRetrievalResults(int decisions, int constraints, int best, int anti, int similar, int refs, int structures, int warnings, long elapsedMs) =>
        JsonSerializer.Serialize(new MergeRetrievalResultsSummaryDto
        {
            Decisions = decisions,
            Constraints = constraints,
            Best = best,
            Anti = anti,
            Similar = similar,
            Refs = refs,
            Structures = structures,
            Warnings = warnings,
            ElapsedMs = elapsedMs
        }, TransportJsonSerializerContext.Default.MergeRetrievalResultsSummaryDto);

    public static string SerializeBuildMemoryContextPack(string? taskId, int decisions, int constraints, int best, int anti, int similar, int refs, int structures, int warnings, long elapsedMs) =>
        JsonSerializer.Serialize(new BuildMemoryContextPackSummaryDto
        {
            TaskId = taskId,
            Decisions = decisions,
            Constraints = constraints,
            Best = best,
            Anti = anti,
            Similar = similar,
            Refs = refs,
            Structures = structures,
            Warnings = warnings,
            ElapsedMs = elapsedMs
        }, TransportJsonSerializerContext.Default.BuildMemoryContextPackSummaryDto);

    public static string SerializeSearchKnowledge(string queryKind, int final, int lexical, int vector, long elapsedMs) =>
        JsonSerializer.Serialize(new SearchKnowledgeSummaryDto
        {
            QueryKind = queryKind,
            Final = final,
            Lexical = lexical,
            Vector = vector,
            ElapsedMs = elapsedMs
        }, TransportJsonSerializerContext.Default.SearchKnowledgeSummaryDto);

    public static string SerializeGetKnowledgeItem(Guid itemId, int relations, int segments) =>
        JsonSerializer.Serialize(new GetKnowledgeItemSummaryDto
        {
            ItemId = itemId.ToString(),
            Relations = relations,
            Segments = segments
        }, TransportJsonSerializerContext.Default.GetKnowledgeItemSummaryDto);

    public static string SerializeGetRelatedKnowledge(Guid root, int relationTypes, int returned) =>
        JsonSerializer.Serialize(new GetRelatedKnowledgeSummaryDto
        {
            Root = root.ToString(),
            RelationTypes = relationTypes,
            Returned = returned
        }, TransportJsonSerializerContext.Default.GetRelatedKnowledgeSummaryDto);

    public static string SerializeServerInfo(string name, string version, string mode) =>
        JsonSerializer.Serialize(new ServerInfoSummaryDto
        {
            Name = name,
            Version = version,
            Mode = mode
        }, TransportJsonSerializerContext.Default.ServerInfoSummaryDto);

    public static string SerializeReadyResponse(bool ready = true) =>
        JsonSerializer.Serialize(new ReadyResponseDto { Ready = ready }, TransportJsonSerializerContext.Default.ReadyResponseDto);
}