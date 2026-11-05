using IniaAi.Infrastructure.Services;
using Microsoft.Extensions.Logging.Abstractions;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;

namespace IniaAi.Infrastructure.Tests.Services;

public class AgentRuntimeResilienceHandlerTests
{
    [Fact]
    public async Task ServerError_IsRetriedUntilItSucceeds()
    {
        var inner = new SequencedHandler(
            () => new HttpResponseMessage(HttpStatusCode.BadGateway),
            () => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("ok") });
        using HttpClient client = BuildClient(inner);

        using HttpResponseMessage response = await client.GetAsync(
            new Uri("http://agent-runtime.test:8130/runs/exec-1"), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, inner.Attempts);
    }

    [Fact]
    public async Task UploadBudgetShed503_ClearsOnTheSingleAdmissionRetry()
    {
        var inner = new SequencedHandler(
            () => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable),
            () => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("ok") });
        using HttpClient client = BuildClient(inner);

        using HttpResponseMessage response = await client.GetAsync(
            new Uri("http://agent-runtime.test:8130/runs/exec-1"), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, inner.Attempts);
    }

    [Fact]
    public async Task CapacityShed503_SurfacesAfterOneRetry_NotTheTransientBudget()
    {
        var inner = new SequencedHandler(
            () => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable),
            () => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable),
            () => new HttpResponseMessage(HttpStatusCode.OK));
        using HttpClient client = BuildClient(inner);

        using HttpResponseMessage response = await client.GetAsync(
            new Uri("http://agent-runtime.test:8130/runs/exec-1"), TestContext.Current.CancellationToken);

        // A pool full of minutes-long runs cannot clear inside any transport budget, so the
        // second 503 must reach the client to be typed rather than burn three more attempts.
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal(2, inner.Attempts);
    }

    [Fact]
    public async Task ConnectFailure_IsRetriedUntilItSucceeds()
    {
        var inner = new SequencedHandler(
            () => throw new HttpRequestException("connection refused"),
            () => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("ok") });
        using HttpClient client = BuildClient(inner);

        using HttpResponseMessage response = await client.GetAsync(
            new Uri("http://agent-runtime.test:8130/runs/exec-1"), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, inner.Attempts);
    }

    [Fact]
    public async Task ExhaustedRetries_SurfaceTheLastServerError()
    {
        var inner = new SequencedHandler(
            () => new HttpResponseMessage(HttpStatusCode.InternalServerError),
            () => new HttpResponseMessage(HttpStatusCode.InternalServerError),
            () => new HttpResponseMessage(HttpStatusCode.InternalServerError));
        using HttpClient client = BuildClient(inner, maxRetryAttempts: 2);

        using HttpResponseMessage response = await client.GetAsync(
            new Uri("http://agent-runtime.test:8130/runs/exec-1"), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal(3, inner.Attempts);
    }

    [Theory]
    [InlineData(HttpStatusCode.Conflict)]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.NotFound)]
    public async Task ContractAnswers_AreNeverRetried(HttpStatusCode statusCode)
    {
        var inner = new SequencedHandler(
            () => new HttpResponseMessage(statusCode),
            () => new HttpResponseMessage(HttpStatusCode.OK));
        using HttpClient client = BuildClient(inner);

        using HttpResponseMessage response = await client.GetAsync(
            new Uri("http://agent-runtime.test:8130/runs/exec-1"), TestContext.Current.CancellationToken);

        Assert.Equal(statusCode, response.StatusCode);
        Assert.Equal(1, inner.Attempts);
    }

    [Fact]
    public async Task PostBodies_SurviveARetry()
    {
        var inner = new SequencedHandler(
            () => new HttpResponseMessage(HttpStatusCode.BadGateway),
            () => new HttpResponseMessage(HttpStatusCode.OK));
        using HttpClient client = BuildClient(inner);

        using var content = new StringContent("""{"resumeToken":"abc"}""");
        using HttpResponseMessage response = await client.PostAsync(
            new Uri("http://agent-runtime.test:8130/runs/exec-1/input"),
            content,
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, inner.Attempts);
        Assert.All(inner.ObservedBodies, body => Assert.Equal("""{"resumeToken":"abc"}""", body));
    }

    [Fact]
    public async Task AdmissionRetry_WaitsAtLeastTheAdvisedRetryAfter()
    {
        TimeSpan advised = TimeSpan.FromMilliseconds(50);
        var inner = new SequencedHandler(
            () => ShedWith(advised),
            () => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("ok") });
        using HttpClient client = BuildClient(inner);

        long start = Stopwatch.GetTimestamp();
        using HttpResponseMessage response = await client.GetAsync(
            new Uri("http://agent-runtime.test:8130/runs/exec-1"), TestContext.Current.CancellationToken);
        TimeSpan elapsed = Stopwatch.GetElapsedTime(start);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, inner.Attempts);
        Assert.True(
            elapsed >= advised,
            $"Retry-After is a floor, not a suggestion; retried after {elapsed}");
    }

    [Fact]
    public async Task AdmissionRetry_WithAWaitLongerThanTheRequestCanHold_SurfacesTheRefusalImmediately()
    {
        var inner = new SequencedHandler(
            () => ShedWith(TimeSpan.FromSeconds(120)),
            () => new HttpResponseMessage(HttpStatusCode.OK));
        using HttpClient client = BuildClient(inner);

        long start = Stopwatch.GetTimestamp();
        using HttpResponseMessage response = await client.GetAsync(
            new Uri("http://agent-runtime.test:8130/runs/exec-1"), TestContext.Current.CancellationToken);
        TimeSpan elapsed = Stopwatch.GetElapsedTime(start);

        // Holding the request (and its whole body) for two minutes would blow the client
        // timeout and turn a typed capacity refusal into an untyped one. Waiting that long
        // belongs to the executor's admission patience, which holds no body.
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal(1, inner.Attempts);
        Assert.True(elapsed < TimeSpan.FromSeconds(5), $"surfaced after {elapsed}");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void AdmissionRetryDelay_IsTheGreaterOfAdvisedAndBase_PlusUpwardJitter(double jitterFraction)
    {
        TimeSpan advised = TimeSpan.FromSeconds(4);
        TimeSpan baseDelay = TimeSpan.FromSeconds(1);

        TimeSpan delay = AgentRuntimeResilienceHandler.AdmissionRetryDelay(advised, baseDelay, jitterFraction);

        Assert.Equal(advised * (1 + jitterFraction), delay);
    }

    [Theory]
    [InlineData(0.25)]
    [InlineData(1)]
    public void InlineAdmissionRetryDelay_NeverOutlastsTheWaitTheGateAdmitted(double jitterFraction)
    {
        TimeSpan delay = AgentRuntimeResilienceHandler.InlineAdmissionRetryDelay(
            TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(1), jitterFraction);

        Assert.Equal(TimeSpan.FromSeconds(5), delay);
    }

    [Fact]
    public void InlineAdmissionRetryDelay_BelowTheCap_KeepsItsJitter()
    {
        TimeSpan delay = AgentRuntimeResilienceHandler.InlineAdmissionRetryDelay(
            TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(1), jitterFraction: 0.25);

        Assert.Equal(TimeSpan.FromSeconds(2.5), delay);
    }

    [Fact]
    public void AdmissionRetryDelay_WithNoAdvice_FallsBackToTheBaseDelay()
    {
        TimeSpan delay = AgentRuntimeResilienceHandler.AdmissionRetryDelay(
            advised: null, TimeSpan.FromSeconds(1), jitterFraction: 0);

        Assert.Equal(TimeSpan.FromSeconds(1), delay);
    }

    private static HttpResponseMessage ShedWith(TimeSpan retryAfter)
    {
        var response = new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
        {
            Content = new StringContent("""{"error":"capacity"}"""),
        };
        response.Headers.RetryAfter = new RetryConditionHeaderValue(retryAfter);

        return response;
    }

    private static HttpClient BuildClient(SequencedHandler inner, int maxRetryAttempts = 3)
    {
        var handler = new AgentRuntimeResilienceHandler(
            NullLogger<AgentRuntimeResilienceHandler>.Instance,
            maxRetryAttempts,
            baseDelay: TimeSpan.Zero,
            admissionRetryDelay: TimeSpan.Zero)
        {
            InnerHandler = inner,
        };

        return new HttpClient(handler);
    }

    internal sealed class SequencedHandler : HttpMessageHandler
    {
        private readonly Queue<Func<HttpResponseMessage>> _responses;

        public SequencedHandler(params Func<HttpResponseMessage>[] responses)
        {
            _responses = new Queue<Func<HttpResponseMessage>>(responses);
        }

        public int Attempts { get; private set; }

        public List<string> ObservedBodies { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Attempts++;
            if (request.Content is not null)
            {
                ObservedBodies.Add(await request.Content.ReadAsStringAsync(cancellationToken));
            }

            Assert.NotEmpty(_responses);
            return _responses.Dequeue()();
        }
    }
}
