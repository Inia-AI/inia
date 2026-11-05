using FluentValidation.Results;
using IniaAi.Application.Commands.Workflows.SaveWorkflow;
using IniaAi.Application.UnitTests.Common.Workflows;
using IniaAi.Common.Interfaces.Services.AgentRuntime;
using IniaAi.Configuration;
using IniaAi.Configuration.Platform;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Blocks;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties;
using IniaAi.Domain.Entities.Workflows.Enums;
using Microsoft.Extensions.Options;

namespace IniaAi.Application.UnitTests.Commands.Workflows.SaveWorkflow;

public class SaveWorkflowCommandValidatorTests
{
    private const string ALLOWED_MODEL = "test-model-a";

    private static SaveWorkflowCommandValidator CreateValidator(
        bool agentRuntimeEnabled,
        List<string>? allowedModels = null,
        IAgentRunLimitsProvider? runLimitsProvider = null)
    {
        var settings = new PlatformSettings
        {
            AgentRuntime = new AgentRuntimeSettings
            {
                Enabled = agentRuntimeEnabled,
                AllowedModels = allowedModels ?? [ALLOWED_MODEL],
            },
        };
        return new SaveWorkflowCommandValidator(Options.Create(settings), runLimitsProvider);
    }

    private static Workflow CreateWorkflowWithAgentBlock(
        Action<AgentProperties>? configure = null)
    {
        var workflow = new Workflow("AgentGateTest") { Title = "Agent gate test" };
        var section = new Section("Body", workflow);
        var block = new Block(BlockTypes.Agent, section, "agentBlock");
        var props = (AgentProperties)block.Properties;
        AgentTestData.ReplaceSkill(props, "extraction", "Extract the things.");
        props.Model = ALLOWED_MODEL;
        configure?.Invoke(props);
        return workflow;
    }

    private static Workflow CreateWorkflowWithoutAgentBlocks()
    {
        var workflow = new Workflow("PlainTest") { Title = "Plain test" };
        var section = new Section("Body", workflow);
        _ = new Block(BlockTypes.AiQuery, section, "aiBlock");
        return workflow;
    }

    private static SaveWorkflowCommand CreateCommand(Workflow workflow)
    {
        return new SaveWorkflowCommand("org-1", "user-1", "wt-agent-gate-test", workflow);
    }

