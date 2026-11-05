using IniaAi.Common.Enums;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Blocks;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties;
using IniaAi.Domain.Entities.Workflows.Enums;
using IniaAi.Domain.Entities.Workflows.Responses;

namespace IniaAi.Domain.Tests.Data.Workflows;

public class CloneMethodsTests
{
    [Fact]
    public void Block_CloneDetached_KeepsContentButBelongsToNoSection()
    {
        var workflow = new Workflow("Report");
        var section = new Section("Body", workflow);
        var source = new Block(BlockTypes.AiQuery, section, "overviews")
        {
            Name = "Page Overviews",
            Properties = new AiQueryProperties { UserPromptTemplate = "Summarise the page." },
        };
        _ = new TextInput(source, "inputTag", "inputValue");

        Block clone = source.CloneDetached();

        Assert.NotEqual(source.Id, clone.Id);
        Assert.Null(clone.Section);
        Assert.Null(clone.SectionId);
        Assert.DoesNotContain(clone, section.Blocks);
        Assert.Equal(source.Name, clone.Name);
        Assert.Equal(BlockTypes.AiQuery, clone.Type);
        Assert.NotSame(source.Properties, clone.Properties);
        Assert.Equal(
            ((AiQueryProperties)source.Properties).UserPromptTemplate,
            ((AiQueryProperties)clone.Properties).UserPromptTemplate);
        Assert.Equal("inputValue", clone.TextInputs.Single().Value);
    }

    [Fact]
    public void TextInput_CloneToBlock_CopiesEveryFieldAndReparents()
    {
        var sourceBlock = new Block(BlockTypes.AiQuery);
        var targetBlock = new Block(BlockTypes.AiQuery);

        var source = new TextInput(sourceBlock, "inputTag", "inputValue");

        TextInput clone = source.CloneToBlock(targetBlock);

        CloneAssertions.AssertAllCloneableFieldsCopied(source, clone);

        Assert.NotSame(source, clone);
        Assert.NotEqual(source.Id, clone.Id);
        Assert.Same(targetBlock, clone.Block);
        Assert.Equal(targetBlock.Id, clone.BlockId);
        Assert.Equal("inputTag", clone.ReplacementTag);
        Assert.Equal("inputValue", clone.Value);
        Assert.Contains(clone, targetBlock.TextInputs);
        Assert.DoesNotContain(clone, sourceBlock.TextInputs);
    }

    [Fact]
    public void RagSettings_CloneToBlock_CopiesEveryFieldAndDuplicatesLists()
    {
        var sourceBlock = new Block(BlockTypes.AiQuery);
        var targetBlock = new Block(BlockTypes.AiQuery);

        var source = new RagSettings(sourceBlock)
        {
            Type = RagTypes.KeywordBased,
            ExactIndexesInput = "1,2,3",
            NumberOfPagesInput = "5",
            StartIndexInput = "10",
            EndIndexInput = "20",
            TableOfContentsInput = "toc",
            PageAdjustmentInput = "1",
            TopicsQuery = "topic?",
            QueryTemplate = "query {tag}",
            IterationLimit = "3",
            ContextLimit = "5000",
            ShouldFilterKeyWordsExclusively = true,
            ShouldSearchOverviews = true,
            ShouldIncludeNeighboringPages = true,
            Keywords = ["risk", "return"],
            SmartFilterExpressions = ["section=intro"],
        };

        RagSettings clone = source.CloneToBlock(targetBlock);

        CloneAssertions.AssertAllCloneableFieldsCopied(source, clone);

        Assert.NotSame(source, clone);
        Assert.Same(targetBlock, clone.Block);
        Assert.Equal(targetBlock.Id, clone.BlockId);

        Assert.NotSame(source.Keywords, clone.Keywords);
        Assert.NotSame(source.SmartFilterExpressions, clone.SmartFilterExpressions);
    }

    [Fact]
    public void BlockResponse_CloneToBlock_CopiesEveryFieldAndSharesBinaryResponse()
    {
        var sourceBlock = new Block(BlockTypes.AiQuery);
        var targetBlock = new Block(BlockTypes.AiQuery);

        byte[] binary = [0x01, 0x02, 0x03];
        var source = new BlockResponse(sourceBlock)
        {
            RawResponse = "raw",
            JsonResponse = "{\"a\":1}",
            HtmlResponse = "<p>html</p>",
            MarkdownResponse = "**md**",
            DebugInfo = "debug",
            RegressionFacts = "{\"facts\":[]}",
            BinaryResponse = binary,
            MediaType = MediaType.ApplicationPdf,
        };

        BlockResponse clone = source.CloneToBlock(targetBlock);

        CloneAssertions.AssertAllCloneableFieldsCopied(source, clone);

        Assert.NotSame(source, clone);
        Assert.Same(targetBlock, clone.Block);
        Assert.Equal(targetBlock.Id, clone.BlockId);
        Assert.Same(binary, clone.BinaryResponse); // shared reference by design (preserved from pre-refactor)
    }

