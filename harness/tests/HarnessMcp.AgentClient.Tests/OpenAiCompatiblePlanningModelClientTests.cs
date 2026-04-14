using FluentAssertions;
using HarnessMcp.AgentClient.Transport;
using HarnessMcp.AgentClient.Support;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using Xunit;

namespace HarnessMcp.AgentClient.Tests;

public sealed class OpenAiCompatiblePlanningModelClientTests
{
    [Fact]
    public async Task parses_valid_json_object_from_model_content()
    {
        var handler = new FakeHttpMessageHandler(async (req, ct) =>
        {
            var resp = new HttpResponseMessage(HttpStatusCode.OK);
            resp.Content = new StringContent(
                """
                {
                  "choices": [
                    {
                      "message": {
                        "content": "{\"TaskType\":\"core\",\"Domain\":null,\"Module\":null,\"Feature\":null,\"Goal\":\"do it\",\"RequestedOperations\":[],\"HardConstraints\":[\"hc\"],\"SoftConstraints\":[],\"RiskSignals\":[],\"CandidateLayers\":[],\"RetrievalFocuses\":[],\"Ambiguities\":[],\"Complexity\":\"low\"}"
                      }
                    }
                  ]
                }
                """,
                Encoding.UTF8,
                "application/json");
            return await Task.FromResult(resp);
        });

        var http = new HttpClient(handler);
        var client = new OpenAiCompatiblePlanningModelClient("http://example", "m", "k", http);

        var result = await client.CompleteJsonAsync("sys", "user", CancellationToken.None);
        JsonHelpers.TryGetJsonObject(result, out _).Should().BeTrue();
        result.Should().Contain("\"TaskType\":\"core\"");
    }

    [Fact]
    public async Task fails_cleanly_when_model_output_is_not_json()
    {
        var handler = new FakeHttpMessageHandler(async (req, ct) =>
        {
            var resp = new HttpResponseMessage(HttpStatusCode.OK);
            resp.Content = new StringContent(
                """
                { "choices": [ { "message": { "content": "not-json" } } ] }
                """,
                Encoding.UTF8,
                "application/json");
            return await Task.FromResult(resp);
        });

        var http = new HttpClient(handler);
        var client = new OpenAiCompatiblePlanningModelClient("http://example", "m", "k", http);

        var act = async () => await client.CompleteJsonAsync("sys", "user", CancellationToken.None);
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    private sealed class FakeHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _responder;

        public FakeHttpMessageHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> responder)
        {
            _responder = responder;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => _responder(request, cancellationToken);
    }
}

