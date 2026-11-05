using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Blocks;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties.Abstractions;
using IniaAi.Domain.Entities.Workflows.Enums;

namespace IniaAi.Domain.Tests.Data.Workflows;

/// <summary>
/// Workflow-level <see cref="WorkflowSignatureField"/> behaviour on the
/// <see cref="Workflow"/> aggregate: clone semantics (parity with RegressionFields),
/// Clear, the FromJson legacy hoist, and the derived token-reference binding.
/// </summary>
public class WorkflowSignatureFieldTests
{
    [Fact]
    public void Workflow_CloneTo_CopiesSignatureFieldsAsNewInstances()
    {
        var source = new Workflow("source");
        var tone = new WorkflowSignatureField(source, "tone")
        {
            DisplayName = "Tone",
            Description = "Writing tone",
            DefaultValue = "formal",
        };
        _ = new WorkflowSignatureField(source, "jurisdiction")
        {
            DisplayName = "Jurisdiction",
            Description = "Governing law",
            DefaultValue = "England and Wales",
        };

        var destination = new Workflow();
        source.CloneTo(destination);

        Assert.Equal(2, destination.SignatureFields.Count);

        WorkflowSignatureField toneClone = destination.SignatureFields.Single(f => f.Name == "tone");
        Assert.NotSame(tone, toneClone);
        Assert.NotEqual(tone.Id, toneClone.Id);
        Assert.Equal("Tone", toneClone.DisplayName);
        Assert.Equal("Writing tone", toneClone.Description);
        Assert.Equal("formal", toneClone.DefaultValue);
        Assert.Same(destination, toneClone.Workflow);
        Assert.Equal(destination.Id, toneClone.WorkflowId);

        WorkflowSignatureField jurisdictionClone =
            destination.SignatureFields.Single(f => f.Name == "jurisdiction");
        Assert.Equal("England and Wales", jurisdictionClone.DefaultValue);
    }

    [Fact]
    public void Workflow_CloneTo_ReplacesStaleSignatureFieldsInsteadOfAccumulating()
    {
        var source = new Workflow("source");
        _ = new WorkflowSignatureField(source, "fresh");

        var destination = new Workflow("destination");
        _ = new WorkflowSignatureField(destination, "stale");

        source.CloneTo(destination);

        WorkflowSignatureField field = Assert.Single(destination.SignatureFields);
        Assert.Equal("fresh", field.Name);
    }

    [Fact]
    public void Workflow_CloneToPreservingIds_CarriesSignatureFieldIds()
    {
        // Id-preservation parity with RegressionFields: editor round-trips must
        // re-land signature field rows on the same DB ids they came from.
        var source = new Workflow("source");
        _ = new WorkflowSignatureField(source, "tone") { DefaultValue = "formal" };
        _ = new WorkflowSignatureField(source, "jurisdiction") { DefaultValue = "E&W" };

        List<string> sourceIds = [.. source.SignatureFields.Select(f => f.Id)];

        var destination = new Workflow();
        source.CloneToPreservingIds(destination);

        List<string> destinationIds = [.. destination.SignatureFields.Select(f => f.Id)];
        Assert.Equal(sourceIds, destinationIds);
    }

    [Fact]
    public void Workflow_Clear_EmptiesSignatureFields()
    {
        var workflow = new Workflow("w");
        _ = new WorkflowSignatureField(workflow, "tone");
        _ = new WorkflowSignatureField(workflow, "jurisdiction");

        workflow.Clear();

        Assert.Empty(workflow.SignatureFields);
    }

    [Fact]
    public void ExportJson_RoundTripsSignatureFields()
    {
        var workflow = new Workflow("Report") { Title = "Report" };
        _ = new WorkflowSignatureField(workflow, "tone")
        {
            DisplayName = "Tone",
            Description = "Writing tone",
            DefaultValue = "formal",
        };

        Workflow restored = Workflow.FromJson(workflow.ToExportJson())!;

        WorkflowSignatureField field = Assert.Single(restored.SignatureFields);
        Assert.Equal("tone", field.Name);
        Assert.Equal("Tone", field.DisplayName);
        Assert.Equal("Writing tone", field.Description);
        Assert.Equal("formal", field.DefaultValue);
        Assert.Same(restored, field.Workflow);
        Assert.Equal(restored.Id, field.WorkflowId);
    }

