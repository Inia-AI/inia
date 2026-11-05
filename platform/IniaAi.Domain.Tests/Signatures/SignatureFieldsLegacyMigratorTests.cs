using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Blocks;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties.Abstractions;
using IniaAi.Domain.Entities.Workflows.Enums;
using IniaAi.Domain.Signatures;

namespace IniaAi.Domain.Tests.Signatures;

public class SignatureFieldsLegacyMigratorTests
{
    [Fact]
    public void Hoist_IdenticalDefinitionsAcrossBlocks_MergeIntoOneFieldAndDictsAreNulled()
    {
        var workflow = new Workflow("w");
        var section = new Section("s", workflow) { Order = 0 };
        var aiBlock = new Block(BlockTypes.AiQuery, section, "a")
        {
            Order = 0,
            Properties = new AiQueryProperties
            {
                UserPromptTemplate = "A {tone}",
                SignatureFields = new Dictionary<string, SignatureFieldDefinition>
                {
                    ["tone"] = new SignatureFieldDefinition("Tone", "desc", "formal"),
                },
            },
        };
        var mergeBlock = new Block(BlockTypes.Merge, section, "b")
        {
            Order = 1,
            Properties = new MergeProperties
            {
                TextTemplate = "B {tone}",
                SignatureFields = new Dictionary<string, SignatureFieldDefinition>
                {
                    ["tone"] = new SignatureFieldDefinition("Tone", "desc", "formal"),
                },
            },
        };

        SignatureFieldsLegacyMigrator.Hoist(workflow);

        WorkflowSignatureField field = Assert.Single(workflow.SignatureFields);
        Assert.Equal("tone", field.Name);
        Assert.Equal("Tone", field.DisplayName);
        Assert.Equal("desc", field.Description);
        Assert.Equal("formal", field.DefaultValue);

        Assert.Null(((IHasSignatureFields)aiBlock.Properties).SignatureFields);
        Assert.Null(((IHasSignatureFields)mergeBlock.Properties).SignatureFields);

        // Both blocks kept their original tokens — no rename happened.
        Assert.Equal("A {tone}", ((AiQueryProperties)aiBlock.Properties).UserPromptTemplate);
        Assert.Equal("B {tone}", ((MergeProperties)mergeBlock.Properties).TextTemplate);
    }

    [Fact]
    public void Hoist_DivergentDefinitionUnderTakenName_RenamedWithTagAndOnlyThatBlockRewritten()
    {
        var workflow = new Workflow("w");
        var section = new Section("s", workflow) { Order = 0 };
        var first = new Block(BlockTypes.AiQuery, section, "first_tag")
        {
            Order = 0,
            Properties = new AiQueryProperties
            {
                UserPromptTemplate = "First {tone}",
                SignatureFields = new Dictionary<string, SignatureFieldDefinition>
                {
                    ["tone"] = new SignatureFieldDefinition("Tone", "desc", "formal"),
                },
            },
        };
        var second = new Block(BlockTypes.Merge, section, "second_tag")
        {
            Order = 1,
            Properties = new MergeProperties
            {
                TextTemplate = "Use {tone} please.",
                SignatureFields = new Dictionary<string, SignatureFieldDefinition>
                {
                    ["tone"] = new SignatureFieldDefinition("Tone", "desc", "casual"),
                },
            },
        };

        SignatureFieldsLegacyMigrator.Hoist(workflow);

        Assert.Equal(2, workflow.SignatureFields.Count);
        WorkflowSignatureField original = workflow.SignatureFields.Single(f => f.Name == "tone");
        Assert.Equal("formal", original.DefaultValue);
        WorkflowSignatureField renamed = workflow.SignatureFields.Single(f => f.Name == "tone_second_tag");
        Assert.Equal("casual", renamed.DefaultValue);

        // Only the divergent block's tokens follow the rename.
        Assert.Equal("First {tone}", ((AiQueryProperties)first.Properties).UserPromptTemplate);
        Assert.Equal("Use {tone_second_tag} please.", ((MergeProperties)second.Properties).TextTemplate);
    }

    [Fact]
    public void Hoist_FirstBinderInSectionAndBlockOrder_KeepsThePlainName()
    {
        var workflow = new Workflow("w");

        // Created first but ordered second — its divergent definition must NOT win the name.
        var lateSection = new Section("late", workflow) { Order = 1 };
        var lateBlock = new Block(BlockTypes.Merge, lateSection, "late_tag")
        {
            Order = 0,
            Properties = new MergeProperties
            {
                TextTemplate = "{tone}",
                SignatureFields = new Dictionary<string, SignatureFieldDefinition>
                {
                    ["tone"] = new SignatureFieldDefinition("Tone", "desc", "late"),
                },
            },
        };

        var earlySection = new Section("early", workflow) { Order = 0 };
        _ = new Block(BlockTypes.Merge, earlySection, "early_tag")
        {
            Order = 0,
            Properties = new MergeProperties
            {
                TextTemplate = "{tone}",
                SignatureFields = new Dictionary<string, SignatureFieldDefinition>
                {
                    ["tone"] = new SignatureFieldDefinition("Tone", "desc", "early"),
                },
            },
        };

        SignatureFieldsLegacyMigrator.Hoist(workflow);

        WorkflowSignatureField plain = workflow.SignatureFields.Single(f => f.Name == "tone");
        Assert.Equal("early", plain.DefaultValue);
        WorkflowSignatureField renamed = workflow.SignatureFields.Single(f => f.Name == "tone_late_tag");
        Assert.Equal("late", renamed.DefaultValue);
        Assert.Equal("{tone_late_tag}", ((MergeProperties)lateBlock.Properties).TextTemplate);
    }

