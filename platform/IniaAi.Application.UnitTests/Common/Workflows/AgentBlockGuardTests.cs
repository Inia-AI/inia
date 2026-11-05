using IniaAi.Application.Common.Workflows;
using IniaAi.Configuration.Platform;
using IniaAi.Core.Services.AgentRuntime;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Blocks;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties;
using IniaAi.Domain.Entities.Workflows.Enums;

namespace IniaAi.Application.UnitTests.Common.Workflows;

public class AgentBlockGuardTests
{
    [Fact]
    public void ValidAgentWorkflow_NoViolations()
    {
        Assert.Empty(AgentBlockGuard.ValidateGraph(WorkflowWithAgent(), AgentTestData.EnabledRuntime()));
    }

    [Fact]
    public void WorkflowWithoutAgentBlocks_NoViolations_EvenWhenDisabled()
    {
        var workflow = new Workflow("Plain") { Title = "Plain" };
        var section = new Section("Body", workflow);
        _ = new Block(BlockTypes.AiQuery, section, "aiBlock");

        Assert.Empty(AgentBlockGuard.ValidateGraph(workflow, new AgentRuntimeSettings()));
    }

    [Fact]
    public void RuntimeDisabled_ClassifiedAsRuntimeDisabled_AndShortCircuits()
    {
        List<AgentBlockGuard.AgentBlockViolation> violations = AgentBlockGuard.ValidateGraph(
            WorkflowWithAgent(props => props.WorkerSkills.Clear()),
            new AgentRuntimeSettings());

        AgentBlockGuard.AgentBlockViolation violation = Assert.Single(violations);
        Assert.Equal(AgentBlockGuard.AgentViolationKind.RuntimeDisabled, violation.Kind);
        Assert.Contains("agentBlock", violation.Message);
    }

    [Fact]
    public void EmptySkills_ClassifiedAsSkillShape()
    {
        List<AgentBlockGuard.AgentBlockViolation> violations = AgentBlockGuard.ValidateGraph(
            WorkflowWithAgent(props => props.WorkerSkills.Clear()),
            AgentTestData.EnabledRuntime());

        AgentBlockGuard.AgentBlockViolation violation = Assert.Single(violations);
        Assert.Equal(AgentBlockGuard.AgentViolationKind.SkillShape, violation.Kind);
    }

