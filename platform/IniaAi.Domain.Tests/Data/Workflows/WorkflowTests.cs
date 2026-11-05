using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Blocks;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties.Abstractions;
using IniaAi.Domain.Entities.Workflows.Enums;
using IniaAi.Domain.Entities.Workflows.Responses;
using IniaAi.Domain.Serialization;

namespace IniaAi.Domain.Tests.Data.Workflows;

public class WorkflowTests
{
    [Fact]
    public void GetBlockOutput_ShouldReturnCorrectOutput()
    {
        // Arrange
        var workflow = new Workflow();
        var section = new Section("default", workflow);
        var block = new Block { ReplacementTag = "block1" };
        _ = new BlockResponse(block) { RawResponse = "response" };
        section.Add(block);

        // Act
        string? output = workflow.GetBlockOutput("block1");

        // Assert
        Assert.Equal("response", output);
    }

    [Fact]
    public void GetBlockOutput_WithProperty_ShouldReturnCorrectOutput()
    {
        // Arrange
        var workflow = new Workflow();
        var section = new Section("default", workflow);
        var block = new Block { ReplacementTag = "block1" };
        _ = new BlockResponse(block) { RawResponse = "<answer>response</answer>" };
        section.Add(block);

        // Act
        string? output = workflow.GetBlockOutput("block1.answer");

        // Assert
        Assert.Equal("response", output);
    }

    [Fact]
    public void GetBlockOutput_BlockNotFound_ShouldReturnNull()
    {
        // Arrange
        var workflow = new Workflow();

        // Act
        string? output = workflow.GetBlockOutput("block1");

        // Assert
        Assert.Null(output);
    }

    [Fact]
    public void GetBlockOutput_PropertyNotFound_ShouldReturnNull()
    {
        // Arrange
        var workflow = new Workflow();
        var section = new Section("default", workflow);
        var block = new Block { ReplacementTag = "block1" };
        _ = new BlockResponse(block) { RawResponse = "<answer>response</answer>" };
        section.Add(block);

        // Act
        string? output = workflow.GetBlockOutput("block1.property");

        // Assert
        Assert.Null(output);
    }

    [Fact]
    public void GetDocumentClasses_WithEmptyWorkflow_ShouldReturnEmptyList()
    {
        // Arrange
        var workflow = new Workflow("empty");

        // Act
        List<string> result = workflow.GetDocumentClasses();

        // Assert
        Assert.Empty(result);
    }

    [Fact]
    public void GetDocumentClasses_WithOnlyBlocks_ShouldReturnBlockDocumentClasses()
    {
        // Arrange
        var workflow = new Workflow("root");

        // Add document classes directly to the workflow
        workflow.DocumentClasses.Add(new DocumentClass { Key = "class1" });
        workflow.DocumentClasses.Add(new DocumentClass { Key = "class2" });

        // Act
        List<string> result = workflow.GetDocumentClasses();

        // Assert
        Assert.Collection(result.OrderBy(x => x),
            item => Assert.Equal("class1", item),
            item => Assert.Equal("class2", item)
        );
    }

    [Fact]
    public void GetInputDocumentClasses_ExcludesOutputDocumentClasses()
    {
        var workflow = new Workflow("root");
        workflow.DocumentClasses.Add(new DocumentClass { Key = "input-a" });
        workflow.DocumentClasses.Add(new DocumentClass { Key = "output-x", IsOutputDocument = true });
        workflow.DocumentClasses.Add(new DocumentClass { Key = "input-b" });

        List<string> result = workflow.GetInputDocumentClasses();

        Assert.Equal(["input-a", "input-b"], result);
    }

