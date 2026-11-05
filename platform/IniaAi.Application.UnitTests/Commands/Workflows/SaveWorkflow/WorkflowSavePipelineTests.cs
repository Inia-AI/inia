using ErrorOr;
using IniaAi.Application.Commands.Workflows.SaveWorkflow;
using IniaAi.Application.Common.Workflows;
using IniaAi.Application.UnitTests.Common.SharedComponents;
using IniaAi.Application.UnitTests.Common.Workflows;
using IniaAi.Core.Workflows.Diff;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Blocks;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties;
using IniaAi.Domain.Entities.Workflows.Enums;
using Microsoft.EntityFrameworkCore;

namespace IniaAi.Application.UnitTests.Commands.Workflows.SaveWorkflow;

public class WorkflowSavePipelineTests
{
    private const string ORG = "org-test";
    private const string USER = "user-test";
    private const string TYPE = "WT-Pipeline";

    [Fact]
    public async Task SaveCoreAsync_WithoutTransaction_SavesWorkflowAndAppendsVersion()
    {
        using var harness = new SharedComponentsTestHarness();
        WorkflowSavePipeline pipeline = harness.CreatePipeline();

        ErrorOr<SaveWorkflowResult> result = await pipeline.SaveCoreAsync(
            new SaveWorkflowCommand(ORG, USER, TYPE, BuildWorkflow()),
            narrativeOverride: null,
            TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        Assert.False(result.Value.WasDeduped);

        Workflow saved = await harness.Context.Workflows
            .SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal(TYPE, saved.WorkflowTypeId);

        WorkflowVersion version = await harness.Context.WorkflowVersions
            .SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal(result.Value.VersionId, version.Id);
        Assert.Equal(result.Value.ContentHash, version.ContentHash);
        Assert.Equal(WorkflowNarrativeGenerator.FirstVersionSummary, version.AutoNarrativeSummary);
    }

    [Fact]
    public async Task SaveCoreAsync_NarrativeOverride_LandsInAutoNarrativeSummary()
    {
        using var harness = new SharedComponentsTestHarness();
        WorkflowSavePipeline pipeline = harness.CreatePipeline();

        _ = await pipeline.SaveCoreAsync(
            new SaveWorkflowCommand(ORG, USER, TYPE, BuildWorkflow()),
            narrativeOverride: null,
            TestContext.Current.CancellationToken);

        const string OVERRIDE = "Updated from shared component 'FX Rates' (abcd1234)";
        ErrorOr<SaveWorkflowResult> second = await pipeline.SaveCoreAsync(
            new SaveWorkflowCommand(ORG, USER, TYPE, BuildWorkflow(prompt: "changed prompt")),
            OVERRIDE,
            TestContext.Current.CancellationToken);

        Assert.False(second.IsError);

        WorkflowVersion head = await harness.Context.WorkflowVersions
            .SingleAsync(v => v.Id == second.Value.VersionId, TestContext.Current.CancellationToken);
        Assert.Equal(OVERRIDE, head.AutoNarrativeSummary);
    }

    [Fact]
    public async Task SaveCoreAsync_AgentBlockOnDisabledRuntime_RefusesWithoutWriting()
    {
        using var harness = new SharedComponentsTestHarness();
        WorkflowSavePipeline pipeline = harness.CreatePipeline();

        ErrorOr<SaveWorkflowResult> result = await pipeline.SaveCoreAsync(
            new SaveWorkflowCommand(ORG, USER, TYPE, BuildAgentWorkflow()),
            narrativeOverride: null,
            TestContext.Current.CancellationToken);

        Assert.True(result.IsError);
        Assert.Contains(result.Errors, e =>
            e.Description.Contains("agent runtime", StringComparison.OrdinalIgnoreCase)
            && e.Description.Contains(TYPE, StringComparison.Ordinal));
        Assert.Empty(await harness.Context.Workflows.ToListAsync(TestContext.Current.CancellationToken));
        Assert.Empty(await harness.Context.WorkflowVersions.ToListAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task SaveCoreAsync_AgentBlockInsideParameterizedSection_Saves()
    {
        using var harness = new SharedComponentsTestHarness();
        WorkflowSavePipeline pipeline = harness.CreatePipeline(AgentTestData.EnabledRuntime());

        var workflow = new Workflow("ParamAgent") { Title = "Param agent" };
        var parameterized = new Section("Parameterized", workflow)
        {
            ParameterizedConfig = AgentTestData.PARAMETERIZED_CONFIG_JSON,
        };
        _ = AgentTestData.AddAgent(parameterized, "agent_block");

        ErrorOr<SaveWorkflowResult> result = await pipeline.SaveCoreAsync(
            new SaveWorkflowCommand(ORG, USER, TYPE, workflow),
            narrativeOverride: null,
            TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        Assert.Single(await harness.Context.WorkflowVersions.ToListAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task SaveCoreAsync_VerificationBlockInsideParameterizedSection_RefusesOnEnabledRuntime()
    {
        using var harness = new SharedComponentsTestHarness();
        WorkflowSavePipeline pipeline = harness.CreatePipeline(AgentTestData.EnabledRuntime());

        var workflow = new Workflow("ParamVerifier") { Title = "Param verifier" };
        var parameterized = new Section("Parameterized", workflow)
        {
            ParameterizedConfig = AgentTestData.PARAMETERIZED_CONFIG_JSON,
        };
        _ = new Block(BlockTypes.Verification, parameterized, "group_verifier");

        ErrorOr<SaveWorkflowResult> result = await pipeline.SaveCoreAsync(
            new SaveWorkflowCommand(ORG, USER, TYPE, workflow),
            narrativeOverride: null,
            TestContext.Current.CancellationToken);

        Assert.True(result.IsError);
        Assert.Contains(result.Errors, e =>
            e.Description.Contains("parameterized", StringComparison.OrdinalIgnoreCase));
        Assert.Empty(await harness.Context.WorkflowVersions.ToListAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task SaveCoreAsync_ValidAgentBlockOnEnabledRuntime_Saves()
    {
        using var harness = new SharedComponentsTestHarness();
        WorkflowSavePipeline pipeline = harness.CreatePipeline(AgentTestData.EnabledRuntime());

        ErrorOr<SaveWorkflowResult> result = await pipeline.SaveCoreAsync(
            new SaveWorkflowCommand(ORG, USER, TYPE, BuildAgentWorkflow()),
            narrativeOverride: null,
            TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        _ = await harness.Context.WorkflowVersions.SingleAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task SaveCoreAsync_FuseAboveTheCeiling_WithFreshLimits_RefusesTheSave()
    {
        using var harness = new SharedComponentsTestHarness();
        WorkflowSavePipeline pipeline = harness.CreatePipeline(
            AgentTestData.EnabledRuntime(),
            new AgentTestData.FixedLimitsProvider(AgentTestData.RuntimeLimits()));

        Workflow workflow = BuildAgentWorkflow();
        Block agentBlock = workflow.GetAllBlocks().Single(b => b.Type == BlockTypes.Agent);
        ((AgentProperties)agentBlock.Properties).MaxBudgetUsd = 5m;

        ErrorOr<SaveWorkflowResult> result = await pipeline.SaveCoreAsync(
            new SaveWorkflowCommand(ORG, USER, TYPE, workflow),
            narrativeOverride: null,
            TestContext.Current.CancellationToken);

        Assert.True(result.IsError);
        Assert.Contains(result.Errors, e => e.Description.Contains("2.50 USD", StringComparison.Ordinal));
        Assert.Empty(await harness.Context.WorkflowVersions.ToListAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task SaveCoreAsync_DeclaredFusesWithUnknownLimits_SavesWithAWarning()
    {
        using var harness = new SharedComponentsTestHarness();
        WorkflowSavePipeline pipeline = harness.CreatePipeline(
            AgentTestData.EnabledRuntime(),
            new AgentTestData.FixedLimitsProvider(limits: null));

        Workflow workflow = BuildAgentWorkflow();
        Block agentBlock = workflow.GetAllBlocks().Single(b => b.Type == BlockTypes.Agent);
        ((AgentProperties)agentBlock.Properties).MaxBudgetUsd = 5m;

        ErrorOr<SaveWorkflowResult> result = await pipeline.SaveCoreAsync(
            new SaveWorkflowCommand(ORG, USER, TYPE, workflow),
            narrativeOverride: null,
            TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        _ = await harness.Context.WorkflowVersions.SingleAsync(TestContext.Current.CancellationToken);
        string warning = Assert.Single(result.Value.Warnings ?? []);
        Assert.Equal(AgentBlockGuard.FUSES_UNCHECKED_WARNING, warning);
    }

    [Fact]
    public async Task SaveCoreAsync_NoDeclaredFuses_CarriesNoWarningEvenWithUnknownLimits()
    {
        using var harness = new SharedComponentsTestHarness();
        WorkflowSavePipeline pipeline = harness.CreatePipeline(
            AgentTestData.EnabledRuntime(),
            new AgentTestData.FixedLimitsProvider(limits: null));

        ErrorOr<SaveWorkflowResult> result = await pipeline.SaveCoreAsync(
            new SaveWorkflowCommand(ORG, USER, TYPE, BuildAgentWorkflow()),
            narrativeOverride: null,
            TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        Assert.True(result.Value.Warnings is null or []);
    }

    private static Workflow BuildAgentWorkflow()
    {
        var workflow = new Workflow("AgentReport") { Title = "Agent report" };
        var section = new Section("Body", workflow);
        _ = AgentTestData.AddAgent(section, "agent_block");

        return workflow;
    }

    private static Workflow BuildWorkflow(string prompt = "Who is the counterparty?")
    {
        var workflow = new Workflow("Report") { Title = "Report" };
        var section = new Section("Body", workflow);
        _ = new Block(BlockTypes.AiQuery, section, "block1")
        {
            Name = "Counterparty",
            Properties = new AiQueryProperties { UserPromptTemplate = prompt },
        };

        return workflow;
    }
}
