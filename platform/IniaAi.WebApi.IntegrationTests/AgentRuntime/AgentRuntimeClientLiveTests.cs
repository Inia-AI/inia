using ErrorOr;
using IniaAi.Common.Interfaces.Services.AgentRuntime;
using IniaAi.Infrastructure.Services;
using Microsoft.Extensions.Logging.Abstractions;
using System.Diagnostics;
using System.Net.Http.Headers;

namespace IniaAi.WebApi.IntegrationTests.AgentRuntime;

// Drives the real AgentRuntimeClient against a live agent-runtime sidecar, so the wire contract
// is verified against the runtime itself rather than against a stub that encodes our assumptions.
// Start the sidecar from the agent-runtime repo (docker compose up) and point
// INIA_TEST_AGENT_RUNTIME at it; the class skips when the variable is unset.
//
// These cases deliberately never wait for a terminal status: reaching one needs model credentials
// in the sidecar, while every mapping under test — routing, monitor parsing, Retry-After, and the
// per-code conflicts — is observable from a run that is merely registered.
public class AgentRuntimeClientLiveTests
{
    private const string BASE_URL_ENV = "INIA_TEST_AGENT_RUNTIME";
    private const string AUTH_TOKEN_ENV = "INIA_TEST_AGENT_RUNTIME_TOKEN";
    private const string MODEL_CREDENTIALS_ENV = "INIA_TEST_AGENT_RUNTIME_MODEL_CREDS";
    private const string PROMPT = "Reply with the single word OK.";

    [Fact]
    public async Task StartRun_RegistersAndPollsWithAPollInterval()
    {
        AgentRuntimeClient client = CreateClientOrSkip();
        string executionId = NewExecutionId();

        ErrorOr<AgentRunMonitor> started = await StartAsync(client, executionId);

        Assert.False(started.IsError, ErrorText(started));
        Assert.Equal(executionId, started.Value.ExecutionId);
        Assert.False(started.Value.AlreadyRegistered);
        Assert.False(started.Value.Status.IsTerminal());

        ErrorOr<AgentRunMonitor> polled = await client.GetRunAsync(
            executionId, TestContext.Current.CancellationToken);

        Assert.False(polled.IsError, ErrorText(polled));
        Assert.Equal(executionId, polled.Value.ExecutionId);
        Assert.NotNull(polled.Value.PollInterval);
        Assert.True(polled.Value.PollInterval > TimeSpan.Zero);

        await CancelQuietlyAsync(client, executionId);
    }

    [Fact]
    public async Task StartRun_SameExecutionIdAndPrompt_IsReportedAsAlreadyRegistered()
    {
        AgentRuntimeClient client = CreateClientOrSkip();
        string executionId = NewExecutionId();

        _ = await StartAsync(client, executionId);
        ErrorOr<AgentRunMonitor> retried = await StartAsync(client, executionId);

        Assert.False(retried.IsError, ErrorText(retried));
        Assert.True(retried.Value.AlreadyRegistered);

        await CancelQuietlyAsync(client, executionId);
    }

    [Fact]
    public async Task StartRun_SameExecutionIdWithADifferentPrompt_IsAPayloadConflict()
    {
        AgentRuntimeClient client = CreateClientOrSkip();
        string executionId = NewExecutionId();

        _ = await StartAsync(client, executionId);
        ErrorOr<AgentRunMonitor> conflicting = await client.StartRunAsync(
            executionId, "A different prompt entirely.", NewRunToken(), TestContext.Current.CancellationToken);

        Assert.True(conflicting.IsError);
        Assert.Equal(ErrorType.Conflict, conflicting.FirstError.Type);
        Assert.Contains("PayloadConflict", conflicting.FirstError.Code, StringComparison.Ordinal);

        await CancelQuietlyAsync(client, executionId);
    }

    [Fact]
    public async Task GetRun_UnknownExecutionId_IsNotFound()
    {
        AgentRuntimeClient client = CreateClientOrSkip();

        ErrorOr<AgentRunMonitor> polled = await client.GetRunAsync(
            NewExecutionId(), TestContext.Current.CancellationToken);

        Assert.True(polled.IsError);
        Assert.Equal(ErrorType.NotFound, polled.FirstError.Type);
    }

    [Fact]
    public async Task CancelRun_MovesTheRunOutOfRunning()
    {
        AgentRuntimeClient client = CreateClientOrSkip();
        string executionId = NewExecutionId();
        _ = await StartAsync(client, executionId);

        ErrorOr<AgentRunMonitor> cancelled = await client.CancelRunAsync(
            executionId, TestContext.Current.CancellationToken);

        Assert.False(cancelled.IsError, ErrorText(cancelled));
        Assert.Equal(executionId, cancelled.Value.ExecutionId);
        Assert.True(
            cancelled.Value.Status is AgentRunStatus.Cancelling or AgentRunStatus.Cancelled,
            $"Expected a cancelling or cancelled monitor, got {cancelled.Value.Status}");
    }

