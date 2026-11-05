using IniaAi.Application.Common.Workflows;
using IniaAi.Common.Constants;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Blocks;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties;
using IniaAi.Domain.Entities.Workflows.Enums;

namespace IniaAi.Application.UnitTests.Common.Workflows;

public class AgentTemplateGrammarTests
{
    private const string AGENT_TAG = "extraction_agent";
    private const string SKILL_NAME = "extraction";
    private const string UPSTREAM_TAG = "upstream_tag";

    private static Workflow BuildWorkflow(
        string skillContent,
        Action<Workflow, Section>? arrange = null)
    {
        var workflow = new Workflow("Grammar test") { Title = "Grammar test" };
        var section = new Section("Body", workflow);

        _ = new Block(BlockTypes.Merge, section, replacementTag: UPSTREAM_TAG)
        {
            Properties = new MergeProperties { TextTemplate = "base" },
        };

        var agent = new Block(BlockTypes.Agent, section, replacementTag: AGENT_TAG);
        var props = (AgentProperties)agent.Properties;
        AgentTestData.ReplaceSkill(props, SKILL_NAME, skillContent);
        props.Model = "test-model-a";

        arrange?.Invoke(workflow, section);

        return workflow;
    }

    private static Section AddParameterizedSection(Workflow workflow, string producerTag)
    {
        var parameterized = new Section("Parameterized", workflow)
        {
            Order = 6,
            ParameterizedConfig = """{"Variants":[{"Params":{"Region":"EU"}}]}""",
        };
        _ = new Block(BlockTypes.Merge, parameterized, replacementTag: producerTag)
        {
            Properties = new MergeProperties { TextTemplate = "parameterized" },
        };
        return parameterized;
    }

    [Fact]
    public void FindUnknownTags_KnownProducers_AreNotFlagged()
    {
        Workflow workflow = BuildWorkflow(
            "Use {{upstream_tag}} with {{Region}}, {{Contract}}, {{" + SharedInputKeys.COUNTERPARTY + "}}, "
            + "{{ManagerName}}, and {{focus_area}}.",
            (workflow, section) =>
            {
                _ = new WorkflowVariable(workflow, "Region");
                _ = new DocumentClass(workflow, "Contract");
                _ = new WorkflowSignatureField(workflow, "ManagerName");
                Block agent = section.Blocks.Single(b => b.Type == BlockTypes.Agent);
                _ = new TextInput(agent, "focus_area", "credit risk");
            });

        Assert.Empty(AgentTemplateGrammar.FindUnknownTags(workflow));
    }

    [Fact]
    public void FindUnknownTags_ParametersOfTheAgentsOwnParameterizedSection_AreKnown()
    {
        var workflow = new Workflow("Param scope") { Title = "Param scope" };
        Section parameterized = new Section("Parameterized", workflow)
        {
            ParameterizedConfig = """{"Variants":[{"Key":"N","Label":"North","Params":{"_REGION":"North"}}]}""",
        };
        var agent = new Block(BlockTypes.Agent, parameterized, replacementTag: AGENT_TAG);
        AgentTestData.ReplaceSkill((AgentProperties)agent.Properties, SKILL_NAME, "Cover {{_REGION}}.");

        Assert.Empty(AgentTemplateGrammar.FindUnknownTags(workflow));
    }

    [Fact]
    public void FindUnknownTags_AParameterOfAnotherSection_IsUnknown()
    {
        Workflow workflow = BuildWorkflow(
            "Cover {{_REGION}}.",
            (workflow, _) => _ = new Section("Parameterized", workflow)
            {
                ParameterizedConfig = """{"Variants":[{"Key":"N","Label":"North","Params":{"_REGION":"North"}}]}""",
            });

        AgentTemplateGrammar.UnknownTagReference reference = Assert.Single(AgentTemplateGrammar.FindUnknownTags(workflow));
        Assert.Equal("_REGION", reference.Tag);
    }

    [Fact]
    public void FindUnknownTags_TheLoopItemInsideTheLoopBody_IsKnown()
    {
        var workflow = new Workflow("Loop scope") { Title = "Loop scope" };
        Section looped = LoopedSectionTestData.AddLoopedSection(workflow, producerTag: "looped_tag");
        Block agent = LoopedSectionTestData.AddAgentInsideLoopBody(looped, AGENT_TAG);
        AgentTestData.ReplaceSkill((AgentProperties)agent.Properties, "extraction", "Fund: {{Funds}}.");

        Assert.Empty(AgentTemplateGrammar.FindUnknownTags(workflow));
    }

