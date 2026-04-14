using FluentAssertions;
using HarnessMcp.AgentClient.Transport;
using HarnessMcp.Contracts;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Xunit;

namespace HarnessMcp.AgentClient.Tests;

public sealed class HttpMcpToolClientTests
{
    [Fact]
    public async Task request_response_mapping_for_get_server_info_and_retrieve_memory()
    {
        var handler = new CapturingHandler(async (req, ct) =>
        {
            var body = await req.Content!.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            root.GetProperty("method").GetString().Should().Be("tools/call");

            var toolName = root.GetProperty("params").GetProperty("name").GetString();
            var id = root.GetProperty("id").GetRawText();

            if (toolName == "get_server_info")
            {
                var responseObj = new
                {
                    jsonrpc = "2.0",
                    id = 1,
                    result = new
                    {
                        schemaVersion = "1.0",
                        kind = "server_info",
                        serverName = "HarnessMcp",
                        serverVersion = "1.0.0",
                        protocolMode = "http+mcp",
                        features = new
                        {
                            retrieveMemoryByChunks = true,
                            mergeRetrievalResults = true,
                            buildMemoryContextPack = true,
                            searchKnowledge = true,
                            getKnowledgeItem = true,
                            getRelatedKnowledge = true,
                            httpTransport = true,
                            stdioTransport = false,
                            writeOperations = false,
                            monitoringUi = false,
                            realtimeTracking = false
                        },
                        schemaSet = new
                        {
                            retrieveMemoryByChunks = "1.0",
                            mergeRetrievalResults = "1.0",
                            buildMemoryContextPack = "1.0",
                            searchKnowledge = "1.0",
                            getKnowledgeItem = "1.0",
                            getRelatedKnowledge = "1.0",
                            getServerInfo = "1.0"
                        }
                    }
                };

                var resp = new HttpResponseMessage(HttpStatusCode.OK);
                resp.Content = new StringContent(JsonSerializer.Serialize(responseObj), Encoding.UTF8, "application/json");
                return resp;
            }

            if (toolName == "retrieve_memory_by_chunks")
            {
                var argsElem = root.GetProperty("params").GetProperty("arguments");
                argsElem.GetProperty("schemaVersion").GetString().Should().Be("1.0");
                argsElem.GetProperty("taskId").GetString().Should().Be("t");

                var responseObj = new
                {
                    jsonrpc = "2.0",
                    id = 1,
                    result = new
                    {
                        schemaVersion = "1.0",
                        kind = "retrieve_memory_by_chunks",
                        requestId = "req",
                        taskId = "t",
                        chunkResults = Array.Empty<object>(),
                        notes = Array.Empty<string>(),
                        elapsedMs = 1
                    }
                };

                var resp = new HttpResponseMessage(HttpStatusCode.OK);
                resp.Content = new StringContent(JsonSerializer.Serialize(responseObj), Encoding.UTF8, "application/json");
                return resp;
            }

            throw new InvalidOperationException("Unexpected tool call: " + toolName);
        });

        var http = new HttpClient(handler);
        var client = new HttpMcpToolClient("http://example/mcp", http);

        var info = await client.GetServerInfoAsync(CancellationToken.None);
        info.Features.RetrieveMemoryByChunks.Should().BeTrue();

        var retrieveReq = new RetrieveMemoryByChunksRequest(
            SchemaVersion: "1.0",
            RequestId: "req",
            TaskId: "t",
            RequirementIntent: new RequirementIntentDto(
                TaskType: "tt",
                Domain: null,
                Module: null,
                Feature: null,
                HardConstraints: Array.Empty<string>(),
                RiskSignals: Array.Empty<string>()),
            RetrievalChunks: Array.Empty<RetrievalChunkDto>(),
            SearchProfile: new ChunkSearchProfileDto(
                ActiveOnly: true,
                MinimumAuthority: AuthorityLevel.Reviewed,
                MaxItemsPerChunk: 5,
                RequireTypeSeparation: true));

        var retrieved = await client.RetrieveMemoryByChunksAsync(retrieveReq, CancellationToken.None);
        retrieved.TaskId.Should().Be("t");
    }

    private sealed class CapturingHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _responder;

        public CapturingHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> responder)
        {
            _responder = responder;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => _responder(request, cancellationToken);
    }
}