    [Fact]
    public void RuntimeDisabled_WorkflowWithAgentBlock_IsRejected()
    {
        SaveWorkflowCommandValidator validator = CreateValidator(agentRuntimeEnabled: false);
        SaveWorkflowCommand command = CreateCommand(CreateWorkflowWithAgentBlock());

        ValidationResult result = validator.Validate(command);

        Assert.False(result.IsValid);
        ValidationFailure failure = Assert.Single(result.Errors);
        Assert.Contains("agent runtime", failure.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RuntimeDisabled_WorkflowWithoutAgentBlocks_IsValid()
    {
        SaveWorkflowCommandValidator validator = CreateValidator(agentRuntimeEnabled: false);
        SaveWorkflowCommand command = CreateCommand(CreateWorkflowWithoutAgentBlocks());

        ValidationResult result = validator.Validate(command);

        Assert.True(result.IsValid);
    }

    [Fact]
    public void RuntimeEnabled_AgentBlockWithSkillAndAllowedModel_IsValid()
    {
        SaveWorkflowCommandValidator validator = CreateValidator(agentRuntimeEnabled: true);
        SaveWorkflowCommand command = CreateCommand(CreateWorkflowWithAgentBlock());

        ValidationResult result = validator.Validate(command);

        Assert.True(result.IsValid);
    }

    [Fact]
    public void RuntimeEnabled_FuseAboveTheCeiling_WithFreshLimits_IsRejectedNamingTheCeiling()
    {
        SaveWorkflowCommandValidator validator = CreateValidator(
            agentRuntimeEnabled: true,
            runLimitsProvider: new AgentTestData.FixedLimitsProvider(AgentTestData.RuntimeLimits()));
        SaveWorkflowCommand command = CreateCommand(
            CreateWorkflowWithAgentBlock(props => props.MaxBudgetUsd = 5m));

        ValidationResult result = validator.Validate(command);

        Assert.False(result.IsValid);
        ValidationFailure failure = Assert.Single(result.Errors);
        Assert.Contains("2.50 USD", failure.ErrorMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void RuntimeEnabled_FuseAboveTheCeiling_WithoutKnownLimits_IsValid()
    {
        SaveWorkflowCommandValidator validator = CreateValidator(agentRuntimeEnabled: true);
        SaveWorkflowCommand command = CreateCommand(
            CreateWorkflowWithAgentBlock(props => props.MaxBudgetUsd = 5m));

        ValidationResult result = validator.Validate(command);

        Assert.True(result.IsValid);
    }

    [Fact]
    public void RuntimeEnabled_AgentBlockWithoutSkills_IsRejected()
    {
        SaveWorkflowCommandValidator validator = CreateValidator(agentRuntimeEnabled: true);
        SaveWorkflowCommand command = CreateCommand(
            CreateWorkflowWithAgentBlock(props => props.WorkerSkills.Clear()));

        ValidationResult result = validator.Validate(command);

        Assert.False(result.IsValid);
        ValidationFailure failure = Assert.Single(result.Errors);
        Assert.Contains("skill", failure.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RuntimeEnabled_AgentBlockWithBlankSkillContent_IsRejected()
    {
        SaveWorkflowCommandValidator validator = CreateValidator(agentRuntimeEnabled: true);
        SaveWorkflowCommand command = CreateCommand(
            CreateWorkflowWithAgentBlock(props => AgentTestData.ReplaceSkill(props, "extraction", "   ")));

        ValidationResult result = validator.Validate(command);

        Assert.False(result.IsValid);
        ValidationFailure failure = Assert.Single(result.Errors);
        Assert.Contains("skill", failure.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RuntimeEnabled_AgentBlockWithoutModel_IsRejected()
    {
        SaveWorkflowCommandValidator validator = CreateValidator(agentRuntimeEnabled: true);
        SaveWorkflowCommand command = CreateCommand(
            CreateWorkflowWithAgentBlock(props => props.Model = null));

        ValidationResult result = validator.Validate(command);

        Assert.False(result.IsValid);
        ValidationFailure failure = Assert.Single(result.Errors);
        Assert.Contains("model", failure.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RuntimeEnabled_AgentBlockWithModelOutsideAllowlist_IsRejected()
    {
        SaveWorkflowCommandValidator validator = CreateValidator(agentRuntimeEnabled: true);
        SaveWorkflowCommand command = CreateCommand(
            CreateWorkflowWithAgentBlock(props => props.Model = "not-pinned-model"));

        ValidationResult result = validator.Validate(command);

        Assert.False(result.IsValid);
        ValidationFailure failure = Assert.Single(result.Errors);
        Assert.Contains("not-pinned-model", failure.ErrorMessage);
    }

    [Fact]
    public void RuntimeEnabled_NullAllowedModels_RejectsModelInsteadOfThrowing()
    {
        var settings = new PlatformSettings
        {
            AgentRuntime = new AgentRuntimeSettings { Enabled = true, AllowedModels = null! },
        };
        var validator = new SaveWorkflowCommandValidator(Options.Create(settings));
        SaveWorkflowCommand command = CreateCommand(CreateWorkflowWithAgentBlock());

        ValidationResult result = validator.Validate(command);

        Assert.False(result.IsValid);
        ValidationFailure failure = Assert.Single(result.Errors);
        Assert.Contains(ALLOWED_MODEL, failure.ErrorMessage);
    }

    [Fact]
    public void RuntimeEnabled_WorkflowWithoutAgentBlocks_IsValid()
    {
        SaveWorkflowCommandValidator validator = CreateValidator(agentRuntimeEnabled: true);
        SaveWorkflowCommand command = CreateCommand(CreateWorkflowWithoutAgentBlocks());

        ValidationResult result = validator.Validate(command);

        Assert.True(result.IsValid);
    }

    [Fact]
    public void RuntimeEnabled_AgentBlockInsideLoopBody_IsValid()
    {
        var workflow = new Workflow("LoopedAgent") { Title = "Looped agent" };
        Section looped = LoopedSectionTestData.AddLoopedSection(workflow, producerTag: "looped_tag");
        Block agent = LoopedSectionTestData.AddAgentInsideLoopBody(looped, "agentBlock");
        ((AgentProperties)agent.Properties).Model = ALLOWED_MODEL;

        ValidationResult result = CreateValidator(agentRuntimeEnabled: true)
            .Validate(CreateCommand(workflow));

        Assert.True(result.IsValid);
    }

    [Fact]
    public void RuntimeEnabled_AgentBlockAfterLoopEnd_IsValid()
    {
        var workflow = new Workflow("AfterLoopAgent") { Title = "After-loop agent" };
        Section looped = LoopedSectionTestData.AddLoopedSection(workflow, producerTag: "looped_tag");
        var agent = new Block(BlockTypes.Agent, looped, "agentBlock");
        var props = (AgentProperties)agent.Properties;
        AgentTestData.ReplaceSkill(props, "extraction", "Compile the final report.");
        props.Model = ALLOWED_MODEL;

        ValidationResult result = CreateValidator(agentRuntimeEnabled: true)
            .Validate(CreateCommand(workflow));

        Assert.True(result.IsValid);
    }

    [Fact]
    public void RuntimeEnabled_AgentBlockInsideParameterizedSection_IsValid()
    {
        var workflow = new Workflow("ParamAgent") { Title = "Parameterized agent" };
        var parameterized = new Section("Parameterized", workflow)
        {
            ParameterizedConfig = """{"Variants":[{"Params":{"Region":"EU"}}]}""",
        };
        var agent = new Block(BlockTypes.Agent, parameterized, "agentBlock");
        var props = (AgentProperties)agent.Properties;
        AgentTestData.ReplaceSkill(props, "extraction", "Extract the things.");
        props.Model = ALLOWED_MODEL;

        ValidationResult result = CreateValidator(agentRuntimeEnabled: true)
            .Validate(CreateCommand(workflow));

        Assert.True(result.IsValid);
    }

    [Fact]
    public void RuntimeEnabled_SkillReferencingProducerInsideLoopedSection_IsValid()
    {
        Workflow workflow = CreateWorkflowWithAgentBlock(
            props => AgentTestData.ReplaceSkill(props, "extraction", "Read {{looped_tag}}."));
        _ = LoopedSectionTestData.AddLoopedSection(workflow, producerTag: "looped_tag");

        ValidationResult result = CreateValidator(agentRuntimeEnabled: true)
            .Validate(CreateCommand(workflow));

        Assert.True(result.IsValid);
    }

    [Fact]
    public void RuntimeEnabled_SkillReferencingAParameterizedProducerFromOutside_IsNotAValidatorError()
    {
        Workflow workflow = CreateWorkflowWithAgentBlock(
            props => AgentTestData.ReplaceSkill(props, "extraction", "Read {{param_tag.answer}}."));
        var parameterized = new Section("Parameterized", workflow)
        {
            ParameterizedConfig = """{"Variants":[{"Key":"EU","Label":"EU"}]}""",
        };
        _ = new Block(BlockTypes.Merge, parameterized, "param_tag")
        {
            Properties = new MergeProperties { TextTemplate = "per variant" },
        };

        ValidationResult result = CreateValidator(agentRuntimeEnabled: true)
            .Validate(CreateCommand(workflow));

        Assert.True(result.IsValid);
    }

    [Fact]
    public void RuntimeEnabled_SkillReferencingUnknownTag_IsNotAValidatorError()
    {
        // Unknown tags are a save warning, never a blocking validation failure.
        SaveWorkflowCommand command = CreateCommand(CreateWorkflowWithAgentBlock(
            props => AgentTestData.ReplaceSkill(props, "extraction", "Consult {{ghost}}.")));

        ValidationResult result = CreateValidator(agentRuntimeEnabled: true).Validate(command);

        Assert.True(result.IsValid);
    }
}