    [Fact]
    public async Task SubmitInput_ToARunThatIsNotAwaitingInput_IsRejectedWithThatCode()
    {
        AgentRuntimeClient client = CreateClientOrSkip();
        string executionId = NewExecutionId();
        _ = await StartAsync(client, executionId);

        ErrorOr<AgentRunMonitor> submitted = await client.SubmitInputAsync(
            executionId,
            "not-a-real-resume-token",
            "question-1",
            "an answer",
            allowFurtherPause: true,
            TestContext.Current.CancellationToken);

        Assert.True(submitted.IsError);
        Assert.Equal(ErrorType.Conflict, submitted.FirstError.Type);

        await CancelQuietlyAsync(client, executionId);
    }

    // Billed and slow, so it is gated separately: the sidecar must additionally hold model
    // credentials. This is the only case that proves the whole loop — the poll cadence the
    // runtime dictates, a terminal monitor, and a result payload the block can actually consume.
    [Fact]
    public async Task StartRun_WithModelCredentials_PollsToATerminalResult()
    {
        AgentRuntimeClient client = CreateClientOrSkip();
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(MODEL_CREDENTIALS_ENV)))
        {
            Assert.Skip($"Set {MODEL_CREDENTIALS_ENV}=1 when the sidecar holds model credentials to run a billed end-to-end run.");
        }

        string executionId = NewExecutionId();
        ErrorOr<AgentRunMonitor> started = await StartAsync(client, executionId);
        Assert.False(started.IsError, ErrorText(started));

        ErrorOr<AgentRunMonitor> terminal = await PollToTerminalAsync(
            client, executionId, TimeSpan.FromMinutes(5), TestContext.Current.CancellationToken);

        Assert.False(terminal.IsError, ErrorText(terminal));
        Assert.Equal(AgentRunStatus.Succeeded, terminal.Value.Status);
        Assert.Null(terminal.Value.PollInterval);
        Assert.True(terminal.Value.TryGetResult(out AgentRunResult? result));
        Assert.False(string.IsNullOrWhiteSpace(result.Output));
        Assert.Contains("OK", result.Output, StringComparison.OrdinalIgnoreCase);
    }

    private static Task<ErrorOr<AgentRunMonitor>> StartAsync(AgentRuntimeClient client, string executionId)
    {
        return client.StartRunAsync(executionId, PROMPT, NewRunToken(), TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// Test-local wait: the client deliberately has no polling loop (the production loop is
    /// <c>AgentRunExecutor</c>'s), so this live gate drives GetRunAsync itself, honouring each
    /// monitor's Retry-After.
    /// </summary>
    private static async Task<ErrorOr<AgentRunMonitor>> PollToTerminalAsync(
        AgentRuntimeClient client,
        string executionId,
        TimeSpan patience,
        CancellationToken cancellationToken)
    {
        long startTimestamp = Stopwatch.GetTimestamp();

        while (true)
        {
            ErrorOr<AgentRunMonitor> polled = await client.GetRunAsync(executionId, cancellationToken);
            if (polled.IsError || polled.Value.Status.IsTerminal())
            {
                return polled;
            }

            TimeSpan interval = polled.Value.PollInterval ?? TimeSpan.FromSeconds(1);
            if (Stopwatch.GetElapsedTime(startTimestamp) + interval > patience)
            {
                return Error.Failure(
                    "AgentRun.PatienceExhausted",
                    $"Run '{executionId}' did not finish within the test's patience of {patience}");
            }

            await Task.Delay(interval, cancellationToken);
        }
    }

    private static async Task CancelQuietlyAsync(AgentRuntimeClient client, string executionId)
    {
        _ = await client.CancelRunAsync(executionId, TestContext.Current.CancellationToken);
    }

    private static string NewExecutionId()
    {
        return AgentRunIds.For(Guid.NewGuid().ToString("N"), "liveblock", "worker", 1, 1);
    }

    private static string NewRunToken()
    {
        return $"live-test-token-{Guid.NewGuid():N}";
    }

    private static string ErrorText(ErrorOr<AgentRunMonitor> result)
    {
        return result.IsError ? $"{result.FirstError.Code}: {result.FirstError.Description}" : string.Empty;
    }

    private static AgentRuntimeClient CreateClientOrSkip()
    {
        string? baseUrl = Environment.GetEnvironmentVariable(BASE_URL_ENV);
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            Assert.Skip($"Set {BASE_URL_ENV} to a running agent-runtime sidecar base URL to run this contract guard.");
        }

        var httpClient = new HttpClient { BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/") };

        string? authToken = Environment.GetEnvironmentVariable(AUTH_TOKEN_ENV);
        if (!string.IsNullOrWhiteSpace(authToken))
        {
            httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", authToken);
        }

        return new AgentRuntimeClient(httpClient, NullLogger<AgentRuntimeClient>.Instance);
    }
}