    [Fact]
    public void FindUnknownTags_AVariantTagReadFromOutsideTheSection_IsKnown()
    {
        Workflow workflow = BuildWorkflow(
            "Read {{param_tag_N}}.",
            (workflow, _) =>
            {
                Section parameterized = AddParameterizedSection(workflow, producerTag: "param_tag");
                parameterized.ParameterizedConfig = """{"Variants":[{"Key":"N","Label":"North"}]}""";
            });

        Assert.Empty(AgentTemplateGrammar.FindUnknownTags(workflow));
    }

    [Fact]
    public void FindUnknownTags_UnknownTag_IsFlaggedWithBlockSkillAndTag()
    {
        Workflow workflow = BuildWorkflow("Consult {{ghost}} before answering.");

        List<AgentTemplateGrammar.UnknownTagReference> unknown =
            AgentTemplateGrammar.FindUnknownTags(workflow);

        AgentTemplateGrammar.UnknownTagReference reference = Assert.Single(unknown);
        Assert.Equal(AGENT_TAG, reference.BlockTag);
        Assert.Equal($"skill '{SKILL_NAME}'", reference.Location.Description);
        Assert.Equal("ghost", reference.Tag);
    }

    [Fact]
    public void FindUnknownTags_UnknownTagInASkillFileFlaggedToFillTags_IsFlaggedUnderTheFile()
    {
        Workflow workflow = BuildWorkflow("Run the staged script.", static (_, section) =>
            AgentSkillOf(section).Assets =
            [
                new AgentWorkerSkillAsset { Name = "filled.py", Content = "x = '{{ghost}}'", FillTags = true },
            ]);

        AgentTemplateGrammar.UnknownTagReference reference = Assert.Single(AgentTemplateGrammar.FindUnknownTags(workflow));
        Assert.Equal(AGENT_TAG, reference.BlockTag);
        Assert.Equal($"skill '{SKILL_NAME} (file filled.py)'", reference.Location.Description);
        Assert.Equal("ghost", reference.Tag);
    }

    [Fact]
    public void FindUnknownTags_TagShapedTextInAnUnflaggedSkillFile_IsNeverFlagged()
    {
        Workflow workflow = BuildWorkflow("Run the staged script.", static (_, section) =>
            AgentSkillOf(section).Assets =
            [
                new AgentWorkerSkillAsset { Name = "verify.py", Content = "print(f'{{ghost}}')" },
            ]);

        Assert.Empty(AgentTemplateGrammar.FindUnknownTags(workflow));
    }

    [Fact]
    public void FindUnknownTags_AKnownProducerReadFromAFlaggedSkillFile_IsNotFlagged()
    {
        Workflow workflow = BuildWorkflow("Run the staged script.", static (_, section) =>
            AgentSkillOf(section).Assets =
            [
                new AgentWorkerSkillAsset { Name = "filled.py", Content = $"x = '{{{{{UPSTREAM_TAG}.answer}}}}'", FillTags = true },
            ]);

        Assert.Empty(AgentTemplateGrammar.FindUnknownTags(workflow));
    }

    private static AgentWorkerSkill AgentSkillOf(Section section)
    {
        Block agent = section.Blocks.Single(static block => block.ReplacementTag == AGENT_TAG);
        return ((AgentProperties)agent.Properties).WorkerSkills.Single();
    }

    [Fact]
    public void FindUnknownTags_UnknownTagInTheTaskPromptOrTheSystemPromptOverride_IsFlagged()
    {
        Workflow workflow = BuildWorkflow(
            "base",
            (_, section) =>
            {
                var props = (AgentProperties)section.Blocks.Single(b => b.Type == BlockTypes.Agent).Properties;
                props.PromptTemplate = "Summarise {{upstream_tag}} and {{upstrem_tag}}.";
                props.SystemPromptTemplate = "You answer for {{client_nmae}}.";
            });

        List<AgentTemplateGrammar.UnknownTagReference> unknown = AgentTemplateGrammar.FindUnknownTags(workflow);

        Assert.Equal(
            [
                new AgentTemplateGrammar.UnknownTagReference(AGENT_TAG, AgentTemplateLocation.Prompt("the task prompt"), "upstrem_tag"),
                new AgentTemplateGrammar.UnknownTagReference(AGENT_TAG, AgentTemplateLocation.Prompt("the system prompt override"), "client_nmae"),
            ],
            unknown);
    }

