using ErrorOr;
using IniaAi.Common.Interfaces.Services.AgentRuntime;
using IniaAi.Core.Tests.Workflows.Scheduling.Helpers;
using IniaAi.Core.Workflows;
using IniaAi.Domain.Entities.AgentRuntime;
using IniaAi.Domain.Entities.WorkflowExecutions;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties;
using IniaAi.Domain.Entities.Workflows.Enums;
using IniaAi.Infrastructure.Persistence.UserData.Contexts;
using IniaAi.Testing.DependencyInjection.Mocks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using static IniaAi.Core.Tests.Workflows.Scheduling.Helpers.VerifiedGroupRuns;
using Block = IniaAi.Domain.Entities.Workflows.Blocks.Block;

namespace IniaAi.Core.Tests.Workflows;

/// <summary>
/// A whole workflow run: an Agent block with a declared output file settles against the stubbed
/// runtime, its trace rows and attempt row persist, and a later block reads the answer and the
/// file through ordinary placeholders.
/// </summary>
public class AgentOutputFilesWorkflowRunTests : IClassFixture<WorkflowsFixture<AgentOutputFilesWorkflowRunTests>>
{
    private const string AGENT_TAG = "numbers_agent";
    private const string NUMBERS_FILE = """{"values":[1250.10,-3.5,7],"label":"Café"}""";
    private const string ANSWER = """{"status":"ok","accounts":[{"currency":"GBP"}]}""";

    private readonly IServiceProvider _sp;

    public AgentOutputFilesWorkflowRunTests(
        WorkflowsFixture<AgentOutputFilesWorkflowRunTests> fixture,
        ITestOutputHelper testOutputHelper)
    {
        _sp = fixture.BuildTestServiceProvider(testOutputHelper);
    }

    [Fact]
    public async Task ALaterBlock_ReadsTheAnswerAndTheFileThroughPlaceholders()
    {
        StubAgentRuntimeClient stub = ArrangeGroupRuns(
            _sp,
            verifierOutput: static _ => throw new InvalidOperationException("No verifier in this workflow."),
            workerOutput: static _ => ANSWER);
        byte[] fileBytes = Encoding.UTF8.GetBytes(NUMBERS_FILE);
        stub.OnListRunFiles = _ => ErrorOrFactory.From<IReadOnlyList<AgentRunFileEntry>>([
            new AgentRunFileEntry
            {
                Path = "out/numbers.json",
                SizeBytes = fileBytes.LongLength,
                Sha256 = Convert.ToHexStringLower(SHA256.HashData(fileBytes)),
                Kind = AgentRunFileKind.Artifact,
            }]);
        stub.OnGetRunFile = (_, _) => fileBytes;

        Workflow root = new() { Id = "outfiles-root", Title = "outfiles" };
        Section section = new("S", root) { Id = "outfiles-sec", Order = 0 };
        AgentProperties agentProps = WorkerProperties("Run the script.");
        agentProps.OutputFiles = ["out/numbers.json"];
        _ = new Block(BlockTypes.Agent, section, replacementTag: AGENT_TAG)
        {
            Id = "outfiles-agent",
            Name = "Numbers agent",
            Properties = agentProps,
        };
        _ = new Block(BlockTypes.StringManipulation, section, replacementTag: "numbers_reader")
        {
            Id = "outfiles-reader",
            Name = "Numbers reader",
            Properties = new StringManipulationProperties
            {
                InputTemplate = $"status={{{AGENT_TAG}.answer.status}} "
                    + $"currency={{{AGENT_TAG}.answer.accounts[0].currency}} second={{{AGENT_TAG}.files.numbers.values[1]}}",
                Find = "unused",
                ReplaceWith = "unused",
            },
        };
        _ = new Block(BlockTypes.StringManipulation, section, replacementTag: "file_reader")
        {
            Id = "outfiles-file-reader",
            Name = "File reader",
            Properties = new StringManipulationProperties
            {
                InputTemplate = $"{{{AGENT_TAG}.files.numbers}}",
                Find = "unused",
                ReplaceWith = "unused",
            },
        };
        _ = new Block(BlockTypes.StringManipulation, section, replacementTag: "whole_reader")
        {
            Id = "outfiles-whole-reader",
            Name = "Whole reader",
            Properties = new StringManipulationProperties
            {
                InputTemplate = $"{{{AGENT_TAG}}}",
                Find = "unused",
                ReplaceWith = "unused",
            },
        };

        WorkflowHandler handler = TestWorkflowHandlers.BuildWithScheduler(_sp, maxBlockParallelism: 2);
        await TestWorkflowRuns.RunWithOutputAsync(_sp, handler, root, TestContext.Current.CancellationToken);

        const string COMPOSED = "{\"answer\":" + ANSWER + ",\"files\":{\"numbers\":" + NUMBERS_FILE + "}}";
        Block agent = TestWorkflowRuns.ReadPersistedBlock(_sp, ReadPersistedBlockByName(_sp, "Numbers agent").Id);
        Assert.Equal(ExecutionStatus.Completed, agent.ExecutionState!.Status);
        Assert.Equal(COMPOSED, agent.BlockResponse!.RawResponse);

        IDbContextFactory<UserDataDbContext> factory = _sp.GetRequiredService<IDbContextFactory<UserDataDbContext>>();
        using (UserDataDbContext read = factory.CreateDbContext())
        {
            AgentRunOutput attempt = read.Set<AgentRunOutput>().AsNoTracking().Single(row => row.BlockId == agent.Id);
            Assert.Equal(COMPOSED, attempt.Output);
            AgentRunTraceFile traceFile = read.Set<AgentRunTraceFile>().AsNoTracking()
                .Single(row => row.BlockId == agent.Id && row.Path == "out/numbers.json");
            Assert.Equal(fileBytes, traceFile.Content);
        }

        Block reader = TestWorkflowRuns.ReadPersistedBlock(_sp, ReadPersistedBlockByName(_sp, "Numbers reader").Id);
        Assert.Equal(ExecutionStatus.Completed, reader.ExecutionState!.Status);
        Assert.Equal("status=ok currency=GBP second=-3.5", reader.BlockResponse!.RawResponse);

        // A sub-path placeholder re-serializes the object it selects: values, number text and
        // letters survive as written.
        Block fileReader = TestWorkflowRuns.ReadPersistedBlock(_sp, ReadPersistedBlockByName(_sp, "File reader").Id);
        string fileValue = fileReader.BlockResponse!.RawResponse!;
        Assert.Equal("""{"values":[1250.10,-3.5,7],"label":"Café"}""", fileValue);
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(NUMBERS_FILE), JsonNode.Parse(fileValue)));

        Block wholeReader = TestWorkflowRuns.ReadPersistedBlock(_sp, ReadPersistedBlockByName(_sp, "Whole reader").Id);
        Assert.Equal(COMPOSED, wholeReader.BlockResponse!.RawResponse);
    }
}
