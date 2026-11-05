using IniaAi.Configuration;
using IniaAi.Core.Tests.Workflows.Helpers;
using IniaAi.Core.Tests.Workflows.Scheduling.Helpers;
using IniaAi.Core.Workflows;
using IniaAi.Core.Workflows.Interfaces;
using IniaAi.Core.Workflows.Strategies;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Blocks;
using IniaAi.Domain.Entities.Workflows.Blocks.ExecutionStates.Abstractions;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties;
using IniaAi.Domain.Entities.Workflows.Enums;
using IniaAi.Domain.Entities.Workflows.Responses;
using IniaAi.Domain.UserData.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace IniaAi.Core.Tests.Workflows;

/// <summary>
/// Dispatch-level coverage for the agent-template grammar: a tag referenced ONLY inside a worker
/// skill (invisible to reflection discovery) still reaches the strategy resolved in
/// <c>context.TextInputs</c>, an unresolvable tag arrives as the NoData sentinel, and
/// strategy-supplied namespaces are never stamped. Runs the real handler and scheduler — the
/// recording strategy is only the observation point at the dispatch seam.
/// </summary>
public class AgentBlockDispatchInputsTests : IClassFixture<WorkflowsFixture<AgentBlockDispatchInputsTests>>
{
    private const string UPSTREAM_TAG = "upstream_tag";
    private const string UPSTREAM_OUTPUT = "Portfolio is healthy";
    private const string MISSING_TAG = "missing_tag";

    private const string SKILL_CONTENT = """
        Read {{upstream_tag}} and {{missing_tag}}.
        Then consult {{Validator.decision}} before finishing.
        Single-brace stays inert: {ClientName} and {"total": 0}.
        """;

    private readonly WorkflowsFixture<AgentBlockDispatchInputsTests> _fixture;
    private readonly IServiceProvider _sp;

    public AgentBlockDispatchInputsTests(
        WorkflowsFixture<AgentBlockDispatchInputsTests> fixture,
        ITestOutputHelper testOutputHelper)
    {
        _fixture = fixture;
        _sp = fixture.BuildTestServiceProvider(testOutputHelper);
    }

    private sealed class RecordingAgentStrategy : IBlockStrategy
    {
        public Dictionary<string, string?>? CapturedTextInputs { get; private set; }

        public Task<BlockStrategyOutput> ExecuteBlockAsync(
            Block block,
            BlockExecutionContext context,
            CancellationToken cancellationToken = default)
        {
            block.ExecutionState ??= new BlockExecutionState(block);
            CapturedTextInputs = new Dictionary<string, string?>(context.TextInputs);
            _ = new BlockResponse(block) { RawResponse = "agent-ran" };
            return Task.FromResult(new BlockStrategyOutput());
        }
    }

    [Fact]
    public async Task SkillOnlyReference_ArrivesResolvedInDispatchTextInputs()
    {
        var recorder = new RecordingAgentStrategy();
        Workflow workflow = BuildWorkflow();

        await RunAsync(workflow, recorder);

        Assert.NotNull(recorder.CapturedTextInputs);
        Assert.Equal(UPSTREAM_OUTPUT, recorder.CapturedTextInputs[UPSTREAM_TAG]);
    }

    [Fact]
    public async Task UnresolvableSkillTag_ArrivesAsNoDataSentinel()
    {
        var recorder = new RecordingAgentStrategy();
        Workflow workflow = BuildWorkflow();

        await RunAsync(workflow, recorder);

        string noDataText = _sp.GetRequiredService<IOptions<PlatformSettings>>()
            .Value.IniaAi.NoDataForPlaceHolderText;

        Assert.NotNull(recorder.CapturedTextInputs);
        Assert.Equal(noDataText, recorder.CapturedTextInputs[MISSING_TAG]);
    }

    [Fact]
    public async Task StrategyNamespacesAndSingleBraceContent_AreNeverStamped()
    {
        var recorder = new RecordingAgentStrategy();
        Workflow workflow = BuildWorkflow();

        await RunAsync(workflow, recorder);

        Assert.NotNull(recorder.CapturedTextInputs);
        Assert.DoesNotContain(
            recorder.CapturedTextInputs.Keys,
            key => key.StartsWith(AgentTemplateTags.VALIDATOR_NAMESPACE, StringComparison.Ordinal)
                || key.StartsWith(AgentTemplateTags.VERIFIER_NAMESPACE, StringComparison.Ordinal));
        Assert.DoesNotContain("ClientName", recorder.CapturedTextInputs.Keys);
    }

    private static Workflow BuildWorkflow()
    {
        Workflow root = new() { Id = "agent-dispatch-root", Title = "agent-dispatch" };
        Section section = new("Body", root) { Id = "agent-dispatch-sec-0", Order = 0 };

        _ = new Block(BlockTypes.Merge, section, replacementTag: UPSTREAM_TAG)
        {
            Properties = new MergeProperties { TextTemplate = UPSTREAM_OUTPUT },
        };

        var agent = new Block(BlockTypes.Agent, section, replacementTag: "extraction_agent");
        var props = (AgentProperties)agent.Properties;
        props.WorkerSkills.Add(new AgentWorkerSkill
        {
            Name = "extraction",
            Description = "Extracts figures. Use when the task asks for totals.",
            Content = SKILL_CONTENT,
        });
        props.Model = "test-model-a";

        return root;
    }

    private async Task RunAsync(Workflow workflow, RecordingAgentStrategy recorder)
    {
        using IServiceScope scope = _sp.GetRequiredService<IServiceScopeFactory>().CreateScope();
        IUserDataUnitOfWork uow = scope.ServiceProvider.GetRequiredService<IUserDataUnitOfWork>();

        WorkflowHandler handler = TestWorkflowHandlers.BuildWithScheduler(_sp, maxBlockParallelism: 1);
        _ = TestBlockStrategies.Swap(handler, BlockTypes.Agent, recorder);

        _ = await handler.ExecuteWorkflowAsync(
            workflow,
            anonymisationMap: new(),
            organizationId: _fixture.GetOrganization().Id,
            jobId: Guid.NewGuid().ToString(),
            uow,
            inputs: [],
            cancellationToken: TestContext.Current.CancellationToken);
    }
}
