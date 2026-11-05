using ErrorOr;
using IniaAi.Common.Interfaces.Services.AgentRuntime;
using IniaAi.Core.Workflows;
using IniaAi.Core.Workflows.Agents;
using IniaAi.Core.Workflows.Strategies;
using IniaAi.Domain.Entities.AgentRuntime;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Blocks;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties;
using IniaAi.Domain.Entities.Workflows.Enums;
using IniaAi.Testing.DependencyInjection.Mocks;
using Microsoft.Extensions.DependencyInjection;
using System.Security.Cryptography;
using System.Text;

namespace IniaAi.Core.Tests.Workflows.Strategies;

/// <summary>
/// The Agent block's declared output files, driven through the real executor and trace
/// collector against the stubbed runtime HTTP client: the out/ rows the composition reads are
/// the rows collection actually produced for each run.
/// </summary>
public class AgentBlockOutputFilesTests : IClassFixture<WorkflowsFixture<AgentBlockOutputFilesTests>>
{
    private const string WORKFLOW_EXECUTION_ID = "wfexec-outfiles";
    private const string ORGANIZATION_ID = "org-outfiles-tests";
    private const string MODEL = "claude-sonnet-4-6";
    private const string NUMBERS_PATH = "out/numbers.json";

    private readonly IServiceProvider _testServiceProvider;

    public AgentBlockOutputFilesTests(WorkflowsFixture<AgentBlockOutputFilesTests> fixture, ITestOutputHelper outputHelper)
    {
        _testServiceProvider = fixture.BuildTestServiceProvider(outputHelper);
    }

    /// <summary>
    /// Each run id gets its own answer and its own published out/ files, so which run's files
    /// reached the block output is readable off it.
    /// </summary>
    private sealed class RunScript
    {
        public Func<string, AgentRunMonitor> Settle { get; init; } = static _ => throw new InvalidOperationException();

        public Func<string, Dictionary<string, string>> FilesOf { get; init; } = static _ => [];
    }

    private (Block Block, BlockExecutionContext Context, StubAgentRuntimeClient Stub) Arrange(
        List<string> outputFiles,
        RunScript script,
        string? prompt = null)
    {
        var block = new Block(BlockTypes.Agent, new Section("test", new Workflow("test")), "numbers_agent")
        {
            Name = "Numbers agent",
            Properties = new AgentProperties
            {
                PromptTemplate = prompt ?? string.Empty,
                WorkerSkills = [new AgentWorkerSkill { Name = "skill", Description = "Handles skill.", Content = "Do it." }],
                Model = MODEL,
                OutputFiles = outputFiles,
            },
        };

        BlockExecutionContext context = _testServiceProvider.GetRequiredService<BlockExecutionContext>();
        var stub = Assert.IsType<StubAgentRuntimeClient>(_testServiceProvider.GetRequiredService<IAgentRuntimeClient>());
        stub.StartRequests.Clear();
        stub.OnStartRun = request => new AgentRunMonitor
        {
            ExecutionId = request.ExecutionId,
            Status = AgentRunStatus.Running,
            Attempt = 1,
            PollInterval = TimeSpan.Zero,
        };
        stub.OnGetRun = executionId => script.Settle(executionId);
        stub.OnCompleteRun = executionId => new AgentRunMonitor
        {
            ExecutionId = executionId,
            Status = AgentRunStatus.Succeeded,
            Attempt = 1,
        };
        stub.OnListRunFiles = executionId => ErrorOrFactory.From<IReadOnlyList<AgentRunFileEntry>>([..
            script.FilesOf(executionId).Select(static file =>
            {
                byte[] bytes = Encoding.UTF8.GetBytes(file.Value);
                return new AgentRunFileEntry
                {
                    Path = file.Key,
                    SizeBytes = bytes.LongLength,
                    Sha256 = Convert.ToHexStringLower(SHA256.HashData(bytes)),
                    Kind = file.Key.StartsWith(AgentRunFilePaths.ARTIFACT_PREFIX, StringComparison.Ordinal)
                        ? AgentRunFileKind.Artifact
                        : AgentRunFileKind.Trace,
                };
            })]);
        stub.OnGetRunFile = (executionId, path) => script.FilesOf(executionId).TryGetValue(path, out string? content)
            ? Encoding.UTF8.GetBytes(content)
            : Error.NotFound("AgentRun.NotFound", $"No such file: {path}");

        context.WorkflowExecutionId = WORKFLOW_EXECUTION_ID;
        context.OrganizationId = ORGANIZATION_ID;
        context.TextInputs = [];
        block.SetExecutionState(false, context.TextInputs);

        return (block, context, stub);
    }

