using IniaAi.Common.Interfaces.Services.AgentRuntime;
using IniaAi.Core.Tests.Workflows.Scheduling.Helpers;
using IniaAi.Core.Workflows;
using IniaAi.Domain.Entities.WorkflowExecutions;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Blocks.ExecutionStates.Abstractions;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties.Abstractions;
using IniaAi.Domain.Entities.Workflows.Enums;
using IniaAi.Testing.DependencyInjection.Mocks;
using static IniaAi.Core.Tests.Workflows.Scheduling.Helpers.VerifiedGroupRuns;
using Block = IniaAi.Domain.Entities.Workflows.Blocks.Block;

namespace IniaAi.Core.Tests.Workflows;

/// <summary>
/// A run on a deployment built without the paid modules. Json2Xlsx, PeerFinancials and
/// Verification blocks stay in its workflows as stubs: each fails with a message that names its
/// type by the editor's label, and the run goes on. The members of a Verification group run as plain Agent blocks, and
/// their work reaches the report.
/// </summary>
public class OpenBuildRunTests : IClassFixture<WorkflowsFixture<OpenBuildRunTests>>
{
    private const string INCOME_TAG = "income_agent";
    private const string BALANCE_TAG = "balance_agent";

    private readonly IServiceProvider _sp;

    public OpenBuildRunTests(WorkflowsFixture<OpenBuildRunTests> fixture, ITestOutputHelper testOutputHelper)
    {
        _sp = fixture.BuildTestServiceProvider(testOutputHelper, withoutClosedModules: true);
    }

    [Theory]
    [InlineData(BlockTypes.Json2Xlsx)]
    [InlineData(BlockTypes.PeerFinancials)]
    [InlineData(BlockTypes.Verification)]
    public async Task AModuleBlock_FailsWithTheStubMessage_AndTheRunGoesOn(BlockTypes type)
    {
        string prefix = $"stub-{type}";
        Workflow root = new() { Id = $"{prefix}-root", Title = prefix };
        Section section = new("S", root) { Id = $"{prefix}-sec", Order = 0 };
        _ = new Block(type, section, replacementTag: $"{prefix}_stub")
        {
            Id = $"{prefix}-stub",
            Name = $"{prefix} stub",
            Properties = StoredPropertiesOf(type),
        };
        _ = new Block(BlockTypes.StringManipulation, section, replacementTag: $"{prefix}_text")
        {
            Id = $"{prefix}-text",
            Name = $"{prefix} text",
            Properties = new StringManipulationProperties
            {
                InputTemplate = "closed text",
                Find = "closed",
                ReplaceWith = "open",
            },
        };

        WorkflowHandler handler = TestWorkflowHandlers.BuildWithScheduler(_sp, maxBlockParallelism: 2);
        await TestWorkflowRuns.RunWithOutputAsync(_sp, handler, root, TestContext.Current.CancellationToken);

        Block stub = ReadPersistedBlockByName(_sp, $"{prefix} stub");
        Assert.Equal(ExecutionStatus.Failed, stub.ExecutionState!.Status);
        Assert.Equal(UnavailableBlockTypes.RunMessage(type), stub.ExecutionState.Error);
        Assert.Contains($"does not include {BlockTypeLabels.Of(type)} blocks", stub.ExecutionState.Error, StringComparison.Ordinal);
        Assert.Contains("Remove the block if the workflow does not need it.", stub.ExecutionState.Error, StringComparison.Ordinal);

        Block text = ReadPersistedBlockByName(_sp, $"{prefix} text");
        Assert.Equal(ExecutionStatus.Completed, text.ExecutionState!.Status);
    }