    [Fact]
    public void ReplacementTagIsInUse_WithEmptyWorkflow_ReturnsFalse()
    {
        // Arrange
        var workflow = new Workflow();

        // Act
        bool result = workflow.ReplacementTagIsInUse("tag1");

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void FromJson_WithInvalidJson_ShouldReturnNull()
    {
        // Arrange
        string json = "invalid json";

        // Act
        var workflow = Workflow.FromJson(json);

        // Assert
        Assert.Null(workflow);
    }

    [Fact]
    public void ClearExecutionState_ShouldClearAllExecutionStates()
    {
        // Arrange
        var workflow = new Workflow("root");
        var section = new Section("default", workflow);

        var block1 = new Block
        {
            Name = "block1",
        };
        block1.SetExecutionState(false, []);
        Assert.NotNull(block1.ExecutionState);
        section.Add(block1);

        var block2 = new Block
        {
            Name = "block2",
        };
        block2.SetExecutionState(false, []);
        Assert.NotNull(block2.ExecutionState);
        section.Add(block2);

        // Act
        workflow.ClearExecutionState();

        // Assert
        Assert.Null(block1.ExecutionState);
        Assert.Null(block2.ExecutionState);
    }

    [Fact]
    public void ClearExecutionState_WithNestedWorkflows_ShouldClearAllExecutionStates()
    {
        // Arrange
        var workflow = new Workflow("root");
        var child = new Section("child2", workflow);

        var block1 = new Block
        {
            Name = "block1",
        };
        block1.SetExecutionState(false, []);
        Assert.NotNull(block1.ExecutionState);
        child.Add(block1);

        var block2 = new Block
        {
            Name = "block2",
        };
        block2.SetExecutionState(false, []);
        Assert.NotNull(block2.ExecutionState);
        child.Add(block2);

        var block3 = new Block
        {
            Name = "block3",
        };
        block3.SetExecutionState(false, []);
        Assert.NotNull(block3.ExecutionState);
        child.Add(block3);

        // Act
        workflow.ClearExecutionState();

        // Assert
        Assert.Null(block1.ExecutionState);
        Assert.Null(block2.ExecutionState);
        Assert.Null(block3.ExecutionState);
    }

    [Fact]
    public void HasPlaceholder_InWorkflowTextInputTemplate_ReturnsTrue()
    {
        // Arrange
        var workflow = new Workflow("root")
        {
            TextInputTemplate = "Hello {name}"
        };

        // Act & Assert
        Assert.True(workflow.HasPlaceholder("name"));
    }

    [Fact]
    public void HasPlaceholder_PlaceholderNotPresent_ReturnsFalse()
    {
        // Arrange
        var workflow = new Workflow("root")
        {
            TextInputTemplate = "Hello {name}"
        };

        // Act & Assert
        Assert.False(workflow.HasPlaceholder("missing"));
    }

    [Fact]
    public void HasPlaceholder_DefaultComparison_IsCaseSensitive()
    {
        // Arrange
        var workflow = new Workflow("root")
        {
            TextInputTemplate = "Hello {Name}"
        };

        // Act & Assert — tags resolve case-sensitively (Ordinal), matching the runtime
        // block-output lookup and the validators.
        Assert.True(workflow.HasPlaceholder("Name"));
        Assert.False(workflow.HasPlaceholder("name"));
        Assert.False(workflow.HasPlaceholder("NAME"));
    }

    [Fact]
    public void FindBlockById_SingleMatch_ReturnsBlock()
    {
        var workflow = new Workflow();
        var section = new Section("default", workflow);
        var block = new Block { Id = "block-1" };
        section.Add(block);

        Block? result = workflow.FindBlockById("block-1");

        Assert.Same(block, result);
    }

    [Fact]
    public void FindBlockById_AcrossSections_ReturnsMatchingBlock()
    {
        var workflow = new Workflow();
        var section1 = new Section("s1", workflow);
        var section2 = new Section("s2", workflow);
        section1.Add(new Block { Id = "block-1" });
        var target = new Block { Id = "block-2" };
        section2.Add(target);

        Block? result = workflow.FindBlockById("block-2");

        Assert.Same(target, result);
    }

    [Fact]
    public void FindBlockById_NoMatch_ReturnsNull()
    {
        var workflow = new Workflow();
        var section = new Section("default", workflow);
        section.Add(new Block { Id = "block-1" });

        Block? result = workflow.FindBlockById("does-not-exist");

        Assert.Null(result);
    }

    [Fact]
    public void FindBlockByStableId_SingleMatch_ReturnsBlock()
    {
        var workflow = new Workflow();
        var section = new Section("default", workflow);
        var block = new Block { StableBlockId = "stable-1" };
        section.Add(block);

        Block? result = workflow.FindBlockByStableId("stable-1");

        Assert.Same(block, result);
    }

    [Fact]
    public void FindBlockByStableId_AcrossSections_ReturnsMatchingBlock()
    {
        var workflow = new Workflow();
        var section1 = new Section("s1", workflow);
        var section2 = new Section("s2", workflow);
        section1.Add(new Block { StableBlockId = "stable-1" });
        var target = new Block { StableBlockId = "stable-2" };
        section2.Add(target);

        Block? result = workflow.FindBlockByStableId("stable-2");

        Assert.Same(target, result);
    }

    [Fact]
    public void FindBlockByStableId_NoMatch_ReturnsNull()
    {
        var workflow = new Workflow();
        var section = new Section("default", workflow);
        section.Add(new Block { StableBlockId = "stable-1" });

        Block? result = workflow.FindBlockByStableId("does-not-exist");

        Assert.Null(result);
    }

    [Fact]
    public void GetSignatureBearingBlocks_PicksUpBothAiQueryAndMergeBlocks()
    {
        var workflow = new Workflow();
        var section = new Section("default", workflow);
        section.Add(new Block { StableBlockId = "ai-stable", Properties = new AiQueryProperties() });
        section.Add(new Block { StableBlockId = "merge-stable", Properties = new MergeProperties() });

        List<string?> ids = [.. workflow.GetSignatureBearingBlocks().Select(b => b.StableBlockId)];

        Assert.Equal(2, ids.Count);
        Assert.Contains("ai-stable", ids);
        Assert.Contains("merge-stable", ids);
    }

    [Fact]
    public void GetSignatureBearingBlocks_SkipsBlocksWithoutSignatureFieldsContract()
    {
        var workflow = new Workflow();
        var section = new Section("default", workflow);
        section.Add(new Block { StableBlockId = "calc-stable", Properties = new CalculateFormulaProperties() });

        Assert.Empty(workflow.GetSignatureBearingBlocks());
    }

    [Fact]
    public void GetSignatureBearingBlocks_IncludesBlocksWithoutStableId()
    {
        // Overrides are keyed by workflow-level field name now, so a bearing block
        // no longer needs a StableBlockId to participate in signature resolution.
        var workflow = new Workflow();
        var section = new Section("default", workflow);
        section.Add(new Block { StableBlockId = null, Properties = new MergeProperties() });

        _ = Assert.Single(workflow.GetSignatureBearingBlocks());
    }

    [Fact]
    public void FindBlocksByReplacementTag_NoBlocks_ReturnsEmpty()
    {
        // Arrange
        var workflow = new Workflow();

        // Act
        IEnumerable<Block> result = workflow.FindBlocksByReplacementTag("tag1");

        // Assert
        Assert.Empty(result);
    }

    [Fact]
    public void FindBlocksByReplacementTag_SingleMatch_ReturnsBlock()
    {
        // Arrange
        var workflow = new Workflow();
        var section = new Section("default", workflow);
        var block = new Block { ReplacementTag = "tag1" };
        section.Add(block);

        // Act
        IEnumerable<Block> result = workflow.FindBlocksByReplacementTag("tag1");

        // Assert
        _ = Assert.Single(result);
        Assert.Contains(block, result);
    }

    [Fact]
    public void FindBlocksByReplacementTag_MultipleMatches_ReturnsAllMatchingBlocks()
    {
        // Arrange
        var workflow = new Workflow();
        var section = new Section("default", workflow);
        var block1 = new Block { ReplacementTag = "tag1" };
        var block2 = new Block { ReplacementTag = "tag1" };
        var block3 = new Block { ReplacementTag = "tag2" };
        section.Add(block1);
        section.Add(block2);
        section.Add(block3);

        // Act
        IEnumerable<Block> result = workflow.FindBlocksByReplacementTag("tag1");

        // Assert
        Assert.Equal(2, result.Count());
        Assert.Contains(block1, result);
        Assert.Contains(block2, result);
        Assert.DoesNotContain(block3, result);
    }

    [Fact]
    public void FindBlocksByReplacementTag_NoMatch_ReturnsEmpty()
    {
        // Arrange
        var workflow = new Workflow();
        var section = new Section("default", workflow);
        var block = new Block { ReplacementTag = "tag1" };
        section.Add(block);

        // Act
        IEnumerable<Block> result = workflow.FindBlocksByReplacementTag("tag2");

        // Assert
        Assert.Empty(result);
    }

    #region IsolateBlockWithDependencies Tests

    // NOTE: In practice, root workflows have children (sections) but no blocks directly.
    // Blocks are always in child workflows (sections). Dependencies can span across sections.

    [Fact]
    public void IsolateBlockWithDependencies_NoDependencies_KeepsOnlyTargetBlockAndItsSection()
    {
        // Arrange - root workflow with multiple sections
        var rootWorkflow = new Workflow("Root");
        var section1 = new Section("Section 1", rootWorkflow);
        var section2 = new Section("Section 2", rootWorkflow);
        var section3 = new Section("Section 3", rootWorkflow);

        _ = new Block(BlockTypes.AiQuery, section1)
        {
            ReplacementTag = "block1",
            Properties = new AiQueryProperties { UserPromptTemplate = "Hello", },
        };
        var targetBlock = new Block(BlockTypes.AiQuery, section2)
        {
            ReplacementTag = "block2",
            Properties = new AiQueryProperties { UserPromptTemplate = "World", },
        };
        _ = new Block(BlockTypes.AiQuery, section3)
        {
            ReplacementTag = "block3",
            Properties = new AiQueryProperties { UserPromptTemplate = "Test", },
        };

        // Act
        rootWorkflow.IsolateBlockWithDependencies(targetBlock);

        // Assert - only section2 with block2 should remain
        _ = Assert.Single(rootWorkflow.Sections);
        Assert.Contains(rootWorkflow.Sections, s => s.Name == "Section 2");
        _ = Assert.Single(section2.Blocks);
        Assert.Contains(section2.Blocks, b => b.ReplacementTag == "block2");
    }

    [Fact]
    public void IsolateBlockWithDependencies_DirectDependencyInSameSection_KeepsBothBlocks()
    {
        // Arrange
        var rootWorkflow = new Workflow("Root");
        var section1 = new Section("Section 1", rootWorkflow);

        var block1 = new Block(BlockTypes.AiQuery, section1) { ReplacementTag = "block1", Properties = new AiQueryProperties { UserPromptTemplate = "Hello", }, };
        var block2 = new Block(BlockTypes.AiQuery, section1) { ReplacementTag = "block2", Properties = new AiQueryProperties { UserPromptTemplate = "{block1}", }, };
        _ = new Block(BlockTypes.AiQuery, section1) { ReplacementTag = "block3", Properties = new AiQueryProperties { UserPromptTemplate = "Unrelated", }, };

        // Act
        rootWorkflow.IsolateBlockWithDependencies(block2);

        // Assert
        _ = Assert.Single(rootWorkflow.Sections);
        Assert.Equal(2, section1.Blocks.Count);
        Assert.Contains(section1.Blocks, b => b.ReplacementTag == "block1");
        Assert.Contains(section1.Blocks, b => b.ReplacementTag == "block2");
        Assert.DoesNotContain(section1.Blocks, b => b.ReplacementTag == "block3");
    }

    [Fact]
    public void IsolateBlockWithDependencies_DirectDependencyInDifferentSection_KeepsBothSections()
    {
        // Arrange
        var rootWorkflow = new Workflow("Root");
        var section1 = new Section("Section 1", rootWorkflow);
        var section2 = new Section("Section 2", rootWorkflow);
        var section3 = new Section("Section 3", rootWorkflow);

        _ = new Block(BlockTypes.AiQuery, section1) { ReplacementTag = "block1", Properties = new AiQueryProperties { UserPromptTemplate = "Hello", }, };
        var targetBlock = new Block(BlockTypes.AiQuery, section2) { ReplacementTag = "block2", Properties = new AiQueryProperties { UserPromptTemplate = "{block1}", }, };
        _ = new Block(BlockTypes.AiQuery, section3) { ReplacementTag = "block3", Properties = new AiQueryProperties { UserPromptTemplate = "Unrelated", }, };

        // Act
        rootWorkflow.IsolateBlockWithDependencies(targetBlock);

        // Assert - section1 and section2 should remain, section3 removed
        Assert.Equal(2, rootWorkflow.Sections.Count);
        Assert.Contains(rootWorkflow.Sections, s => s.Name == "Section 1");
        Assert.Contains(rootWorkflow.Sections, s => s.Name == "Section 2");
        Assert.DoesNotContain(rootWorkflow.Sections, s => s.Name == "Section 3");
        _ = Assert.Single(section1.Blocks);
        _ = Assert.Single(section2.Blocks);
    }

    [Fact]
    public void IsolateBlockWithDependencies_TransitiveDependencyAcrossSections_KeepsEntireChain()
    {
        // Arrange
        var rootWorkflow = new Workflow("Root");
        var section1 = new Section("Section 1", rootWorkflow);
        var section2 = new Section("Section 2", rootWorkflow);
        var section3 = new Section("Section 3", rootWorkflow);
        var section4 = new Section("Section 4", rootWorkflow);

        _ = new Block(BlockTypes.AiQuery, section1) { ReplacementTag = "block1", Properties = new AiQueryProperties { UserPromptTemplate = "Hello", }, };
        _ = new Block(BlockTypes.AiQuery, section2) { ReplacementTag = "block2", Properties = new AiQueryProperties { UserPromptTemplate = "{block1}", }, };
        var targetBlock = new Block(BlockTypes.AiQuery, section3) { ReplacementTag = "block3", Properties = new AiQueryProperties { UserPromptTemplate = "{block2}", }, };
        _ = new Block(BlockTypes.AiQuery, section4) { ReplacementTag = "block4", Properties = new AiQueryProperties { UserPromptTemplate = "Unrelated", }, };

        // Act - isolate block3 which depends on block2 which depends on block1
        rootWorkflow.IsolateBlockWithDependencies(targetBlock);

        // Assert
        Assert.Equal(3, rootWorkflow.Sections.Count);
        Assert.Contains(rootWorkflow.Sections, s => s.Name == "Section 1");
        Assert.Contains(rootWorkflow.Sections, s => s.Name == "Section 2");
        Assert.Contains(rootWorkflow.Sections, s => s.Name == "Section 3");
        Assert.DoesNotContain(rootWorkflow.Sections, s => s.Name == "Section 4");
    }

    [Fact]
    public void IsolateBlockWithDependencies_DeepTransitiveDependency_KeepsEntireChain()
    {
        // Arrange - 5 blocks in chain across 5 sections
        var rootWorkflow = new Workflow("Root");
        var section1 = new Section("Section 1", rootWorkflow);
        var section2 = new Section("Section 2", rootWorkflow);
        var section3 = new Section("Section 3", rootWorkflow);
        var section4 = new Section("Section 4", rootWorkflow);
        var section5 = new Section("Section 5", rootWorkflow);
        var sectionUnrelated = new Section("Unrelated", rootWorkflow);

        _ = new Block(BlockTypes.AiQuery, section1) { ReplacementTag = "block1", Properties = new AiQueryProperties { UserPromptTemplate = "Hello", }, };
        _ = new Block(BlockTypes.AiQuery, section2) { ReplacementTag = "block2", Properties = new AiQueryProperties { UserPromptTemplate = "{block1}", }, };
        _ = new Block(BlockTypes.AiQuery, section3) { ReplacementTag = "block3", Properties = new AiQueryProperties { UserPromptTemplate = "{block2}", }, };
        _ = new Block(BlockTypes.AiQuery, section4) { ReplacementTag = "block4", Properties = new AiQueryProperties { UserPromptTemplate = "{block3}", }, };
        var targetBlock = new Block(BlockTypes.AiQuery, section5) { ReplacementTag = "block5", Properties = new AiQueryProperties { UserPromptTemplate = "{block4}", }, };
        _ = new Block(BlockTypes.AiQuery, sectionUnrelated) { ReplacementTag = "unrelated", Properties = new AiQueryProperties { UserPromptTemplate = "Unrelated", }, };

        // Act
        rootWorkflow.IsolateBlockWithDependencies(targetBlock);

        // Assert
        Assert.Equal(5, rootWorkflow.Sections.Count);
        Assert.DoesNotContain(rootWorkflow.Sections, s => s.Name == "Unrelated");
    }

    [Fact]
    public void IsolateBlockWithDependencies_MultipleDependenciesFromDifferentSections_KeepsAllBranches()
    {
        // Arrange
        var rootWorkflow = new Workflow("Root");
        var sectionA = new Section("Section A", rootWorkflow);
        var sectionB = new Section("Section B", rootWorkflow);
        var sectionC = new Section("Section C", rootWorkflow);
        var sectionUnrelated = new Section("Unrelated", rootWorkflow);

        _ = new Block(BlockTypes.AiQuery, sectionA) { ReplacementTag = "blockA", Properties = new AiQueryProperties { UserPromptTemplate = "A", }, };
        _ = new Block(BlockTypes.AiQuery, sectionB) { ReplacementTag = "blockB", Properties = new AiQueryProperties { UserPromptTemplate = "B", }, };
        var targetBlock = new Block(BlockTypes.AiQuery, sectionC) { ReplacementTag = "blockC", Properties = new AiQueryProperties { UserPromptTemplate = "{blockA} and {blockB}", }, };
        _ = new Block(BlockTypes.AiQuery, sectionUnrelated) { ReplacementTag = "unrelated", Properties = new AiQueryProperties { UserPromptTemplate = "Unrelated", }, };

        // Act
        rootWorkflow.IsolateBlockWithDependencies(targetBlock);

        // Assert
        Assert.Equal(3, rootWorkflow.Sections.Count);
        Assert.Contains(rootWorkflow.Sections, s => s.Name == "Section A");
        Assert.Contains(rootWorkflow.Sections, s => s.Name == "Section B");
        Assert.Contains(rootWorkflow.Sections, s => s.Name == "Section C");
        Assert.DoesNotContain(rootWorkflow.Sections, s => s.Name == "Unrelated");
    }

    [Fact]
    public void IsolateBlockWithDependencies_DiamondDependencyAcrossSections_KeepsAllNodes()
    {
        // Arrange - Diamond pattern: D depends on B and C, both B and C depend on A
        var rootWorkflow = new Workflow("Root");
        var sectionA = new Section("Section A", rootWorkflow);
        var sectionB = new Section("Section B", rootWorkflow);
        var sectionC = new Section("Section C", rootWorkflow);
        var sectionD = new Section("Section D", rootWorkflow);
        var sectionUnrelated = new Section("Unrelated", rootWorkflow);

        _ = new Block(BlockTypes.AiQuery, sectionA) { ReplacementTag = "blockA", Properties = new AiQueryProperties { UserPromptTemplate = "A", }, };
        _ = new Block(BlockTypes.AiQuery, sectionB) { ReplacementTag = "blockB", Properties = new AiQueryProperties { UserPromptTemplate = "{blockA}", }, };
        _ = new Block(BlockTypes.AiQuery, sectionC) { ReplacementTag = "blockC", Properties = new AiQueryProperties { UserPromptTemplate = "{blockA}", }, };
        var targetBlock = new Block(BlockTypes.AiQuery, sectionD) { ReplacementTag = "blockD", Properties = new AiQueryProperties { UserPromptTemplate = "{blockB} and {blockC}", }, };
        _ = new Block(BlockTypes.AiQuery, sectionUnrelated) { ReplacementTag = "unrelated", Properties = new AiQueryProperties { UserPromptTemplate = "Unrelated", }, };

        // Act
        rootWorkflow.IsolateBlockWithDependencies(targetBlock);

        // Assert
        Assert.Equal(4, rootWorkflow.Sections.Count);
        Assert.Contains(rootWorkflow.Sections, s => s.Name == "Section A");
        Assert.Contains(rootWorkflow.Sections, s => s.Name == "Section B");
        Assert.Contains(rootWorkflow.Sections, s => s.Name == "Section C");
        Assert.Contains(rootWorkflow.Sections, s => s.Name == "Section D");
        Assert.DoesNotContain(rootWorkflow.Sections, s => s.Name == "Unrelated");
    }

    [Fact]
    public void IsolateBlockWithDependencies_DependencyWithProperty_KeepsBaseBlock()
    {
        // Arrange
        var rootWorkflow = new Workflow("Root");
        var section1 = new Section("Section 1", rootWorkflow);
        var section2 = new Section("Section 2", rootWorkflow);

        _ = new Block(BlockTypes.AiQuery, section1) { ReplacementTag = "block1", Properties = new AiQueryProperties { UserPromptTemplate = "Hello", }, };
        var targetBlock = new Block(BlockTypes.AiQuery, section2) { ReplacementTag = "block2", Properties = new AiQueryProperties { UserPromptTemplate = "{block1.answer}", }, };

        // Act
        rootWorkflow.IsolateBlockWithDependencies(targetBlock);

        // Assert
        Assert.Equal(2, rootWorkflow.Sections.Count);
        _ = Assert.Single(section1.Blocks);
        _ = Assert.Single(section2.Blocks);
    }

    [Fact]
    public void IsolateBlockWithDependencies_DependencyWithNestedProperty_KeepsBaseBlock()
    {
        // Arrange
        var rootWorkflow = new Workflow("Root");
        var section1 = new Section("Section 1", rootWorkflow);
        var section2 = new Section("Section 2", rootWorkflow);

        _ = new Block(BlockTypes.AiQuery, section1) { ReplacementTag = "block1", Properties = new AiQueryProperties { UserPromptTemplate = "Hello", }, };
        var targetBlock = new Block(BlockTypes.AiQuery, section2) { ReplacementTag = "block2", Properties = new AiQueryProperties { UserPromptTemplate = "{block1.answer.value}", }, };

        // Act
        rootWorkflow.IsolateBlockWithDependencies(targetBlock);

        // Assert
        Assert.Equal(2, rootWorkflow.Sections.Count);
    }

    [Fact]
    public void IsolateBlockWithDependencies_DoubleBraceDependency_KeepsBlock()
    {
        // Arrange
        var rootWorkflow = new Workflow("Root");
        var section1 = new Section("Section 1", rootWorkflow);
        var section2 = new Section("Section 2", rootWorkflow);

        _ = new Block(BlockTypes.AiQuery, section1) { ReplacementTag = "block1", Properties = new AiQueryProperties { UserPromptTemplate = "Hello", }, };
        var targetBlock = new Block(BlockTypes.AiQuery, section2) { ReplacementTag = "block2", Properties = new AiQueryProperties { UserPromptTemplate = "{{block1}}", }, };

        // Act
        rootWorkflow.IsolateBlockWithDependencies(targetBlock);

        // Assert
        Assert.Equal(2, rootWorkflow.Sections.Count);
    }

    [Fact]
    public void IsolateBlockWithDependencies_DoubleBraceDependencyWithProperty_KeepsBaseBlock()
    {
        // Arrange
        var rootWorkflow = new Workflow("Root");
        var section1 = new Section("Section 1", rootWorkflow);
        var section2 = new Section("Section 2", rootWorkflow);

        _ = new Block(BlockTypes.AiQuery, section1) { ReplacementTag = "block1", Properties = new AiQueryProperties { UserPromptTemplate = "Hello", }, };
        var targetBlock = new Block(BlockTypes.AiQuery, section2) { ReplacementTag = "block2", Properties = new AiQueryProperties { UserPromptTemplate = "{{block1.answer}}", }, };

        // Act
        rootWorkflow.IsolateBlockWithDependencies(targetBlock);

        // Assert
        Assert.Equal(2, rootWorkflow.Sections.Count);
    }

    [Fact]
    public void IsolateBlockWithDependencies_DependencyNotInWorkflow_IgnoresMissing()
    {
        // Arrange
        var rootWorkflow = new Workflow("Root");
        var section1 = new Section("Section 1", rootWorkflow);
        var sectionUnrelated = new Section("Unrelated", rootWorkflow);

        var targetBlock = new Block(BlockTypes.AiQuery, section1) { ReplacementTag = "block1", Properties = new AiQueryProperties { UserPromptTemplate = "{missingBlock}", }, };
        _ = new Block(BlockTypes.AiQuery, sectionUnrelated) { ReplacementTag = "unrelated", Properties = new AiQueryProperties { UserPromptTemplate = "Unrelated", }, };

        // Act - should not throw even if referenced block doesn't exist
        rootWorkflow.IsolateBlockWithDependencies(targetBlock);

        // Assert
        _ = Assert.Single(rootWorkflow.Sections);
        Assert.Contains(rootWorkflow.Sections, s => s.Name == "Section 1");
    }

    [Fact]
    public void IsolateBlockWithDependencies_RemovesUnrelatedBlocksFromKeptSections()
    {
        // Arrange - section has both needed and unneeded blocks
        var rootWorkflow = new Workflow("Root");
        var section1 = new Section("Section 1", rootWorkflow);

        _ = new Block(BlockTypes.AiQuery, section1) { ReplacementTag = "needed", Properties = new AiQueryProperties { UserPromptTemplate = "Hello", }, };
        var targetBlock = new Block(BlockTypes.AiQuery, section1) { ReplacementTag = "target", Properties = new AiQueryProperties { UserPromptTemplate = "{needed}", }, };
        _ = new Block(BlockTypes.AiQuery, section1) { ReplacementTag = "unneeded", Properties = new AiQueryProperties { UserPromptTemplate = "Unrelated", }, };

        // Act
        rootWorkflow.IsolateBlockWithDependencies(targetBlock);

        // Assert - unneeded block should be removed from section
        Assert.Equal(2, section1.Blocks.Count);
        Assert.Contains(section1.Blocks, b => b.ReplacementTag == "needed");
        Assert.Contains(section1.Blocks, b => b.ReplacementTag == "target");
        Assert.DoesNotContain(section1.Blocks, b => b.ReplacementTag == "unneeded");
    }

    [Fact]
    public void IsolateBlockWithDependencies_NormalizesBlockOrdering()
    {
        // Arrange
        var rootWorkflow = new Workflow("Root");
        var section1 = new Section("Section 1", rootWorkflow);

        _ = new Block(BlockTypes.AiQuery, section1) { ReplacementTag = "block1", Properties = new AiQueryProperties { UserPromptTemplate = "Hello", }, };
        _ = new Block(BlockTypes.AiQuery, section1) { ReplacementTag = "block2", Properties = new AiQueryProperties { UserPromptTemplate = "{block1}", }, };
        var targetBlock = new Block(BlockTypes.AiQuery, section1) { ReplacementTag = "block3", Properties = new AiQueryProperties { UserPromptTemplate = "{block2}", }, };
        _ = new Block(BlockTypes.AiQuery, section1) { ReplacementTag = "unrelated", Properties = new AiQueryProperties { UserPromptTemplate = "Unrelated", }, };

        // Act
        rootWorkflow.IsolateBlockWithDependencies(targetBlock);

        // Assert - blocks should be renumbered 0, 1, 2
        var orderedBlocks = section1.Blocks.OrderBy(b => b.Order).ToList();
        Assert.Equal(3, orderedBlocks.Count);
        Assert.Equal(0, orderedBlocks[0].Order);
        Assert.Equal(1, orderedBlocks[1].Order);
        Assert.Equal(2, orderedBlocks[2].Order);
    }

    [Fact]
    public void IsolateBlockWithDependencies_NormalizesSectionOrdering()
    {
        // Arrange
        var rootWorkflow = new Workflow("Root");
        var section1 = new Section("Section 1", rootWorkflow);
        var section2 = new Section("Section 2", rootWorkflow);
        var section3 = new Section("Section 3", rootWorkflow);

        _ = new Block(BlockTypes.AiQuery, section1) { ReplacementTag = "block1", Properties = new AiQueryProperties { UserPromptTemplate = "Hello", }, };
        _ = new Block(BlockTypes.AiQuery, section2) { ReplacementTag = "unrelated", Properties = new AiQueryProperties { UserPromptTemplate = "Unrelated", }, };
        var targetBlock = new Block(BlockTypes.AiQuery, section3) { ReplacementTag = "block3", Properties = new AiQueryProperties { UserPromptTemplate = "{block1}", }, };

        // Act
        rootWorkflow.IsolateBlockWithDependencies(targetBlock);

        // Assert - sections should be renumbered 0, 1
        var orderedSections = rootWorkflow.Sections.OrderBy(s => s.Order).ToList();
        Assert.Equal(2, orderedSections.Count);
        Assert.Equal(0, orderedSections[0].Order);
        Assert.Equal(1, orderedSections[1].Order);
    }

    [Fact]
    public void IsolateBlockWithDependencies_BlockNotInWorkflow_ThrowsException()
    {
        // Arrange
        var rootWorkflow = new Workflow("Root");
        var section1 = new Section("Section 1", rootWorkflow);
        _ = new Block(BlockTypes.AiQuery, section1) { ReplacementTag = "block1" };

        var otherWorkflow = new Workflow("Other");
        var otherSection = new Section("Other Section", otherWorkflow);
        var foreignBlock = new Block(BlockTypes.AiQuery, otherSection) { ReplacementTag = "foreign" };

        // Act & Assert
        _ = Assert.Throws<ArgumentException>(() => rootWorkflow.IsolateBlockWithDependencies(foreignBlock));
    }

    [Fact]
    public void IsolateBlockWithDependencies_MixedDependenciesWithinAndAcrossSections()
    {
        // Arrange - complex scenario with multiple blocks per section and cross-section deps
        var rootWorkflow = new Workflow("Root");
        var section1 = new Section("Section 1", rootWorkflow);
        var section2 = new Section("Section 2", rootWorkflow);
        var section3 = new Section("Section 3", rootWorkflow);

        // Section 1: base block and unrelated
        _ = new Block(BlockTypes.AiQuery, section1) { ReplacementTag = "base", Properties = new AiQueryProperties { UserPromptTemplate = "Base", }, };
        _ = new Block(BlockTypes.AiQuery, section1) { ReplacementTag = "s1_unrelated", Properties = new AiQueryProperties { UserPromptTemplate = "Unrelated", }, };

        // Section 2: depends on base, has another unrelated
        _ = new Block(BlockTypes.AiQuery, section2) { ReplacementTag = "middle", Properties = new AiQueryProperties { UserPromptTemplate = "{base}", }, };
        _ = new Block(BlockTypes.AiQuery, section2) { ReplacementTag = "s2_unrelated", Properties = new AiQueryProperties { UserPromptTemplate = "Unrelated", }, };

        // Section 3: target depends on middle, plus unrelated blocks
        var targetBlock = new Block(BlockTypes.AiQuery, section3) { ReplacementTag = "target", Properties = new AiQueryProperties { UserPromptTemplate = "{middle}", }, };
        _ = new Block(BlockTypes.AiQuery, section3) { ReplacementTag = "s3_unrelated", Properties = new AiQueryProperties { UserPromptTemplate = "Unrelated", }, };

        // Act
        rootWorkflow.IsolateBlockWithDependencies(targetBlock);

        // Assert - all 3 sections kept, but unrelated blocks removed
        Assert.Equal(3, rootWorkflow.Sections.Count);
        _ = Assert.Single(section1.Blocks);
        Assert.Contains(section1.Blocks, b => b.ReplacementTag == "base");
        _ = Assert.Single(section2.Blocks);
        Assert.Contains(section2.Blocks, b => b.ReplacementTag == "middle");
        _ = Assert.Single(section3.Blocks);
        Assert.Contains(section3.Blocks, b => b.ReplacementTag == "target");
    }

    #endregion

    #region IsolateBlockWithDependencies Tests - Properties-Based Blocks

    [Fact]
    public void IsolateBlockWithDependencies_AiQueryPropertiesDependency_KeepsDependencies()
    {
        // Arrange
        var rootWorkflow = new Workflow("Root");
        var section1 = new Section("Section 1", rootWorkflow);
        var section2 = new Section("Section 2", rootWorkflow);
        var sectionUnrelated = new Section("Unrelated", rootWorkflow);

        _ = new Block(BlockTypes.AiQuery, section1)
        {
            ReplacementTag = "dataSource",
            Properties = new AiQueryProperties
            {
                UserPromptTemplate = "Extract the financial data"
            }
        };

        var targetBlock = new Block(BlockTypes.AiQuery, section2)
        {
            ReplacementTag = "analysis",
            Properties = new AiQueryProperties
            {
                UserPromptTemplate = "Analyze: {dataSource.answer}",
                SystemPromptTemplate = "You are a financial analyst"
            }
        };

        _ = new Block(BlockTypes.AiQuery, sectionUnrelated)
        {
            ReplacementTag = "unrelated",
            Properties = new AiQueryProperties
            {
                UserPromptTemplate = "Something unrelated"
            }
        };

        // Act
        rootWorkflow.IsolateBlockWithDependencies(targetBlock);

        // Assert
        Assert.Equal(2, rootWorkflow.Sections.Count);
        Assert.Contains(rootWorkflow.Sections, s => s.Name == "Section 1");
        Assert.Contains(rootWorkflow.Sections, s => s.Name == "Section 2");
        Assert.DoesNotContain(rootWorkflow.Sections, s => s.Name == "Unrelated");
    }

    [Fact]
    public void IsolateBlockWithDependencies_WebApiCallPropertiesDependency_KeepsUrlAndPayloadDeps()
    {
        // Arrange
        var rootWorkflow = new Workflow("Root");
        var sectionConfig = new Section("Config", rootWorkflow);
        var sectionQuery = new Section("Query", rootWorkflow);
        var sectionApi = new Section("API", rootWorkflow);
        var sectionUnrelated = new Section("Unrelated", rootWorkflow);

        _ = new Block(BlockTypes.AiQuery, sectionQuery)
        {
            ReplacementTag = "searchQuery",
            Properties = new AiQueryProperties
            {
                UserPromptTemplate = "Generate search query"
            }
        };

        var targetBlock = new Block(BlockTypes.WebApiCall, sectionApi)
        {
            ReplacementTag = "apiCall",
            Properties = new WebApiCallProperties
            {
                UrlTemplate = "https://api.example.com/{apiConfig}/search",
                RequestPayloadTemplate = """{"query": "{searchQuery.result}"}"""
            }
        };

        // Act
        rootWorkflow.IsolateBlockWithDependencies(targetBlock);

        // Assert - should keep Config, Query, and API sections
        Assert.Equal(2, rootWorkflow.Sections.Count);
        Assert.Contains(rootWorkflow.Sections, s => s.Name == "Query");
        Assert.Contains(rootWorkflow.Sections, s => s.Name == "API");
        Assert.DoesNotContain(rootWorkflow.Sections, s => s.Name == "Unrelated");
    }

    [Fact]
    public void IsolateBlockWithDependencies_TransitivePropertiesDependencies_KeepsEntireChain()
    {
        // Arrange - Chain: Extract -> Analyze -> Format -> Final
        var rootWorkflow = new Workflow("Root");
        var section1 = new Section("Section 1", rootWorkflow);
        var section2 = new Section("Section 2", rootWorkflow);
        var section3 = new Section("Section 3", rootWorkflow);
        var section4 = new Section("Section 4", rootWorkflow);
        var sectionUnrelated = new Section("Unrelated", rootWorkflow);

        _ = new Block(BlockTypes.AiQuery, section1)
        {
            ReplacementTag = "extract",
            Properties = new AiQueryProperties
            {
                UserPromptTemplate = "Extract data from document"
            }
        };

        _ = new Block(BlockTypes.AiQuery, section2)
        {
            ReplacementTag = "analyze",
            Properties = new AiQueryProperties
            {
                UserPromptTemplate = "Analyze: {extract.answer}"
            }
        };

        _ = new Block(BlockTypes.Merge, section3)
        {
            ReplacementTag = "format",
            Properties = new MergeProperties
            {
                TextTemplate = "Result: {analyze.answer}"
            }
        };

        var targetBlock = new Block(BlockTypes.Merge, section4)
        {
            ReplacementTag = "final",
            Properties = new MergeProperties
            {
                TextTemplate = "<div>{format}</div>"
            }
        };

        _ = new Block(BlockTypes.Merge, sectionUnrelated)
        {
            ReplacementTag = "unrelated",
            Properties = new MergeProperties
            {
                TextTemplate = "Not part of chain"
            }
        };

        // Act
        rootWorkflow.IsolateBlockWithDependencies(targetBlock);

        // Assert - entire chain should be kept
        Assert.Equal(4, rootWorkflow.Sections.Count);
        Assert.DoesNotContain(rootWorkflow.Sections, s => s.Name == "Unrelated");
    }

    [Fact]
    public void IsolateBlockWithDependencies_CalculateFormulaWithDependencies_KeepsNumericSources()
    {
        // Arrange
        var rootWorkflow = new Workflow("Root");
        var sectionRevenue = new Section("Revenue", rootWorkflow);
        var sectionExpenses = new Section("Expenses", rootWorkflow);
        var sectionCalc = new Section("Calculation", rootWorkflow);
        var sectionUnrelated = new Section("Unrelated", rootWorkflow);

        _ = new Block(BlockTypes.AiQuery, sectionRevenue)
        {
            ReplacementTag = "revenue",
            Properties = new AiQueryProperties
            {
                UserPromptTemplate = "Extract revenue amount"
            }
        };

        _ = new Block(BlockTypes.AiQuery, sectionExpenses)
        {
            ReplacementTag = "expenses",
            Properties = new AiQueryProperties
            {
                UserPromptTemplate = "Extract expenses amount"
            }
        };

        var targetBlock = new Block(BlockTypes.CalculateNumber, sectionCalc)
        {
            ReplacementTag = "profit",
            Properties = new CalculateFormulaProperties
            {
                FormulaTemplate = "{revenue.value} - {expenses.value}"
            }
        };

        _ = new Block(BlockTypes.AiQuery, sectionUnrelated)
        {
            ReplacementTag = "unrelated",
            Properties = new AiQueryProperties
            {
                UserPromptTemplate = "Other analysis"
            }
        };

        // Act
        rootWorkflow.IsolateBlockWithDependencies(targetBlock);

        // Assert
        Assert.Equal(3, rootWorkflow.Sections.Count);
        Assert.Contains(rootWorkflow.Sections, s => s.Name == "Revenue");
        Assert.Contains(rootWorkflow.Sections, s => s.Name == "Expenses");
        Assert.Contains(rootWorkflow.Sections, s => s.Name == "Calculation");
        Assert.DoesNotContain(rootWorkflow.Sections, s => s.Name == "Unrelated");
    }

    [Fact]
    public void IsolateBlockWithDependencies_IntelligentSearchWithDependency_KeepsSearchTermSource()
    {
        // Arrange
        var rootWorkflow = new Workflow("Root");
        var sectionCompany = new Section("Company", rootWorkflow);
        var sectionSearch = new Section("Search", rootWorkflow);
        var sectionUnrelated = new Section("Unrelated", rootWorkflow);

        _ = new Block(BlockTypes.AiQuery, sectionCompany)
        {
            ReplacementTag = "companyInfo",
            Properties = new AiQueryProperties
            {
                UserPromptTemplate = "Extract company name"
            }
        };

        var targetBlock = new Block(BlockTypes.IntelligentSearch, sectionSearch)
        {
            ReplacementTag = "search",
            Properties = new IntelligentSearchProperties
            {
                SearchTermTemplate = "{companyInfo.name} financial statements 2024"
            }
        };

        // Act
        rootWorkflow.IsolateBlockWithDependencies(targetBlock);

        // Assert
        Assert.Equal(2, rootWorkflow.Sections.Count);
        Assert.Contains(rootWorkflow.Sections, s => s.Name == "Company");
        Assert.Contains(rootWorkflow.Sections, s => s.Name == "Search");
        Assert.DoesNotContain(rootWorkflow.Sections, s => s.Name == "Unrelated");
    }

    [Fact]
    public void IsolateBlockWithDependencies_DuplicateReplacementTags_ProcessesAllBlocksWithSameTag()
    {
        // Arrange - Bug reproduction: Two blocks have the same ReplacementTag
        // One block references the dependency, the other doesn't
        // The algorithm must process BOTH blocks to find all dependencies
        var rootWorkflow = new Workflow("Root");
        var section1 = new Section("Section 1", rootWorkflow);
        var section2 = new Section("Section 2", rootWorkflow);

        // The base block that should be kept
        _ = new Block(BlockTypes.CalculateNumber, section1)
        {
            ReplacementTag = "base_calculation",
            Properties = new CalculateFormulaProperties
            {
                FormulaTemplate = "100 + 200",
            },
        };

        // First block with tag "duplicate_tag" - does NOT reference base_calculation
        _ = new Block(BlockTypes.CalculateNumber, section1)
        {
            ReplacementTag = "duplicate_tag",
            Properties = new CalculateFormulaProperties
            {
                FormulaTemplate = "50 * 2",
            },
        };

        // Second block with SAME tag "duplicate_tag" - DOES reference base_calculation
        _ = new Block(BlockTypes.AiQuery, section1)
        {
            ReplacementTag = "duplicate_tag",
            Properties = new AiQueryProperties
            {
                UserPromptTemplate = "Process this: {base_calculation}",
            },
        };

        // Block that depends on "duplicate_tag"
        _ = new Block(BlockTypes.AiQuery, section1)
        {
            ReplacementTag = "downstream",
            Properties = new AiQueryProperties
            {
                UserPromptTemplate = "Use duplicate: {duplicate_tag}",
            },
        };

        // Target block that depends on downstream
        var targetBlock = new Block(BlockTypes.AiQuery, section2)
        {
            ReplacementTag = "target",
            Properties = new AiQueryProperties
            {
                UserPromptTemplate = "Final: {downstream}",
            },
        };

        // Act
        rootWorkflow.IsolateBlockWithDependencies(targetBlock);

        // Assert - base_calculation MUST be kept because one of the duplicate_tag blocks references it
        Assert.Contains(section1.Blocks, b => b.ReplacementTag == "base_calculation");
        Assert.Contains(section1.Blocks, b => b.ReplacementTag == "duplicate_tag");
        Assert.Contains(section1.Blocks, b => b.ReplacementTag == "downstream");
        Assert.Contains(section2.Blocks, b => b.ReplacementTag == "target");
    }

    [Fact]
    public void IsolateBlockWithDependencies_SetsIsOutputSourceAndOutputTitleTemplate()
    {
        // Arrange
        var rootWorkflow = new Workflow("Root");
        var section1 = new Section("Section 1", rootWorkflow);

        // Block with name - should use name for OutputTitleTemplate
        _ = new Block(BlockTypes.Merge, section1)
        {
            ReplacementTag = "block1",
            Name = "Block One Name",
            IsOutputSource = false,
            OutputTitleTemplate = null
        };

        // Block without name - should use replacement tag for OutputTitleTemplate
        _ = new Block(BlockTypes.AiQuery, section1)
        {
            ReplacementTag = "block2",
            Name = null!,
            IsOutputSource = false,
            OutputTitleTemplate = null,
            Type = BlockTypes.AiQuery,
            Properties = new AiQueryProperties
            {
                UserPromptTemplate = "{block1}",
            },
        };

        // Block with existing OutputTitleTemplate - should NOT be overwritten
        var targetBlock = new Block(BlockTypes.AiQuery, section1)
        {
            ReplacementTag = "target",
            Name = "Target Block",
            IsOutputSource = false,
            OutputTitleTemplate = "Custom Title",
            Properties = new AiQueryProperties
            {
                UserPromptTemplate = "{block2}",
            },
        };

        // Act
        rootWorkflow.IsolateBlockWithDependencies(targetBlock);

        // Assert
        Block? block1 = section1.Blocks.FirstOrDefault(b => b.ReplacementTag == "block1");
        Block? block2 = section1.Blocks.FirstOrDefault(b => b.ReplacementTag == "block2");
        Block? target = section1.Blocks.FirstOrDefault(b => b.ReplacementTag == "target");

        // All blocks should have IsOutputSource = true
        Assert.True(block1?.IsOutputSource);
        Assert.True(block2?.IsOutputSource);
        Assert.True(target?.IsOutputSource);

        // block1 has name, should use name
        Assert.Equal("Block One Name", block1?.OutputTitleTemplate);

        // block2 has no name, should use replacement tag
        Assert.Equal("block2", block2?.OutputTitleTemplate);

        // target had existing OutputTitleTemplate, should NOT be overwritten
        Assert.Equal("Custom Title", target?.OutputTitleTemplate);
    }

    [Fact]
    public void IsolateBlockWithDependencies_RealWorldScenario_LegacyCalculationBlockKept()
    {
        // Arrange - Reproduces reported bug scenario:
        // - ownership_ave_rating_raw is a calculation block (uses FormulaTemplate)
        // - Block B depends on ownership_ave_rating_raw via UserPromptTemplate
        // - Both are in the same section (Section 1)
        // - User clicks on a block way further down the chain in a different section
        var rootWorkflow = new Workflow("Root");
        var section1 = new Section("Section 1", rootWorkflow);
        var section2 = new Section("Section 2", rootWorkflow);

        // Base blocks that ownership_ave_rating_raw depends on
        _ = new Block(BlockTypes.AiQuery, section1)
        {
            ReplacementTag = "ownership_rating_StructureParentalSupport",
            Properties = new AiQueryProperties
            {
                UserPromptTemplate = "Rate structure",
            },
        };

        _ = new Block(BlockTypes.AiQuery, section1)
        {
            ReplacementTag = "ownership_rating_KeyMan",
            Properties = new AiQueryProperties
            {
                UserPromptTemplate = "Rate key man",
            },
        };

        // The calculation block that got incorrectly deleted
        _ = new Block(BlockTypes.CalculateNumber, section1)
        {
            ReplacementTag = "ownership_ave_rating_raw",
            Properties = new CalculateFormulaProperties
            {
                FormulaTemplate = "{ownership_rating_StructureParentalSupport.score}*0.5+{ownership_rating_KeyMan.score}*0.5",
            },
        };

        // Block that depends on ownership_ave_rating_raw (same section)
        _ = new Block(BlockTypes.AiQuery, section1)
        {
            ReplacementTag = "ownership_ave_rating_rounded",
            Properties = new AiQueryProperties
            {
                UserPromptTemplate = """
                    Round the weighted average score to the nearest whole number.
                    INPUT:
                    <weighted_average_score>
                    {ownership_ave_rating_raw}
                    </weighted_average_score>
                    """,
            },
        };

        // The block user clicked on in a different section (depends on ownership_ave_rating_rounded)
        var targetBlock = new Block(BlockTypes.AiQuery, section2)
        {
            ReplacementTag = "final_analysis",
            Properties = new AiQueryProperties
            {
                UserPromptTemplate = "Final analysis using rating: {ownership_ave_rating_rounded}",
            },
        };

        // Act
        rootWorkflow.IsolateBlockWithDependencies(targetBlock);

        // Assert - ownership_ave_rating_raw should be kept because it's in the dependency chain
        Assert.Equal(2, rootWorkflow.Sections.Count);

        // Section 1 should have all 4 blocks in the chain
        Assert.Equal(4, section1.Blocks.Count);
        Assert.Contains(section1.Blocks, b => b.ReplacementTag == "ownership_rating_StructureParentalSupport");
        Assert.Contains(section1.Blocks, b => b.ReplacementTag == "ownership_rating_KeyMan");
        Assert.Contains(section1.Blocks, b => b.ReplacementTag == "ownership_ave_rating_raw");
        Assert.Contains(section1.Blocks, b => b.ReplacementTag == "ownership_ave_rating_rounded");

        // Section 2 should have the target block
        _ = Assert.Single(section2.Blocks);
        Assert.Contains(section2.Blocks, b => b.ReplacementTag == "final_analysis");
    }

    #endregion

    #region IsolateBlockWithDependencies Tests - Document Class Dependencies

    [Fact]
    public void IsolateBlockWithDependencies_DocumentClassDependency_KeepsProducerBlock()
    {
        // Arrange
        var rootWorkflow = new Workflow("Root");
        var section1 = new Section("Section 1", rootWorkflow);
        var section2 = new Section("Section 2", rootWorkflow);
        var sectionUnrelated = new Section("Unrelated", rootWorkflow);

        // Block that produces documents with class "web-data"
        _ = new Block(BlockTypes.IntelligentSearch, section1)
        {
            ReplacementTag = "webSearch",
            Properties = new IntelligentSearchProperties
            {
                CreatesDocumentClass = "web-data"
            }
        };

        // Block that requires documents with class "web-data"
        var targetBlock = new Block(BlockTypes.AiQuery, section2)
        {
            ReplacementTag = "analyzer",
            SupportedDocumentClasses = ["web-data"]
        };
        _ = new RagSettings(targetBlock) { Type = RagTypes.WholeDocument };

        _ = new Block(BlockTypes.AiQuery, sectionUnrelated) { ReplacementTag = "unrelated" };

        // Act
        rootWorkflow.IsolateBlockWithDependencies(targetBlock);

        // Assert - section1 and section2 should remain
        Assert.Equal(2, rootWorkflow.Sections.Count);
        Assert.Contains(rootWorkflow.Sections, s => s.Name == "Section 1");
        Assert.Contains(rootWorkflow.Sections, s => s.Name == "Section 2");
        Assert.DoesNotContain(rootWorkflow.Sections, s => s.Name == "Unrelated");
    }

    [Fact]
    public void IsolateBlockWithDependencies_MultipleDocumentClassDependencies_KeepsAllProducers()
    {
        // Arrange
        var rootWorkflow = new Workflow("Root");
        var section1 = new Section("Section 1", rootWorkflow);
        var section2 = new Section("Section 2", rootWorkflow);
        var section3 = new Section("Section 3", rootWorkflow);
        var sectionUnrelated = new Section("Unrelated", rootWorkflow);

        _ = new Block(BlockTypes.IntelligentSearch, section1)
        {
            ReplacementTag = "webSearch",
            Properties = new IntelligentSearchProperties { CreatesDocumentClass = "web-data" }
        };

        _ = new Block(BlockTypes.Json2Xlsx, section2)
        {
            ReplacementTag = "json2Xlsx",
            Properties = new Json2XlsxProperties { CreatesDocumentClass = "api-data" }
        };

        var targetBlock = new Block(BlockTypes.AiQuery, section3)
        {
            ReplacementTag = "analyzer",
            SupportedDocumentClasses = ["web-data", "api-data"]
        };
        _ = new RagSettings(targetBlock) { Type = RagTypes.WholeDocument };

        _ = new Block(BlockTypes.AiQuery, sectionUnrelated) { ReplacementTag = "unrelated" };

        // Act
        rootWorkflow.IsolateBlockWithDependencies(targetBlock);

        // Assert
        Assert.Equal(3, rootWorkflow.Sections.Count);
        Assert.Contains(rootWorkflow.Sections, s => s.Name == "Section 1");
        Assert.Contains(rootWorkflow.Sections, s => s.Name == "Section 2");
        Assert.Contains(rootWorkflow.Sections, s => s.Name == "Section 3");
        Assert.DoesNotContain(rootWorkflow.Sections, s => s.Name == "Unrelated");
    }

    [Fact]
    public void IsolateBlockWithDependencies_TransitiveDocumentClassDependencies_KeepsEntireChain()
    {
        // Arrange - Chain: webSearch -> processor (needs web-data, creates processed-data) -> analyzer
        var rootWorkflow = new Workflow("Root");
        var section1 = new Section("Section 1", rootWorkflow);
        var section2 = new Section("Section 2", rootWorkflow);
        var section3 = new Section("Section 3", rootWorkflow);

        _ = new Block(BlockTypes.IntelligentSearch, section1)
        {
            ReplacementTag = "webSearch",
            Properties = new IntelligentSearchProperties { CreatesDocumentClass = "web-data" }
        };

        _ = new Block(BlockTypes.Json2Xlsx, section2)
        {
            ReplacementTag = "processor",
            Properties = new Json2XlsxProperties { InputDocumentClass = "web-data", CreatesDocumentClass = "processed-data" }
        };

        var targetBlock = new Block(BlockTypes.AiQuery, section3)
        {
            ReplacementTag = "analyzer",
            SupportedDocumentClasses = ["processed-data"]
        };
        _ = new RagSettings(targetBlock) { Type = RagTypes.WholeDocument };

        // Act
        rootWorkflow.IsolateBlockWithDependencies(targetBlock);

        // Assert - entire chain should be kept
        Assert.Equal(3, rootWorkflow.Sections.Count);
    }

    [Fact]
    public void IsolateBlockWithDependencies_MixedTagAndDocumentClassDependencies_KeepsAll()
    {
        // Arrange
        var rootWorkflow = new Workflow("Root");
        var section1 = new Section("Section 1", rootWorkflow);
        var section2 = new Section("Section 2", rootWorkflow);
        var section3 = new Section("Section 3", rootWorkflow);
        var sectionUnrelated = new Section("Unrelated", rootWorkflow);

        // Tag-based dependency
        _ = new Block(BlockTypes.AiQuery, section1) { ReplacementTag = "config" };

        // Document class producer
        _ = new Block(BlockTypes.IntelligentSearch, section2)
        {
            ReplacementTag = "webSearch",
            Properties = new IntelligentSearchProperties { CreatesDocumentClass = "web-data" }
        };

        // Target block depends on both via tag and document class
        var targetBlock = new Block(BlockTypes.AiQuery, section3)
        {
            ReplacementTag = "analyzer",
            SupportedDocumentClasses = ["web-data"],
            Properties = new AiQueryProperties { UserPromptTemplate = "Use config: {config}" }
        };
        _ = new RagSettings(targetBlock) { Type = RagTypes.WholeDocument };

        _ = new Block(BlockTypes.AiQuery, sectionUnrelated) { ReplacementTag = "unrelated" };

        // Act
        rootWorkflow.IsolateBlockWithDependencies(targetBlock);

        // Assert
        Assert.Equal(3, rootWorkflow.Sections.Count);
        Assert.Contains(rootWorkflow.Sections, s => s.Name == "Section 1");
        Assert.Contains(rootWorkflow.Sections, s => s.Name == "Section 2");
        Assert.Contains(rootWorkflow.Sections, s => s.Name == "Section 3");
        Assert.DoesNotContain(rootWorkflow.Sections, s => s.Name == "Unrelated");
    }

    [Fact]
    public void IsolateBlockWithDependencies_NonMatchingDocumentClass_DoesNotKeepBlock()
    {
        // Arrange
        var rootWorkflow = new Workflow("Root");
        var section1 = new Section("Section 1", rootWorkflow);
        var section2 = new Section("Section 2", rootWorkflow);

        _ = new Block(BlockTypes.IntelligentSearch, section1)
        {
            ReplacementTag = "webSearch",
            Properties = new IntelligentSearchProperties { CreatesDocumentClass = "web-data" }
        };

        var targetBlock = new Block(BlockTypes.AiQuery, section2)
        {
            ReplacementTag = "analyzer",
            SupportedDocumentClasses = ["different-class"]
        };
        _ = new RagSettings(targetBlock) { Type = RagTypes.WholeDocument };

        // Act
        rootWorkflow.IsolateBlockWithDependencies(targetBlock);

        // Assert - only section2 remains since document classes don't match
        _ = Assert.Single(rootWorkflow.Sections);
        Assert.Contains(rootWorkflow.Sections, s => s.Name == "Section 2");
    }

    [Fact]
    public void IsolateBlockWithDependencies_NullCreatesDocumentClass_DoesNotCreateDependency()
    {
        // Arrange
        var rootWorkflow = new Workflow("Root");
        var section1 = new Section("Section 1", rootWorkflow);
        var section2 = new Section("Section 2", rootWorkflow);

        _ = new Block(BlockTypes.IntelligentSearch, section1)
        {
            ReplacementTag = "webSearch",
            Properties = new IntelligentSearchProperties { CreatesDocumentClass = null }
        };

        var targetBlock = new Block(BlockTypes.AiQuery, section2)
        {
            ReplacementTag = "analyzer",
            SupportedDocumentClasses = ["web-data"]
        };
        _ = new RagSettings(targetBlock) { Type = RagTypes.WholeDocument };

        // Act
        rootWorkflow.IsolateBlockWithDependencies(targetBlock);

        // Assert - only section2 remains
        _ = Assert.Single(rootWorkflow.Sections);
        Assert.Contains(rootWorkflow.Sections, s => s.Name == "Section 2");
    }

    [Fact]
    public void IsolateBlockWithDependencies_EmptyCreatesDocumentClass_DoesNotCreateDependency()
    {
        // Arrange
        var rootWorkflow = new Workflow("Root");
        var section1 = new Section("Section 1", rootWorkflow);
        var section2 = new Section("Section 2", rootWorkflow);

        _ = new Block(BlockTypes.IntelligentSearch, section1)
        {
            ReplacementTag = "webSearch",
            Properties = new IntelligentSearchProperties { CreatesDocumentClass = "" }
        };

        var targetBlock = new Block(BlockTypes.AiQuery, section2)
        {
            ReplacementTag = "analyzer",
            SupportedDocumentClasses = ["web-data"]
        };
        _ = new RagSettings(targetBlock) { Type = RagTypes.WholeDocument };

        // Act
        rootWorkflow.IsolateBlockWithDependencies(targetBlock);

        // Assert - only section2 remains
        _ = Assert.Single(rootWorkflow.Sections);
        Assert.Contains(rootWorkflow.Sections, s => s.Name == "Section 2");
    }

    [Fact]
    public void IsolateBlockWithDependencies_EmptySupportedDocumentClasses_DoesNotCreateDependency()
    {
        // Arrange
        var rootWorkflow = new Workflow("Root");
        var section1 = new Section("Section 1", rootWorkflow);
        var section2 = new Section("Section 2", rootWorkflow);

        _ = new Block(BlockTypes.IntelligentSearch, section1)
        {
            ReplacementTag = "webSearch",
            Properties = new IntelligentSearchProperties { CreatesDocumentClass = "web-data" }
        };

        var targetBlock = new Block(BlockTypes.AiQuery, section2)
        {
            ReplacementTag = "analyzer",
            SupportedDocumentClasses = []
        };
        _ = new RagSettings(targetBlock) { Type = RagTypes.WholeDocument };

        // Act
        rootWorkflow.IsolateBlockWithDependencies(targetBlock);

        // Assert - only section2 remains
        _ = Assert.Single(rootWorkflow.Sections);
        Assert.Contains(rootWorkflow.Sections, s => s.Name == "Section 2");
    }

    [Fact]
    public void IsolateBlockWithDependencies_AllProducerBlockTypes_KeepsDependencies()
    {
        // Arrange - test all block types implementing IProducesDocumentBlockProperties
        var rootWorkflow = new Workflow("Root");
        var section1 = new Section("Section 1", rootWorkflow);
        var section2 = new Section("Section 2", rootWorkflow);
        var section3 = new Section("Section 3", rootWorkflow);

        _ = new Block(BlockTypes.IntelligentSearch, section1)
        {
            ReplacementTag = "intelligentSearch",
            Properties = new IntelligentSearchProperties { CreatesDocumentClass = "class-a" }
        };

        _ = new Block(BlockTypes.Json2Xlsx, section2)
        {
            ReplacementTag = "json2Xlsx",
            Properties = new Json2XlsxProperties { CreatesDocumentClass = "class-b" }
        };

        var targetBlock = new Block(BlockTypes.AiQuery, section3)
        {
            ReplacementTag = "analyzer",
            SupportedDocumentClasses = ["class-a", "class-b", "class-c"]
        };
        _ = new RagSettings(targetBlock) { Type = RagTypes.WholeDocument };

        // Act
        rootWorkflow.IsolateBlockWithDependencies(targetBlock);

        // Assert - all 3 sections should be kept
        Assert.Equal(3, rootWorkflow.Sections.Count);
    }

    [Fact]
    public void IsolateBlockWithDependencies_DocumentClassDependencyInSameSection_KeepsBothBlocks()
    {
        // Arrange
        var rootWorkflow = new Workflow("Root");
        var section1 = new Section("Section 1", rootWorkflow);

        _ = new Block(BlockTypes.IntelligentSearch, section1)
        {
            ReplacementTag = "webSearch",
            Properties = new IntelligentSearchProperties { CreatesDocumentClass = "web-data" }
        };

        var targetBlock = new Block(BlockTypes.AiQuery, section1)
        {
            ReplacementTag = "analyzer",
            SupportedDocumentClasses = ["web-data"]
        };
        _ = new RagSettings(targetBlock) { Type = RagTypes.WholeDocument };

        _ = new Block(BlockTypes.IntelligentSearch, section1) { ReplacementTag = "unrelated" };

        // Act
        rootWorkflow.IsolateBlockWithDependencies(targetBlock);

        // Assert
        _ = Assert.Single(rootWorkflow.Sections);
        Assert.Equal(2, section1.Blocks.Count);
        Assert.Contains(section1.Blocks, b => b.ReplacementTag == "webSearch");
        Assert.Contains(section1.Blocks, b => b.ReplacementTag == "analyzer");
        Assert.DoesNotContain(section1.Blocks, b => b.ReplacementTag == "unrelated");
    }

    [Fact]
    public void IsolateBlockWithDependencies_AiQueryReaderWithoutRagSettings_DoesNotKeepProducer()
    {
        // Arrange - an AiQuery without RAG settings never receives documents at run time
        var rootWorkflow = new Workflow("Root");
        var section1 = new Section("Section 1", rootWorkflow);
        var section2 = new Section("Section 2", rootWorkflow);

        _ = new Block(BlockTypes.IntelligentSearch, section1)
        {
            ReplacementTag = "webSearch",
            Properties = new IntelligentSearchProperties { CreatesDocumentClass = "web-data" }
        };

        var targetBlock = new Block(BlockTypes.AiQuery, section2)
        {
            ReplacementTag = "analyzer",
            SupportedDocumentClasses = ["web-data"]
        };

        // Act
        rootWorkflow.IsolateBlockWithDependencies(targetBlock);

        // Assert
        _ = Assert.Single(rootWorkflow.Sections);
        Assert.Contains(rootWorkflow.Sections, s => s.Name == "Section 2");
    }

    #endregion

    #region Block criticality Tests

    [Fact]
    public void Criticality_UnsetThroughATemplateSave_StillFollowsTheBlockType()
    {
        var workflow = new Workflow("Test");
        var section = new Section("default", workflow);
        _ = new Block(BlockTypes.Agent, section, "agent");
        _ = new Block(BlockTypes.MergeJson, section, "merge");

        Workflow reloaded = Workflow.FromJson(new WorkflowCanonicalizer().ToCanonicalJson(workflow))!;

        Block agent = reloaded.GetAllBlocks().Single(block => block.Type is BlockTypes.Agent);
        Block merge = reloaded.GetAllBlocks().Single(block => block.Type is BlockTypes.MergeJson);
        Assert.Null(agent.Properties.IsCritical);
        Assert.Null(merge.Properties.IsCritical);
        Assert.True(agent.AbortsExecutionOnFailure());
        Assert.False(merge.AbortsExecutionOnFailure());
    }

    [Fact]
    public void Criticality_ExplicitFalseOnAnAgentThroughATemplateSave_DoesNotAbort()
    {
        var workflow = new Workflow("Test");
        var section = new Section("default", workflow);
        var agent = new Block(BlockTypes.Agent, section, "agent");
        agent.Properties.IsCritical = false;

        Workflow reloaded = Workflow.FromJson(new WorkflowCanonicalizer().ToCanonicalJson(workflow))!;

        Block reloadedAgent = reloaded.GetAllBlocks().Single();
        Assert.False(reloadedAgent.Properties.IsCritical);
        Assert.False(reloadedAgent.AbortsExecutionOnFailure());
    }

    [Fact]
    public void Criticality_Unset_IsNotWrittenToTheTemplate()
    {
        var workflow = new Workflow("Test");
        var section = new Section("default", workflow);
        _ = new Block(BlockTypes.MergeJson, section, "merge");

        string json = new WorkflowCanonicalizer().ToCanonicalJson(workflow);

        Assert.DoesNotContain(nameof(BlockProperties.IsCritical), json);
    }

    [Fact]
    public void SetEveryBlockToStopTheRun_SetsEachBlockInEverySection_AndSkipsLoopBoundaries()
    {
        var workflow = new Workflow("Test");
        var first = new Section("first", workflow);
        var second = new Section("second", workflow);
        var query = new Block(BlockTypes.AiQuery, first, "query");
        var agent = new Block(BlockTypes.Agent, first, "agent");
        agent.Properties.IsCritical = false;
        var alreadyCritical = new Block(BlockTypes.MergeJson, second, "merge");
        alreadyCritical.Properties.IsCritical = true;
        var loopStart = new Block(BlockTypes.LoopStart, second, "loop_start");
        var loopEnd = new Block(BlockTypes.LoopEnd, second, "loop_end");

        Assert.Equal([query, agent], workflow.GetBlocksNotSetToStopTheRun());

        workflow.SetEveryBlockToStopTheRun();

        Assert.True(query.Properties.IsCritical);
        Assert.True(agent.Properties.IsCritical);
        Assert.True(alreadyCritical.Properties.IsCritical);
        Assert.Null(loopStart.Properties.IsCritical);
        Assert.Null(loopEnd.Properties.IsCritical);
        Assert.Empty(workflow.GetBlocksNotSetToStopTheRun());
    }

    [Fact]
    public void SetEveryBlockToStopTheRun_SkipsABlockWhoseSettingsAreKeptAsStored()
    {
        var workflow = new Workflow("Test");
        var section = new Section("first", workflow);
        var query = new Block(BlockTypes.AiQuery, section, "query");
        _ = new Block(BlockTypes.Merge, section, "unavailable")
        {
            Type = (BlockTypes)1001,
            Properties = new UnavailableBlockProperties((BlockTypes)1001, """{"Type":1001}"""),
        };

        Assert.Equal([query], workflow.GetBlocksNotSetToStopTheRun());
    }

    #endregion

    #region ExposeAllBlocksAsOutputs Tests

    [Fact]
    public void ExposeAllBlocksAsOutputs_SetsIsOutputSourceTrue()
    {
        // Arrange
        var workflow = new Workflow("Test");
        var section = new Section("default", workflow);
        var block1 = new Block(BlockTypes.AiQuery, section) { ReplacementTag = "block1", IsOutputSource = false };
        var block2 = new Block(BlockTypes.AiQuery, section) { ReplacementTag = "block2", IsOutputSource = false };

        // Act
        workflow.ExposeAllBlocksAsOutputs();

        // Assert
        Assert.True(block1.IsOutputSource);
        Assert.True(block2.IsOutputSource);
    }

    [Fact]
    public void ExposeAllBlocksAsOutputs_PreservesExistingOutputTitleTemplate()
    {
        // Arrange
        var workflow = new Workflow("Test");
        var section = new Section("default", workflow);
        var block = new Block(BlockTypes.AiQuery, section)
        {
            ReplacementTag = "block1",
            Name = "Block Name",
            OutputTitleTemplate = "Custom Title"
        };

        // Act
        workflow.ExposeAllBlocksAsOutputs();

        // Assert
        Assert.Equal("Custom Title", block.OutputTitleTemplate);
    }

    [Fact]
    public void ExposeAllBlocksAsOutputs_UsesNameWhenNoOutputTitleTemplate()
    {
        // Arrange
        var workflow = new Workflow("Test");
        var section = new Section("default", workflow);
        var block = new Block(BlockTypes.AiQuery, section)
        {
            ReplacementTag = "block1",
            Name = "Block Name",
            OutputTitleTemplate = null
        };

        // Act
        workflow.ExposeAllBlocksAsOutputs();

        // Assert
        Assert.Equal("Block Name", block.OutputTitleTemplate);
    }

    [Fact]
    public void ExposeAllBlocksAsOutputs_UsesReplacementTagWhenNoNameOrTitle()
    {
        // Arrange
        var workflow = new Workflow("Test");
        var section = new Section("default", workflow);
        var block = new Block(BlockTypes.AiQuery, section)
        {
            ReplacementTag = "my_replacement_tag",
            Name = null!,
            OutputTitleTemplate = null
        };

        // Act
        workflow.ExposeAllBlocksAsOutputs();

        // Assert
        Assert.Equal("my_replacement_tag", block.OutputTitleTemplate);
    }

    [Fact]
    public void ExposeAllBlocksAsOutputs_TopLevelWorkflow_AffectsAllNestedBlocks()
    {
        // Arrange
        var rootWorkflow = new Workflow("Root");
        var section1 = new Section("Section 1", rootWorkflow);
        var section2 = new Section("Section 2", rootWorkflow);

        var block1 = new Block(BlockTypes.AiQuery, section1) { ReplacementTag = "block1", IsOutputSource = false };
        var block2 = new Block(BlockTypes.AiQuery, section2) { ReplacementTag = "block2", IsOutputSource = false };

        // Act
        rootWorkflow.ExposeAllBlocksAsOutputs();

        // Assert
        Assert.True(block1.IsOutputSource);
        Assert.True(block2.IsOutputSource);
    }

    [Fact]
    public void ExposeAllBlocksAsOutputs_ChildWorkflow_OnlyAffectsDirectBlocks()
    {
        // Arrange
        var rootWorkflow = new Workflow("Root");
        var section1 = new Section("Section 1", rootWorkflow);
        var section2 = new Section("Section 2", rootWorkflow);

        var block1 = new Block(BlockTypes.AiQuery, section1) { ReplacementTag = "block1", IsOutputSource = false };
        var block2 = new Block(BlockTypes.AiQuery, section2) { ReplacementTag = "block2", IsOutputSource = false };

        // Act - call on child section
        section1.ExposeAllBlocksAsOutputs();

        // Assert
        Assert.True(block1.IsOutputSource);
        Assert.False(block2.IsOutputSource); // Not affected since it's in section2
    }

    [Fact]
    public void ExposeAllBlocksAsOutputs_EmptyWorkflow_DoesNotThrow()
    {
        // Arrange
        var workflow = new Workflow("Test");

        // Act & Assert (should not throw)
        workflow.ExposeAllBlocksAsOutputs();
    }

    [Fact]
    public void ExposeAllBlocksAsOutputs_WhitespaceOnlyName_UsesReplacementTag()
    {
        // Arrange
        var workflow = new Workflow("Test");
        var section = new Section("default", workflow);
        var block = new Block(BlockTypes.AiQuery, section)
        {
            ReplacementTag = "fallback_tag",
            Name = "   ",
            OutputTitleTemplate = null
        };

        // Act
        workflow.ExposeAllBlocksAsOutputs();

        // Assert
        Assert.Equal("fallback_tag", block.OutputTitleTemplate);
    }

    [Fact]
    public void ExposeAllBlocksAsOutputs_WhitespaceOutputTitleTemplate_UsesNameOrTag()
    {
        // Arrange
        var workflow = new Workflow("Test");
        var section = new Section("default", workflow);
        var block = new Block(BlockTypes.AiQuery, section)
        {
            ReplacementTag = "my_tag",
            Name = "Block Name",
            OutputTitleTemplate = "   "
        };

        // Act
        workflow.ExposeAllBlocksAsOutputs();

        // Assert
        Assert.Equal("Block Name", block.OutputTitleTemplate);
    }

    #endregion

    [Theory]
    [InlineData(WorkflowType.GENERIC, false)]
    [InlineData(WorkflowType.TEST_WORKFLOW_TYPE, false)]
    [InlineData("CorporatesCreditReport", true)]
    public void GetAvailability_ReflectsUserFacingTypes(string workflowType, bool expected)
    {
        Assert.Equal(expected, Workflow.GetAvailability(workflowType));
    }
}