    private static AgentRunMonitor Succeeded(string executionId, string output)
    {
        return new AgentRunMonitor
        {
            ExecutionId = executionId,
            Status = AgentRunStatus.Succeeded,
            Attempt = 1,
            Result = new AgentRunResult(output, null),
        };
    }

    private static RunScript OneRun(string answer, Dictionary<string, string> files)
    {
        return new RunScript
        {
            Settle = executionId => Succeeded(executionId, answer),
            FilesOf = _ => files,
        };
    }

    [Fact]
    public async Task NoOutputFiles_OutputAndPromptAreUnchanged_EvenWhenTheRunPublishedFiles()
    {
        (Block block, BlockExecutionContext context, StubAgentRuntimeClient stub) = Arrange(
            [], OneRun("plain answer", new() { [NUMBERS_PATH] = "[1,2,3]" }));

        _ = await new AgentBlockStrategy().ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);

        Assert.Equal("plain answer", block.BlockResponse!.RawResponse);
        Assert.Equal("plain answer", Assert.Single(block.AgentRunOutputs).Output);
        AgentRunStartRequest request = Assert.Single(stub.StartRequests);
        Assert.Equal(AgentRunBlockSupport.DEFAULT_WORKER_PROMPT, request.Prompt);
        Assert.Equal(string.Empty, ((AgentProperties)block.Properties).ExecutionState!.Prompt);
    }

    [Fact]
    public async Task OutputFiles_ComposeTheAnswerAndTheRunsPublishedFile_AndTheTaskNamesTheFile()
    {
        (Block block, BlockExecutionContext context, StubAgentRuntimeClient stub) = Arrange(
            [NUMBERS_PATH],
            OneRun("""{"status":"ok"}""", new() { [NUMBERS_PATH] = """{"values":[10,20.50,30]}""", ["trace/t.jsonl"] = "{}" }),
            prompt: "Run the script.");

        _ = await new AgentBlockStrategy().ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);

        const string EXPECTED = """{"answer":{"status":"ok"},"files":{"numbers":{"values":[10,20.50,30]}}}""";
        Assert.Equal(EXPECTED, block.BlockResponse!.RawResponse);
        AgentRunOutput attempt = Assert.Single(block.AgentRunOutputs);
        Assert.Equal(EXPECTED, attempt.Output);

        AgentRunStartRequest request = Assert.Single(stub.StartRequests);
        Assert.Equal("Run the script.\n\n" + AgentRunOutputFiles.TaskInstruction([NUMBERS_PATH]), request.Prompt);
        Assert.Equal(request.Prompt, ((AgentProperties)block.Properties).ExecutionState!.Prompt);
        Assert.Contains(block.AgentRunTraceFiles, file => file.Path == NUMBERS_PATH);
    }

    [Fact]
    public async Task OutputSchema_StillGoesToTheRuntimeUnchanged()
    {
        const string SCHEMA = """{"type":"object","properties":{"status":{"type":"string"}},"required":["status"]}""";
        (Block block, BlockExecutionContext context, StubAgentRuntimeClient stub) = Arrange(
            [NUMBERS_PATH], OneRun("""{"status":"ok"}""", new() { [NUMBERS_PATH] = "[1]" }));
        ((AgentProperties)block.Properties).OutputSchema = SCHEMA;
        block.SetExecutionState(false, context.TextInputs);

        _ = await new AgentBlockStrategy().ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);

        AgentRunStartRequest request = Assert.Single(stub.StartRequests);
        Assert.Equal(SCHEMA, request.OutputSchema!.ToJsonString());
    }

    [Fact]
    public async Task MissingFile_FailsTheBlockNamingTheFile_AfterSpendIsRecorded()
    {
        (Block block, BlockExecutionContext context, _) = Arrange(
            [NUMBERS_PATH], OneRun("ok", new() { ["out/other.json"] = "{}" }));

        InvalidOperationException failure = await Assert.ThrowsAsync<InvalidOperationException>(
            () => new AgentBlockStrategy().ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken));

        Assert.Contains(NUMBERS_PATH, failure.Message, StringComparison.Ordinal);
        Assert.Null(block.BlockResponse);
        Assert.Empty(block.AgentRunOutputs);
        AgentRunPass pass = Assert.Single(((AgentProperties)block.Properties).ExecutionState!.RunPasses);
        Assert.Equal(nameof(AgentRunStatus.Failed), pass.Status);
        Assert.Equal(AgentBlockStrategy.OUTPUT_FILES_UNUSABLE_SUBTYPE, pass.FailureSubtype);
        Assert.Null(AgentRunBlockSupport.SettledPassFor(block, 1));
        Assert.Null(AgentRunBlockSupport.LatestSettledPass(block));
    }

    [Fact]
    public async Task InvalidJsonFile_FailsTheBlockNamingTheFile()
    {
        (Block block, BlockExecutionContext context, _) = Arrange(
            [NUMBERS_PATH], OneRun("ok", new() { [NUMBERS_PATH] = "1,2,3" }));

        InvalidOperationException failure = await Assert.ThrowsAsync<InvalidOperationException>(
            () => new AgentBlockStrategy().ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken));

        Assert.Contains($"'{NUMBERS_PATH}' is not valid JSON", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task StemCollision_FailsBeforeAnyRunStarts()
    {
        (Block block, BlockExecutionContext context, StubAgentRuntimeClient stub) = Arrange(
            ["out/a/rows.json", "out/rows.json"], OneRun("ok", []));

        _ = await Assert.ThrowsAsync<InvalidOperationException>(
            () => new AgentBlockStrategy().ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken));

        Assert.Empty(stub.StartRequests);
    }

    [Fact]
    public async Task RetriedPass_UsesTheSettledPassesFiles_NotTheFailedPasses()
    {
        var script = new RunScript
        {
            Settle = executionId => executionId.EndsWith("_1", StringComparison.Ordinal)
                ? new AgentRunMonitor
                {
                    ExecutionId = executionId,
                    Status = AgentRunStatus.Failed,
                    Attempt = 1,
                    Error = new AgentRunError(AgentRunSubtypes.RUNTIME_SHUTDOWN),
                }
                : Succeeded(executionId, "second pass"),
            FilesOf = executionId => new() { [NUMBERS_PATH] = $"{{\"run\":\"{executionId[^1]}\"}}" },
        };
        (Block block, BlockExecutionContext context, StubAgentRuntimeClient stub) = Arrange([NUMBERS_PATH], script);

        _ = await new AgentBlockStrategy().ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);

        Assert.Equal(2, stub.StartRequests.Count);
        Assert.Equal(
            """{"answer":"second pass","files":{"numbers":{"run":"2"}}}""",
            block.BlockResponse!.RawResponse);
        Assert.Equal(2, block.AgentRunTraceFiles.Count(file => file.Path == NUMBERS_PATH));
    }

    [Fact]
    public async Task EachRoundsAttemptRow_CarriesItsOwnRunsFiles_SoARestoreBringsBackTheRightOnes()
    {
        var script = new RunScript
        {
            Settle = executionId => Succeeded(executionId, executionId.Contains("_r1_", StringComparison.Ordinal) ? "r1" : "r2"),
            FilesOf = executionId => new()
            {
                [NUMBERS_PATH] = executionId.Contains("_r1_", StringComparison.Ordinal) ? "[1]" : "[2]",
            },
        };
        (Block block, BlockExecutionContext context, _) = Arrange([NUMBERS_PATH], script);

        _ = await new AgentBlockStrategy().ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);
        context.VerificationRound = 2;
        _ = await new AgentBlockStrategy().ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);

        Assert.Equal("""{"answer":"r2","files":{"numbers":[2]}}""", block.BlockResponse!.RawResponse);

        AgentRunOutput roundOne = AgentRunBlockSupport.AttemptOutputFor(block, 1)!;
        AgentRunBlockSupport.SettleResponse(block, roundOne);

        Assert.Equal("""{"answer":"r1","files":{"numbers":[1]}}""", block.BlockResponse!.RawResponse);
    }
}