    [Fact]
    public void Hoist_RunTwice_IsIdempotent()
    {
        var workflow = new Workflow("w");
        var section = new Section("s", workflow) { Order = 0 };
        var block = new Block(BlockTypes.Merge, section, "a")
        {
            Order = 0,
            Properties = new MergeProperties
            {
                TextTemplate = "{tone}",
                SignatureFields = new Dictionary<string, SignatureFieldDefinition>
                {
                    ["tone"] = new SignatureFieldDefinition("Tone", "desc", "formal"),
                },
            },
        };

        SignatureFieldsLegacyMigrator.Hoist(workflow);
        SignatureFieldsLegacyMigrator.Hoist(workflow);

        WorkflowSignatureField field = Assert.Single(workflow.SignatureFields);
        Assert.Equal("tone", field.Name);
        Assert.Equal("formal", field.DefaultValue);
        Assert.Null(((IHasSignatureFields)block.Properties).SignatureFields);
        Assert.Equal("{tone}", ((MergeProperties)block.Properties).TextTemplate);
    }

    [Fact]
    public void Hoist_TagSuffixedNameAlsoTakenByDivergentField_FallsBackToNumericDisambiguator()
    {
        var workflow = new Workflow("w");
        _ = new WorkflowSignatureField(workflow, "tone")
        {
            DisplayName = "Tone",
            Description = "desc",
            DefaultValue = "existing",
        };
        _ = new WorkflowSignatureField(workflow, "tone_btag")
        {
            DisplayName = "Tone",
            Description = "desc",
            DefaultValue = "also divergent",
        };

        var section = new Section("s", workflow) { Order = 0 };
        var block = new Block(BlockTypes.Merge, section, "btag")
        {
            Order = 0,
            Properties = new MergeProperties
            {
                TextTemplate = "{tone}",
                SignatureFields = new Dictionary<string, SignatureFieldDefinition>
                {
                    ["tone"] = new SignatureFieldDefinition("Tone", "desc", "incoming"),
                },
            },
        };

        SignatureFieldsLegacyMigrator.Hoist(workflow);

        Assert.Equal(3, workflow.SignatureFields.Count);
        WorkflowSignatureField landed = workflow.SignatureFields.Single(f => f.Name == "tone_btag_2");
        Assert.Equal("incoming", landed.DefaultValue);
        Assert.Equal("{tone_btag_2}", ((MergeProperties)block.Properties).TextTemplate);
    }

    [Fact]
    public void Hoist_TagSuffixedNameHoldsIdenticalDefinition_MergesIntoItAndRewritesTokens()
    {
        var workflow = new Workflow("w");
        _ = new WorkflowSignatureField(workflow, "tone")
        {
            DisplayName = "Tone",
            Description = "desc",
            DefaultValue = "divergent",
        };
        _ = new WorkflowSignatureField(workflow, "tone_btag")
        {
            DisplayName = "Tone",
            Description = "desc",
            DefaultValue = "incoming",
        };

        var section = new Section("s", workflow) { Order = 0 };
        var block = new Block(BlockTypes.Merge, section, "btag")
        {
            Order = 0,
            Properties = new MergeProperties
            {
                TextTemplate = "{tone}",
                SignatureFields = new Dictionary<string, SignatureFieldDefinition>
                {
                    ["tone"] = new SignatureFieldDefinition("Tone", "desc", "incoming"),
                },
            },
        };

        SignatureFieldsLegacyMigrator.Hoist(workflow);

        // No third field minted — the identical tag-suffixed definition absorbed it.
        Assert.Equal(2, workflow.SignatureFields.Count);
        Assert.Equal("{tone_btag}", ((MergeProperties)block.Properties).TextTemplate);
    }

    [Fact]
    public void HoistDefinition_NullReplacementTag_DisambiguatesOnBareName()
    {
        var workflow = new Workflow("w");
        _ = new WorkflowSignatureField(workflow, "tone")
        {
            DisplayName = "Tone",
            Description = "desc",
            DefaultValue = "existing",
        };

        string landed = SignatureFieldsLegacyMigrator.HoistDefinition(
            workflow,
            "tone",
            new SignatureFieldDefinition("Tone", "desc", "incoming"),
            replacementTag: null);

        Assert.Equal("tone_2", landed);
        WorkflowSignatureField field = workflow.SignatureFields.Single(f => f.Name == "tone_2");
        Assert.Equal("incoming", field.DefaultValue);
    }

    [Fact]
    public void HoistDefinition_NameTakenCaseInsensitively_IdenticalDefinitionMergesKeepingExistingCasing()
    {
        var workflow = new Workflow("w");
        _ = new WorkflowSignatureField(workflow, "Tone")
        {
            DisplayName = "Tone",
            Description = "desc",
            DefaultValue = "formal",
        };

        string landed = SignatureFieldsLegacyMigrator.HoistDefinition(
            workflow,
            "tone",
            new SignatureFieldDefinition("Tone", "desc", "formal"),
            replacementTag: "tag");

        Assert.Equal("Tone", landed);
        _ = Assert.Single(workflow.SignatureFields);
    }
}