    [Fact]
    public void FindUnknownTags_UnknownTagInAVerifierPrompt_IsFlaggedUnderItsOwnFieldName()
    {
        Workflow workflow = BuildWorkflow(
            "base",
            (_, section) =>
            {
                Block verifier = AgentTestData.AddVerifier(section, "group_verifier", AGENT_TAG);
                var props = (VerificationProperties)verifier.Properties;
                props.PromptTemplate = "Judge against {{ghost}}.";
            });

        AgentTemplateGrammar.UnknownTagReference reference =
            Assert.Single(AgentTemplateGrammar.FindUnknownTags(workflow));
        Assert.Equal(
            new AgentTemplateGrammar.UnknownTagReference("group_verifier", AgentTemplateLocation.Prompt("the extra judging instructions"), "ghost"),
            reference);
    }

    [Fact]
    public void FindUnknownTags_DottedUnknown_KeepsFullTagForm()
    {
        Workflow workflow = BuildWorkflow("Read {{ghost.answer}}.");

        AgentTemplateGrammar.UnknownTagReference reference =
            Assert.Single(AgentTemplateGrammar.FindUnknownTags(workflow));
        Assert.Equal("ghost.answer", reference.Tag);
    }

    [Fact]
    public void FindUnknownTags_DottedReferenceToKnownProducer_IsNotFlagged()
    {
        Workflow workflow = BuildWorkflow("Read {{upstream_tag.answer}} and {{upstream_tag}}.");

        Assert.Empty(AgentTemplateGrammar.FindUnknownTags(workflow));
    }

    [Fact]
    public void FindUnknownTags_StrategyNamespaces_AreNeverFlagged()
    {
        Workflow workflow = BuildWorkflow("Loop on {{Validator.decision}} and {{Verifier.verdict}}.");

        Assert.Empty(AgentTemplateGrammar.FindUnknownTags(workflow));
    }

    [Fact]
    public void FindUnknownTags_SingleBraceContent_IsNeverFlagged()
    {
        Workflow workflow = BuildWorkflow("""Emit {ghost} and {"total": 0} literally.""");

        Assert.Empty(AgentTemplateGrammar.FindUnknownTags(workflow));
    }

    [Fact]
    public void FindUnknownTags_NonAgentBlocks_AreIgnored()
    {
        var workflow = new Workflow("No agents") { Title = "No agents" };
        var section = new Section("Body", workflow);
        _ = new Block(BlockTypes.AiQuery, section, replacementTag: "ai_block")
        {
            Properties = new AiQueryProperties { UserPromptTemplate = "Prompt with {{ghost}}." },
        };

        Assert.Empty(AgentTemplateGrammar.FindUnknownTags(workflow));
    }

    [Fact]
    public void FindLoopScopeViolations_AgentInsideLoopBody_NoViolation()
    {
        var workflow = new Workflow("Looped agent") { Title = "Looped agent" };
        Section looped = LoopedSectionTestData.AddLoopedSection(workflow, producerTag: "looped_tag");
        _ = LoopedSectionTestData.AddAgentInsideLoopBody(looped, AGENT_TAG);

        Assert.Empty(AgentTemplateGrammar.FindLoopScopeViolations(workflow));
    }

    [Fact]
    public void FindLoopScopeViolations_VerifierInsideLoopBody_IsViolation()
    {
        var workflow = new Workflow("Looped verifier") { Title = "Looped verifier" };
        Section looped = LoopedSectionTestData.AddLoopedSection(workflow, producerTag: "looped_tag");
        _ = new Block(BlockTypes.Verification, looped, index: 2, replacementTag: "group_verifier");

        AgentTemplateGrammar.LoopScopeViolation violation =
            Assert.Single(AgentTemplateGrammar.FindLoopScopeViolations(workflow));
        Assert.Equal("group_verifier", violation.BlockTag);
        Assert.Equal(BlockTypes.Verification, violation.BlockType);
    }

