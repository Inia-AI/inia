using IniaAi.Core.Workflows.Scheduling;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Blocks;
using IniaAi.Domain.Entities.Workflows.Enums;

namespace IniaAi.Core.Tests.Workflows.Scheduling;

public class DispatchSegmentsTests
{
    private static Block AddBlock(Section section, string tag, BlockTypes type = BlockTypes.Merge)
    {
        return new Block(type, section, tag) { Order = section.Blocks.Count };
    }

    private static bool NoFence(Block block)
    {
        return false;
    }

    [Fact]
    public void PlainSections_CollapseIntoOneSegment()
    {
        var workflow = new Workflow("W");
        Section first = new Section("One", workflow);
        _ = AddBlock(first, "a");
        Section second = new Section("Two", workflow);
        _ = AddBlock(second, "b");

        List<DispatchSegment> segments = DispatchSegments.Split(workflow, NoFence);

        DispatchSegment only = Assert.Single(segments);
        Assert.False(only.IsLoop);
        Assert.Equal(2, only.Sections.Count);
    }

    [Fact]
    public void LoopSection_IsItsOwnSegmentAndSplitsTheSectionsAroundIt()
    {
        var workflow = new Workflow("W");
        Section before = new Section("Before", workflow);
        _ = AddBlock(before, "a");
        Section loop = new Section("Loop", workflow);
        _ = AddBlock(loop, "loopStart", BlockTypes.LoopStart);
        Section after = new Section("After", workflow);
        _ = AddBlock(after, "b");

        List<DispatchSegment> segments = DispatchSegments.Split(workflow, NoFence);

        Assert.Equal(3, segments.Count);
        Assert.False(segments[0].IsLoop);
        Assert.True(segments[1].IsLoop);
        Assert.Same(loop, Assert.Single(segments[1].Sections));
        Assert.False(segments[2].IsLoop);
    }

    [Fact]
    public void ParameterizedSection_IsItsOwnSegment()
    {
        var workflow = new Workflow("W");
        Section before = new Section("Before", workflow);
        _ = AddBlock(before, "a");
        Section parameterized = new Section("Parameterized", workflow);
        parameterized.ParameterizedConfig = "{\"variants\":[]}";
        _ = AddBlock(parameterized, "p");
        Section after = new Section("After", workflow);
        _ = AddBlock(after, "b");

        List<DispatchSegment> segments = DispatchSegments.Split(workflow, NoFence);

        Assert.Equal(3, segments.Count);
        Assert.True(segments[1].IsLoop);
    }

    [Fact]
    public void FenceBlock_ClosesTheSegmentAfterItsOwnSection()
    {
        var workflow = new Workflow("W");
        Section first = new Section("One", workflow);
        _ = AddBlock(first, "a");
        Block fence = AddBlock(first, "topics", BlockTypes.BuildTopicsForAllDocuments);
        Section second = new Section("Two", workflow);
        _ = AddBlock(second, "b");

        List<DispatchSegment> segments = DispatchSegments.Split(workflow, block => ReferenceEquals(block, fence));

        Assert.Equal(2, segments.Count);
        Assert.Single(segments[0].Sections);
        Assert.Single(segments[1].Sections);
    }

    [Fact]
    public void SectionGateReadingAPendingTag_ClosesTheSegment()
    {
        var workflow = new Workflow("W");
        Section first = new Section("One", workflow);
        _ = AddBlock(first, "producer");
        Section second = new Section("Two", workflow);
        second.TextInputTemplate = "{producer}";
        _ = AddBlock(second, "b");

        List<DispatchSegment> segments = DispatchSegments.Split(workflow, NoFence);

        Assert.Equal(2, segments.Count);
        Assert.Same(first, Assert.Single(segments[0].Sections));
        Assert.Same(second, Assert.Single(segments[1].Sections));
    }

    [Fact]
    public void SectionGateReadingItsOwnBlocks_NeitherSplitsNorHangs()
    {
        var workflow = new Workflow("W");
        Section only = new Section("Only", workflow) { TextInputTemplate = "{a}" };
        _ = AddBlock(only, "a");

        List<DispatchSegment> segments = DispatchSegments.Split(workflow, NoFence);

        Assert.Same(only, Assert.Single(Assert.Single(segments).Sections));
    }

    [Fact]
    public void SeparatorSections_AreExcludedAndDoNotSplitTheRun()
    {
        var workflow = new Workflow("W");
        Section first = new Section("One", workflow);
        _ = AddBlock(first, "a");
        var divider = new Section("Divider", workflow);
        divider.Properties.IsSeparator = true;
        Section second = new Section("Two", workflow);
        _ = AddBlock(second, "b");

        List<DispatchSegment> segments = DispatchSegments.Split(workflow, NoFence);

        DispatchSegment only = Assert.Single(segments);
        Assert.Equal([first, second], only.Sections);
    }

    [Fact]
    public void SegmentIndexByBlockId_NumbersEveryBlockBySegment()
    {
        var workflow = new Workflow("W");
        Section first = new Section("One", workflow);
        Block a = AddBlock(first, "a");
        Section loop = new Section("Loop", workflow);
        _ = AddBlock(loop, "loopStart", BlockTypes.LoopStart);
        Section after = new Section("After", workflow);
        Block b = AddBlock(after, "b");

        IReadOnlyDictionary<string, int> byBlock =
            DispatchSegments.IndexByBlockId(DispatchSegments.Split(workflow, NoFence));

        Assert.Equal(0, byBlock[a.Id]);
        Assert.Equal(2, byBlock[b.Id]);
    }
}
