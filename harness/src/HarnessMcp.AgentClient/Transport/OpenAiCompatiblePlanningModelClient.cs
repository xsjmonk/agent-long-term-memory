using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace HarnessMcp.AgentClient.Transport;

public sealed class OpenAiCompatiblePlanningModelClient : IPlanningModelClient
{
    private readonly HttpClient _http;
    private readonly string _modelName;
    private readonly string _apiKey;
    private readonly string _endpointBaseUrl;

    public OpenAiCompatiblePlanningModelClient(
        string endpointBaseUrl,
        string modelName,
        string apiKey,
        HttpClient? httpClient = null)
    {
        _endpointBaseUrl = endpointBaseUrl.TrimEnd('/');
        _modelName = modelName;
        _apiKey = apiKey;
        _http = httpClient ?? new HttpClient();
    }

    public async Task<string> CompleteJsonAsync(
        string systemPrompt,
        string userPrompt,
        CancellationToken cancellationToken)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, $"{_endpointBaseUrl}/v1/chat/completions");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        var payload = new
        {
            model = _modelName,
            temperature = 0,
            stream = false,
            messages = new object[]
            {
                new { role = "system", content = systemPrompt },
                new { role = "user", content = userPrompt }
            }
        };

        req.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

        using var resp = await _http.SendAsync(req, cancellationToken).ConfigureAwait(false);
        var body = await resp.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (!resp.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"Model request failed: {(int)resp.StatusCode} {resp.ReasonPhrase}. Body: {body}");
        }

        using var doc = JsonDocument.Parse(body);
        // Expected: choices[0].message.content contains a JSON object string.
        if (!doc.RootElement.TryGetProperty("choices", out var choices) || choices.ValueKind != JsonValueKind.Array || choices.GetArrayLength() == 0)
            throw new InvalidOperationException("Model response missing choices[0].message.content.");

        var choice0 = choices[0];
        var message = choice0.GetProperty("message");
        var content = message.GetProperty("content").GetString();
        if (string.IsNullOrWhiteSpace(content))
            throw new InvalidOperationException("Model response content is empty.");

        // Fail cleanly if model output is not JSON object string.
        try
        {
            using var contentDoc = JsonDocument.Parse(content);
            if (contentDoc.RootElement.ValueKind != JsonValueKind.Object)
                throw new InvalidOperationException("Model output must be a JSON object.");
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException("Model output must be a JSON object.", ex);
        }

        // Return the raw JSON string (object).
        return content;
    }
}

