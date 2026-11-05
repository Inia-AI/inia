using ErrorOr;
using IniaAi.Common.Interfaces.Services.AgentRuntime;
using IniaAi.Infrastructure.Services;
using Microsoft.Extensions.Logging.Abstractions;
using System.Net;
using System.Text.Json;
using RecordedRequest = IniaAi.Infrastructure.Tests.Services.AgentRuntimeClientTests.RecordedRequest;
using SequencedHandler = IniaAi.Infrastructure.Tests.Services.AgentRuntimeResilienceHandlerTests.SequencedHandler;
using StubHttpHandler = IniaAi.Infrastructure.Tests.Services.AgentRuntimeClientTests.StubHttpHandler;

namespace IniaAi.Infrastructure.Tests.Services;

/// <summary>
/// How a request is framed, not what it says. The runtime charges its in-flight upload budget
/// from the declared Content-Length before it reads a byte, so a chunked request — which
/// declares nothing — is charged the whole budget and admits exactly one start at a time
/// service-wide.
/// </summary>
public class AgentRuntimeClientFramingTests : IDisposable
{
    private const string BASE_URL = "http://agent-runtime.test:8130";
    private const string EXECUTION_ID = "exec-1";
    private const string PROMPT = "Generate the report";
    private const string RUN_TOKEN = "per-run-jwt";

    private readonly StubHttpHandler _handler = new();

    public void Dispose()
    {
        _handler.Dispose();
        GC.SuppressFinalize(this);
    }

    private AgentRuntimeClient CreateClient()
    {
        var httpClient = new HttpClient(_handler) { BaseAddress = new Uri($"{BASE_URL}/") };

        return new AgentRuntimeClient(httpClient, NullLogger<AgentRuntimeClient>.Instance);
    }

    private static string MonitorJson()
    {
        return AgentRuntimeClientTests.MonitorJson(status: "running", EXECUTION_ID);
    }

    [Fact]
    public async Task StartRunAsync_DeclaresItsBodyLengthInsteadOfStreamingChunked()
    {
        _handler.StatusCode = HttpStatusCode.Accepted;
        _handler.ResponseBody = MonitorJson();

        var request = new AgentRunStartRequest(EXECUTION_ID, PROMPT, RUN_TOKEN)
        {
            Documents = [new AgentRunDocument("input.txt", new byte[] { 1, 2, 3 })],
        };

        ErrorOr<AgentRunMonitor> result = await CreateClient().StartRunAsync(
            request, TestContext.Current.CancellationToken);

        Assert.False(result.IsError, result.IsError ? result.FirstError.Description : "");
        RecordedRequest recorded = Assert.Single(_handler.Requests);
        Assert.False(recorded.TransferEncodingChunked);
        Assert.NotNull(recorded.ContentLength);
        Assert.Equal(
            System.Text.Encoding.UTF8.GetByteCount(recorded.Body!),
            recorded.ContentLength);
    }

    [Fact]
    public async Task StartRunAsync_KeepsTheContentChannelBodyByteIdentical()
    {
        _handler.StatusCode = HttpStatusCode.Accepted;
        _handler.ResponseBody = MonitorJson();

        var request = new AgentRunStartRequest(EXECUTION_ID, PROMPT, RUN_TOKEN)
        {
            Documents = [new AgentRunDocument("input.txt", new byte[] { 1, 2, 3 }, MediaType: "text/plain")],
            SkillFiles = [new AgentRunSkillFile("skill", "Do the work.", "Does the work.")],
            Model = "claude-sonnet-4-6",
            MaxTurns = 7,
        };

        ErrorOr<AgentRunMonitor> result = await CreateClient().StartRunAsync(
            request, TestContext.Current.CancellationToken);

        Assert.False(result.IsError, result.IsError ? result.FirstError.Description : "");
        RecordedRequest recorded = Assert.Single(_handler.Requests);
        Assert.Equal("application/json", recorded.ContentType);

        using JsonDocument body = JsonDocument.Parse(recorded.Body!);
        JsonElement root = body.RootElement;
        Assert.Equal(EXECUTION_ID, root.GetProperty("executionId").GetString());
        Assert.Equal("claude-sonnet-4-6", root.GetProperty("model").GetString());
        Assert.Equal(7, root.GetProperty("maxTurns").GetInt32());
        Assert.Equal(
            "text/plain",
            Assert.Single(root.GetProperty("documents").EnumerateArray()).GetProperty("mediaType").GetString());
        Assert.False(root.TryGetProperty("effort", out _));
    }

    [Fact]
    public async Task StartRunAsync_RetriedThroughTheResilienceHandler_ReSendsTheWholeBody()
    {
        var inner = new SequencedHandler(
            static () => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable),
            () => new HttpResponseMessage(HttpStatusCode.Accepted)
            {
                Content = new StringContent(MonitorJson()),
            });
        var resilience = new AgentRuntimeResilienceHandler(
            NullLogger<AgentRuntimeResilienceHandler>.Instance,
            maxRetryAttempts: 1,
            baseDelay: TimeSpan.Zero,
            admissionRetryDelay: TimeSpan.Zero)
        {
            InnerHandler = inner,
        };
        var httpClient = new HttpClient(resilience) { BaseAddress = new Uri($"{BASE_URL}/") };
        var client = new AgentRuntimeClient(httpClient, NullLogger<AgentRuntimeClient>.Instance);