    [Fact]
    public void FindLoopScopeViolations_AgentAfterLoopEnd_NoViolation()
    {
        // Loop membership is order-bounded (Section.GetLoopBodyBlockIds) — a block after the
        // LoopEnd in the same section is never rewritten per iteration, so it is a legal spot.
        var workflow = new Workflow("After loop") { Title = "After loop" };
        Section looped = LoopedSectionTestData.AddLoopedSection(workflow, producerTag: "looped_tag");
        var agent = new Block(BlockTypes.Agent, looped, replacementTag: AGENT_TAG);
        AgentTestData.ReplaceSkill((AgentProperties)agent.Properties, SKILL_NAME, "Work.");

        Assert.Empty(AgentTemplateGrammar.FindLoopScopeViolations(workflow));
    }

    [Fact]
    public void FindLoopScopeViolations_DanglingLoopStart_NoViolation()
    {
        var workflow = new Workflow("Dangling loop") { Title = "Dangling loop" };
        var section = new Section("Half-formed", workflow);
        _ = new Block(BlockTypes.LoopStart, section)
        {
            Properties = new LoopStartProperties { ListTitle = "Funds", LoopedListTemplate = "Fund A" },
        };
        var agent = new Block(BlockTypes.Agent, section, replacementTag: AGENT_TAG);
        AgentTestData.ReplaceSkill((AgentProperties)agent.Properties, SKILL_NAME, "Work.");

        Assert.Empty(AgentTemplateGrammar.FindLoopScopeViolations(workflow));
    }

    [Fact]
    public void FindLoopScopeViolations_AgentInsideParameterizedSection_NoViolation()
    {
        var workflow = new Workflow("Param agent") { Title = "Param agent" };
        Section parameterized = AddParameterizedSection(workflow, producerTag: "param_tag");
        var agent = new Block(BlockTypes.Agent, parameterized, replacementTag: AGENT_TAG);
        AgentTestData.ReplaceSkill((AgentProperties)agent.Properties, SKILL_NAME, "Work.");

        Assert.Empty(AgentTemplateGrammar.FindLoopScopeViolations(workflow));
    }

    [Fact]
    public void FindLoopScopeViolations_VerifierInsideParameterizedSection_IsViolation()
    {
        var workflow = new Workflow("Param verifier") { Title = "Param verifier" };
        Section parameterized = AddParameterizedSection(workflow, producerTag: "param_tag");
        _ = new Block(BlockTypes.Verification, parameterized, replacementTag: "group_verifier");

        AgentTemplateGrammar.LoopScopeViolation violation =
            Assert.Single(AgentTemplateGrammar.FindLoopScopeViolations(workflow));
        Assert.Equal("group_verifier", violation.BlockTag);
    }

    [Fact]
    public void FindLoopScopeViolations_AgentInPlainSection_NoViolation()
    {
        Workflow workflow = BuildWorkflow("Work.");

        Assert.Empty(AgentTemplateGrammar.FindLoopScopeViolations(workflow));
    }

    [Fact]
    public void FindLoopScopeViolations_NonAgentBlocksInLoops_NoViolation()
    {
        var workflow = new Workflow("Plain loop") { Title = "Plain loop" };
        _ = LoopedSectionTestData.AddLoopedSection(workflow, producerTag: "looped_tag");

        Assert.Empty(AgentTemplateGrammar.FindLoopScopeViolations(workflow));
    }

    [Fact]
    public void FindLoopProducerViolations_AgentReadingALoopedProducersWholeOutput_NoViolation()
    {
        // After the loop the reference receives every iteration's output, one per line.
        Workflow workflow = BuildWorkflow(
            "Read {{looped_tag}}.",
            (workflow, _) => LoopedSectionTestData.AddLoopedSection(workflow, producerTag: "looped_tag"));

        Assert.Empty(AgentTemplateGrammar.FindLoopProducerViolations(workflow));
    }

    [Fact]
    public void FindLoopProducerViolations_AgentReadingALoopedProducersFieldFromAfterTheLoop_IsViolation()
    {
        // The fan-in only rewrites the bare tag, so a field path would reach the run unresolved.
        Workflow workflow = BuildWorkflow(
            "Read {{looped_tag.answer}}.",
            (workflow, _) => LoopedSectionTestData.AddLoopedSection(workflow, producerTag: "looped_tag"));

        AgentTemplateGrammar.LoopProducerViolation violation =
            Assert.Single(AgentTemplateGrammar.FindLoopProducerViolations(workflow));
        Assert.Equal("looped_tag.answer", violation.Tag);
        Assert.False(violation.ProducerIsParameterized);
    }