    [Fact]
    public void BlankSkillDescription_ClassifiedAsSkillShape()
    {
        List<AgentBlockGuard.AgentBlockViolation> violations = AgentBlockGuard.ValidateGraph(
            WorkflowWithAgent(static props => props.WorkerSkills[0].Description = "   "),
            AgentTestData.EnabledRuntime());

        AgentBlockGuard.AgentBlockViolation violation = Assert.Single(violations);
        Assert.Equal(AgentBlockGuard.AgentViolationKind.SkillShape, violation.Kind);
        Assert.Contains("description", violation.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("SKILL.md")]
    [InlineData("Income Statement")]
    [InlineData("extraction_v2")]
    [InlineData("-leading-hyphen")]
    public void SkillNameThatIsNotAnIdentity_ClassifiedAsSkillNameShape(string name)
    {
        List<AgentBlockGuard.AgentBlockViolation> violations = AgentBlockGuard.ValidateGraph(
            WorkflowWithAgent(props => props.WorkerSkills[0].Name = name),
            AgentTestData.EnabledRuntime());

        AgentBlockGuard.AgentBlockViolation violation = Assert.Single(violations);
        Assert.Equal(AgentBlockGuard.AgentViolationKind.SkillNameShape, violation.Kind);
    }

    [Theory]
    [InlineData("extraction")]
    [InlineData("income-statement")]
    [InlineData("balance-sheet-v2")]
    public void DirectoryShapedSkillName_IsAccepted(string name)
    {
        Assert.Empty(AgentBlockGuard.ValidateGraph(
            WorkflowWithAgent(props => props.WorkerSkills[0].Name = name),
            AgentTestData.EnabledRuntime()));
    }

    [Fact]
    public void TwoSkillNamesOnOneIdentity_ClassifiedAsSkillNameCollision()
    {
        List<AgentBlockGuard.AgentBlockViolation> violations = AgentBlockGuard.ValidateGraph(
            WorkflowWithAgent(static props => props.WorkerSkills.Add(
                AgentTestData.Skill("extraction", "Second body."))),
            AgentTestData.EnabledRuntime());

        AgentBlockGuard.AgentBlockViolation violation = Assert.Single(violations);
        Assert.Equal(AgentBlockGuard.AgentViolationKind.SkillNameCollision, violation.Kind);
    }

    [Fact]
    public void OverlongSkillDescription_ClassifiedAsSkillDescriptionLength()
    {
        List<AgentBlockGuard.AgentBlockViolation> violations = AgentBlockGuard.ValidateGraph(
            WorkflowWithAgent(static props => props.WorkerSkills[0].Description = new string('d', 1401)),
            AgentTestData.EnabledRuntime());

        AgentBlockGuard.AgentBlockViolation violation = Assert.Single(violations);
        Assert.Equal(AgentBlockGuard.AgentViolationKind.SkillDescriptionLength, violation.Kind);
    }

    [Fact]
    public void SkillWithAValidAsset_NoViolations()
    {
        Assert.Empty(AgentBlockGuard.ValidateGraph(
            WorkflowWithAgent(static props => props.WorkerSkills[0].Assets =
                [new AgentWorkerSkillAsset { Name = "verify.py", Content = "print(1)" }]),
            AgentTestData.EnabledRuntime()));
    }

    [Fact]
    public void MoreAssetsThanTheRuntimeCap_ClassifiedAsSkillAssetCount()
    {
        List<AgentBlockGuard.AgentBlockViolation> violations = AgentBlockGuard.ValidateGraph(
            WorkflowWithAgent(static props => props.WorkerSkills[0].Assets = [.. Enumerable.Range(0, 17)
                .Select(static index => new AgentWorkerSkillAsset
                {
                    Name = $"asset-{index}.py",
                    Content = "print(1)",
                })]),
            AgentTestData.EnabledRuntime());

        AgentBlockGuard.AgentBlockViolation violation = Assert.Single(violations);
        Assert.Equal(AgentBlockGuard.AgentViolationKind.SkillAssetCount, violation.Kind);
        Assert.Contains("16", violation.Message);
    }

    [Theory]
    [InlineData("nested/path.py")]
    [InlineData("manifest.json")]
    [InlineData("..")]
    public void IllegalOrReservedAssetName_ClassifiedAsSkillAssetNameShape(string assetName)
    {
        List<AgentBlockGuard.AgentBlockViolation> violations = AgentBlockGuard.ValidateGraph(
            WorkflowWithAgent(props => props.WorkerSkills[0].Assets =
                [new AgentWorkerSkillAsset { Name = assetName, Content = "print(1)" }]),
            AgentTestData.EnabledRuntime());

        AgentBlockGuard.AgentBlockViolation violation = Assert.Single(violations);
        Assert.Equal(AgentBlockGuard.AgentViolationKind.SkillAssetNameShape, violation.Kind);
    }

    [Fact]
    public void DuplicateAssetNamesInOneSkill_ClassifiedAsSkillAssetNameCollision()
    {
        List<AgentBlockGuard.AgentBlockViolation> violations = AgentBlockGuard.ValidateGraph(
            WorkflowWithAgent(static props => props.WorkerSkills[0].Assets =
            [
                new AgentWorkerSkillAsset { Name = "verify.py", Content = "print(1)" },
                new AgentWorkerSkillAsset { Name = "verify.py", Content = "print(2)" },
            ]),
            AgentTestData.EnabledRuntime());

        AgentBlockGuard.AgentBlockViolation violation = Assert.Single(violations);
        Assert.Equal(AgentBlockGuard.AgentViolationKind.SkillAssetNameCollision, violation.Kind);
    }

    [Fact]
    public void BlankAssetContent_ClassifiedAsSkillAssetShape()
    {
        List<AgentBlockGuard.AgentBlockViolation> violations = AgentBlockGuard.ValidateGraph(
            WorkflowWithAgent(static props => props.WorkerSkills[0].Assets =
                [new AgentWorkerSkillAsset { Name = "verify.py", Content = "   " }]),
            AgentTestData.EnabledRuntime());

        AgentBlockGuard.AgentBlockViolation violation = Assert.Single(violations);
        Assert.Equal(AgentBlockGuard.AgentViolationKind.SkillAssetShape, violation.Kind);
    }

    [Fact]
    public void BlankModel_TierModelOutsideTheAllowlist_ClassifiedAsTierModelNotAllowed()
    {
        List<AgentBlockGuard.AgentBlockViolation> violations = AgentBlockGuard.ValidateGraph(
            WorkflowWithAgent(props => props.Model = null),
            AgentTestData.EnabledRuntime());

        AgentBlockGuard.AgentBlockViolation violation = Assert.Single(violations);
        Assert.Equal(AgentBlockGuard.AgentViolationKind.TierModelNotAllowed, violation.Kind);
        Assert.Contains($"advanced agent model '{AgentRuntimeSettings.DEFAULT_ADVANCED_MODEL}'", violation.Message);
        Assert.Contains($"{AgentRuntimeSettings.SectionName}:{nameof(AgentRuntimeSettings.AdvancedModel)}", violation.Message);
        Assert.Contains(AgentTestData.MODEL, violation.Message);
    }

    [Theory]
    [InlineData(true, TIER_BEDROCK_OPUS)]
    [InlineData(false, TIER_BEDROCK_SONNET)]
    [InlineData(true, TIER_NEUTRAL_OPUS)]
    [InlineData(false, TIER_NEUTRAL_SONNET)]
    public void BlankModel_TierModelInTheAllowlistInAnyForm_IsValid(bool shouldUseAdvancedModel, string allowedModel)
    {
        List<AgentBlockGuard.AgentBlockViolation> violations = AgentBlockGuard.ValidateGraph(
            WorkflowWithAgent(props =>
            {
                props.Model = null;
                props.ShouldUseAdvancedModel = shouldUseAdvancedModel;
            }),
            AgentTestData.EnabledRuntime(allowedModel));

        Assert.Empty(violations);
    }

    [Fact]
    public void BlankModel_StandardTierWhenOnlyTheAdvancedModelIsAllowed_RefusesNamingTheBasicSetting()
    {
        List<AgentBlockGuard.AgentBlockViolation> violations = AgentBlockGuard.ValidateGraph(
            WorkflowWithAgent(props =>
            {
                props.Model = null;
                props.ShouldUseAdvancedModel = false;
            }),
            AgentTestData.EnabledRuntime(TIER_BEDROCK_OPUS));

        AgentBlockGuard.AgentBlockViolation violation = Assert.Single(violations);
        Assert.Equal(AgentBlockGuard.AgentViolationKind.TierModelNotAllowed, violation.Kind);
        Assert.Contains($"basic agent model '{AgentRuntimeSettings.DEFAULT_BASIC_MODEL}'", violation.Message);
        Assert.Contains(nameof(AgentRuntimeSettings.BasicModel), violation.Message);
    }

    [Fact]
    public void BlankModel_OrganizationOverrideNamesAnAllowedModel_IsValid_WhereTheDeploymentTierIsNot()
    {
        AgentRuntimeSettings runtime = AgentTestData.EnabledRuntime(TIER_BEDROCK_SONNET);
        var routing = new ModelRoutingSettings
        {
            TenantModelOverrides = new Dictionary<string, TenantModelOverride>
            {
                [TIER_ORG] = new() { AgentAdvancedModel = TIER_NEUTRAL_SONNET },
            },
        };
        Workflow workflow = WorkflowWithAgent(props => props.Model = null);

        Assert.Empty(AgentBlockGuard.ValidateGraph(
            workflow, runtime, modelTiers: AgentModelTiers.For(runtime, routing, TIER_ORG)));
        Assert.Equal(
            AgentBlockGuard.AgentViolationKind.TierModelNotAllowed,
            Assert.Single(AgentBlockGuard.ValidateGraph(
                workflow, runtime, modelTiers: AgentModelTiers.For(runtime, routing, "another-org"))).Kind);
    }

    [Fact]
    public void SetModel_OverridesTheTier_SoAnUnallowedTierModelDoesNotMatter()
    {
        List<AgentBlockGuard.AgentBlockViolation> violations = AgentBlockGuard.ValidateGraph(
            WorkflowWithAgent(props =>
            {
                props.Model = AgentTestData.MODEL;
                props.ShouldUseAdvancedModel = true;
            }),
            AgentTestData.EnabledRuntime());

        Assert.Empty(violations);
    }

    [Fact]
    public void DisallowedModel_ClassifiedAsModelNotAllowed()
    {
        List<AgentBlockGuard.AgentBlockViolation> violations = AgentBlockGuard.ValidateGraph(
            WorkflowWithAgent(props => props.Model = "rogue-model"),
            AgentTestData.EnabledRuntime());

        AgentBlockGuard.AgentBlockViolation violation = Assert.Single(violations);
        Assert.Equal(AgentBlockGuard.AgentViolationKind.ModelNotAllowed, violation.Kind);
        Assert.Contains("rogue-model", violation.Message);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void UnsetEffort_IsValid_TheRuntimeDefaultApplies(string? effort)
    {
        Assert.Empty(AgentBlockGuard.ValidateGraph(
            WorkflowWithAgent(props => props.Effort = effort),
            AgentTestData.EnabledRuntime()));
    }

    [Theory]
    [InlineData("low")]
    [InlineData("medium")]
    [InlineData("high")]
    [InlineData("xhigh")]
    [InlineData("max")]
    public void EveryEffortLevel_IsValid(string effort)
    {
        Assert.Empty(AgentBlockGuard.ValidateGraph(
            WorkflowWithAgent(props => props.Effort = effort),
            AgentTestData.EnabledRuntime()));
    }

    [Theory]
    [InlineData("Medium")]
    [InlineData("turbo")]
    public void EffortOutsideTheVocabulary_ClassifiedAsEffortNotAllowed(string effort)
    {
        List<AgentBlockGuard.AgentBlockViolation> violations = AgentBlockGuard.ValidateGraph(
            WorkflowWithAgent(props => props.Effort = effort),
            AgentTestData.EnabledRuntime());

        AgentBlockGuard.AgentBlockViolation violation = Assert.Single(violations);
        Assert.Equal(AgentBlockGuard.AgentViolationKind.EffortNotAllowed, violation.Kind);
        Assert.Contains(effort, violation.Message);
    }

    [Fact]
    public void FusesLeftUndeclared_AreValid()
    {
        Assert.Empty(AgentBlockGuard.ValidateGraph(WorkflowWithAgent(), AgentTestData.EnabledRuntime()));
    }

    [Fact]
    public void FuseOverrideAtOrBelowZero_ClassifiedAsFuseValue()
    {
        List<AgentBlockGuard.AgentBlockViolation> violations = AgentBlockGuard.ValidateGraph(
            WorkflowWithAgent(props =>
            {
                props.DeadlineMinutes = 0;
                props.MaxBudgetUsd = -1m;
                props.MaxTurns = -3;
                props.MaxRetainedOutputMib = 0;
            }),
            AgentTestData.EnabledRuntime());

        Assert.Equal(4, violations.Count);
        Assert.All(violations, violation =>
            Assert.Equal(AgentBlockGuard.AgentViolationKind.FuseValue, violation.Kind));
    }

    [Fact]
    public void OneBadFuseAmongUndeclaredOnes_IsTheOnlyViolation()
    {
        List<AgentBlockGuard.AgentBlockViolation> violations = AgentBlockGuard.ValidateGraph(
            WorkflowWithAgent(props => props.MaxTurns = -1),
            AgentTestData.EnabledRuntime());

        AgentBlockGuard.AgentBlockViolation violation = Assert.Single(violations);
        Assert.Equal(AgentBlockGuard.AgentViolationKind.FuseValue, violation.Kind);
        Assert.Contains(nameof(AgentProperties.MaxTurns), violation.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AFuseAboveTheCeiling_WhenLimitsAreUnknown_IsLeftToTheRuntime()
    {
        Assert.Empty(AgentBlockGuard.ValidateGraph(
            WorkflowWithAgent(props =>
            {
                props.MaxTurns = 100_000;
                props.MaxBudgetUsd = 1_000m;
            }),
            AgentTestData.EnabledRuntime(),
            runLimits: null));
    }

    [Fact]
    public void DeadlineAboveTheCeiling_ClassifiedAsFuseAboveCeiling_NamingTheCeilingInMinutes()
    {
        List<AgentBlockGuard.AgentBlockViolation> violations = AgentBlockGuard.ValidateGraph(
            WorkflowWithAgent(props => props.DeadlineMinutes = 16),
            AgentTestData.EnabledRuntime(),
            AgentTestData.RuntimeLimits());

        AgentBlockGuard.AgentBlockViolation violation = Assert.Single(violations);
        Assert.Equal(AgentBlockGuard.AgentViolationKind.FuseAboveCeiling, violation.Kind);
        Assert.Contains(nameof(AgentProperties.DeadlineMinutes), violation.Message, StringComparison.Ordinal);
        Assert.Contains("15 minutes", violation.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void BudgetAboveTheCeiling_ClassifiedAsFuseAboveCeiling_NamingTheCeilingInUsd()
    {
        List<AgentBlockGuard.AgentBlockViolation> violations = AgentBlockGuard.ValidateGraph(
            WorkflowWithAgent(props => props.MaxBudgetUsd = 5m),
            AgentTestData.EnabledRuntime(),
            AgentTestData.RuntimeLimits());

        AgentBlockGuard.AgentBlockViolation violation = Assert.Single(violations);
        Assert.Equal(AgentBlockGuard.AgentViolationKind.FuseAboveCeiling, violation.Kind);
        Assert.Contains(nameof(AgentProperties.MaxBudgetUsd), violation.Message, StringComparison.Ordinal);
        Assert.Contains("2.50 USD", violation.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TurnsAboveTheCeiling_ClassifiedAsFuseAboveCeiling_NamingTheCeiling()
    {
        List<AgentBlockGuard.AgentBlockViolation> violations = AgentBlockGuard.ValidateGraph(
            WorkflowWithAgent(props => props.MaxTurns = 1000),
            AgentTestData.EnabledRuntime(),
            AgentTestData.RuntimeLimits());

        AgentBlockGuard.AgentBlockViolation violation = Assert.Single(violations);
        Assert.Equal(AgentBlockGuard.AgentViolationKind.FuseAboveCeiling, violation.Kind);
        Assert.Contains(nameof(AgentProperties.MaxTurns), violation.Message, StringComparison.Ordinal);
        Assert.Contains("300 turns", violation.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RetainedOutputAboveTheCeiling_ClassifiedAsFuseAboveCeiling_NamingTheCeilingInMib()
    {
        List<AgentBlockGuard.AgentBlockViolation> violations = AgentBlockGuard.ValidateGraph(
            WorkflowWithAgent(props => props.MaxRetainedOutputMib = 265),
            AgentTestData.EnabledRuntime(),
            AgentTestData.RuntimeLimits());

        AgentBlockGuard.AgentBlockViolation violation = Assert.Single(violations);
        Assert.Equal(AgentBlockGuard.AgentViolationKind.FuseAboveCeiling, violation.Kind);
        Assert.Contains(nameof(AgentProperties.MaxRetainedOutputMib), violation.Message, StringComparison.Ordinal);
        Assert.Contains("264 MiB", violation.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void EveryFuseExactlyAtItsCeiling_IsAccepted()
    {
        Assert.Empty(AgentBlockGuard.ValidateGraph(
            WorkflowWithAgent(props =>
            {
                props.DeadlineMinutes = 15;
                props.MaxBudgetUsd = 2.50m;
                props.MaxTurns = 300;
                props.MaxRetainedOutputMib = 264;
            }),
            AgentTestData.EnabledRuntime(),
            AgentTestData.RuntimeLimits()));
    }

    [Fact]
    public void AFuseAtOrBelowZero_IsAFuseValueViolation_NeverAlsoACeilingOne()
    {
        List<AgentBlockGuard.AgentBlockViolation> violations = AgentBlockGuard.ValidateGraph(
            WorkflowWithAgent(props => props.MaxTurns = -1),
            AgentTestData.EnabledRuntime(),
            AgentTestData.RuntimeLimits());

        AgentBlockGuard.AgentBlockViolation violation = Assert.Single(violations);
        Assert.Equal(AgentBlockGuard.AgentViolationKind.FuseValue, violation.Kind);
    }

    [Fact]
    public void DeclaresAnyFuse_AnswersTrueOnlyWhenAnAgentBlockDeclaresOne()
    {
        Assert.False(AgentBlockGuard.DeclaresAnyFuse(WorkflowWithAgent()));
        Assert.True(AgentBlockGuard.DeclaresAnyFuse(WorkflowWithAgent(props => props.MaxBudgetUsd = 1m)));

        var plain = new Workflow("Plain") { Title = "Plain" };
        var section = new Section("Body", plain);
        _ = new Block(BlockTypes.AiQuery, section, "aiBlock");
        Assert.False(AgentBlockGuard.DeclaresAnyFuse(plain));
    }

    [Fact]
    public void DescribeSubmitRefusal_DisabledRuntimeWithAgentBlocks_NamesTheBlocksAndTheFlag()
    {
        AgentBlockGuard.AgentBlockViolation? refusal = AgentBlockGuard.DescribeSubmitRefusal(
            WorkflowWithAgent(), new AgentRuntimeSettings());

        Assert.NotNull(refusal);
        Assert.Contains("agentBlock", refusal.Message, StringComparison.Ordinal);
        Assert.Contains(nameof(AgentRuntimeSettings.Enabled), refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void DescribeSubmitRefusal_EnabledRuntime_OrNoAgentBlocks_AnswersNull()
    {
        Assert.Null(AgentBlockGuard.DescribeSubmitRefusal(
            WorkflowWithAgent(), AgentTestData.EnabledRuntime()));

        var plain = new Workflow("Plain") { Title = "Plain" };
        var section = new Section("Body", plain);
        _ = new Block(BlockTypes.AiQuery, section, "aiBlock");
        Assert.Null(AgentBlockGuard.DescribeSubmitRefusal(plain, new AgentRuntimeSettings()));
    }

    [Fact]
    public void AgentInsideParameterizedSection_IsAllowed()
    {
        var workflow = new Workflow("ParamGuard") { Title = "Param guard" };
        var parameterized = new Section("Parameterized", workflow)
        {
            ParameterizedConfig = AgentTestData.PARAMETERIZED_CONFIG_JSON,
        };
        _ = AgentTestData.AddAgent(parameterized, "agentBlock");

        Assert.Empty(AgentBlockGuard.ValidateGraph(workflow, AgentTestData.EnabledRuntime()));
    }

    [Fact]
    public void AgentInsideLoopBody_IsAllowed()
    {
        var workflow = new Workflow("LoopGuard") { Title = "Loop guard" };
        Section looped = LoopedSectionTestData.AddLoopedSection(workflow, producerTag: "looped_tag");
        _ = LoopedSectionTestData.AddAgentInsideLoopBody(looped, "agentBlock");
        ((AgentProperties)looped.Blocks.Single(b => b.Type == BlockTypes.Agent).Properties).Model =
            AgentTestData.EnabledRuntime().AllowedModels[0];

        Assert.Empty(AgentBlockGuard.ValidateGraph(workflow, AgentTestData.EnabledRuntime()));
    }

    [Fact]
    public void SkillReferencingLoopedProducersWholeOutput_IsAllowed()
    {
        Workflow workflow = WorkflowWithAgent(
            props => AgentTestData.ReplaceSkill(props, AgentTestData.SKILL_NAME, "Read {{looped_tag}}."));
        _ = LoopedSectionTestData.AddLoopedSection(workflow, producerTag: "looped_tag");

        Assert.Empty(AgentBlockGuard.ValidateGraph(workflow, AgentTestData.EnabledRuntime()));
    }

    [Fact]
    public void SkillReferencingALoopedProducersField_WarnsAndDoesNotBlock()
    {
        Workflow workflow = WorkflowWithAgent(
            props => AgentTestData.ReplaceSkill(props, AgentTestData.SKILL_NAME, "Read {{looped_tag.answer}}."));
        _ = LoopedSectionTestData.AddLoopedSection(workflow, producerTag: "looped_tag");

        Assert.Empty(AgentBlockGuard.ValidateGraph(workflow, AgentTestData.EnabledRuntime()));
        string warning = Assert.Single(AgentTemplateGrammar.LoopProducerWarnings(workflow));
        Assert.Contains("inside a loop", warning);
        Assert.Contains("'{{looped_tag}}'", warning);
    }

    [Fact]
    public void DescribeBlockRunRefusal_AnAgentInsideALoopBody_IsRefused()
    {
        var workflow = new Workflow("LoopRun") { Title = "Loop run" };
        Section looped = LoopedSectionTestData.AddLoopedSection(workflow, producerTag: "looped_tag");
        Block agent = LoopedSectionTestData.AddAgentInsideLoopBody(looped, "agentBlock");

        AgentBlockGuard.AgentBlockViolation? refusal =
            AgentBlockGuard.DescribeBlockRunRefusal(agent, AgentTestData.EnabledRuntime());

        Assert.NotNull(refusal);
        Assert.Contains("Run the section instead", refusal.Message);
    }

    [Fact]
    public void TaskPromptReferencingAParameterizedProducerFromOutside_WarnsNamingThePrompt()
    {
        Workflow workflow = WorkflowWithAgent(props => props.PromptTemplate = "Summarise {{param_tag.answer}}.");
        var parameterized = new Section("Parameterized", workflow)
        {
            ParameterizedConfig = AgentTestData.PARAMETERIZED_CONFIG_JSON,
        };
        _ = new Block(BlockTypes.Merge, parameterized, replacementTag: "param_tag")
        {
            Properties = new MergeProperties { TextTemplate = "per variant" },
        };

        Assert.Empty(AgentBlockGuard.ValidateGraph(workflow, AgentTestData.EnabledRuntime()));
        string warning = Assert.Single(AgentTemplateGrammar.LoopProducerWarnings(workflow));
        Assert.StartsWith("The task prompt of agent block ", warning);
        Assert.Contains("'param_tag_<variant key>'", warning);
    }

    [Fact]
    public void ValidOutputSchema_NoViolations()
    {
        Assert.Empty(AgentBlockGuard.ValidateGraph(
            WorkflowWithAgent(static props => props.OutputSchema =
                """{"type":"object","additionalProperties":false,"required":["status"],"properties":{"status":{"type":"string"}}}"""),
            AgentTestData.EnabledRuntime()));
    }

    [Fact]
    public void OutputSchemaWithUnknownKeyword_ClassifiedAsOutputSchemaInvalid_WithItsPath()
    {
        List<AgentBlockGuard.AgentBlockViolation> violations = AgentBlockGuard.ValidateGraph(
            WorkflowWithAgent(static props => props.OutputSchema =
                """{"type":"object","properties":{"total":{"type":"number","minmum":0}}}"""),
            AgentTestData.EnabledRuntime());

        AgentBlockGuard.AgentBlockViolation violation = Assert.Single(violations);
        Assert.Equal(AgentBlockGuard.AgentViolationKind.OutputSchemaInvalid, violation.Kind);
        Assert.Contains("#/properties/total", violation.Message);
        Assert.Contains("minmum", violation.Message);
    }

    [Fact]
    public void OutputSchemaThatIsNotJson_ClassifiedAsOutputSchemaInvalid()
    {
        List<AgentBlockGuard.AgentBlockViolation> violations = AgentBlockGuard.ValidateGraph(
            WorkflowWithAgent(static props => props.OutputSchema = "{not json"),
            AgentTestData.EnabledRuntime());

        AgentBlockGuard.AgentBlockViolation violation = Assert.Single(violations);
        Assert.Equal(AgentBlockGuard.AgentViolationKind.OutputSchemaInvalid, violation.Kind);
    }

    [Fact]
    public void OutputSchemaThatIsNotAnObject_ClassifiedAsOutputSchemaInvalid()
    {
        List<AgentBlockGuard.AgentBlockViolation> violations = AgentBlockGuard.ValidateGraph(
            WorkflowWithAgent(static props => props.OutputSchema = "[1, 2]"),
            AgentTestData.EnabledRuntime());

        AgentBlockGuard.AgentBlockViolation violation = Assert.Single(violations);
        Assert.Equal(AgentBlockGuard.AgentViolationKind.OutputSchemaInvalid, violation.Kind);
    }

    [Fact]
    public void OutputSchemaWithSeveralProblems_ReportsEachPath()
    {
        List<AgentBlockGuard.AgentBlockViolation> violations = AgentBlockGuard.ValidateGraph(
            WorkflowWithAgent(static props => props.OutputSchema =
                """{"type":"objct","propertys":{}}"""),
            AgentTestData.EnabledRuntime());

        AgentBlockGuard.AgentBlockViolation violation = Assert.Single(violations);
        Assert.Equal(AgentBlockGuard.AgentViolationKind.OutputSchemaInvalid, violation.Kind);
        Assert.Contains("#/type", violation.Message);
        Assert.Contains("propertys", violation.Message);
    }

    [Fact]
    public void ValidateDefinition_BlockDefinition_ChecksBlockRules()
    {
        List<AgentBlockGuard.AgentBlockViolation> violations = AgentBlockGuard.ValidateDefinition(
            AgentTestData.AgentBlockDefinition(model: "rogue-model"),
            sectionDefinition: null,
            AgentTestData.EnabledRuntime());

        AgentBlockGuard.AgentBlockViolation violation = Assert.Single(violations);
        Assert.Equal(AgentBlockGuard.AgentViolationKind.ModelNotAllowed, violation.Kind);
    }

    [Fact]
    public void ValidateDefinition_ParameterizedSectionDefinition_IsAllowed()
    {
        List<AgentBlockGuard.AgentBlockViolation> violations = AgentBlockGuard.ValidateDefinition(
            blockDefinition: null,
            AgentTestData.AgentSection(parameterized: true),
            AgentTestData.EnabledRuntime());

        Assert.Empty(violations);
    }

    [Theory]
    [InlineData("Use /skills:extraction on the staged documents.")]
    [InlineData("Read /docs/report.pdf, then extract word1 /word2 pairs.")]
    [InlineData("Use /skills:{{chosenSkill}} for this filing.")]
    [InlineData("Emphasis around **/skills:extraction** is ordinary markdown.")]
    [InlineData("Invoke skills with /skills:<name> as documented.")]
    [InlineData("")]
    public void PromptInvokingTheBlockSkill_OrOrdinaryProse_IsValid(string prompt)
    {
        Assert.Empty(AgentBlockGuard.ValidateGraph(
            WorkflowWithAgent(props => props.PromptTemplate = prompt),
            AgentTestData.EnabledRuntime()));
    }

    [Fact]
    public void PromptInvokingAnUnknownSkillName_ClassifiedAsSkillInvocationUnknown_NamingNameAndBlock()
    {
        List<AgentBlockGuard.AgentBlockViolation> violations = AgentBlockGuard.ValidateGraph(
            WorkflowWithAgent(static props => props.PromptTemplate = "Use /skills:extractoin now."),
            AgentTestData.EnabledRuntime());

        AgentBlockGuard.AgentBlockViolation violation = Assert.Single(violations);
        Assert.Equal(AgentBlockGuard.AgentViolationKind.SkillInvocationUnknown, violation.Kind);
        Assert.Contains("/skills:extractoin", violation.Message);
        Assert.Contains("agentBlock", violation.Message);
    }

    [Fact]
    public void PromptMisspellingTheBlockSkill_SuggestsTheResolvingInvocation()
    {
        List<AgentBlockGuard.AgentBlockViolation> violations = AgentBlockGuard.ValidateGraph(
            WorkflowWithAgent(static props => props.PromptTemplate = "Use /skills:Extraction.md now."),
            AgentTestData.EnabledRuntime());

        AgentBlockGuard.AgentBlockViolation violation = Assert.Single(violations);
        Assert.Equal(AgentBlockGuard.AgentViolationKind.SkillInvocationUnknown, violation.Kind);
        Assert.Contains("/skills:extraction", violation.Message);
    }

    [Fact]
    public void PromptInvokingThePrefixWithoutAName_ClassifiedAsSkillInvocationUnknown()
    {
        List<AgentBlockGuard.AgentBlockViolation> violations = AgentBlockGuard.ValidateGraph(
            WorkflowWithAgent(static props => props.PromptTemplate = "Invoke /skills: to begin."),
            AgentTestData.EnabledRuntime());

        AgentBlockGuard.AgentBlockViolation violation = Assert.Single(violations);
        Assert.Equal(AgentBlockGuard.AgentViolationKind.SkillInvocationUnknown, violation.Kind);
        Assert.Contains("no skill name", violation.Message);
        Assert.Contains("/skills:<name>", violation.Message);
    }

    [Fact]
    public void PromptInvokingTheBlockSkillBare_ClassifiedAsSkillInvocationBareForm_PointingAtThePrefixedForm()
    {
        List<AgentBlockGuard.AgentBlockViolation> violations = AgentBlockGuard.ValidateGraph(
            WorkflowWithAgent(static props => props.PromptTemplate = "Start with /extraction on page one."),
            AgentTestData.EnabledRuntime());

        AgentBlockGuard.AgentBlockViolation violation = Assert.Single(violations);
        Assert.Equal(AgentBlockGuard.AgentViolationKind.SkillInvocationBareForm, violation.Kind);
        Assert.Contains("/skills:extraction", violation.Message);
        Assert.Contains("agentBlock", violation.Message);
    }

    [Fact]
    public void SystemPromptInvocations_AreValidatedLikeThePrompt()
    {
        List<AgentBlockGuard.AgentBlockViolation> violations = AgentBlockGuard.ValidateGraph(
            WorkflowWithAgent(static props => props.SystemPromptTemplate = "Always run /skills:missing first."),
            AgentTestData.EnabledRuntime());

        AgentBlockGuard.AgentBlockViolation violation = Assert.Single(violations);
        Assert.Equal(AgentBlockGuard.AgentViolationKind.SkillInvocationUnknown, violation.Kind);
        Assert.Contains("system prompt", violation.Message);
    }

    [Fact]
    public void InvocationsResolveAgainstTheOwnBlocksSkillsOnly()
    {
        var workflow = new Workflow("GuardTest") { Title = "Guard test" };
        var section = new Section("Body", workflow);
        Block first = AgentTestData.AddAgent(section, "firstAgent");
        ((AgentProperties)first.Properties).WorkerSkills[0].Name = "first-skill";
        Block second = AgentTestData.AddAgent(section, "secondAgent");
        ((AgentProperties)second.Properties).PromptTemplate = "Use /skills:first-skill now.";

        List<AgentBlockGuard.AgentBlockViolation> violations =
            AgentBlockGuard.ValidateGraph(workflow, AgentTestData.EnabledRuntime());

        AgentBlockGuard.AgentBlockViolation violation = Assert.Single(violations);
        Assert.Equal(AgentBlockGuard.AgentViolationKind.SkillInvocationUnknown, violation.Kind);
        Assert.Contains("secondAgent", violation.Message);
    }

    private const string TIER_BEDROCK_OPUS = "us.anthropic.claude-opus-5-5";
    private const string TIER_BEDROCK_SONNET = "us.anthropic.claude-sonnet-5-5";
    private const string TIER_NEUTRAL_OPUS = "claude-opus-5-5";
    private const string TIER_NEUTRAL_SONNET = "claude-sonnet-5-5";
    private const string TIER_ORG = "tier-org";
    private const string FOUNDRY_SONNET = "claude-sonnet-5";
    private const string FOUNDRY_HAIKU = "claude-haiku-4-5";
    private const string BEDROCK_SONNET = "us.anthropic.claude-sonnet-5";
    private const string BEDROCK_HAIKU = "us.anthropic.claude-haiku-4-5-20251001-v1:0";
    private const string BEDROCK_OPUS = "us.anthropic.claude-opus-5";
    private const string SUBMIT_REFUSED_BEFORE_SPEND = "nothing was spent";

    #region Provider forms — any provider's id of an allowlisted model is the same model

    [Fact]
    public void FoundryAllowlist_FoundryDeploymentName_IsAccepted()
    {
        Assert.Empty(AgentBlockGuard.ValidateGraph(
            WorkflowWithAgent(props => props.Model = FOUNDRY_SONNET),
            AgentTestData.EnabledRuntime(FOUNDRY_SONNET, FOUNDRY_HAIKU)));
    }

    [Fact]
    public void FoundryAllowlist_BedrockIdOfAnAllowedModel_IsAccepted()
    {
        Assert.Empty(AgentBlockGuard.ValidateGraph(
            WorkflowWithAgent(props => props.Model = BEDROCK_SONNET),
            AgentTestData.EnabledRuntime(FOUNDRY_SONNET, FOUNDRY_HAIKU)));
    }

    [Fact]
    public void BedrockAllowlist_NeutralIdOfAnAllowedModel_IsAccepted()
    {
        Assert.Empty(AgentBlockGuard.ValidateGraph(
            WorkflowWithAgent(props => props.Model = FOUNDRY_SONNET),
            AgentTestData.EnabledRuntime(BEDROCK_SONNET, BEDROCK_HAIKU)));
    }

    [Fact]
    public void BedrockAllowlist_AnotherRegionsProfileOfAnAllowedModel_IsAccepted()
    {
        Assert.Empty(AgentBlockGuard.ValidateGraph(
            WorkflowWithAgent(props => props.Model = "eu.anthropic.claude-sonnet-5"),
            AgentTestData.EnabledRuntime(BEDROCK_SONNET, BEDROCK_HAIKU)));
    }

    [Fact]
    public void FoundryAllowlist_BedrockIdOfAModelNotAllowed_RefusedWithTheNeutralList()
    {
        List<AgentBlockGuard.AgentBlockViolation> violations = AgentBlockGuard.ValidateGraph(
            WorkflowWithAgent(props => props.Model = BEDROCK_OPUS),
            AgentTestData.EnabledRuntime(FOUNDRY_SONNET, FOUNDRY_HAIKU));

        AgentBlockGuard.AgentBlockViolation violation = Assert.Single(violations);
        Assert.Equal(AgentBlockGuard.AgentViolationKind.ModelNotAllowed, violation.Kind);
        Assert.Contains(
            $"selects model '{BEDROCK_OPUS}', which is not in the allowed model list ({FOUNDRY_SONNET}, {FOUNDRY_HAIKU})",
            violation.Message);
    }

    [Fact]
    public void BedrockAllowlist_RefusalListsTheNeutralIds()
    {
        List<AgentBlockGuard.AgentBlockViolation> violations = AgentBlockGuard.ValidateGraph(
            WorkflowWithAgent(props => props.Model = "claude-opus-5"),
            AgentTestData.EnabledRuntime(BEDROCK_SONNET, BEDROCK_HAIKU));

        AgentBlockGuard.AgentBlockViolation violation = Assert.Single(violations);
        Assert.Equal(AgentBlockGuard.AgentViolationKind.ModelNotAllowed, violation.Kind);
        Assert.Contains(
            "which is not in the allowed model list (claude-sonnet-5, claude-haiku-4-5-20251001)",
            violation.Message);
    }

    #endregion

    #region A model gateway — an allowlist of the US inference profiles the gateway serves

    private static readonly string[] _gatewayAllowlist =
    [
        "us.anthropic.claude-sonnet-4-6",
        "us.anthropic.claude-sonnet-5-5",
        "us.anthropic.claude-opus-4-8",
        "us.anthropic.claude-opus-5",
        "us.anthropic.claude-opus-5-5",
    ];

    private const string GATEWAY_UNPRICED_HAIKU = "claude-haiku-5-5";

    [Theory]
    [InlineData("claude-sonnet-4-6")]
    [InlineData("us.anthropic.claude-sonnet-4-6")]
    [InlineData("claude-opus-4-8")]
    public void GatewayAllowlist_AnyFormOfAnAllowedModel_IsAccepted(string model)
    {
        Assert.Empty(AgentBlockGuard.ValidateGraph(
            WorkflowWithAgent(props => props.Model = model),
            AgentTestData.EnabledRuntime(_gatewayAllowlist)));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void GatewayAllowlist_BlankModel_TheDefaultTierModelsAreAllowed(bool shouldUseAdvancedModel)
    {
        Assert.Empty(AgentBlockGuard.ValidateGraph(
            WorkflowWithAgent(props =>
            {
                props.Model = null;
                props.ShouldUseAdvancedModel = shouldUseAdvancedModel;
            }),
            AgentTestData.EnabledRuntime(_gatewayAllowlist)));
    }

    [Fact]
    public void GatewayAllowlist_AModelTheGatewayDoesNotPrice_RefusedWithTheNeutralList()
    {
        AgentBlockGuard.AgentBlockViolation violation = Assert.Single(AgentBlockGuard.ValidateGraph(
            WorkflowWithAgent(props => props.Model = GATEWAY_UNPRICED_HAIKU),
            AgentTestData.EnabledRuntime(_gatewayAllowlist)));

        Assert.Equal(AgentBlockGuard.AgentViolationKind.ModelNotAllowed, violation.Kind);
        Assert.Contains(
            $"selects model '{GATEWAY_UNPRICED_HAIKU}', which is not in the allowed model list (claude-sonnet-4-6, "
            + "claude-sonnet-5-5, claude-opus-4-8, claude-opus-5, claude-opus-5-5)",
            violation.Message);
    }

    [Fact]
    public void GatewayAllowlist_DescribeSubmitRefusal_AllowsAnAllowedModelAndRefusesAnUnpricedOneBeforeSpend()
    {
        Assert.Null(AgentBlockGuard.DescribeSubmitRefusal(
            WorkflowWithAgent(props => props.Model = "claude-sonnet-4-6"),
            AgentTestData.EnabledRuntime(_gatewayAllowlist)));

        AgentBlockGuard.AgentBlockViolation? refusal = AgentBlockGuard.DescribeSubmitRefusal(
            WorkflowWithAgent(props => props.Model = GATEWAY_UNPRICED_HAIKU),
            AgentTestData.EnabledRuntime(_gatewayAllowlist));

        Assert.NotNull(refusal);
        Assert.Equal(AgentBlockGuard.AgentViolationKind.ModelNotAllowed, refusal.Kind);
        Assert.Contains(SUBMIT_REFUSED_BEFORE_SPEND, refusal.Message);
    }

    #endregion

    #region Submit-time gate — refuses before any execution row exists

    [Fact]
    public void DescribeSubmitRefusal_DisabledRuntime_RefusesAsRuntimeDisabled()
    {
        AgentBlockGuard.AgentBlockViolation? refusal = AgentBlockGuard.DescribeSubmitRefusal(
            WorkflowWithAgent(), new AgentRuntimeSettings());

        Assert.NotNull(refusal);
        Assert.Equal(AgentBlockGuard.AgentViolationKind.RuntimeDisabled, refusal.Kind);
        Assert.Contains("agent runtime is disabled", refusal.Message);
        Assert.Contains(SUBMIT_REFUSED_BEFORE_SPEND, refusal.Message);
    }

    [Fact]
    public void DescribeSubmitRefusal_PlainWorkflow_AllowsSubmissionEvenWhenDisabled()
    {
        var workflow = new Workflow("Plain") { Title = "Plain" };
        _ = new Block(BlockTypes.AiQuery, new Section("Body", workflow), "aiBlock");

        Assert.Null(AgentBlockGuard.DescribeSubmitRefusal(workflow, new AgentRuntimeSettings()));
    }

    [Fact]
    public void DescribeSubmitRefusal_ModelInTheAllowlist_AllowsSubmission()
    {
        Assert.Null(AgentBlockGuard.DescribeSubmitRefusal(
            WorkflowWithAgent(props => props.Model = FOUNDRY_SONNET),
            AgentTestData.EnabledRuntime(FOUNDRY_SONNET)));
    }

    [Fact]
    public void DescribeSubmitRefusal_ModelNotAllowedInAnyForm_RefusesBeforeAnySpend()
    {
        AgentBlockGuard.AgentBlockViolation? refusal = AgentBlockGuard.DescribeSubmitRefusal(
            WorkflowWithAgent(props => props.Model = BEDROCK_OPUS),
            AgentTestData.EnabledRuntime(FOUNDRY_SONNET, FOUNDRY_HAIKU));

        Assert.NotNull(refusal);
        Assert.Equal(AgentBlockGuard.AgentViolationKind.ModelNotAllowed, refusal.Kind);
        Assert.Contains(
            $"selects model '{BEDROCK_OPUS}', which is not in the allowed model list ({FOUNDRY_SONNET}, {FOUNDRY_HAIKU})",
            refusal.Message);
        Assert.Contains(SUBMIT_REFUSED_BEFORE_SPEND, refusal.Message);
    }

    [Fact]
    public void DescribeSubmitRefusal_OtherProvidersFormOfAnAllowedModel_AllowsSubmission()
    {
        Assert.Null(AgentBlockGuard.DescribeSubmitRefusal(
            WorkflowWithAgent(props => props.Model = BEDROCK_SONNET),
            AgentTestData.EnabledRuntime(FOUNDRY_SONNET, FOUNDRY_HAIKU)));
        Assert.Null(AgentBlockGuard.DescribeSubmitRefusal(
            WorkflowWithAgent(props => props.Model = FOUNDRY_SONNET),
            AgentTestData.EnabledRuntime(BEDROCK_SONNET)));
    }

    [Fact]
    public void DescribeSubmitRefusal_BlankModelWithAnUnallowedTierModel_RefusesAsTierModelNotAllowed()
    {
        AgentBlockGuard.AgentBlockViolation? refusal = AgentBlockGuard.DescribeSubmitRefusal(
            WorkflowWithAgent(props => props.Model = null),
            AgentTestData.EnabledRuntime(FOUNDRY_SONNET));

        Assert.NotNull(refusal);
        Assert.Equal(AgentBlockGuard.AgentViolationKind.TierModelNotAllowed, refusal.Kind);
        Assert.Contains(SUBMIT_REFUSED_BEFORE_SPEND, refusal.Message);
    }

    [Fact]
    public void DescribeSubmitRefusal_BlankModelWithAnAllowedTierModel_AllowsTheSubmission()
    {
        Assert.Null(AgentBlockGuard.DescribeSubmitRefusal(
            WorkflowWithAgent(props => props.Model = null),
            AgentTestData.EnabledRuntime(TIER_BEDROCK_OPUS)));
    }

    [Fact]
    public void DescribeSubmitRefusal_VerifierOutsideTheAllowlist_Refuses()
    {
        Workflow workflow = WorkflowWithAgent(props => props.Model = FOUNDRY_SONNET);
        Block verifier = AgentTestData.AddVerifier(workflow.Sections.First(), "verifier", "agentBlock");
        ((VerificationProperties)verifier.Properties).Model = BEDROCK_OPUS;

        AgentBlockGuard.AgentBlockViolation? refusal = AgentBlockGuard.DescribeSubmitRefusal(
            workflow, AgentTestData.EnabledRuntime(FOUNDRY_SONNET));

        Assert.NotNull(refusal);
        Assert.Equal(AgentBlockGuard.AgentViolationKind.ModelNotAllowed, refusal.Kind);
        Assert.Contains("Verification block 'verifier'", refusal.Message);
        Assert.Contains(BEDROCK_OPUS, refusal.Message);
    }

    #endregion

    #region Block-run gate — the editor's single-block run, before its documents are processed

    private const string BLOCK_RUN_REFUSED_BEFORE_SPEND = "before its documents were processed";

    [Fact]
    public void DescribeBlockRunRefusal_ModelInTheAllowlist_AllowsTheRun()
    {
        Block block = WorkflowWithAgent(props => props.Model = FOUNDRY_SONNET).GetAllBlocks().Single();

        Assert.Null(AgentBlockGuard.DescribeBlockRunRefusal(block, AgentTestData.EnabledRuntime(FOUNDRY_SONNET)));
    }

    [Fact]
    public void DescribeBlockRunRefusal_PlainBlock_AllowsTheRunEvenWhenDisabled()
    {
        var workflow = new Workflow("Plain") { Title = "Plain" };
        var block = new Block(BlockTypes.AiQuery, new Section("Body", workflow), "aiBlock");

        Assert.Null(AgentBlockGuard.DescribeBlockRunRefusal(block, new AgentRuntimeSettings()));
    }

    [Fact]
    public void DescribeBlockRunRefusal_DisabledRuntime_RefusesAsRuntimeDisabled()
    {
        Block block = WorkflowWithAgent().GetAllBlocks().Single();

        AgentBlockGuard.AgentBlockViolation? refusal = AgentBlockGuard.DescribeBlockRunRefusal(
            block, new AgentRuntimeSettings());

        Assert.NotNull(refusal);
        Assert.Equal(AgentBlockGuard.AgentViolationKind.RuntimeDisabled, refusal.Kind);
        Assert.Contains("agentBlock", refusal.Message);
        Assert.Contains("agent runtime is disabled", refusal.Message);
        Assert.Contains(BLOCK_RUN_REFUSED_BEFORE_SPEND, refusal.Message);
        Assert.Contains(SUBMIT_REFUSED_BEFORE_SPEND, refusal.Message);
    }

    [Fact]
    public void DescribeBlockRunRefusal_ModelNotAllowedInAnyForm_RefusesBeforeDocumentsAreProcessed()
    {
        Block block = WorkflowWithAgent(props => props.Model = BEDROCK_OPUS).GetAllBlocks().Single();

        AgentBlockGuard.AgentBlockViolation? refusal = AgentBlockGuard.DescribeBlockRunRefusal(
            block, AgentTestData.EnabledRuntime(FOUNDRY_SONNET, FOUNDRY_HAIKU));

        Assert.NotNull(refusal);
        Assert.Equal(AgentBlockGuard.AgentViolationKind.ModelNotAllowed, refusal.Kind);
        Assert.Contains(
            $"selects model '{BEDROCK_OPUS}', which is not in the allowed model list ({FOUNDRY_SONNET}, {FOUNDRY_HAIKU})",
            refusal.Message);
        Assert.Contains(BLOCK_RUN_REFUSED_BEFORE_SPEND, refusal.Message);
        Assert.Contains(SUBMIT_REFUSED_BEFORE_SPEND, refusal.Message);
    }

    [Fact]
    public void DescribeBlockRunRefusal_BlankModelWithAnUnallowedTierModel_RefusesAsTierModelNotAllowed()
    {
        Block block = WorkflowWithAgent(props => props.Model = null).GetAllBlocks().Single();

        AgentBlockGuard.AgentBlockViolation? refusal = AgentBlockGuard.DescribeBlockRunRefusal(
            block, AgentTestData.EnabledRuntime(FOUNDRY_SONNET));

        Assert.NotNull(refusal);
        Assert.Equal(AgentBlockGuard.AgentViolationKind.TierModelNotAllowed, refusal.Kind);
        Assert.Contains(BLOCK_RUN_REFUSED_BEFORE_SPEND, refusal.Message);
    }

    [Fact]
    public void DescribeBlockRunRefusal_JudgesOnlyTheBlockBeingRun()
    {
        Workflow workflow = WorkflowWithAgent(props => props.Model = FOUNDRY_SONNET);
        Block verifier = AgentTestData.AddVerifier(workflow.Sections.First(), "verifier", "agentBlock");
        ((VerificationProperties)verifier.Properties).Model = BEDROCK_OPUS;
        Block agent = workflow.GetAllBlocks().Single(static block => block.Type == BlockTypes.Agent);
        AgentRuntimeSettings runtime = AgentTestData.EnabledRuntime(FOUNDRY_SONNET);

        Assert.Null(AgentBlockGuard.DescribeBlockRunRefusal(agent, runtime));

        AgentBlockGuard.AgentBlockViolation? refusal = AgentBlockGuard.DescribeBlockRunRefusal(verifier, runtime);
        Assert.NotNull(refusal);
        Assert.Equal(AgentBlockGuard.AgentViolationKind.ModelNotAllowed, refusal.Kind);
        Assert.Contains("Verification block 'verifier'", refusal.Message);
        Assert.Contains(BEDROCK_OPUS, refusal.Message);
    }

    #endregion

    [Fact]
    public void OutputFilesWithDistinctStems_NoViolations()
    {
        Assert.Empty(AgentBlockGuard.ValidateGraph(
            WorkflowWithAgent(props => props.OutputFiles = ["out/sub_1_rows.json", "out/checks/totals.json"]),
            AgentTestData.EnabledRuntime()));
    }

    [Fact]
    public void TwoOutputFilesWithOneStem_ClassifiedAsOutputFilesInvalid()
    {
        List<AgentBlockGuard.AgentBlockViolation> violations = AgentBlockGuard.ValidateGraph(
            WorkflowWithAgent(props => props.OutputFiles = ["out/a/rows.json", "out/b/rows.json"]),
            AgentTestData.EnabledRuntime());

        AgentBlockGuard.AgentBlockViolation violation = Assert.Single(violations);
        Assert.Equal(AgentBlockGuard.AgentViolationKind.OutputFilesInvalid, violation.Kind);
        Assert.Contains("'rows'", violation.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("rows.json")]
    [InlineData("out/rows.csv")]
    [InlineData("out/../rows.json")]
    [InlineData("out/rows.v2.json")]
    [InlineData("trace/rows.json")]
    [InlineData("")]
    public void OutputFileOutsideTheShape_ClassifiedAsOutputFilesInvalid(string path)
    {
        List<AgentBlockGuard.AgentBlockViolation> violations = AgentBlockGuard.ValidateGraph(
            WorkflowWithAgent(props => props.OutputFiles = [path]),
            AgentTestData.EnabledRuntime());

        AgentBlockGuard.AgentBlockViolation violation = Assert.Single(violations);
        Assert.Equal(AgentBlockGuard.AgentViolationKind.OutputFilesInvalid, violation.Kind);
    }

    private static Workflow WorkflowWithAgent(Action<AgentProperties>? configure = null)
    {
        var workflow = new Workflow("GuardTest") { Title = "Guard test" };
        var section = new Section("Body", workflow);
        Block block = AgentTestData.AddAgent(section, "agentBlock");
        configure?.Invoke((AgentProperties)block.Properties);

        return workflow;
    }
}