        ErrorOr<AgentRunMonitor> result = await client.StartRunAsync(
            new AgentRunStartRequest(EXECUTION_ID, PROMPT, RUN_TOKEN),
            TestContext.Current.CancellationToken);

        Assert.False(result.IsError, result.IsError ? result.FirstError.Description : "");
        Assert.Equal(2, inner.ObservedBodies.Count);
        Assert.Equal(inner.ObservedBodies[0], inner.ObservedBodies[1]);
        Assert.Contains(PROMPT, inner.ObservedBodies[1], StringComparison.Ordinal);
    }

    /// <summary>
    /// The caller's admission patience re-posts the same instance until a slot frees, and every
    /// attempt must put the same bytes on the wire — a mutation between attempts must not
    /// produce a torn body.
    /// </summary>
    [Fact]
    public async Task StartRunAsync_ReSendingAcrossAnAdmissionRefusal_ReusesTheFirstSerialization()
    {
        _handler.StatusCode = HttpStatusCode.ServiceUnavailable;
        _handler.ResponseBody = "";

        var documents = new List<AgentRunDocument> { new("input.txt", new byte[] { 1, 2, 3 }) };
        var request = new AgentRunStartRequest(EXECUTION_ID, PROMPT, RUN_TOKEN) { Documents = documents };
        AgentRuntimeClient client = CreateClient();

        _ = await client.StartRunAsync(request, TestContext.Current.CancellationToken);
        documents.Add(new AgentRunDocument("added-after-the-first-send.txt", new byte[] { 4, 5, 6 }));
        _handler.StatusCode = HttpStatusCode.Accepted;
        _handler.ResponseBody = MonitorJson();
        _ = await client.StartRunAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(2, _handler.Requests.Count);
        Assert.Equal(_handler.Requests[0].ContentLength, _handler.Requests[1].ContentLength);
        Assert.Equal(_handler.Requests[0].Body, _handler.Requests[1].Body);
        Assert.DoesNotContain(
            "added-after-the-first-send.txt", _handler.Requests[1].Body!, StringComparison.Ordinal);
    }

    /// <summary>
    /// Only an admission refusal re-posts an instance, so a settled start's cached body is dead
    /// weight — for a continuation start it can pin ~85 MiB of base64 for as long as the caller
    /// polls the run. Re-sending a settled instance is out of contract; the fresh serialization
    /// asserted here is just the observable of the release.
    /// </summary>
    [Fact]
    public async Task StartRunAsync_ASettledStart_ReleasesItsCachedBody()
    {
        _handler.StatusCode = HttpStatusCode.Accepted;
        _handler.ResponseBody = MonitorJson();

        var documents = new List<AgentRunDocument> { new("input.txt", new byte[] { 1, 2, 3 }) };
        var request = new AgentRunStartRequest(EXECUTION_ID, PROMPT, RUN_TOKEN) { Documents = documents };
        AgentRuntimeClient client = CreateClient();

        _ = await client.StartRunAsync(request, TestContext.Current.CancellationToken);
        documents.Add(new AgentRunDocument("added-after-the-first-send.txt", new byte[] { 4, 5, 6 }));
        _ = await client.StartRunAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(2, _handler.Requests.Count);
        Assert.Contains(
            "added-after-the-first-send.txt", _handler.Requests[1].Body!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task StartRunAsync_ANewRequestInstance_SerializesItsOwnBytes()
    {
        _handler.StatusCode = HttpStatusCode.Accepted;
        _handler.ResponseBody = MonitorJson();

        AgentRuntimeClient client = CreateClient();

        _ = await client.StartRunAsync(
            new AgentRunStartRequest(EXECUTION_ID, PROMPT, RUN_TOKEN) { MaxTurns = 7 },
            TestContext.Current.CancellationToken);
        _ = await client.StartRunAsync(
            new AgentRunStartRequest(EXECUTION_ID, PROMPT, RUN_TOKEN) { MaxTurns = 9 },
            TestContext.Current.CancellationToken);

        Assert.Equal(2, _handler.Requests.Count);
        Assert.Equal(7, TurnsIn(_handler.Requests[0]));
        Assert.Equal(9, TurnsIn(_handler.Requests[1]));
    }

    private static int TurnsIn(RecordedRequest recorded)
    {
        using JsonDocument body = JsonDocument.Parse(recorded.Body!);

        return body.RootElement.GetProperty("maxTurns").GetInt32();
    }

    [Fact]
    public async Task SubmitInputAsync_DeclaresItsBodyLengthToo()
    {
        _handler.StatusCode = HttpStatusCode.OK;
        _handler.ResponseBody = MonitorJson();

        ErrorOr<AgentRunMonitor> result = await CreateClient().SubmitInputAsync(
            EXECUTION_ID, "resume-token", "question-1", "the answer",
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.False(result.IsError, result.IsError ? result.FirstError.Description : "");
        RecordedRequest recorded = Assert.Single(_handler.Requests);
        Assert.False(recorded.TransferEncodingChunked);
        Assert.NotNull(recorded.ContentLength);
    }
}
