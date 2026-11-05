using System.Text.Json;

namespace IniaAi.Core.Tests.Workflows.Scheduling.Helpers;

/// <summary>
/// Records what actually crossed the wire: every request body by URL, and every JSON
/// response body, so a live gate asserts the continuation start and the reported session ids
/// off the transport instead of trusting any in-process value.
/// </summary>
public sealed class WireRecorder : DelegatingHandler
{
    private readonly Lock _lock = new();
    private readonly List<(string Url, string Body)> _requests = [];
    private readonly List<(string Url, string Body)> _responses = [];

    public WireRecorder()
        : base(new HttpClientHandler())
    {
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        string url = request.RequestUri?.ToString() ?? string.Empty;
        if (request.Content is not null)
        {
            string body = await request.Content.ReadAsStringAsync(cancellationToken);
            lock (_lock)
            {
                _requests.Add((url, body));
            }
        }

        HttpResponseMessage response = await base.SendAsync(request, cancellationToken);

        if (response.Content.Headers.ContentType?.MediaType == "application/json")
        {
            await response.Content.LoadIntoBufferAsync(cancellationToken);
            string body = await response.Content.ReadAsStringAsync(cancellationToken);
            lock (_lock)
            {
                _responses.Add((url, body));
            }
        }

        return response;
    }

    public JsonDocument StartBodyOf(string executionId)
    {
        lock (_lock)
        {
            (string Url, string Body) start = _requests.Single(entry =>
                entry.Url.EndsWith("/v1/runs", StringComparison.Ordinal)
                && entry.Body.Contains($"\"{executionId}\"", StringComparison.Ordinal));

            return JsonDocument.Parse(start.Body);
        }
    }

    /// <summary>The session id the runtime reported on the run's settled result.</summary>
    public string ReportedSessionIdOf(string executionId)
    {
        lock (_lock)
        {
            foreach ((string url, string body) in Enumerable.Reverse(_responses))
            {
                if (!url.Contains($"/v1/runs/{executionId}", StringComparison.Ordinal))
                {
                    continue;
                }

                using JsonDocument monitor = JsonDocument.Parse(body);
                if (monitor.RootElement.TryGetProperty("result", out JsonElement result)
                    && result.ValueKind == JsonValueKind.Object
                    && result.TryGetProperty("sessionId", out JsonElement sessionId)
                    && sessionId.ValueKind == JsonValueKind.String)
                {
                    return sessionId.GetString()!;
                }
            }
        }

        Assert.Fail($"no settled monitor of run '{executionId}' reported a session id");
        return string.Empty;
    }
}
