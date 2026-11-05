using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Blocks;
using IniaAi.Domain.Entities.Workflows.Enums;

namespace IniaAi.Domain.Tests.Data.Workflows;

public class StableSectionIdTests
{
    [Fact]
    public void Ctor_WorkflowBound_MintsNonNullStableSectionId()
    {
        var workflow = new Workflow("root");

        var section = new Section("s", workflow);

        Assert.False(string.IsNullOrEmpty(section.StableSectionId));
    }

    [Fact]
    public void Ctor_Parameterless_LeavesStableSectionIdNull()
    {
        var section = new Section();

        Assert.Null(section.StableSectionId);
    }

    [Fact]
    public void CloneTo_PreservesStableSectionId()
    {
        var workflow = new Workflow("root");
        var source = new Section("source", workflow);
        var destination = new Section("dest", workflow);

        source.CloneTo(destination);

        Assert.False(string.IsNullOrEmpty(destination.StableSectionId));
        Assert.Equal(source.StableSectionId, destination.StableSectionId);
    }

    [Fact]
    public void WorkflowCloneTo_PreservesSectionAndBlockStableIds()
    {
        var source = new Workflow("source");
        var section = new Section("s", source) { Title = "s" };
        var block = new Block(BlockTypes.AiQuery, section, "tag");
        var destination = new Workflow("destination");
        string? templateStableSectionId = section.StableSectionId;
        string? templateStableBlockId = block.StableBlockId;

        source.CloneTo(destination);

        Section clonedSection = destination.Sections.Single();
        Block clonedBlock = clonedSection.Blocks.Single();
        Assert.False(string.IsNullOrEmpty(clonedSection.StableSectionId));
        Assert.Equal(templateStableSectionId, clonedSection.StableSectionId);
        Assert.False(string.IsNullOrEmpty(clonedBlock.StableBlockId));
        Assert.Equal(templateStableBlockId, clonedBlock.StableBlockId);
    }

    [Fact]
    public void CloneToWorkflow_ReMintsStableSectionId()
    {
        var source = new Workflow("source");
        var section = new Section("s", source) { Title = "s" };
        var destination = new Workflow("destination");
        string? donorStableSectionId = section.StableSectionId;

        Section copied = section.CloneToWorkflow(destination, 0);

        Assert.Equal(donorStableSectionId, section.StableSectionId);
        Assert.False(string.IsNullOrEmpty(copied.StableSectionId));
        Assert.NotEqual(donorStableSectionId, copied.StableSectionId);
    }

    [Fact]
    public void DuplicateSectionAt_ReMintsStableSectionId()
    {
        var workflow = new Workflow("root");
        var section = new Section("s", workflow) { Title = "s" };
        string? donorStableSectionId = section.StableSectionId;

        Section duplicated = workflow.DuplicateSectionAt(section.Order);

        Assert.Equal(donorStableSectionId, section.StableSectionId);
        Assert.False(string.IsNullOrEmpty(duplicated.StableSectionId));
        Assert.NotEqual(donorStableSectionId, duplicated.StableSectionId);
    }
}
