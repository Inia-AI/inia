using ErrorOr;
using IniaAi.Common.Interfaces.Services.AgentRuntime;
using IniaAi.Infrastructure.Services;
using Microsoft.Extensions.Logging.Abstractions;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using RecordedRequest = IniaAi.Infrastructure.Tests.Services.AgentRuntimeClientTests.RecordedRequest;
using StubHttpHandler = IniaAi.Infrastructure.Tests.Services.AgentRuntimeClientTests.StubHttpHandler;

namespace IniaAi.Infrastructure.Tests.Services;

public class AgentRuntimeClientContentChannelTests : IDisposable
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
    public async Task StartRunAsync_WithFullRequest_PostsTheContentChannelBody()
    {
        _handler.StatusCode = HttpStatusCode.Accepted;
        _handler.ResponseBody = MonitorJson(status: "running");

        AgentRuntimeClient client = CreateClient();
        var request = new AgentRunStartRequest(EXECUTION_ID, PROMPT, RUN_TOKEN)
        {
            Documents =
            [
                new AgentRunDocument(
                    "loan-agreement.pdf",
                    new byte[] { 1, 2, 3 },
                    MediaType: "application/pdf",
                    DocumentClass: "LoanAgreement"),
            ],
            SkillFiles = [new AgentRunSkillFile("skill", "Do the work.", "Does the work. Use for the work.")],
            Model = "claude-sonnet-4-6",
            SystemPrompt = "You are meticulous.",
            OutputSchema = new JsonObject
            {
                ["type"] = "object",
                ["properties"] = new JsonObject { ["MarkerCode"] = new JsonObject { ["type"] = "string" } },
            },
            McpServerUrl = "https://platform.example/mcp",
        };

        ErrorOr<AgentRunMonitor> result = await client.StartRunAsync(request, TestContext.Current.CancellationToken);

        Assert.False(result.IsError, result.IsError ? result.FirstError.Description : "");
        RecordedRequest recorded = Assert.Single(_handler.Requests);
        Assert.Equal(HttpMethod.Post, recorded.Method);
        Assert.Equal(RUNS_URL, recorded.Uri);

        using JsonDocument body = JsonDocument.Parse(recorded.Body!);
        JsonElement root = body.RootElement;
        Assert.Equal(EXECUTION_ID, root.GetProperty("executionId").GetString());
        Assert.Equal(PROMPT, root.GetProperty("prompt").GetString());
        Assert.Equal(RUN_TOKEN, root.GetProperty("runToken").GetString());
        Assert.Equal("claude-sonnet-4-6", root.GetProperty("model").GetString());
        Assert.Equal("You are meticulous.", root.GetProperty("systemPrompt").GetString());
        JsonElement outputFormat = root.GetProperty("outputFormat");
        Assert.Equal("json_schema", outputFormat.GetProperty("type").GetString());
        Assert.Equal("object", outputFormat.GetProperty("schema").GetProperty("type").GetString());
        Assert.True(
            outputFormat.GetProperty("schema").GetProperty("properties").TryGetProperty("MarkerCode", out _),
            "schema keys must pass through with their original casing");
        Assert.Equal("https://platform.example/mcp", root.GetProperty("mcpServerUrl").GetString());

        JsonElement document = Assert.Single(root.GetProperty("documents").EnumerateArray());
        Assert.Equal("loan-agreement.pdf", document.GetProperty("name").GetString());
        Assert.Equal(Convert.ToBase64String(new byte[] { 1, 2, 3 }), document.GetProperty("contentBase64").GetString());
        Assert.Equal("application/pdf", document.GetProperty("mediaType").GetString());
        Assert.Equal("LoanAgreement", document.GetProperty("documentClass").GetString());

        JsonElement skill = Assert.Single(root.GetProperty("skillFiles").EnumerateArray());
        Assert.Equal("skill", skill.GetProperty("name").GetString());
        Assert.Equal(
            Convert.ToBase64String(Encoding.UTF8.GetBytes("Do the work.")),
            skill.GetProperty("contentBase64").GetString());
    }

    [Fact]
    public async Task StartRunAsync_OmitsNullOptionalsInsteadOfSerializingThem()
    {
        _handler.StatusCode = HttpStatusCode.Accepted;
        _handler.ResponseBody = MonitorJson(status: "running");

        AgentRuntimeClient client = CreateClient();
        var request = new AgentRunStartRequest(EXECUTION_ID, PROMPT, RunToken: null)
        {
            Documents = [new AgentRunDocument("plain.txt", new byte[] { 1 })],
        };

        ErrorOr<AgentRunMonitor> result = await client.StartRunAsync(request, TestContext.Current.CancellationToken);

        Assert.False(result.IsError, result.IsError ? result.FirstError.Description : "");
        RecordedRequest recorded = Assert.Single(_handler.Requests);

        using JsonDocument body = JsonDocument.Parse(recorded.Body!);
        JsonElement root = body.RootElement;
        Assert.False(root.TryGetProperty("runToken", out _));
        Assert.False(root.TryGetProperty("model", out _));
        Assert.False(root.TryGetProperty("mcpServerUrl", out _));
        Assert.False(root.TryGetProperty("systemPrompt", out _));
        Assert.False(root.TryGetProperty("outputFormat", out _));

        JsonElement document = Assert.Single(root.GetProperty("documents").EnumerateArray());
        Assert.False(document.TryGetProperty("mediaType", out _));
        Assert.False(document.TryGetProperty("documentClass", out _));
    }

    [Fact]
    public async Task StartRunAsync_WithDeclaredFuses_RidesThemOnTheWire()
    {
        _handler.StatusCode = HttpStatusCode.Accepted;
        _handler.ResponseBody = MonitorJson(status: "running");

        AgentRuntimeClient client = CreateClient();
        var request = new AgentRunStartRequest(EXECUTION_ID, PROMPT, RUN_TOKEN)
        {
            DeadlineMs = 300_000,
            MaxBudgetUsd = 0.75m,
            MaxTurns = 40,
            MaxRetainedOutputBytes = 8 * 1024 * 1024,
        };

        ErrorOr<AgentRunMonitor> result = await client.StartRunAsync(request, TestContext.Current.CancellationToken);

        Assert.False(result.IsError, result.IsError ? result.FirstError.Description : "");
        RecordedRequest recorded = Assert.Single(_handler.Requests);

        using JsonDocument body = JsonDocument.Parse(recorded.Body!);
        JsonElement root = body.RootElement;
        Assert.Equal(300_000, root.GetProperty("deadlineMs").GetInt32());
        Assert.Equal(0.75m, root.GetProperty("maxBudgetUsd").GetDecimal());
        Assert.Equal(40, root.GetProperty("maxTurns").GetInt32());
        Assert.Equal(8 * 1024 * 1024, root.GetProperty("maxRetainedOutputBytes").GetInt64());
    }

    [Fact]
    public async Task StartRunAsync_WithUndeclaredFuses_OmitsThemSoTheRuntimeDefaultGoverns()
    {
        _handler.StatusCode = HttpStatusCode.Accepted;
        _handler.ResponseBody = MonitorJson(status: "running");

        AgentRuntimeClient client = CreateClient();
        var request = new AgentRunStartRequest(EXECUTION_ID, PROMPT, RUN_TOKEN);

        ErrorOr<AgentRunMonitor> result = await client.StartRunAsync(request, TestContext.Current.CancellationToken);

        Assert.False(result.IsError, result.IsError ? result.FirstError.Description : "");
        RecordedRequest recorded = Assert.Single(_handler.Requests);

        using JsonDocument body = JsonDocument.Parse(recorded.Body!);
        JsonElement root = body.RootElement;
        Assert.False(root.TryGetProperty("deadlineMs", out _));
        Assert.False(root.TryGetProperty("maxBudgetUsd", out _));
        Assert.False(root.TryGetProperty("maxTurns", out _));
        Assert.False(root.TryGetProperty("maxRetainedOutputBytes", out _));
    }

    [Fact]
    public async Task StartRunAsync_WithABlankSystemPrompt_FailsWithoutHttp()
    {
        AgentRuntimeClient client = CreateClient();
        var request = new AgentRunStartRequest(EXECUTION_ID, PROMPT, RUN_TOKEN) { SystemPrompt = "   " };

        ErrorOr<AgentRunMonitor> result = await client.StartRunAsync(request, TestContext.Current.CancellationToken);

        Assert.True(result.IsError);
        Assert.Equal(ErrorType.Validation, result.FirstError.Type);
        Assert.Empty(_handler.Requests);
    }

    [Fact]
    public async Task StartRunAsync_WithAnOversizedOutputSchema_FailsWithoutHttp()
    {
        AgentRuntimeClient client = CreateClient();
        var request = new AgentRunStartRequest(EXECUTION_ID, PROMPT, RUN_TOKEN)
        {
            OutputSchema = new JsonObject { ["description"] = new string('a', 256 * 1024) },
        };

        ErrorOr<AgentRunMonitor> result = await client.StartRunAsync(request, TestContext.Current.CancellationToken);

        Assert.True(result.IsError);
        Assert.Equal(ErrorType.Validation, result.FirstError.Type);
        Assert.Empty(_handler.Requests);
    }

    [Fact]
    public async Task StartRunAsync_WithAnUnknownKeywordOutputSchema_FailsWithoutHttp_NamingThePath()
    {
        AgentRuntimeClient client = CreateClient();
        var request = new AgentRunStartRequest(EXECUTION_ID, PROMPT, RUN_TOKEN)
        {
            OutputSchema = new JsonObject
            {
                ["type"] = "object",
                ["properties"] = new JsonObject
                {
                    ["total"] = new JsonObject { ["type"] = "number", ["minmum"] = 0 },
                },
            },
        };

        ErrorOr<AgentRunMonitor> result = await client.StartRunAsync(request, TestContext.Current.CancellationToken);

        Assert.True(result.IsError);
        Assert.Equal(ErrorType.Validation, result.FirstError.Type);
        Assert.Contains("#/properties/total", result.FirstError.Description);
        Assert.Contains("minmum", result.FirstError.Description);
        Assert.Empty(_handler.Requests);
    }

    [Fact]
    public async Task StartRunAsync_WithADuplicateNameAcrossDocumentsAndSkills_FailsWithoutHttp()
    {
        AgentRuntimeClient client = CreateClient();
        var request = new AgentRunStartRequest(EXECUTION_ID, PROMPT, RUN_TOKEN)
        {
            Documents = [new AgentRunDocument("shared-name.md", new byte[] { 1 })],
            SkillFiles = [new AgentRunSkillFile("shared-name.md", "content", "Shared name.")],
        };

        ErrorOr<AgentRunMonitor> result = await client.StartRunAsync(request, TestContext.Current.CancellationToken);

        Assert.True(result.IsError);
        Assert.Equal("AgentRun.InvalidDocument", result.FirstError.Code);
        Assert.Empty(_handler.Requests);
    }

    [Theory]
    [InlineData("nested/path.md")]
    [InlineData("manifest.json")]
    [InlineData(".")]
    [InlineData("..")]
    [InlineData("/etc/passwd")]
    [InlineData("nul\0byte.md")]
    [InlineData("line\nbreak.md")]
    public async Task StartRunAsync_WithAnIllegalOrReservedName_FailsWithoutHttp(string name)
    {
        AgentRuntimeClient client = CreateClient();
        var request = new AgentRunStartRequest(EXECUTION_ID, PROMPT, RUN_TOKEN)
        {
            SkillFiles = [new AgentRunSkillFile(name, "content", "Named skill.")],
        };

        ErrorOr<AgentRunMonitor> result = await client.StartRunAsync(request, TestContext.Current.CancellationToken);

        Assert.True(result.IsError);
        Assert.Equal("AgentRun.InvalidDocument", result.FirstError.Code);
        Assert.Empty(_handler.Requests);
    }

    [Theory]
    [InlineData("../escape.pdf")]
    [InlineData("/etc/passwd")]
    [InlineData("nul\0byte.pdf")]
    [InlineData("line\nbreak.pdf")]
    [InlineData("trailing-newline.pdf\n")]
    public async Task StartRunAsync_WithADocumentNameThatIsNotAnOrdinarySegment_FailsWithoutHttp(string name)
    {
        AgentRuntimeClient client = CreateClient();
        var request = new AgentRunStartRequest(EXECUTION_ID, PROMPT, RUN_TOKEN)
        {
            Documents = [new AgentRunDocument(name, new byte[] { 1 })],
        };

        ErrorOr<AgentRunMonitor> result = await client.StartRunAsync(request, TestContext.Current.CancellationToken);

        Assert.True(result.IsError);
        Assert.Equal("AgentRun.InvalidDocument", result.FirstError.Code);
        Assert.Empty(_handler.Requests);
    }

    [Theory]
    [InlineData("nested/path.py")]
    [InlineData("manifest.json")]
    [InlineData(".")]
    [InlineData("..")]
    [InlineData("../escape.py")]
    public async Task StartRunAsync_WithAnIllegalOrReservedAssetName_FailsWithoutHttp(string assetName)
    {
        AgentRuntimeClient client = CreateClient();
        var request = new AgentRunStartRequest(EXECUTION_ID, PROMPT, RUN_TOKEN)
        {
            SkillFiles =
            [
                new AgentRunSkillFile("extraction", "content", "Named skill.")
                {
                    Assets = [new AgentRunSkillAsset(assetName, "print(1)")],
                },
            ],
        };

        ErrorOr<AgentRunMonitor> result = await client.StartRunAsync(request, TestContext.Current.CancellationToken);

        Assert.True(result.IsError);
        Assert.Equal("AgentRun.InvalidDocument", result.FirstError.Code);
        Assert.Empty(_handler.Requests);
    }

    [Fact]
    public async Task StartRunAsync_WithADuplicateAssetNameInOneSkill_FailsWithoutHttp()
    {
        AgentRuntimeClient client = CreateClient();
        var request = new AgentRunStartRequest(EXECUTION_ID, PROMPT, RUN_TOKEN)
        {
            SkillFiles =
            [
                new AgentRunSkillFile("extraction", "content", "Named skill.")
                {
                    Assets =
                    [
                        new AgentRunSkillAsset("verify.py", "print(1)"),
                        new AgentRunSkillAsset("verify.py", "print(2)"),
                    ],
                },
            ],
        };

        ErrorOr<AgentRunMonitor> result = await client.StartRunAsync(request, TestContext.Current.CancellationToken);

        Assert.True(result.IsError);
        Assert.Equal("AgentRun.InvalidDocument", result.FirstError.Code);
        Assert.Empty(_handler.Requests);
    }

    [Fact]
    public async Task StartRunAsync_WithMoreAssetsThanTheRuntimeCap_FailsWithoutHttp()
    {
        AgentRuntimeClient client = CreateClient();
        var request = new AgentRunStartRequest(EXECUTION_ID, PROMPT, RUN_TOKEN)
        {
            SkillFiles =
            [
                new AgentRunSkillFile("extraction", "content", "Named skill.")
                {
                    Assets = [.. Enumerable.Range(0, 17)
                        .Select(static index => new AgentRunSkillAsset($"asset-{index}.py", "print(1)"))],
                },
            ],
        };

        ErrorOr<AgentRunMonitor> result = await client.StartRunAsync(request, TestContext.Current.CancellationToken);

        Assert.True(result.IsError);
        Assert.Equal(ErrorType.Validation, result.FirstError.Type);
        Assert.Contains("16", result.FirstError.Description);
        Assert.Empty(_handler.Requests);
    }

    [Fact]
    public async Task StartRunAsync_AssetBytesCountAgainstTheSharedBudget_FailsWithoutHttp()
    {
        AgentRuntimeClient client = CreateClient();
        // Document plus skill content sit one byte under the budget; the two asset bytes are
        // what tip it over, so a pass here would mean asset bytes were never counted.
        var request = new AgentRunStartRequest(EXECUTION_ID, PROMPT, RUN_TOKEN)
        {
            Documents = [new AgentRunDocument("huge.bin", new byte[200 * 1024 * 1024 - "content".Length - 1])],
            SkillFiles =
            [
                new AgentRunSkillFile("extraction", "content", "Named skill.")
                {
                    Assets = [new AgentRunSkillAsset("tipping-point.py", "xx")],
                },
            ],
        };

        ErrorOr<AgentRunMonitor> result = await client.StartRunAsync(request, TestContext.Current.CancellationToken);

        Assert.True(result.IsError);
        Assert.Equal("AgentRun.DocumentsOverBudget", result.FirstError.Code);
        Assert.Empty(_handler.Requests);
    }

    [Fact]
    public async Task StartRunAsync_OverTheDecodedByteBudget_FailsWithoutHttp()
    {
        AgentRuntimeClient client = CreateClient();
        var request = new AgentRunStartRequest(EXECUTION_ID, PROMPT, RUN_TOKEN)
        {
            Documents = [new AgentRunDocument("huge.bin", new byte[200 * 1024 * 1024 + 1])],
        };

        ErrorOr<AgentRunMonitor> result = await client.StartRunAsync(request, TestContext.Current.CancellationToken);

        Assert.True(result.IsError);
        Assert.Equal("AgentRun.DocumentsOverBudget", result.FirstError.Code);
        Assert.Empty(_handler.Requests);
    }

    [Fact]
    public async Task StartRunAsync_PayloadConflict_MapsToTheDistinctTypedError()
    {
        _handler.StatusCode = HttpStatusCode.Conflict;
        _handler.ResponseBody = """{ "code": "payload_conflict", "status": "running" }""";

        AgentRuntimeClient client = CreateClient();
        var request = new AgentRunStartRequest(EXECUTION_ID, PROMPT, RUN_TOKEN)
        {
            Documents = [new AgentRunDocument("other.pdf", new byte[] { 9 })],
        };

        ErrorOr<AgentRunMonitor> result = await client.StartRunAsync(request, TestContext.Current.CancellationToken);

        Assert.True(result.IsError);
        Assert.Equal(ErrorType.Conflict, result.FirstError.Type);
        Assert.Equal("AgentRun.PayloadConflict", result.FirstError.Code);
    }

    [Fact]
    public async Task ListRunFilesAsync_ParsesTheManifestIncludingKinds()
    {
        _handler.StatusCode = HttpStatusCode.OK;
        _handler.ResponseBody = """
            {
              "files": [
                { "path": "out/report.json", "sizeBytes": 512, "sha256": "abc123", "kind": "artifact" },
                { "path": "trace/run.jsonl", "sizeBytes": 2048, "sha256": "def456", "kind": "trace" },
                { "path": "out/new-thing.bin", "sizeBytes": 1, "sha256": "0ff", "kind": "hologram" }
              ]
            }
            """;

        AgentRuntimeClient client = CreateClient();

        ErrorOr<IReadOnlyList<AgentRunFileEntry>> result = await client.ListRunFilesAsync(
            EXECUTION_ID, TestContext.Current.CancellationToken);

        Assert.False(result.IsError, result.IsError ? result.FirstError.Description : "");
        RecordedRequest recorded = Assert.Single(_handler.Requests);
        Assert.Equal($"{RUNS_URL}/{EXECUTION_ID}/files", recorded.Uri);

        Assert.Equal(3, result.Value.Count);
        Assert.Equal(
            new AgentRunFileEntry { Path = "out/report.json", SizeBytes = 512, Sha256 = "abc123", Kind = AgentRunFileKind.Artifact },
            result.Value[0]);
        Assert.Equal(
            new AgentRunFileEntry { Path = "trace/run.jsonl", SizeBytes = 2048, Sha256 = "def456", Kind = AgentRunFileKind.Trace },
            result.Value[1]);
        Assert.Equal(AgentRunFileKind.Unknown, result.Value[2].Kind);
    }

    [Fact]
    public async Task ListRunFilesAsync_OnACompletedRun_MapsGoneToTheCompletedError()
    {
        _handler.StatusCode = HttpStatusCode.Gone;
        _handler.ResponseBody = """{ "code": "completed" }""";

        AgentRuntimeClient client = CreateClient();

        ErrorOr<IReadOnlyList<AgentRunFileEntry>> result = await client.ListRunFilesAsync(
            EXECUTION_ID, TestContext.Current.CancellationToken);

        Assert.True(result.IsError);
        Assert.Equal(ErrorType.Conflict, result.FirstError.Type);
        Assert.Equal("AgentRun.Completed", result.FirstError.Code);
    }

    [Fact]
    public async Task GetRunFileAsync_ReturnsRawBytesAndEscapesPathSegments()
    {
        _handler.StatusCode = HttpStatusCode.OK;
        _handler.ResponseBody = "raw file payload";

        AgentRuntimeClient client = CreateClient();

        ErrorOr<byte[]> result = await client.GetRunFileAsync(
            EXECUTION_ID, "out/report#1.json", TestContext.Current.CancellationToken);

        Assert.False(result.IsError, result.IsError ? result.FirstError.Description : "");
        Assert.Equal("raw file payload", Encoding.UTF8.GetString(result.Value));
        RecordedRequest recorded = Assert.Single(_handler.Requests);
        Assert.Equal($"{RUNS_URL}/{EXECUTION_ID}/files/out/report%231.json", recorded.Uri);
    }

    [Fact]
    public async Task GetRunFileAsync_UnknownPath_IsNotFound()
    {
        _handler.StatusCode = HttpStatusCode.NotFound;
        _handler.ResponseBody = """{ "error": "not found" }""";

        AgentRuntimeClient client = CreateClient();

        ErrorOr<byte[]> result = await client.GetRunFileAsync(
            EXECUTION_ID, "out/missing.json", TestContext.Current.CancellationToken);

        Assert.True(result.IsError);
        Assert.Equal(ErrorType.NotFound, result.FirstError.Type);
    }

    [Fact]
    public async Task GetRunFileAsync_WithAnEmptyPath_FailsWithoutHttp()
    {
        AgentRuntimeClient client = CreateClient();

        ErrorOr<byte[]> result = await client.GetRunFileAsync(
            EXECUTION_ID, "  ", TestContext.Current.CancellationToken);

        Assert.True(result.IsError);
        Assert.Equal(ErrorType.Validation, result.FirstError.Type);
        Assert.Empty(_handler.Requests);
    }

    [Theory]
    [InlineData("../exec-2/files/report.json")]
    [InlineData("out/../../exec-2/files/report.json")]
    [InlineData("./report.json")]
    [InlineData("out//report.json")]
    [InlineData("out/")]
    public async Task GetRunFileAsync_WithDotOrEmptySegments_FailsWithoutHttp(string path)
    {
        AgentRuntimeClient client = CreateClient();

        ErrorOr<byte[]> result = await client.GetRunFileAsync(
            EXECUTION_ID, path, TestContext.Current.CancellationToken);

        Assert.True(result.IsError);
        Assert.Equal(ErrorType.Validation, result.FirstError.Type);
        Assert.Empty(_handler.Requests);
    }

    [Fact]
    public async Task CompleteRunAsync_PostsCompleteAndReturnsTheMonitor()
    {
        _handler.StatusCode = HttpStatusCode.OK;
        _handler.ResponseBody = MonitorJson(status: "succeeded");

        AgentRuntimeClient client = CreateClient();

        ErrorOr<AgentRunMonitor> result = await client.CompleteRunAsync(
            EXECUTION_ID, TestContext.Current.CancellationToken);

        Assert.False(result.IsError, result.IsError ? result.FirstError.Description : "");
        Assert.Equal(AgentRunStatus.Succeeded, result.Value.Status);
        RecordedRequest recorded = Assert.Single(_handler.Requests);
        Assert.Equal(HttpMethod.Post, recorded.Method);
        Assert.Equal($"{RUNS_URL}/{EXECUTION_ID}/complete", recorded.Uri);
    }

    [Fact]
    public async Task CompleteRunAsync_OnANonTerminalRun_MapsNotCompletableWithStatusMetadata()
    {
        _handler.StatusCode = HttpStatusCode.Conflict;
        _handler.ResponseBody = """{ "code": "not_completable", "status": "running" }""";

        AgentRuntimeClient client = CreateClient();

        ErrorOr<AgentRunMonitor> result = await client.CompleteRunAsync(
            EXECUTION_ID, TestContext.Current.CancellationToken);

        Assert.True(result.IsError);
        Assert.Equal(ErrorType.Conflict, result.FirstError.Type);
        Assert.Equal("AgentRun.NotCompletable", result.FirstError.Code);
        Assert.Equal("running", result.FirstError.Metadata?["status"]);
    }

    private AgentRuntimeClient CreateClient()
    {
        var httpClient = new HttpClient(_handler) { BaseAddress = new Uri($"{BASE_URL}/") };
        httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", AUTH_TOKEN);

        return new AgentRuntimeClient(httpClient, NullLogger<AgentRuntimeClient>.Instance);
    }

    private static string MonitorJson(string status)
    {
        return $$"""
            {
              "executionId": "{{EXECUTION_ID}}",
              "status": "{{status}}",
              "attempt": 1,
              "estimatedCostUsd": 0.25,
              "pausedAt": null,
              "resumedAt": null,
              "lastPauseMs": null
            }
            """;
    }
}
