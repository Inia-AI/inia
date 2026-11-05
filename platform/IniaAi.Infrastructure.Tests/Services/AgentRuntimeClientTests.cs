using ErrorOr;
using IniaAi.Common.Interfaces.Services.AgentRuntime;
using IniaAi.Infrastructure.Services;
using Microsoft.Extensions.Logging.Abstractions;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

namespace IniaAi.Infrastructure.Tests.Services;

public class AgentRuntimeClientTests : IDisposable
{
    private const string BASE_URL = "http://agent-runtime.test:8130";
    private const string RUNS_URL = $"{BASE_URL}/v1/runs";
    private const string AUTH_TOKEN = "static-service-token";
    private const string EXECUTION_ID = "exec-1";
    private const string PROMPT = "Generate the report";
    private const string RUN_TOKEN = "per-run-jwt";

    private readonly StubHttpHandler _handler = new();

    public void Dispose()
    {
        _handler.Dispose();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task StartRunAsync_PostsContractBodyWithBearerAuth()
    {
        _handler.StatusCode = HttpStatusCode.Accepted;
        _handler.ResponseBody = MonitorJson(status: "running");

        AgentRuntimeClient client = CreateClient();

        ErrorOr<AgentRunMonitor> result = await client.StartRunAsync(
            EXECUTION_ID, PROMPT, RUN_TOKEN, TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        RecordedRequest request = Assert.Single(_handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal(RUNS_URL, request.Uri);
        Assert.Equal("Bearer", request.AuthScheme);
        Assert.Equal(AUTH_TOKEN, request.AuthParameter);

        using JsonDocument body = JsonDocument.Parse(request.Body!);
        Assert.Equal(EXECUTION_ID, body.RootElement.GetProperty("executionId").GetString());
        Assert.Equal(PROMPT, body.RootElement.GetProperty("prompt").GetString());
        Assert.Equal(RUN_TOKEN, body.RootElement.GetProperty("runToken").GetString());
        Assert.False(body.RootElement.TryGetProperty("effort", out _));
        Assert.False(body.RootElement.TryGetProperty("admissionClass", out _));
    }

    [Theory]
    [InlineData(AgentRunAdmissionClass.Chat, "chat")]
    [InlineData(AgentRunAdmissionClass.Workflow, "workflow")]
    public async Task StartRunAsync_AdmissionClass_RidesTheWireInTheRuntimesVocabulary(
        AgentRunAdmissionClass admissionClass,
        string wireValue)
    {
        _handler.StatusCode = HttpStatusCode.Accepted;
        _handler.ResponseBody = MonitorJson(status: "running");

        AgentRuntimeClient client = CreateClient();

        ErrorOr<AgentRunMonitor> result = await client.StartRunAsync(
            new AgentRunStartRequest(EXECUTION_ID, PROMPT, RUN_TOKEN) { AdmissionClass = admissionClass },
            TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        RecordedRequest request = Assert.Single(_handler.Requests);
        using JsonDocument body = JsonDocument.Parse(request.Body!);
        Assert.Equal(wireValue, body.RootElement.GetProperty("admissionClass").GetString());
    }

    [Fact]
    public async Task StartRunAsync_Effort_RidesTheWireVerbatim()
    {
        _handler.StatusCode = HttpStatusCode.Accepted;
        _handler.ResponseBody = MonitorJson(status: "running");

        AgentRuntimeClient client = CreateClient();

        ErrorOr<AgentRunMonitor> result = await client.StartRunAsync(
            new AgentRunStartRequest(EXECUTION_ID, PROMPT, RUN_TOKEN) { Effort = "xhigh" },
            TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        RecordedRequest request = Assert.Single(_handler.Requests);
        using JsonDocument body = JsonDocument.Parse(request.Body!);
        Assert.Equal("xhigh", body.RootElement.GetProperty("effort").GetString());
    }

    [Fact]
    public async Task StartRunAsync_SkillDescription_RidesTheWire()
    {
        _handler.StatusCode = HttpStatusCode.Accepted;
        _handler.ResponseBody = MonitorJson(status: "running");

        AgentRuntimeClient client = CreateClient();

        ErrorOr<AgentRunMonitor> result = await client.StartRunAsync(
            new AgentRunStartRequest(EXECUTION_ID, PROMPT, RUN_TOKEN)
            {
                SkillFiles = [new AgentRunSkillFile("income-statement", "Do it.", "Standardizes a balance sheet.")],
            },
            TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        RecordedRequest request = Assert.Single(_handler.Requests);
        using JsonDocument body = JsonDocument.Parse(request.Body!);
        JsonElement skill = body.RootElement.GetProperty("skillFiles")[0];
        Assert.Equal("income-statement", skill.GetProperty("name").GetString());
        Assert.Equal("Standardizes a balance sheet.", skill.GetProperty("description").GetString());
    }

    [Fact]
    public async Task StartRunAsync_SkillAssets_RideTheWireBase64()
    {
        _handler.StatusCode = HttpStatusCode.Accepted;
        _handler.ResponseBody = MonitorJson(status: "running");

        AgentRuntimeClient client = CreateClient();

        ErrorOr<AgentRunMonitor> result = await client.StartRunAsync(
            new AgentRunStartRequest(EXECUTION_ID, PROMPT, RUN_TOKEN)
            {
                SkillFiles =
                [
                    new AgentRunSkillFile("verify-bd-balance-sheet", "Run the staged script.", "Verifies the extraction.")
                    {
                        Assets = [new AgentRunSkillAsset("verify_bd_bs.py", "print(1)")],
                    },
                ],
            },
            TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        RecordedRequest request = Assert.Single(_handler.Requests);
        using JsonDocument body = JsonDocument.Parse(request.Body!);
        JsonElement asset = body.RootElement.GetProperty("skillFiles")[0].GetProperty("assets")[0];
        Assert.Equal("verify_bd_bs.py", asset.GetProperty("name").GetString());
        Assert.Equal(
            Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("print(1)")),
            asset.GetProperty("contentBase64").GetString());
        Assert.Equal(2, asset.EnumerateObject().Count());
    }

    [Fact]
    public async Task StartRunAsync_Continuation_CarriesBundleAndRecarriedSetWithDigests()
    {
        _handler.StatusCode = HttpStatusCode.Accepted;
        _handler.ResponseBody = MonitorJson(status: "running");

        byte[] bundleBytes = System.Text.Encoding.UTF8.GetBytes("platform-held session bundle");
        byte[] fileBytes = System.Text.Encoding.UTF8.GetBytes("print('check')");

        AgentRuntimeClient client = CreateClient();

        ErrorOr<AgentRunMonitor> result = await client.StartRunAsync(
            new AgentRunStartRequest(EXECUTION_ID, PROMPT, RUN_TOKEN)
            {
                Continuation = new AgentRunContinuation(
                    bundleBytes,
                    Sha256Of(bundleBytes),
                    [new AgentRunRecarriedFile("checks/model.py", fileBytes, Sha256Of(fileBytes))]),
            },
            TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        RecordedRequest request = Assert.Single(_handler.Requests);
        using JsonDocument body = JsonDocument.Parse(request.Body!);
        JsonElement continuation = body.RootElement.GetProperty("continuation");
        JsonElement sessionBundle = continuation.GetProperty("sessionBundle");
        Assert.Equal(Sha256Of(bundleBytes), sessionBundle.GetProperty("sha256").GetString());
        Assert.Equal(Convert.ToBase64String(bundleBytes), sessionBundle.GetProperty("bytesBase64").GetString());
        JsonElement outFile = Assert.Single(continuation.GetProperty("outFiles").EnumerateArray());
        Assert.Equal("checks/model.py", outFile.GetProperty("path").GetString());
        Assert.Equal(Convert.ToBase64String(fileBytes), outFile.GetProperty("contentBase64").GetString());
        Assert.Equal(Sha256Of(fileBytes), outFile.GetProperty("sha256").GetString());
    }

    [Fact]
    public async Task StartRunAsync_WithoutContinuation_OmitsTheContinuationKey()
    {
        _handler.StatusCode = HttpStatusCode.Accepted;
        _handler.ResponseBody = MonitorJson(status: "running");

        AgentRuntimeClient client = CreateClient();

        ErrorOr<AgentRunMonitor> result = await client.StartRunAsync(
            new AgentRunStartRequest(EXECUTION_ID, PROMPT, RUN_TOKEN),
            TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        RecordedRequest request = Assert.Single(_handler.Requests);
        using JsonDocument body = JsonDocument.Parse(request.Body!);
        Assert.False(body.RootElement.TryGetProperty("continuation", out _));
    }

    [Fact]
    public async Task StartRunAsync_RecarriedSetOverTheEntryCap_FailsBeforeAnyUpload()
    {
        AgentRuntimeClient client = CreateClient();

        List<AgentRunRecarriedFile> outFiles = [.. Enumerable
            .Range(0, AgentRunContinuationCapacity.MAX_RECARRIED_OUT_FILES + 1)
            .Select(static index => RecarriedFile($"file-{index}.txt"))];

        ErrorOr<AgentRunMonitor> result = await client.StartRunAsync(
            RequestWithContinuation(outFiles), TestContext.Current.CancellationToken);

        Assert.True(result.IsError);
        Assert.Contains("continuation ceiling", result.FirstError.Description, StringComparison.Ordinal);
        Assert.Empty(_handler.Requests);
    }

    [Fact]
    public async Task StartRunAsync_RecarriedBytesOverTheSharedBudget_FailsBeforeAnyUpload()
    {
        AgentRuntimeClient client = CreateClient();

        // One large document plus a re-carried file that together cross the budget: the
        // re-carried bytes must charge the same ledger the documents do.
        var document = new AgentRunDocument(
            "big.bin", new byte[AgentRunInputCapacity.INPUT_BUDGET_BYTES - 1]);
        AgentRunRecarriedFile recarried = RecarriedFile("out.txt", new byte[2]);

        ErrorOr<AgentRunMonitor> result = await client.StartRunAsync(
            RequestWithContinuation([recarried]) with { Documents = [document] },
            TestContext.Current.CancellationToken);

        Assert.True(result.IsError);
        Assert.Contains("re-carried out/ files decode to", result.FirstError.Description, StringComparison.Ordinal);
        Assert.Empty(_handler.Requests);
    }

    [Theory]
    [InlineData("checks//model.py")]
    [InlineData("./model.py")]
    [InlineData("checks/../model.py")]
    [InlineData("..")]
    public async Task StartRunAsync_RecarriedPathWithUnsafeSegment_FailsBeforeAnyUpload(string path)
    {
        AgentRuntimeClient client = CreateClient();

        ErrorOr<AgentRunMonitor> result = await client.StartRunAsync(
            RequestWithContinuation([RecarriedFile(path)]), TestContext.Current.CancellationToken);

        Assert.True(result.IsError);
        Assert.Contains("segments", result.FirstError.Description, StringComparison.Ordinal);
        Assert.Empty(_handler.Requests);
    }

    [Fact]
    public async Task StartRunAsync_RecarriedPathNestedInsideAnother_FailsBeforeAnyUpload()
    {
        AgentRuntimeClient client = CreateClient();

        ErrorOr<AgentRunMonitor> result = await client.StartRunAsync(
            RequestWithContinuation([RecarriedFile("checks"), RecarriedFile("checks/model.py")]),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsError);
        Assert.Contains("directory prefix", result.FirstError.Description, StringComparison.Ordinal);
        Assert.Empty(_handler.Requests);
    }

    [Fact]
    public async Task StartRunAsync_RecarriedPathEqualToADocumentName_IsItsOwnNamespace()
    {
        _handler.StatusCode = HttpStatusCode.Accepted;
        _handler.ResponseBody = MonitorJson(status: "running");

        AgentRuntimeClient client = CreateClient();

        // The runtime stages re-carried files in their own area, so a path that matches a
        // staged document name is not a collision and must not be refused.
        ErrorOr<AgentRunMonitor> result = await client.StartRunAsync(
            RequestWithContinuation([RecarriedFile("report.txt")]) with
            {
                Documents = [new AgentRunDocument("report.txt", new byte[3])],
            },
            TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        _ = Assert.Single(_handler.Requests);
    }

    [Fact]
    public async Task StartRunAsync_BundleOverTheRuntimeCap_FailsBeforeAnyUpload()
    {
        AgentRuntimeClient client = CreateClient();

        byte[] oversized = new byte[AgentRunContinuationCapacity.MAX_SESSION_BUNDLE_BYTES + 1];

        ErrorOr<AgentRunMonitor> result = await client.StartRunAsync(
            new AgentRunStartRequest(EXECUTION_ID, PROMPT, RUN_TOKEN)
            {
                Continuation = new AgentRunContinuation(oversized, Sha256Of(oversized), []),
            },
            TestContext.Current.CancellationToken);

        Assert.True(result.IsError);
        Assert.Contains("bundle cap", result.FirstError.Description, StringComparison.Ordinal);
        Assert.Empty(_handler.Requests);
    }

    [Fact]
    public async Task StartRunAsync_SkillWithoutAssets_OmitsTheAssetsKey()
    {
        _handler.StatusCode = HttpStatusCode.Accepted;
        _handler.ResponseBody = MonitorJson(status: "running");

        AgentRuntimeClient client = CreateClient();

        ErrorOr<AgentRunMonitor> result = await client.StartRunAsync(
            new AgentRunStartRequest(EXECUTION_ID, PROMPT, RUN_TOKEN)
            {
                SkillFiles = [new AgentRunSkillFile("extraction", "Do it.", "Extracts.")],
            },
            TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        RecordedRequest request = Assert.Single(_handler.Requests);
        using JsonDocument body = JsonDocument.Parse(request.Body!);
        Assert.False(body.RootElement.GetProperty("skillFiles")[0].TryGetProperty("assets", out _));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task StartRunAsync_SkillWithoutDescription_FailsBeforeAnyUpload(string? description)
    {
        AgentRuntimeClient client = CreateClient();

        ErrorOr<AgentRunMonitor> result = await client.StartRunAsync(
            new AgentRunStartRequest(EXECUTION_ID, PROMPT, RUN_TOKEN)
            {
                SkillFiles = [new AgentRunSkillFile("extraction", "Do it.", description)],
            },
            TestContext.Current.CancellationToken);

        Assert.True(result.IsError);
        Assert.Empty(_handler.Requests);
        Assert.Contains("description", result.FirstError.Description, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task StartRunAsync_OverlongSkillDescription_FailsBeforeAnyUpload()
    {
        AgentRuntimeClient client = CreateClient();

        ErrorOr<AgentRunMonitor> result = await client.StartRunAsync(
            new AgentRunStartRequest(EXECUTION_ID, PROMPT, RUN_TOKEN)
            {
                SkillFiles = [new AgentRunSkillFile("extraction", "Do it.", new string('d', 1401))],
            },
            TestContext.Current.CancellationToken);

        Assert.True(result.IsError);
        Assert.Empty(_handler.Requests);
        Assert.Contains("1400", result.FirstError.Description);
    }

    [Fact]
    public async Task StartRunAsync_RepeatedSkillName_FailsBeforeAnyUpload()
    {
        AgentRuntimeClient client = CreateClient();

        ErrorOr<AgentRunMonitor> result = await client.StartRunAsync(
            new AgentRunStartRequest(EXECUTION_ID, PROMPT, RUN_TOKEN)
            {
                SkillFiles =
                [
                    new AgentRunSkillFile("extraction", "First.", "d"),
                    new AgentRunSkillFile("extraction", "Second.", "d"),
                ],
            },
            TestContext.Current.CancellationToken);

        Assert.True(result.IsError);
        Assert.Empty(_handler.Requests);
        Assert.Contains("skill", result.FirstError.Description, StringComparison.Ordinal);
    }

    [Fact]
    public async Task StartRunAsync_SkillNameWithNoIdentity_FailsBeforeAnyUpload()
    {
        AgentRuntimeClient client = CreateClient();

        ErrorOr<AgentRunMonitor> result = await client.StartRunAsync(
            new AgentRunStartRequest(EXECUTION_ID, PROMPT, RUN_TOKEN)
            {
                SkillFiles = [new AgentRunSkillFile("---", "Do it.", "d")],
            },
            TestContext.Current.CancellationToken);

        Assert.True(result.IsError);
        Assert.Empty(_handler.Requests);
    }

    [Fact]
    public async Task StartRunAsync_202Created_IsNotMarkedAlreadyRegistered()
    {
        _handler.StatusCode = HttpStatusCode.Accepted;
        _handler.ResponseBody = MonitorJson(status: "running");

        AgentRuntimeClient client = CreateClient();

        ErrorOr<AgentRunMonitor> result = await client.StartRunAsync(
            EXECUTION_ID, PROMPT, RUN_TOKEN, TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        Assert.False(result.Value.AlreadyRegistered);
        Assert.Equal(AgentRunStatus.Running, result.Value.Status);
    }

    [Fact]
    public async Task StartRunAsync_200IdempotentDuplicate_IsMarkedAlreadyRegistered()
    {
        _handler.StatusCode = HttpStatusCode.OK;
        _handler.ResponseBody = MonitorJson(status: "running");

        AgentRuntimeClient client = CreateClient();

        ErrorOr<AgentRunMonitor> result = await client.StartRunAsync(
            EXECUTION_ID, PROMPT, RUN_TOKEN, TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        Assert.True(result.Value.AlreadyRegistered);
    }

    [Fact]
    public async Task GetRunAsync_PendingIsNoLongerAStatus_MapsToUnknown()
    {
        _handler.StatusCode = HttpStatusCode.OK;
        _handler.ResponseBody = MonitorJson(status: "pending");

        AgentRuntimeClient client = CreateClient();

        ErrorOr<AgentRunMonitor> result = await client.GetRunAsync(
            EXECUTION_ID, TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        Assert.Equal(AgentRunStatus.Unknown, result.Value.Status);
    }

    [Fact]
    public async Task GetRunAsync_ParsesMonitorAndHonoursRetryAfter()
    {
        _handler.StatusCode = HttpStatusCode.OK;
        _handler.ResponseBody = MonitorJson(status: "running");
        _handler.RetryAfterSeconds = 7;

        AgentRuntimeClient client = CreateClient();

        ErrorOr<AgentRunMonitor> result = await client.GetRunAsync(
            EXECUTION_ID, TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        RecordedRequest request = Assert.Single(_handler.Requests);
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Equal($"{RUNS_URL}/{EXECUTION_ID}", request.Uri);
        Assert.Equal(TimeSpan.FromSeconds(7), result.Value.PollInterval);
        Assert.Equal(AgentRunStatus.Running, result.Value.Status);
        Assert.False(result.Value.AlreadyRegistered);
        Assert.Equal(EXECUTION_ID, result.Value.ExecutionId);
        Assert.Equal(1, result.Value.Attempt);
        Assert.Equal(0.25m, result.Value.EstimatedCostUsd);
        Assert.Null(result.Value.PausedAt);
        Assert.Null(result.Value.ResumedAt);
        Assert.Null(result.Value.LastPauseMs);
    }

    [Fact]
    public async Task GetRunAsync_ReadsTheBudgetTheRuntimeEchoesForTheRun()
    {
        _handler.StatusCode = HttpStatusCode.OK;
        _handler.ResponseBody = MonitorJson(status: "failed").Replace(
            "\"requiredInput\": null",
            "\"requiredInput\": null, \"fuses\": {\"deadlineMs\": 900000, \"maxBudgetUsd\": 2.5, "
            + "\"maxTurns\": 300, \"maxRetainedOutputBytes\": 25165824}",
            StringComparison.Ordinal);

        ErrorOr<AgentRunMonitor> result = await CreateClient().GetRunAsync(
            EXECUTION_ID, TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        Assert.Equal(2.5m, result.Value.Fuses?.MaxBudgetUsd);
    }

    [Fact]
    public async Task GetRunAsync_AMonitorWithoutFuses_LeavesThemNull()
    {
        _handler.StatusCode = HttpStatusCode.OK;
        _handler.ResponseBody = MonitorJson(status: "failed");

        ErrorOr<AgentRunMonitor> result = await CreateClient().GetRunAsync(
            EXECUTION_ID, TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        Assert.Null(result.Value.Fuses);
    }

    [Fact]
    public async Task GetRunAsync_RetryAfterDateForm_ComputesIntervalFromNow()
    {
        _handler.StatusCode = HttpStatusCode.OK;
        _handler.ResponseBody = MonitorJson(status: "running");
        _handler.RetryAfterDate = DateTimeOffset.UtcNow.AddSeconds(30);

        AgentRuntimeClient client = CreateClient();

        ErrorOr<AgentRunMonitor> result = await client.GetRunAsync(
            EXECUTION_ID, TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        Assert.NotNull(result.Value.PollInterval);
        Assert.InRange(result.Value.PollInterval.Value, TimeSpan.FromSeconds(20), TimeSpan.FromSeconds(31));
    }

    [Fact]
    public async Task GetRunAsync_RetryAfterDateInThePast_ClampsToZero()
    {
        _handler.StatusCode = HttpStatusCode.OK;
        _handler.ResponseBody = MonitorJson(status: "running");
        _handler.RetryAfterDate = DateTimeOffset.UtcNow.AddSeconds(-30);

        AgentRuntimeClient client = CreateClient();

        ErrorOr<AgentRunMonitor> result = await client.GetRunAsync(
            EXECUTION_ID, TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        Assert.Equal(TimeSpan.Zero, result.Value.PollInterval);
    }

    [Fact]
    public async Task GetRunAsync_TerminalWithoutRetryAfter_HasNullPollInterval()
    {
        _handler.StatusCode = HttpStatusCode.OK;
        _handler.ResponseBody = MonitorJson(status: "succeeded");

        AgentRuntimeClient client = CreateClient();

        ErrorOr<AgentRunMonitor> result = await client.GetRunAsync(
            EXECUTION_ID, TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        Assert.Null(result.Value.PollInterval);
    }

    [Fact]
    public async Task GetRunAsync_MissingRetryAfterOnNonTerminal_IsNullNotAFallback()
    {
        _handler.StatusCode = HttpStatusCode.OK;
        _handler.ResponseBody = MonitorJson(status: "running");

        AgentRuntimeClient client = CreateClient();

        ErrorOr<AgentRunMonitor> result = await client.GetRunAsync(
            EXECUTION_ID, TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        Assert.Null(result.Value.PollInterval);
    }

    [Fact]
    public async Task GetRunAsync_MonitorForADifferentExecution_MapsToFailure()
    {
        _handler.StatusCode = HttpStatusCode.OK;
        _handler.ResponseBody = MonitorJson(status: "running", executionId: "exec-other");

        AgentRuntimeClient client = CreateClient();

        ErrorOr<AgentRunMonitor> result = await client.GetRunAsync(
            EXECUTION_ID, TestContext.Current.CancellationToken);

        Assert.True(result.IsError);
        Assert.Equal(ErrorType.Failure, result.FirstError.Type);
    }

    [Fact]
    public async Task StartRunAsync_MonitorWithDifferentlyCasedExecutionId_MapsToFailure()
    {
        _handler.StatusCode = HttpStatusCode.Accepted;
        _handler.ResponseBody = MonitorJson(status: "running", executionId: "EXEC-9");

        AgentRuntimeClient client = CreateClient();

        ErrorOr<AgentRunMonitor> result = await client.StartRunAsync(
            "exec-9", PROMPT, RUN_TOKEN, TestContext.Current.CancellationToken);

        Assert.True(result.IsError);
        Assert.Equal(ErrorType.Failure, result.FirstError.Type);
    }

    [Fact]
    public async Task GetRunAsync_UnknownStatusString_MapsToUnknownWithoutThrowing()
    {
        _handler.StatusCode = HttpStatusCode.OK;
        _handler.ResponseBody = MonitorJson(status: "some_future_status");

        AgentRuntimeClient client = CreateClient();

        ErrorOr<AgentRunMonitor> result = await client.GetRunAsync(
            EXECUTION_ID, TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        Assert.Equal(AgentRunStatus.Unknown, result.Value.Status);
        Assert.False(result.Value.Status.IsTerminal());
    }

    [Fact]
    public async Task GetRunAsync_FailedRun_ParsesSubtypeOnlyError()
    {
        _handler.StatusCode = HttpStatusCode.OK;
        _handler.ResponseBody = """
            {
              "executionId": "exec-1",
              "attempt": 1,
              "segment": 0,
              "status": "failed",
              "createdAt": "2026-08-03T10:00:00Z",
              "updatedAt": "2026-08-03T10:05:00Z",
              "estimatedCostUsd": 1.5,
              "error": { "subtype": "budget_exhausted" },
              "result": null,
              "requiredInput": null,
              "pausedAt": null,
              "resumedAt": null,
              "lastPauseMs": null
            }
            """;

        AgentRuntimeClient client = CreateClient();

        ErrorOr<AgentRunMonitor> result = await client.GetRunAsync(
            EXECUTION_ID, TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        AgentRunMonitor monitor = result.Value;
        Assert.Equal(AgentRunStatus.Failed, monitor.Status);
        Assert.NotNull(monitor.Error);
        Assert.Equal("budget_exhausted", monitor.Error.Subtype);
        Assert.Null(monitor.Result);
        Assert.Null(monitor.RequiredInput);
    }

    [Fact]
    public async Task GetRunAsync_SucceededRun_ParsesOutputAndSessionIdResult()
    {
        _handler.StatusCode = HttpStatusCode.OK;
        _handler.ResponseBody = """
            {
              "executionId": "exec-1",
              "attempt": 1,
              "segment": 2,
              "status": "succeeded",
              "createdAt": "2026-08-03T10:00:00Z",
              "updatedAt": "2026-08-03T10:05:00Z",
              "estimatedCostUsd": 1.5,
              "error": null,
              "result": { "output": "the report", "sessionId": "sess-9" },
              "requiredInput": null,
              "pausedAt": "2026-08-03T10:03:00Z",
              "resumedAt": "2026-08-03T10:04:30Z",
              "lastPauseMs": 90000
            }
            """;

        AgentRuntimeClient client = CreateClient();

        ErrorOr<AgentRunMonitor> result = await client.GetRunAsync(
            EXECUTION_ID, TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        AgentRunMonitor monitor = result.Value;
        Assert.Equal(AgentRunStatus.Succeeded, monitor.Status);
        Assert.Equal(2, monitor.Segment);
        Assert.NotNull(monitor.Result);
        Assert.Equal("the report", monitor.Result.Output);
        Assert.Equal("sess-9", monitor.Result.SessionId);
        Assert.Null(monitor.Error);
        Assert.Equal(new DateTimeOffset(2026, 8, 3, 10, 3, 0, TimeSpan.Zero), monitor.PausedAt);
        Assert.Equal(new DateTimeOffset(2026, 8, 3, 10, 4, 30, TimeSpan.Zero), monitor.ResumedAt);
        Assert.Equal(90000, monitor.LastPauseMs);
    }

    [Fact]
    public async Task GetRunAsync_AwaitingInput_ParsesSingularQuestionWithStructuredInput()
    {
        _handler.StatusCode = HttpStatusCode.OK;
        _handler.ResponseBody = AwaitingInputMonitorJson();
        _handler.RetryAfterSeconds = 15;

        AgentRuntimeClient client = CreateClient();

        ErrorOr<AgentRunMonitor> result = await client.GetRunAsync(
            EXECUTION_ID, TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        AgentRunMonitor monitor = result.Value;
        Assert.Equal(AgentRunStatus.AwaitingInput, monitor.Status);
        Assert.Equal(1, monitor.Attempt);
        Assert.Equal(TimeSpan.FromSeconds(15), monitor.PollInterval);
        Assert.NotNull(monitor.PausedAt);
        Assert.Null(monitor.ResumedAt);
        Assert.NotNull(monitor.RequiredInput);
        Assert.Equal("resume-abc", monitor.RequiredInput.ResumeToken);
        Assert.Equal(new DateTimeOffset(2026, 8, 4, 10, 0, 0, TimeSpan.Zero), monitor.RequiredInput.ExpiresAt);

        AgentRunQuestion question = monitor.RequiredInput.Question;
        Assert.Equal("q1", question.Id);
        Assert.Equal("select_fund", question.ToolName);
        Assert.Equal("Which fund?", question.Prompt);
        Assert.Equal(JsonValueKind.Object, question.Input.ValueKind);
        Assert.Equal("Fund A", question.Input.GetProperty("draft").GetString());
    }

    [Fact]
    public async Task GetRunAsync_404_MapsToNotFound()
    {
        _handler.StatusCode = HttpStatusCode.NotFound;
        _handler.ResponseBody = "";

        AgentRuntimeClient client = CreateClient();

        ErrorOr<AgentRunMonitor> result = await client.GetRunAsync(
            EXECUTION_ID, TestContext.Current.CancellationToken);

        Assert.True(result.IsError);
        Assert.Equal(ErrorType.NotFound, result.FirstError.Type);
    }

    [Fact]
    public async Task GetRunAsync_ServerError_MapsToFailure()
    {
        _handler.StatusCode = HttpStatusCode.InternalServerError;
        _handler.ResponseBody = "boom";

        AgentRuntimeClient client = CreateClient();

        ErrorOr<AgentRunMonitor> result = await client.GetRunAsync(
            EXECUTION_ID, TestContext.Current.CancellationToken);

        Assert.True(result.IsError);
        Assert.Equal(ErrorType.Failure, result.FirstError.Type);
    }

    [Theory]
    [InlineData("""{"error":"shutting down"}""", null, AgentRunAdmissionErrors.SHUTTING_DOWN)]
    [InlineData("""{"error":"Shutting Down"}""", null, AgentRunAdmissionErrors.SHUTTING_DOWN)]
    [InlineData("""{"error":"capacity"}""", 0, AgentRunAdmissionErrors.CAPACITY)]
    public async Task StartRunAsync_503WithoutUsableRetryAfter_CarriesNoDelay(
        string responseBody, int? advertisedSeconds, string expectedCode)
    {
        _handler.StatusCode = HttpStatusCode.ServiceUnavailable;
        _handler.ResponseBody = responseBody;
        _handler.RetryAfterSeconds = advertisedSeconds;

        AgentRuntimeClient client = CreateClient();

        ErrorOr<AgentRunMonitor> result = await client.StartRunAsync(
            EXECUTION_ID, PROMPT, RUN_TOKEN, TestContext.Current.CancellationToken);

        Assert.True(result.IsError);
        Assert.Equal(expectedCode, result.FirstError.Code);
        Assert.Equal(ErrorType.Failure, result.FirstError.Type);
        Assert.Empty(result.FirstError.Metadata ?? []);
    }

    [Fact]
    public async Task StartRunAsync_503ChatDisabled_IsAClearErrorNoCallerWaitsOut()
    {
        _handler.StatusCode = HttpStatusCode.ServiceUnavailable;
        _handler.ResponseBody = """{"error":"capacity","reason":"chat_disabled"}""";

        AgentRuntimeClient client = CreateClient();

        ErrorOr<AgentRunMonitor> result = await client.StartRunAsync(
            new AgentRunStartRequest(EXECUTION_ID, PROMPT, RUN_TOKEN) { AdmissionClass = AgentRunAdmissionClass.Chat },
            TestContext.Current.CancellationToken);

        Assert.True(result.IsError);
        Assert.Equal(AgentRunAdmissionErrors.CHAT_DISABLED, result.FirstError.Code);
        Assert.False(AgentRunAdmissionErrors.IsAdmissionRefusal(result.FirstError.Code));
        Assert.Empty(result.FirstError.Metadata ?? []);
    }

    [Theory]
    [InlineData("""{"error":"capacity"}""", 30, 30, 36)]
    // 3600 s is clamped to the runtime's own 300 s ceiling before the jitter widens it.
    [InlineData("""{"error":"capacity"}""", 3600, 300, 360)]
    [InlineData("overloaded", 5, 5, 6)]
    public async Task StartRunAsync_503WithRetryAfter_MapsToCapacityWithHonouredDelay(
        string responseBody, int advertisedSeconds, int minSeconds, int maxSeconds)
    {
        _handler.StatusCode = HttpStatusCode.ServiceUnavailable;
        _handler.ResponseBody = responseBody;
        _handler.RetryAfterSeconds = advertisedSeconds;

        AgentRuntimeClient client = CreateClient();

        ErrorOr<AgentRunMonitor> result = await client.StartRunAsync(
            EXECUTION_ID, PROMPT, RUN_TOKEN, TestContext.Current.CancellationToken);

        Assert.True(result.IsError);
        Assert.Equal(AgentRunAdmissionErrors.CAPACITY, result.FirstError.Code);
        Assert.Equal(ErrorType.Failure, result.FirstError.Type);
        TimeSpan honoured = Assert.IsType<TimeSpan>(
            result.FirstError.Metadata![AgentRunAdmissionErrors.RETRY_AFTER_METADATA_KEY]);
        Assert.InRange(honoured, TimeSpan.FromSeconds(minSeconds), TimeSpan.FromSeconds(maxSeconds));
    }

    [Fact]
    public async Task StartRunAsync_503RetryAfter_IsJitteredNotConstant()
    {
        _handler.StatusCode = HttpStatusCode.ServiceUnavailable;
        _handler.ResponseBody = """{"error":"capacity"}""";
        _handler.RetryAfterSeconds = 30;

        AgentRuntimeClient client = CreateClient();
        var honoured = new HashSet<TimeSpan>();

        for (int draw = 0; draw < 20; draw++)
        {
            ErrorOr<AgentRunMonitor> result = await client.StartRunAsync(
                EXECUTION_ID, PROMPT, RUN_TOKEN, TestContext.Current.CancellationToken);

            _ = honoured.Add((TimeSpan)result.FirstError.Metadata![
                AgentRunAdmissionErrors.RETRY_AFTER_METADATA_KEY]);
        }

        // Callers refused together must not return together, so a constant delay is the defect.
        Assert.True(honoured.Count > 1, "The honoured Retry-After never varied across 20 refusals");
        Assert.All(honoured, delay => Assert.InRange(
            delay, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(36)));
    }

    [Fact]
    public async Task SubmitInputAsync_503_IsTypedWithoutClaimingTheRunIsUnregistered()
    {
        _handler.StatusCode = HttpStatusCode.ServiceUnavailable;
        _handler.ResponseBody = """{"error":"capacity"}""";
        _handler.RetryAfterSeconds = 5;

        AgentRuntimeClient client = CreateClient();

        ErrorOr<AgentRunMonitor> result = await client.SubmitInputAsync(
            EXECUTION_ID, "resume-token", "q1", "answer",
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(result.IsError);
        Assert.Equal(AgentRunAdmissionErrors.CAPACITY, result.FirstError.Code);
        // A live paused run is registered by definition; the text must never say otherwise.
        Assert.DoesNotContain("registering", result.FirstError.Description);
    }

    [Fact]
    public async Task StartRunAsync_503SubSecondRetryAfter_DoesNotReadAsRetryImmediately()
    {
        _handler.StatusCode = HttpStatusCode.ServiceUnavailable;
        _handler.ResponseBody = """{"error":"capacity"}""";
        _handler.RetryAfterDate = DateTimeOffset.UtcNow.AddMilliseconds(800);

        AgentRuntimeClient client = CreateClient();

        ErrorOr<AgentRunMonitor> result = await client.StartRunAsync(
            EXECUTION_ID, PROMPT, RUN_TOKEN, TestContext.Current.CancellationToken);

        Assert.True(result.IsError);
        TimeSpan honoured = Assert.IsType<TimeSpan>(
            result.FirstError.Metadata![AgentRunAdmissionErrors.RETRY_AFTER_METADATA_KEY]);
        Assert.True(honoured > TimeSpan.Zero);
        // Rounding a real sub-second delay to "0s" would contradict the metadata beside it.
        Assert.DoesNotContain("retry after 0s", result.FirstError.Description);
    }

    [Fact]
    public async Task StartRunAsync_503UnknownReason_SurfacesTheRuntimesOwnWords()
    {
        _handler.StatusCode = HttpStatusCode.ServiceUnavailable;
        _handler.ResponseBody = """{"error":"quota exhausted"}""";

        AgentRuntimeClient client = CreateClient();

        ErrorOr<AgentRunMonitor> result = await client.StartRunAsync(
            EXECUTION_ID, PROMPT, RUN_TOKEN, TestContext.Current.CancellationToken);

        Assert.True(result.IsError);
        Assert.Equal(AgentRunAdmissionErrors.CAPACITY, result.FirstError.Code);
        Assert.Contains("quota exhausted", result.FirstError.Description);
    }

    [Fact]
    public async Task GetRunAsync_UnparseableBody_MapsToFailure()
    {
        _handler.StatusCode = HttpStatusCode.OK;
        _handler.ResponseBody = "not json";

        AgentRuntimeClient client = CreateClient();

        ErrorOr<AgentRunMonitor> result = await client.GetRunAsync(
            EXECUTION_ID, TestContext.Current.CancellationToken);

        Assert.True(result.IsError);
        Assert.Equal(ErrorType.Failure, result.FirstError.Type);
    }

    [Fact]
    public async Task GetRunAsync_ConnectionFailure_MapsToFailure()
    {
        _handler.ThrowOnSend = new HttpRequestException("connection refused");

        AgentRuntimeClient client = CreateClient();

        ErrorOr<AgentRunMonitor> result = await client.GetRunAsync(
            EXECUTION_ID, TestContext.Current.CancellationToken);

        Assert.True(result.IsError);
        Assert.Equal(ErrorType.Failure, result.FirstError.Type);
    }

    [Fact]
    public async Task CancelRunAsync_PostsToCancelPath_ParsesCancellingMonitor()
    {
        _handler.StatusCode = HttpStatusCode.Accepted;
        _handler.ResponseBody = MonitorJson(status: "cancelling");

        AgentRuntimeClient client = CreateClient();

        ErrorOr<AgentRunMonitor> result = await client.CancelRunAsync(
            EXECUTION_ID, TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        RecordedRequest request = Assert.Single(_handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal($"{RUNS_URL}/{EXECUTION_ID}/cancel", request.Uri);
        Assert.Equal(AgentRunStatus.Cancelling, result.Value.Status);
    }

    [Fact]
    public async Task CancelRunAsync_AlreadyTerminal_ReturnsTerminalMonitor()
    {
        _handler.StatusCode = HttpStatusCode.OK;
        _handler.ResponseBody = MonitorJson(status: "cancelled");

        AgentRuntimeClient client = CreateClient();

        ErrorOr<AgentRunMonitor> result = await client.CancelRunAsync(
            EXECUTION_ID, TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        Assert.Equal(AgentRunStatus.Cancelled, result.Value.Status);
        Assert.True(result.Value.Status.IsTerminal());
        Assert.False(result.Value.AlreadyRegistered);
    }

    [Fact]
    public async Task CancelRunAsync_WithAnAlreadyCancelledToken_NeverReachesTheWire()
    {
        _handler.StatusCode = HttpStatusCode.Accepted;
        _handler.ResponseBody = MonitorJson(status: "cancelling");
        using var dyingWorkflowToken = new CancellationTokenSource();
        await dyingWorkflowToken.CancelAsync();

        AgentRuntimeClient client = CreateClient();

        _ = await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => client.CancelRunAsync(EXECUTION_ID, dyingWorkflowToken.Token));

        Assert.Empty(_handler.Requests);
    }

    [Fact]
    public async Task CancelRunAsync_WithAFreshToken_SendsEvenWhileTheWorkflowTokenIsDying()
    {
        _handler.StatusCode = HttpStatusCode.Accepted;
        _handler.ResponseBody = MonitorJson(status: "cancelling");
        using var dyingWorkflowToken = new CancellationTokenSource();
        await dyingWorkflowToken.CancelAsync();
        using var freshCancelToken = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        AgentRuntimeClient client = CreateClient();

        ErrorOr<AgentRunMonitor> result = await client.CancelRunAsync(
            EXECUTION_ID, freshCancelToken.Token);

        Assert.False(result.IsError);
        Assert.Equal(AgentRunStatus.Cancelling, result.Value.Status);
        _ = Assert.Single(_handler.Requests);
    }

    [Theory]
    [InlineData("succeeded", AgentRunStatus.Succeeded)]
    [InlineData("failed", AgentRunStatus.Failed)]
    [InlineData("cancelled", AgentRunStatus.Cancelled)]
    [InlineData("expired", AgentRunStatus.Expired)]
    public async Task GetRunAsync_TerminalStatuses_ParseAndAreTerminal(string wireStatus, AgentRunStatus expected)
    {
        _handler.StatusCode = HttpStatusCode.OK;
        _handler.ResponseBody = MonitorJson(status: wireStatus);

        AgentRuntimeClient client = CreateClient();

        ErrorOr<AgentRunMonitor> result = await client.GetRunAsync(
            EXECUTION_ID, TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        Assert.Equal(expected, result.Value.Status);
        Assert.True(result.Value.Status.IsTerminal());
    }

    [Fact]
    public async Task GetRunAsync_Timeout_MapsToTimeoutFailure()
    {
        _handler.ThrowOnSend = new TaskCanceledException("the request timed out");

        AgentRuntimeClient client = CreateClient();

        ErrorOr<AgentRunMonitor> result = await client.GetRunAsync(
            EXECUTION_ID, TestContext.Current.CancellationToken);

        Assert.True(result.IsError);
        Assert.Equal(ErrorType.Failure, result.FirstError.Type);
        Assert.Equal("AgentRun.Timeout", result.FirstError.Code);
    }

    [Fact]
    public async Task GetRunAsync_EmptyJsonObjectBody_MapsToFailureNotAMonitor()
    {
        _handler.StatusCode = HttpStatusCode.OK;
        _handler.ResponseBody = "{}";

        AgentRuntimeClient client = CreateClient();

        ErrorOr<AgentRunMonitor> result = await client.GetRunAsync(
            EXECUTION_ID, TestContext.Current.CancellationToken);

        Assert.True(result.IsError);
        Assert.Equal(ErrorType.Failure, result.FirstError.Type);
    }

    [Fact]
    public async Task GetRunAsync_StructuredStatusValue_MapsToUnknownWithoutCorruptingParse()
    {
        _handler.StatusCode = HttpStatusCode.OK;
        _handler.ResponseBody = """
            {
              "executionId": "exec-1",
              "attempt": 1,
              "segment": 0,
              "status": { "weird": true },
              "createdAt": "2026-08-03T10:00:00Z",
              "updatedAt": "2026-08-03T10:00:05Z",
              "estimatedCostUsd": 0.25,
              "error": null,
              "result": null,
              "requiredInput": null
            }
            """;

        AgentRuntimeClient client = CreateClient();

        ErrorOr<AgentRunMonitor> result = await client.GetRunAsync(
            EXECUTION_ID, TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        Assert.Equal(AgentRunStatus.Unknown, result.Value.Status);
        Assert.Equal(EXECUTION_ID, result.Value.ExecutionId);
        Assert.Equal(0.25m, result.Value.EstimatedCostUsd);
    }

    [Fact]
    public async Task SubmitInputAsync_PostsResumeTokenQuestionIdAndAnswer()
    {
        _handler.StatusCode = HttpStatusCode.OK;
        _handler.ResponseBody = MonitorJson(status: "running");

        AgentRuntimeClient client = CreateClient();

        ErrorOr<AgentRunMonitor> result = await client.SubmitInputAsync(
            EXECUTION_ID,
            "resume-abc",
            "q1",
            "Fund A",
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        RecordedRequest request = Assert.Single(_handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal($"{RUNS_URL}/{EXECUTION_ID}/input", request.Uri);

        using JsonDocument body = JsonDocument.Parse(request.Body!);
        Assert.Equal("resume-abc", body.RootElement.GetProperty("resumeToken").GetString());
        Assert.Equal("q1", body.RootElement.GetProperty("questionId").GetString());
        Assert.Equal("Fund A", body.RootElement.GetProperty("answer").GetString());
        Assert.True(body.RootElement.GetProperty("allowFurtherPause").GetBoolean());
    }

    [Fact]
    public async Task SubmitInputAsync_AllowFurtherPauseFalse_IsTheReleaseVerb()
    {
        _handler.StatusCode = HttpStatusCode.OK;
        _handler.ResponseBody = MonitorJson(status: "running");

        AgentRuntimeClient client = CreateClient();

        ErrorOr<AgentRunMonitor> result = await client.SubmitInputAsync(
            EXECUTION_ID,
            "resume-abc",
            "q1",
            "Fund A",
            allowFurtherPause: false,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        RecordedRequest request = Assert.Single(_handler.Requests);
        using JsonDocument body = JsonDocument.Parse(request.Body!);
        Assert.False(body.RootElement.GetProperty("allowFurtherPause").GetBoolean());
    }

    [Theory]
    [InlineData("payload_conflict", "AgentRun.PayloadConflict")]
    [InlineData("stale_resume_token", "AgentRun.StaleResumeToken")]
    [InlineData("not_awaiting_input", "AgentRun.NotAwaitingInput")]
    [InlineData("unknown_question", "AgentRun.UnknownQuestion")]
    public async Task SubmitInputAsync_409Codes_MapToDistinctConflictErrors(string wireCode, string expectedErrorCode)
    {
        _handler.StatusCode = HttpStatusCode.Conflict;
        _handler.ResponseBody = $$"""{"code":"{{wireCode}}"}""";

        AgentRuntimeClient client = CreateClient();

        ErrorOr<AgentRunMonitor> result = await client.SubmitInputAsync(
            EXECUTION_ID,
            "resume-abc",
            "q1",
            "Fund A",
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(result.IsError);
        Assert.Equal(ErrorType.Conflict, result.FirstError.Type);
        Assert.Equal(expectedErrorCode, result.FirstError.Code);
    }

    [Fact]
    public async Task SubmitInputAsync_409WithStatus_CarriesStatusMetadata()
    {
        _handler.StatusCode = HttpStatusCode.Conflict;
        _handler.ResponseBody = """{"code":"stale_resume_token","status":"awaiting_input"}""";

        AgentRuntimeClient client = CreateClient();

        ErrorOr<AgentRunMonitor> result = await client.SubmitInputAsync(
            EXECUTION_ID,
            "stale-token",
            "q1",
            "Fund A",
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(result.IsError);
        Assert.Equal(ErrorType.Conflict, result.FirstError.Type);
        Assert.Equal("AgentRun.StaleResumeToken", result.FirstError.Code);
        Assert.NotNull(result.FirstError.Metadata);
        Assert.Equal("awaiting_input", result.FirstError.Metadata["status"]);
    }

    [Fact]
    public async Task StartRunAsync_409PayloadConflict_MapsToPayloadConflict()
    {
        _handler.StatusCode = HttpStatusCode.Conflict;
        _handler.ResponseBody = """{"code":"payload_conflict","status":"running"}""";

        AgentRuntimeClient client = CreateClient();

        ErrorOr<AgentRunMonitor> result = await client.StartRunAsync(
            EXECUTION_ID, PROMPT, RUN_TOKEN, TestContext.Current.CancellationToken);

        Assert.True(result.IsError);
        Assert.Equal(ErrorType.Conflict, result.FirstError.Type);
        Assert.Equal("AgentRun.PayloadConflict", result.FirstError.Code);
        Assert.NotNull(result.FirstError.Metadata);
        Assert.Equal("running", result.FirstError.Metadata["status"]);
    }

    [Theory]
    [InlineData("")]
    [InlineData("""{"code":"some_future_conflict"}""")]
    [InlineData("not json")]
    public async Task StartRunAsync_409WithoutAKnownCode_MapsToGenericConflict(string responseBody)
    {
        _handler.StatusCode = HttpStatusCode.Conflict;
        _handler.ResponseBody = responseBody;

        AgentRuntimeClient client = CreateClient();

        ErrorOr<AgentRunMonitor> result = await client.StartRunAsync(
            EXECUTION_ID, PROMPT, RUN_TOKEN, TestContext.Current.CancellationToken);

        Assert.True(result.IsError);
        Assert.Equal(ErrorType.Conflict, result.FirstError.Type);
        Assert.Equal("AgentRun.Conflict", result.FirstError.Code);
    }

    [Fact]
    public async Task SubmitInputAsync_400_MapsToValidation()
    {
        _handler.StatusCode = HttpStatusCode.BadRequest;
        _handler.ResponseBody = "";

        AgentRuntimeClient client = CreateClient();

        ErrorOr<AgentRunMonitor> result = await client.SubmitInputAsync(
            EXECUTION_ID,
            "resume-abc",
            "q1",
            "",
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(result.IsError);
        Assert.Equal(ErrorType.Validation, result.FirstError.Type);
    }

    [Theory]
    [InlineData("exec with spaces")]
    [InlineData("exec/../escape")]
    [InlineData("exec?query=1")]
    [InlineData("")]
    public async Task GetRunAsync_IllegalExecutionId_FailsValidationBeforeAnyHttp(string illegalExecutionId)
    {
        AgentRuntimeClient client = CreateClient();

        ErrorOr<AgentRunMonitor> result = await client.GetRunAsync(
            illegalExecutionId, TestContext.Current.CancellationToken);

        Assert.True(result.IsError);
        Assert.Equal(ErrorType.Validation, result.FirstError.Type);
        Assert.Empty(_handler.Requests);
    }

    [Fact]
    public async Task StartRunAsync_IllegalExecutionId_FailsValidationBeforeAnyHttp()
    {
        AgentRuntimeClient client = CreateClient();

        ErrorOr<AgentRunMonitor> result = await client.StartRunAsync(
            "exec id with spaces", PROMPT, RUN_TOKEN, TestContext.Current.CancellationToken);

        Assert.True(result.IsError);
        Assert.Equal(ErrorType.Validation, result.FirstError.Type);
        Assert.Empty(_handler.Requests);
    }

    private const string LIMITS_JSON = """
        {
          "deadlineMs": { "default": 900000, "ceiling": 900000, "unit": "milliseconds" },
          "maxBudgetUsd": { "default": 2.5, "ceiling": 2.5, "unit": "usd" },
          "maxTurns": { "default": 300, "ceiling": 300, "unit": "turns" },
          "maxRetainedOutputBytes": { "default": 134217728, "ceiling": 276824064, "unit": "bytes" }
        }
        """;

    [Fact]
    public async Task GetLimitsAsync_FetchesAndParsesTheLimitsView()
    {
        _handler.StatusCode = HttpStatusCode.OK;
        _handler.ResponseBody = LIMITS_JSON;

        AgentRuntimeClient client = CreateClient();

        ErrorOr<AgentRunLimits> result = await client.GetLimitsAsync(TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        RecordedRequest request = Assert.Single(_handler.Requests);
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Equal($"{BASE_URL}/v1/limits", request.Uri);
        Assert.Equal("Bearer", request.AuthScheme);
        Assert.Equal(AUTH_TOKEN, request.AuthParameter);

        Assert.Equal(900_000m, result.Value.DeadlineMs.Ceiling);
        Assert.Equal(2.5m, result.Value.MaxBudgetUsd.Ceiling);
        Assert.Equal(2.5m, result.Value.MaxBudgetUsd.Default);
        Assert.Equal(300m, result.Value.MaxTurns.Ceiling);
        Assert.Equal(134_217_728m, result.Value.MaxRetainedOutputBytes.Default);
        Assert.Equal(276_824_064m, result.Value.MaxRetainedOutputBytes.Ceiling);
        Assert.Equal("bytes", result.Value.MaxRetainedOutputBytes.Unit);
    }

    [Fact]
    public async Task GetLimitsAsync_MissingFuse_IsAnInvalidResponse()
    {
        _handler.StatusCode = HttpStatusCode.OK;
        _handler.ResponseBody = """{ "deadlineMs": { "default": 900000, "ceiling": 900000, "unit": "milliseconds" } }""";

        AgentRuntimeClient client = CreateClient();

        ErrorOr<AgentRunLimits> result = await client.GetLimitsAsync(TestContext.Current.CancellationToken);

        Assert.True(result.IsError);
        Assert.Equal("AgentRun.InvalidResponse", result.FirstError.Code);
    }

    [Fact]
    public async Task GetLimitsAsync_UnparseableBody_IsAnInvalidResponse()
    {
        _handler.StatusCode = HttpStatusCode.OK;
        _handler.ResponseBody = "not json";

        AgentRuntimeClient client = CreateClient();

        ErrorOr<AgentRunLimits> result = await client.GetLimitsAsync(TestContext.Current.CancellationToken);

        Assert.True(result.IsError);
        Assert.Equal("AgentRun.InvalidResponse", result.FirstError.Code);
    }

    [Fact]
    public async Task GetLimitsAsync_NonSuccessStatus_IsAFailure()
    {
        _handler.StatusCode = HttpStatusCode.InternalServerError;
        _handler.ResponseBody = "boom";

        AgentRuntimeClient client = CreateClient();

        ErrorOr<AgentRunLimits> result = await client.GetLimitsAsync(TestContext.Current.CancellationToken);

        Assert.True(result.IsError);
        Assert.Equal("AgentRun.Failure", result.FirstError.Code);
    }

    [Fact]
    public async Task GetLimitsAsync_UnreachableRuntime_IsUnreachable()
    {
        _handler.ThrowOnSend = new HttpRequestException("connection refused");

        AgentRuntimeClient client = CreateClient();

        ErrorOr<AgentRunLimits> result = await client.GetLimitsAsync(TestContext.Current.CancellationToken);

        Assert.True(result.IsError);
        Assert.Equal("AgentRun.Unreachable", result.FirstError.Code);
    }

    [Fact]
    public async Task GetEnvironmentAsync_FetchesTheRuntimeVersionAndText()
    {
        _handler.StatusCode = HttpStatusCode.OK;
        _handler.ResponseBody = """{ "runtimeVersion": "0.3.1030", "text": "## Your environment\nNo network." }""";

        AgentRuntimeClient client = CreateClient();

        ErrorOr<AgentRuntimeEnvironment> result = await client.GetEnvironmentAsync(TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        RecordedRequest request = Assert.Single(_handler.Requests);
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Equal($"{BASE_URL}/v1/environment", request.Uri);
        Assert.Equal(AUTH_TOKEN, request.AuthParameter);
        Assert.Equal("0.3.1030", result.Value.RuntimeVersion);
        Assert.Equal("## Your environment\nNo network.", result.Value.Text);
    }

    [Fact]
    public async Task GetEnvironmentAsync_OlderRuntimeAnswering404_IsNotServed()
    {
        _handler.StatusCode = HttpStatusCode.NotFound;
        _handler.ResponseBody = "not found";

        AgentRuntimeClient client = CreateClient();

        ErrorOr<AgentRuntimeEnvironment> result = await client.GetEnvironmentAsync(TestContext.Current.CancellationToken);

        Assert.True(result.IsError);
        Assert.Equal(AgentRuntimeEnvironmentErrors.NOT_SERVED, result.FirstError.Code);
    }

    [Fact]
    public async Task GetEnvironmentAsync_BlankText_IsAnInvalidResponse()
    {
        _handler.StatusCode = HttpStatusCode.OK;
        _handler.ResponseBody = """{ "runtimeVersion": "0.3.1030", "text": "" }""";

        AgentRuntimeClient client = CreateClient();

        ErrorOr<AgentRuntimeEnvironment> result = await client.GetEnvironmentAsync(TestContext.Current.CancellationToken);

        Assert.True(result.IsError);
        Assert.Equal("AgentRun.InvalidResponse", result.FirstError.Code);
    }

    [Fact]
    public async Task GetEnvironmentAsync_ServerError_IsAFailure()
    {
        _handler.StatusCode = HttpStatusCode.InternalServerError;
        _handler.ResponseBody = "boom";

        AgentRuntimeClient client = CreateClient();

        ErrorOr<AgentRuntimeEnvironment> result = await client.GetEnvironmentAsync(TestContext.Current.CancellationToken);

        Assert.True(result.IsError);
        Assert.Equal("AgentRun.Failure", result.FirstError.Code);
    }

    private AgentRuntimeClient CreateClient()
    {
        var httpClient = new HttpClient(_handler)
        {
            BaseAddress = new Uri(BASE_URL + "/"),
            Timeout = TimeSpan.FromSeconds(30),
        };
        httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", AUTH_TOKEN);

        return new AgentRuntimeClient(httpClient, NullLogger<AgentRuntimeClient>.Instance);
    }

    private static AgentRunStartRequest RequestWithContinuation(IReadOnlyList<AgentRunRecarriedFile> outFiles)
    {
        byte[] bundle = System.Text.Encoding.UTF8.GetBytes("bundle");

        return new AgentRunStartRequest(EXECUTION_ID, PROMPT, RUN_TOKEN)
        {
            Continuation = new AgentRunContinuation(bundle, Sha256Of(bundle), outFiles),
        };
    }

    private static AgentRunRecarriedFile RecarriedFile(string path, byte[]? content = null)
    {
        byte[] bytes = content ?? [1];

        return new AgentRunRecarriedFile(path, bytes, Sha256Of(bytes));
    }

    private static string Sha256Of(byte[] bytes)
    {
        return Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(bytes));
    }

    internal static string MonitorJson(string status, string executionId = EXECUTION_ID)
    {
        return $$"""
            {
              "executionId": "{{executionId}}",
              "attempt": 1,
              "segment": 0,
              "status": "{{status}}",
              "createdAt": "2026-08-03T10:00:00Z",
              "updatedAt": "2026-08-03T10:00:05Z",
              "estimatedCostUsd": 0.25,
              "error": null,
              "result": null,
              "requiredInput": null
            }
            """;
    }

    internal static string AwaitingInputMonitorJson(string executionId = EXECUTION_ID)
    {
        return $$"""
            {
              "executionId": "{{executionId}}",
              "attempt": 1,
              "segment": 1,
              "status": "awaiting_input",
              "createdAt": "2026-08-03T10:00:00Z",
              "updatedAt": "2026-08-03T10:03:00Z",
              "estimatedCostUsd": 0.75,
              "error": null,
              "result": null,
              "requiredInput": {
                "resumeToken": "resume-abc",
                "expiresAt": "2026-08-04T10:00:00Z",
                "question": {
                  "id": "q1",
                  "toolName": "select_fund",
                  "prompt": "Which fund?",
                  "input": { "draft": "Fund A", "candidates": ["Fund A", "Fund B"] }
                }
              },
              "pausedAt": "2026-08-03T10:03:00Z",
              "resumedAt": null,
              "lastPauseMs": null
            }
            """;
    }

    internal sealed record RecordedRequest(
        HttpMethod Method,
        string Uri,
        string? AuthScheme,
        string? AuthParameter,
        string? Body,
        long? ContentLength = null,
        bool TransferEncodingChunked = false,
        string? ContentType = null);

    /// <summary>
    /// Stub HTTP handler that records requests (including bodies) and returns configurable responses.
    /// </summary>
    internal sealed class StubHttpHandler : HttpMessageHandler
    {
        public string ResponseBody { get; set; } = "";
        public HttpStatusCode StatusCode { get; set; } = HttpStatusCode.OK;
        public int? RetryAfterSeconds { get; set; }
        public DateTimeOffset? RetryAfterDate { get; set; }
        public Exception? ThrowOnSend { get; set; }
        public List<RecordedRequest> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            // Snapshot the framing BEFORE touching the body: reading a streaming JsonContent
            // buffers it, and a buffered content reports a Content-Length it never sent.
            long? contentLength = request.Content?.Headers.ContentLength;
            bool chunked = request.Headers.TransferEncodingChunked ?? false;
            string? contentType = request.Content?.Headers.ContentType?.MediaType;

            string? body = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken);

            Requests.Add(new RecordedRequest(
                request.Method,
                request.RequestUri!.ToString(),
                request.Headers.Authorization?.Scheme,
                request.Headers.Authorization?.Parameter,
                body,
                contentLength,
                chunked,
                contentType));

            if (ThrowOnSend is not null)
            {
                throw ThrowOnSend;
            }

            var response = new HttpResponseMessage(StatusCode)
            {
                Content = new StringContent(ResponseBody),
            };

            if (RetryAfterSeconds is not null)
            {
                response.Headers.RetryAfter =
                    new RetryConditionHeaderValue(TimeSpan.FromSeconds(RetryAfterSeconds.Value));
            }

            if (RetryAfterDate is not null)
            {
                response.Headers.RetryAfter = new RetryConditionHeaderValue(RetryAfterDate.Value);
            }

            return response;
        }
    }
}