    [Fact]
    public void FromJson_LegacyInlineBlockDicts_AreHoistedToWorkflowLevel()
    {
        var workflow = new Workflow("Legacy") { Title = "Legacy" };
        var section = new Section("Body", workflow);
        _ = new Block(BlockTypes.Merge, section, "merge")
        {
            Name = "Merge",
            Properties = new MergeProperties
            {
                TextTemplate = "Use {tone} please.",
                SignatureFields = new Dictionary<string, SignatureFieldDefinition>
                {
                    ["tone"] = new SignatureFieldDefinition("Tone", "desc", "formal"),
                },
            },
        };

        Workflow restored = Workflow.FromJson(workflow.ToJson(pretty: false))!;

        WorkflowSignatureField field = Assert.Single(restored.SignatureFields);
        Assert.Equal("tone", field.Name);
        Assert.Equal("formal", field.DefaultValue);

        Block block = restored.GetAllBlocks().Single();
        Assert.Null(((IHasSignatureFields)block.Properties).SignatureFields);
        Assert.Equal("Use {tone} please.", ((MergeProperties)block.Properties).TextTemplate);
    }

    [Fact]
    public void GetSignatureFieldReferences_TokenMatchIsCaseInsensitive()
    {
        var workflow = new Workflow("w");
        _ = new WorkflowSignatureField(workflow, "Tone");
        var section = new Section("s", workflow);
        var block = new Block(BlockTypes.Merge, section, "merge")
        {
            Properties = new MergeProperties { TextTemplate = "Use {tone} please." },
        };

        Dictionary<string, List<Block>> references = workflow.GetSignatureFieldReferences();

        Block referencing = Assert.Single(references["Tone"]);
        Assert.Same(block, referencing);

        // The returned dictionary itself is keyed case-insensitively.
        Assert.True(references.ContainsKey("tone"));
        Assert.True(references.ContainsKey("TONE"));
    }

    [Fact]
    public void GetSignatureFieldReferences_DoubleBracedToken_StillMatches()
    {
        // {{tone}} contains the single-braced {tone} token, so a double-braced
        // reference counts as a binding.
        var workflow = new Workflow("w");
        _ = new WorkflowSignatureField(workflow, "tone");
        var section = new Section("s", workflow);
        var block = new Block(BlockTypes.Merge, section, "merge")
        {
            Properties = new MergeProperties { TextTemplate = "Use {{tone}} please." },
        };

        Dictionary<string, List<Block>> references = workflow.GetSignatureFieldReferences();

        Block referencing = Assert.Single(references["tone"]);
        Assert.Same(block, referencing);
    }

    [Fact]
    public void GetSignatureFieldReferences_UnreferencedDefinition_MapsToEmptyList()
    {
        var workflow = new Workflow("w");
        _ = new WorkflowSignatureField(workflow, "tone");
        _ = new WorkflowSignatureField(workflow, "orphan");
        var section = new Section("s", workflow);
        _ = new Block(BlockTypes.Merge, section, "merge")
        {
            Properties = new MergeProperties { TextTemplate = "Use {tone} please." },
        };

        Dictionary<string, List<Block>> references = workflow.GetSignatureFieldReferences();

        Assert.Equal(2, references.Count);
        _ = Assert.Single(references["tone"]);
        Assert.Empty(references["orphan"]);
    }

    [Fact]
    public void GetSignatureFieldReferences_ReturnsBearingBlocksInSectionThenBlockOrder()
    {
        var workflow = new Workflow("w");
        _ = new WorkflowSignatureField(workflow, "tone");

        var second = new Section("second", workflow) { Order = 1 };
        var laterBlock = new Block(BlockTypes.Merge, second, "later")
        {
            Order = 0,
            Properties = new MergeProperties { TextTemplate = "{tone}" },
        };

        var first = new Section("first", workflow) { Order = 0 };
        var earlierBlock = new Block(BlockTypes.AiQuery, first, "earlier")
        {
            Order = 0,
            Properties = new AiQueryProperties { UserPromptTemplate = "{tone}" },
        };

        Dictionary<string, List<Block>> references = workflow.GetSignatureFieldReferences();

        Assert.Equal(2, references["tone"].Count);
        Assert.Same(earlierBlock, references["tone"][0]);
        Assert.Same(laterBlock, references["tone"][1]);
    }

    [Fact]
    public void GetSignatureFieldReferences_NoDefinitions_ReturnsEmptyDictionary()
    {
        var workflow = new Workflow("w");
        var section = new Section("s", workflow);
        _ = new Block(BlockTypes.Merge, section, "merge")
        {
            Properties = new MergeProperties { TextTemplate = "{tone}" },
        };

        Assert.Empty(workflow.GetSignatureFieldReferences());
    }

    [Fact]
    public void SignatureFieldToken_WrapsNameInSingleBraces()
    {
        Assert.Equal("{tone}", Workflow.SignatureFieldToken("tone"));
    }
}