    [Fact]
    public void FindLoopProducerViolations_AgentReadingASiblingsFieldInsideItsOwnLoopBody_NoViolation()
    {
        var workflow = new Workflow("Loop sibling") { Title = "Loop sibling" };
        Section looped = LoopedSectionTestData.AddLoopedSection(workflow, producerTag: "looped_tag");
        Block agent = LoopedSectionTestData.AddAgentInsideLoopBody(looped, AGENT_TAG);
        AgentTestData.ReplaceSkill((AgentProperties)agent.Properties, "extraction", "Read {{looped_tag.answer}}.");

        Assert.Empty(AgentTemplateGrammar.FindLoopProducerViolations(workflow));
    }

    [Fact]
    public void FindUnknownTags_TheLoopItemInsideALoopInAParameterizedSection_IsKnown()
    {
        var workflow = new Workflow("Loop in variant") { Title = "Loop in variant" };
        Section looped = LoopedSectionTestData.AddLoopedSection(workflow, producerTag: "looped_tag");
        looped.ParameterizedConfig = """{"Variants":[{"Key":"N","Label":"North"}]}""";
        Block agent = LoopedSectionTestData.AddAgentInsideLoopBody(looped, AGENT_TAG);
        AgentTestData.ReplaceSkill((AgentProperties)agent.Properties, "extraction", "Fund: {{Funds}}.");

        Assert.Empty(AgentTemplateGrammar.FindUnknownTags(workflow));
    }

    [Fact]
    public void FindLoopProducerViolations_AgentReadingAParameterizedProducerFromOutside_IsViolation()
    {
        Workflow workflow = BuildWorkflow(
            "Read {{param_tag.answer}}.",
            (workflow, _) => AddParameterizedSection(workflow, producerTag: "param_tag"));

        AgentTemplateGrammar.LoopProducerViolation violation =
            Assert.Single(AgentTemplateGrammar.FindLoopProducerViolations(workflow));
        Assert.Equal(AGENT_TAG, violation.BlockTag);
        Assert.Equal(SKILL_NAME, violation.SkillName);
        Assert.Equal("param_tag.answer", violation.Tag);
        Assert.Equal("param_tag", violation.ProducerTag);
        Assert.Equal(BlockTypes.Agent, violation.BlockType);
    }

    [Fact]
    public void FindLoopProducerViolations_AgentReadingASiblingInItsOwnParameterizedSection_NoViolation()
    {
        var workflow = new Workflow("Param sibling") { Title = "Param sibling" };
        Section parameterized = AddParameterizedSection(workflow, producerTag: "param_tag");
        var agent = new Block(BlockTypes.Agent, parameterized, replacementTag: AGENT_TAG);
        AgentTestData.ReplaceSkill((AgentProperties)agent.Properties, SKILL_NAME, "Read {{param_tag}}.");

        Assert.Empty(AgentTemplateGrammar.FindLoopProducerViolations(workflow));
    }

    [Fact]
    public void FindLoopProducerViolations_VerifierSkillReadingALoopedProducer_CarriesTheVerificationType()
    {
        Workflow workflow = BuildWorkflow(
            "base",
            (workflow, section) =>
            {
                _ = LoopedSectionTestData.AddLoopedSection(workflow, producerTag: "looped_tag");
                var verifier = new Block(BlockTypes.Verification, section, replacementTag: "group_verifier");
                var props = (VerificationProperties)verifier.Properties;
                props.WorkerSkills =
                [
                    new AgentWorkerSkill
                    {
                        Name = "rubric",
                        Description = "How to judge the group.",
                        Content = "Read {{looped_tag.answer}}.",
                    },
                ];
            });

        AgentTemplateGrammar.LoopProducerViolation violation =
            Assert.Single(AgentTemplateGrammar.FindLoopProducerViolations(workflow));
        Assert.Equal("group_verifier", violation.BlockTag);
        Assert.Equal(BlockTypes.Verification, violation.BlockType);
    }