    [Fact]
    public async Task AVerificationGroup_RunsItsMembersAsPlainAgentBlocks_AndTheirWorkReachesTheReport()
    {
        StubAgentRuntimeClient stub = ArrangeGroupRuns(
            _sp,
            verifierOutput: static _ => throw new InvalidOperationException("The open build has no verifier run."));

        WorkflowHandler handler = TestWorkflowHandlers.BuildWithScheduler(_sp, maxBlockParallelism: 4);
        await TestWorkflowRuns.RunWithOutputAsync(
            _sp, handler, BuildGroupWorkflow("open-group"), TestContext.Current.CancellationToken);

        List<AgentRunStartRequest> started = [.. stub.StartRequests];
        Assert.Equal(2, started.Count);
        Assert.All(started, request => Assert.DoesNotContain("_v_", request.ExecutionId, StringComparison.Ordinal));

        foreach (string memberName in new[] { "Income agent", "Balance agent" })
        {
            Block member = ReadPersistedBlockByName(_sp, memberName);
            Assert.Equal(ExecutionStatus.Completed, member.ExecutionState!.Status);
            Assert.Contains("member deliverable", ReadParagraph(_sp, member.Id).Content, StringComparison.Ordinal);
        }

        Block verifier = ReadPersistedBlockByName(_sp, "Verifier");
        Assert.Equal(ExecutionStatus.Failed, verifier.ExecutionState!.Status);
        Assert.Equal(
            "The platform does not include Verification Group blocks, so this block did not run. Remove the block if the workflow does not need it. The full edition includes Verification Group blocks.",
            verifier.ExecutionState.Error);

        Block reader = TestWorkflowRuns.ReadPersistedBlock(_sp, ReadPersistedBlockByName(_sp, "Income reader").Id);
        Assert.Equal(ExecutionStatus.Completed, reader.ExecutionState!.Status);
        Assert.Contains("member deliverable", reader.BlockResponse?.RawResponse, StringComparison.Ordinal);
    }

    private static BlockProperties StoredPropertiesOf(BlockTypes type)
    {
        return type switch
        {
            BlockTypes.Json2Xlsx => new Json2XlsxProperties(),
            BlockTypes.PeerFinancials => new PeerFinancialsProperties(),
            BlockTypes.Verification => new VerificationProperties(),
            _ => throw new ArgumentOutOfRangeException(nameof(type)),
        };
    }

    private static Workflow BuildGroupWorkflow(string idPrefix)
    {
        Workflow root = new() { Id = $"{idPrefix}-root", Title = idPrefix };
        Section section = new("S", root) { Id = $"{idPrefix}-sec", Order = 0 };

        _ = new Block(BlockTypes.Agent, section, replacementTag: INCOME_TAG)
        {
            Id = $"{idPrefix}-inc",
            Name = "Income agent",
            IsOutputSource = true,
            Properties = WorkerProperties("Extract the income figures."),
        };
        _ = new Block(BlockTypes.Agent, section, replacementTag: BALANCE_TAG)
        {
            Id = $"{idPrefix}-bal",
            Name = "Balance agent",
            IsOutputSource = true,
            Properties = WorkerProperties("Extract the balance figures."),
        };
        _ = new Block(BlockTypes.Verification, section, replacementTag: $"{idPrefix}_verifier")
        {
            Id = $"{idPrefix}-ver",
            Name = "Verifier",
            Properties = new VerificationProperties
            {
                Members =
                [
                    new VerificationMember { MemberTag = INCOME_TAG, RedoMode = VerificationRedoMode.Revise },
                    new VerificationMember { MemberTag = BALANCE_TAG, RedoMode = VerificationRedoMode.Scratch },
                ],
                Criteria =
                [
                    new VerificationCriterion
                    {
                        Id = "figures-cited",
                        Label = "Every figure cites its page",
                        AppliesTo = [INCOME_TAG, BALANCE_TAG],
                    },
                ],
                WorkerSkills =
                [
                    new AgentWorkerSkill { Name = "judge", Description = "Judges the group.", Content = "judge" },
                ],
                Model = MODEL,
            },
        };
        _ = new Block(BlockTypes.StringManipulation, section, replacementTag: "income_reader")
        {
            Id = $"{idPrefix}-reader",
            Name = "Income reader",
            Properties = new StringManipulationProperties
            {
                InputTemplate = $"{{{INCOME_TAG}}}",
                Find = "unused",
                ReplaceWith = "unused",
            },
        };

        return root;
    }
}