    [Fact]
    public void WorkflowVariable_CloneToWorkflow_CopiesEveryFieldAndDuplicatesOptions()
    {
        var sourceWorkflow = new Workflow("source");
        var targetWorkflow = new Workflow("target");

        var source = new WorkflowVariable(sourceWorkflow, "myVar")
        {
            DisplayName = "My Variable",
            Description = "a thing",
            Type = WorkflowVariableTypes.Dropdown,
            IsRequired = true,
            IsHidden = false,
            IsConfidential = true,
            DefaultValue = "default",
            Options = new Dictionary<string, string>
            {
                { "opt1", "Option One" },
                { "opt2", "Option Two" },
            },
        };

        WorkflowVariable clone = source.CloneToWorkflow(targetWorkflow);

        CloneAssertions.AssertAllCloneableFieldsCopied(source, clone);

        Assert.NotSame(source, clone);
        Assert.NotEqual(source.Id, clone.Id);
        Assert.Same(targetWorkflow, clone.Workflow);
        Assert.Equal(targetWorkflow.Id, clone.WorkflowId);
        Assert.NotSame(source.Options, clone.Options);
        Assert.Equal(2, clone.Options.Count);
        Assert.Equal("Option One", clone.Options["opt1"]);
    }

    [Fact]
    public void DocumentClass_CloneToWorkflow_CopiesEveryFieldAndReparents()
    {
        var sourceWorkflow = new Workflow("source");
        var targetWorkflow = new Workflow("target");

        var source = new DocumentClass(sourceWorkflow, "myClass")
        {
            DisplayName = "My Class",
            Description = "describes something",
            Requirement = DocumentClassRequirement.Required,
            ShouldBeSingular = true,
            IsOutputDocument = true,
            DocumentCategoryId = "cat-123",
        };

        DocumentClass clone = source.CloneToWorkflow(targetWorkflow);

        CloneAssertions.AssertAllCloneableFieldsCopied(source, clone);

        Assert.NotSame(source, clone);
        Assert.NotEqual(source.Id, clone.Id);
        Assert.Same(targetWorkflow, clone.Workflow);
        Assert.Equal(targetWorkflow.Id, clone.WorkflowId);
        Assert.Equal("myClass", clone.Key);
        Assert.Contains(clone, targetWorkflow.DocumentClasses);
    }

    [Fact]
    public void Workflow_CloneTo_WithPrePopulatedDestination_ReplacesVariablesAndDocumentClassesInsteadOfAccumulating()
    {
        var source = new Workflow("source");
        _ = new WorkflowVariable(source, "sourceVar");
        _ = new DocumentClass(source, "sourceClass");

        var destination = new Workflow("destination");
        _ = new WorkflowVariable(destination, "staleVar");
        _ = new DocumentClass(destination, "staleClass");

        source.CloneTo(destination);

        Assert.Single(destination.Variables);
        Assert.Equal("sourceVar", destination.Variables.First().ReplacementTag);
        Assert.Single(destination.DocumentClasses);
        Assert.Equal("sourceClass", destination.DocumentClasses.First().Key);
    }

    [Fact]
    public void Workflow_CloneTo_CopiesEveryScalarAndWalksChildren()
    {
        var source = new Workflow("source")
        {
            Title = "Source Title",
            Description = "source description",
            TextInputTemplate = "template {tag}",
            ParameterizedConfig = "{\"variant\":\"A\"}",
            UserId = "user-123",
            OrganizationId = "org-456",
            WorkflowTypeId = "wftype-789",
            Configuration = new WorkflowConfiguration
            {
                Version = "2.0",
                CustomSettings = new Dictionary<string, object>
                {
                    { "key", "value" },
                },
            },
        };
        var sourceSection = new Section("Sec 1", source);
        _ = new Block(BlockTypes.Merge, sourceSection) { Name = "Merge Block" };
        _ = new WorkflowVariable(source, "var1") { DisplayName = "Var 1" };

        var destination = new Workflow();
        source.CloneTo(destination);

        CloneAssertions.AssertAllCloneableFieldsCopied(source, destination);

        Assert.Equal("source", destination.Name);
        Assert.Equal("Source Title", destination.Title);
        Assert.Equal("source description", destination.Description);
        Assert.Null(destination.UserId);
        Assert.Null(destination.OrganizationId);

        Assert.NotNull(destination.Configuration);
        Assert.NotSame(source.Configuration, destination.Configuration);
        Assert.Equal("2.0", destination.Configuration.Version);
        Assert.NotNull(destination.Configuration.CustomSettings);
        Assert.NotSame(source.Configuration.CustomSettings, destination.Configuration.CustomSettings);

        Section destinationSection = Assert.Single(destination.Sections);
        Block destinationBlock = Assert.Single(destinationSection.Blocks);
        Assert.Equal("Merge Block", destinationBlock.Name);

        Assert.Single(destination.Variables);
        Assert.Equal("Var 1", destination.Variables.First().DisplayName);
    }