    [Fact]
    public void FindLoopProducerViolations_TaskPromptReadingAParameterizedProducer_NamesThePrompt()
    {
        Workflow workflow = BuildWorkflow(
            "base",
            (workflow, section) =>
            {
                _ = AddParameterizedSection(workflow, producerTag: "looped_tag");
                var props = (AgentProperties)section.Blocks.Single(b => b.Type == BlockTypes.Agent).Properties;
                props.PromptTemplate = "Summarise {{looped_tag}}.";
            });

        AgentTemplateGrammar.LoopProducerViolation violation =
            Assert.Single(AgentTemplateGrammar.FindLoopProducerViolations(workflow));
        Assert.Equal(AGENT_TAG, violation.BlockTag);
        Assert.Null(violation.SkillName);
        Assert.Equal(AgentTemplateLocation.Prompt("the task prompt"), violation.Location);
        Assert.Equal("looped_tag", violation.ProducerTag);
    }

    [Fact]
    public void FindSkillsReferencingField_ASystemPromptOverrideReference_IsFound()
    {
        Workflow workflow = BuildWorkflow(
            "base",
            (_, section) =>
            {
                var props = (AgentProperties)section.Blocks.Single(b => b.Type == BlockTypes.Agent).Properties;
                props.SystemPromptTemplate = "Sign as {{ManagerName}}.";
            });

        AgentTemplateGrammar.SkillFieldReference reference =
            Assert.Single(AgentTemplateGrammar.FindSkillsReferencingField(workflow, "managername"));
        Assert.Equal(new AgentTemplateGrammar.SkillFieldReference(AGENT_TAG, AgentTemplateLocation.Prompt("the system prompt override")), reference);
    }

    [Fact]
    public void FindLoopProducerViolations_ReferenceToProducerAfterLoopEnd_NoViolation()
    {
        Workflow workflow = BuildWorkflow(
            "Read {{summary_tag}}.",
            (workflow, section) =>
            {
                Section looped = LoopedSectionTestData.AddLoopedSection(workflow, producerTag: "looped_tag");
                _ = new Block(BlockTypes.Merge, looped, replacementTag: "summary_tag")
                {
                    Properties = new MergeProperties { TextTemplate = "after the loop" },
                };
            });

        Assert.Empty(AgentTemplateGrammar.FindLoopProducerViolations(workflow));
    }

    [Fact]
    public void FindLoopProducerViolations_ReferenceToParameterizedProducer_IsViolation()
    {
        Workflow workflow = BuildWorkflow(
            "Read {{param_tag}}.",
            (workflow, _) => AddParameterizedSection(workflow, producerTag: "param_tag"));

        AgentTemplateGrammar.LoopProducerViolation violation =
            Assert.Single(AgentTemplateGrammar.FindLoopProducerViolations(workflow));
        Assert.Equal("param_tag", violation.ProducerTag);
    }

    [Fact]
    public void FindLoopProducerViolations_ReferenceToPlainProducer_NoViolation()
    {
        Workflow workflow = BuildWorkflow("Read {{upstream_tag}}.");

        Assert.Empty(AgentTemplateGrammar.FindLoopProducerViolations(workflow));
    }

    [Fact]
    public void FindSkillsReferencingField_DoubleBraceReference_IsFound()
    {
        Workflow workflow = BuildWorkflow(
            "Sign as {{ManagerName}}.",
            (workflow, section) =>
            {
                _ = new WorkflowSignatureField(workflow, "ManagerName");
            });

        AgentTemplateGrammar.SkillFieldReference reference =
            Assert.Single(AgentTemplateGrammar.FindSkillsReferencingField(workflow, "ManagerName"));
        Assert.Equal(AGENT_TAG, reference.BlockTag);
        Assert.Equal(SKILL_NAME, reference.SkillName);
    }

    [Fact]
    public void FindSkillsReferencingField_MatchIsCaseInsensitive()
    {
        Workflow workflow = BuildWorkflow("Sign as {{managername}}.");

        _ = Assert.Single(AgentTemplateGrammar.FindSkillsReferencingField(workflow, "ManagerName"));
    }

    [Fact]
    public void FindSkillsReferencingField_SingleBraceReference_IsNotFound()
    {
        Workflow workflow = BuildWorkflow("Sign as {ManagerName}.");

        Assert.Empty(AgentTemplateGrammar.FindSkillsReferencingField(workflow, "ManagerName"));
    }
}
