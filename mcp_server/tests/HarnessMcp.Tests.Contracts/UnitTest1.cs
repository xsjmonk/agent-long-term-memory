using System.Reflection;
using System.Text.Json;
using HarnessMcp.Contracts;
using Xunit;

namespace HarnessMcp.Tests.Contracts;

public sealed class UnitTest1
{
    [Fact]
    public void AppJsonSerializerContext_SerializesMonitorSnapshot_CamelCaseFields()
    {
        var snap = new MonitorSnapshotDto(
            Server: new MonitorServerSummaryDto(
                ServerName: "HarnessMcp",
                ServerVersion: "1.0.0",
                ProtocolMode: "http+mcp",
                MonitoringEnabled: true,
                RealtimeEnabled: false,
                StartedUtc: DateTimeOffset.UtcNow,
                Environment: "Test",
                DatabaseConfigured: true,
                EmbeddingProviderSummary: "NoOp model="),
            RecentLogs: Array.Empty<MonitorEventDto>(),
            RecentOperations: Array.Empty<MonitorEventDto>(),
            RecentTimings: Array.Empty<MonitorEventDto>(),
            RecentWarnings: Array.Empty<MonitorEventDto>(),
            RecentOutputs: Array.Empty<MonitorEventDto>(),
            LastSequence: 123);

        var json = JsonSerializer.Serialize(snap, AppJsonSerializerContext.Default.MonitorSnapshotDto);

        // Spot-check a few key property names that must be stable for the UI contract.
        Assert.Contains("\"server\":", json);
        Assert.Contains("\"serverName\"", json);
        Assert.Contains("\"lastSequence\":123", json);
        Assert.DoesNotContain("MonitorLogEntryDto", json);
    }

    [Fact]
    public void MonitorEventKind_SerializesAsStableRepresentation()
    {
        var evt = new MonitorEventDto(
            Sequence: 1,
            TimestampUtc: DateTimeOffset.UnixEpoch,
            EventKind: MonitorEventKind.RequestSuccess,
            RequestId: "r",
            ToolName: "search_knowledge",
            TaskId: "t",
            Level: "Info",
            Summary: "ok",
            PayloadPreviewJson: null);

        var json = JsonSerializer.Serialize(evt, AppJsonSerializerContext.Default.MonitorEventDto);
        using var doc = JsonDocument.Parse(json);
        var elem = doc.RootElement.GetProperty("eventKind");
        Assert.True(elem.ValueKind is JsonValueKind.Number or JsonValueKind.String);

        if (elem.ValueKind == JsonValueKind.Number)
        {
            Assert.Equal(2, elem.GetInt32()); // enum underlying value for RequestSuccess
        }
        else
        {
            Assert.Equal("RequestSuccess", elem.GetString());
        }
    }

    [Fact]
    public void KnowledgeQueryTools_ExposeRequiredToolNames()
    {
        var type = typeof(HarnessMcp.Transport.Mcp.KnowledgeQueryTools);
        var methods = type.GetMethods(BindingFlags.Public | BindingFlags.Instance);

        string[] expected =
        [
            "retrieve_memory_by_chunks",
            "merge_retrieval_results",
            "build_memory_context_pack",
            "search_knowledge",
            "get_knowledge_item",
            "get_related_knowledge",
            "get_server_info"
        ];

        static string? GetToolName(MethodInfo m)
        {
            var attr = m.GetCustomAttributes(inherit: true)
                .FirstOrDefault(a => a.GetType().Name is "McpServerToolAttribute" or "McpServerTool");
            if (attr is null)
                return null;

            var nameProp = attr.GetType().GetProperty("Name");
            return nameProp?.GetValue(attr) as string;
        }

        foreach (var name in expected)
        {
            var toolMethod = methods.FirstOrDefault(m => GetToolName(m) == name);
            Assert.NotNull(toolMethod);
        }
    }
}