    [Fact]
    public void Workflow_CloneToPreservingIds_CarriesIdsAcrossTheEntireGraph()
    {
        var source = new Workflow("source");
        _ = new WorkflowVariable(source, "var1");
        _ = new DocumentClass(source, "class1");

        var sourceChild = new Section("child", source) { Order = 0 };
        var sourceChildBlock = new Block(BlockTypes.AiQuery, sourceChild)
        {
            Name = "Child Block",
            Properties = new AiQueryProperties { UserPromptTemplate = "prompt" },
        };
        _ = new TextInput(sourceChildBlock, "input1", "value1");
        _ = new RagSettings(sourceChildBlock) { Type = RagTypes.TopNPages };
        _ = new BlockResponse(sourceChildBlock) { RawResponse = "raw" };

        string sourceWorkflowId = source.Id;
        string sourceVariableId = source.Variables.First().Id;
        string sourceDocumentClassId = source.DocumentClasses.First().Id;
        string sourceChildId = sourceChild.Id;
        string sourceChildBlockId = sourceChildBlock.Id;
        string sourceBlockResponseId = sourceChildBlock.BlockResponse!.Id;
        string sourceTextInputId = sourceChildBlock.TextInputs.First().Id;
        string sourceRagSettingsId = sourceChildBlock.RagSettings.First().Id;

        var destination = new Workflow();
        source.CloneToPreservingIds(destination);

        Assert.Equal(sourceWorkflowId, destination.Id);
        Assert.Equal(sourceVariableId, destination.Variables.First().Id);
        Assert.Equal(sourceDocumentClassId, destination.DocumentClasses.First().Id);

        Section destinationSection = destination.Sections.First();
        Assert.Equal(sourceChildId, destinationSection.Id);
        Block destinationChildBlock = destinationSection.Blocks.First();
        Assert.Equal(sourceChildBlockId, destinationChildBlock.Id);
        Assert.Equal(sourceBlockResponseId, destinationChildBlock.BlockResponse!.Id);
        Assert.Equal(sourceTextInputId, destinationChildBlock.TextInputs.First().Id);
        Assert.Equal(sourceRagSettingsId, destinationChildBlock.RagSettings.First().Id);

        Assert.NotSame(source, destination);
        Assert.NotSame(sourceChildBlock, destinationChildBlock);
    }

    [Fact]
    public void Workflow_CloneToPreservingIds_KeepsForeignKeysPointingAtTheirParent()
    {
        // Regression: CloneToPreservingIds rewrites entity Ids back to the
        // source's, but the FK columns on Block / WorkflowVariable /
        // DocumentClass were left pointing at the transient GUIDs that CloneTo
        // set during the initial copy. Those GUIDs no longer exist anywhere in
        // the cloned tree, leaving every FK dangling — which surfaced as a
        // null lookup when the editor's WorkflowMap clicked through to the
        // section the block claimed to belong to.
        // Root has variables + document classes (a workflow may not hold both
        // children and blocks at the same level — Workflow.Add enforces that —
        // so the parent here uses non-block FK-bearing entities).
        var source = new Workflow("source");
        _ = new WorkflowVariable(source, "var1");
        _ = new DocumentClass(source, "class1");

        var sourceChild = new Section("child", source) { Order = 0 };
        var sourceChildBlock = new Block(BlockTypes.AiQuery, sourceChild) { Name = "Child Block" };
        _ = new TextInput(sourceChildBlock, "input1", "value1");
        _ = new RagSettings(sourceChildBlock) { Type = RagTypes.TopNPages };
        _ = new BlockResponse(sourceChildBlock) { RawResponse = "raw" };

        var destination = new Workflow();
        source.CloneToPreservingIds(destination);

        Assert.Equal(destination.Id, destination.Variables.First().WorkflowId);
        Assert.Equal(destination.Id, destination.DocumentClasses.First().WorkflowId);

        Section destinationSection = destination.Sections.First();
        Block destinationChildBlock = destinationSection.Blocks.First();
        Assert.Equal(destinationSection.Id, destinationChildBlock.SectionId);
        Assert.Equal(destination.Id, destinationSection.WorkflowId);

        Assert.Equal(destinationChildBlock.Id, destinationChildBlock.TextInputs.First().BlockId);
        Assert.Equal(destinationChildBlock.Id, destinationChildBlock.RagSettings.First().BlockId);
        Assert.NotNull(destinationChildBlock.BlockResponse);
        Assert.Equal(destinationChildBlock.Id, destinationChildBlock.BlockResponse.BlockId);
        Assert.Equal(destinationChildBlock.BlockResponse.Id, destinationChildBlock.BlockResponseId);
    }

    [Fact]
    public void Workflow_CloneTo_StillRegeneratesIdsForDuplicationCallers()
    {
        var source = new Workflow("source");
        var sourceChild = new Section("child", source) { Order = 0 };
        _ = new Block(BlockTypes.AiQuery, sourceChild) { Name = "Block A" };

        var destination = new Workflow();
        source.CloneTo(destination);

        Assert.NotEqual(source.Id, destination.Id);
        Assert.NotEqual(sourceChild.Id, destination.Sections.First().Id);
        Assert.NotEqual(
            sourceChild.Blocks.First().Id,
            destination.Sections.First().Blocks.First().Id);
    }
}
