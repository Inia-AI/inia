using IniaAi.Core.Workflows.Annotations;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Blocks;
using IniaAi.Domain.Entities.Workflows.Enums;

namespace IniaAi.Core.Tests.Workflows.Annotations;

public class WorkflowAnnotationsContextTests
{
    [Fact]
    public void AddBlockAnnotations_ShouldAddAnnotationsToMap()
    {
        // Arrange
        var context = new WorkflowAnnotationsContext();
        var workflow = new Section("workflow", new Workflow());
        var block = new Block(BlockTypes.Merge, workflow);
        var annotations = new BlockAnnotations();
        annotations.AddFact(new Fact("Test fact"));

        // Act
        context.AddBlockAnnotations(block, annotations);
        BlockAnnotations? retrievedAnnotations = context.GetAnnotationsForBlock(block.Id);

        // Assert
        Assert.NotNull(retrievedAnnotations);
        _ = Assert.Single(retrievedAnnotations.Facts);
        Assert.Equal("Test fact", retrievedAnnotations.Facts[0].Value);
    }

    [Fact]
    public void AddBlockAnnotations_ASecondWriteForTheSameBlock_ReplacesTheFirst()
    {
        // A verification redo leg re-processes its block's annotations; the released attempt's
        // write must win so downstream citations never read a superseded round's.
        var context = new WorkflowAnnotationsContext();
        var workflow = new Section("workflow", new Workflow());
        var block = new Block(BlockTypes.Merge, workflow);
        var roundOne = new BlockAnnotations();
        roundOne.AddFact(new Fact("round-one fact"));
        var roundTwo = new BlockAnnotations();
        roundTwo.AddFact(new Fact("round-two fact"));

        context.AddBlockAnnotations(block, roundOne);
        context.AddBlockAnnotations(block, roundTwo);

        BlockAnnotations? retrieved = context.GetAnnotationsForBlock(block.Id);
        Assert.NotNull(retrieved);
        Assert.Same(roundTwo, retrieved);
        Assert.Equal("round-two fact", Assert.Single(retrieved.Facts).Value);
    }

    [Fact]
    public void AddBlockAnnotations_WithNullAnnotations_ShouldNotFail()
    {
        // Arrange
        var context = new WorkflowAnnotationsContext();
        var workflow = new Section("workflow", new Workflow());
        var block = new Block(BlockTypes.Merge, workflow);

        // Act & Assert (no exception)
        context.AddBlockAnnotations(block, null);
    }

    [Fact]
    public void GetAnnotationsForBlock_ForNonExistingBlock_ShouldReturnNull()
    {
        // Arrange
        var context = new WorkflowAnnotationsContext();
        var workflow = new Section("workflow", new Workflow());
        var block = new Block(BlockTypes.Merge, workflow);

        // Act
        BlockAnnotations? result = context.GetAnnotationsForBlock(block.Id);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void GetReferencedAnnotations_WithReferencedBlocks_ReturnsTheirAnnotations()
    {
        // Arrange
        var context = new WorkflowAnnotationsContext();
        var workflow = new Section("workflow", new Workflow());

        // Create first block with annotations
        var block1 = new Block(BlockTypes.Merge, workflow);
        var annotations1 = new BlockAnnotations();
        annotations1.AddFact(new Fact("Fact from block 1"));
        context.AddBlockAnnotations(block1, annotations1);

        // Create second block with annotations
        var block2 = new Block(BlockTypes.Merge, workflow);
        var annotations2 = new BlockAnnotations();
        annotations2.AddFact(new Fact("Fact from block 2"));
        context.AddBlockAnnotations(block2, annotations2);

        // Act
        List<BlockAnnotations> referencedAnnotations = context.GetReferencedAnnotations([block1, block2]);

        // Assert
        Assert.Equal(2, referencedAnnotations.Count);
        Assert.Contains(referencedAnnotations, a => a.Facts.Count == 1 && a.Facts[0].Value == "Fact from block 1");
        Assert.Contains(referencedAnnotations, a => a.Facts.Count == 1 && a.Facts[0].Value == "Fact from block 2");
    }

    [Fact]
    public void GetReferencedAnnotations_WithEmptyBlockList_ReturnsEmptyList()
    {
        // Arrange
        var context = new WorkflowAnnotationsContext();

        // Act
        List<BlockAnnotations> result = context.GetReferencedAnnotations([]);

        // Assert
        Assert.Empty(result);
    }

    [Fact]
    public void GetReferencedAnnotations_WithBlockWithoutAnnotations_SkipsIt()
    {
        // Arrange
        var context = new WorkflowAnnotationsContext();
        var workflow = new Section("workflow", new Workflow());

        // Create block with annotations
        var blockWithAnnotations = new Block(BlockTypes.Merge, workflow);
        var annotations = new BlockAnnotations();
        annotations.AddFact(new Fact("Fact from block"));
        context.AddBlockAnnotations(blockWithAnnotations, annotations);

        // Create block without annotations
        var blockWithoutAnnotations = new Block(BlockTypes.Merge, workflow);

        // Act
        List<BlockAnnotations> result = context.GetReferencedAnnotations([blockWithAnnotations, blockWithoutAnnotations]);

        // Assert - only the block with annotations is included
        _ = Assert.Single(result);
        Assert.Equal("Fact from block", result[0].Facts[0].Value);
    }
}
