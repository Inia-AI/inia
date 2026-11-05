using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Blocks;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties;
using IniaAi.Domain.Entities.Workflows.Enums;

namespace IniaAi.Domain.Tests.Data.Workflows;

public class StableBlockIdTests
{
    [Fact]
    public void Ctor_SectionBound_MintsNonNullStableBlockId()
    {
        var workflow = new Workflow("root");
        var section = new Section("s", workflow);

        var block = new Block(BlockTypes.AiQuery, section, "tag");

        Assert.False(string.IsNullOrEmpty(block.StableBlockId));
    }

    [Fact]
    public void CloneToSection_PreservesStableBlockId()
    {
        var workflow = new Workflow("root");
        var source = new Section("source", workflow);
        var destination = new Section("dest", workflow);
        var original = new Block(BlockTypes.AiQuery, source, "tag");

        Block clone = original.CloneToSection(destination);

        Assert.Equal(original.StableBlockId, clone.StableBlockId);
    }

    [Fact]
    public void DuplicateBlockToSection_ReMintsFreshStableBlockId()
    {
        var workflow = new Workflow("root");
        var section = new Section("s", workflow);
        var original = new Block(BlockTypes.AiQuery, section, "tag");

        Block duplicate = original.DuplicateBlockToSection(section);

        Assert.False(string.IsNullOrEmpty(duplicate.StableBlockId));
        Assert.NotEqual(original.StableBlockId, duplicate.StableBlockId);
    }

    [Fact]
    public void CopyToSection_MintsFreshStableBlockIdAndKeepsReplacementTag()
    {
        var source = new Workflow("source");
        var sourceSection = new Section("s", source) { Title = "s" };
        var original = new Block(BlockTypes.AiQuery, sourceSection, "tag");
        var destination = new Workflow("destination");
        var destinationSection = new Section("d", destination) { Title = "d" };
        string? donorStableBlockId = original.StableBlockId;

        Block copy = original.CopyToSection(destinationSection, 0);

        Assert.Equal(donorStableBlockId, original.StableBlockId);
        Assert.False(string.IsNullOrEmpty(copy.StableBlockId));
        Assert.NotEqual(donorStableBlockId, copy.StableBlockId);
        Assert.Equal(original.ReplacementTag, copy.ReplacementTag);
    }

    [Fact]
    public void ProcessLoops_ClonedStableBlockIds_AreDeterministicAcrossRuns()
    {
        List<string> first = ExpandAndCollectStableIds();
        List<string> second = ExpandAndCollectStableIds();

        Assert.Equal(first, second);
    }

    [Fact]
    public void ProcessLoops_SuffixesClonedAndLoopItemStableBlockIds()
    {
        List<string> ids = ExpandAndCollectStableIds();

        Assert.Contains("loopitem_loopstart_0", ids);
        Assert.Contains("loopitem_loopstart_1", ids);
        Assert.Contains("srcblock_items_0", ids);
        Assert.Contains("srcblock_items_1", ids);
    }

    [Fact]
    public void ProcessLoops_NullSourceStableBlockId_StaysNullAfterExpansion()
    {
        Section section = BuildLoopSection();

        section.ProcessLoops([]);

        int nullStableCount = section.Blocks.Count(b => string.IsNullOrEmpty(b.StableBlockId));

        // The null-source body block produces one clone per list item (two), each staying null.
        Assert.Equal(2, nullStableCount);
    }

    [Fact]
    public void ProcessLoops_GivesEachCopyOfADataOutputItsOwnName()
    {
        Section section = BuildLoopSection();
        section.Blocks.Single(block => block.ReplacementTag == "body").DataOutputName = "facility";

        section.ProcessLoops([]);

        Assert.Equal(
            ["facility_0", "facility_1"],
            section.Blocks.Where(block => block.IsDataOutput).Select(block => block.DataOutputName));
    }

    [Fact]
    public void ProcessLoops_KeepsACopysDataOutputNameWithinTheColumn()
    {
        Section section = BuildLoopSection();
        section.Blocks.Single(block => block.ReplacementTag == "body").DataOutputName = new string('n', Block.MAX_REPLACEMENT_TAG_LENGTH);

        section.ProcessLoops([]);

        Assert.All(
            section.Blocks.Where(block => block.IsDataOutput),
            block => Assert.Equal(Block.MAX_REPLACEMENT_TAG_LENGTH, block.DataOutputName!.Length));
        Assert.Equal(2, section.Blocks.Where(block => block.IsDataOutput).Select(block => block.DataOutputName).Distinct().Count());
    }

    [Fact]
    public void DuplicateSectionAt_ReMintsStableBlockIds()
    {
        var workflow = new Workflow("root");
        var section = new Section("s", workflow) { Title = "s" };
        var original = new Block(BlockTypes.AiQuery, section, "tag");

        Section duplicated = workflow.DuplicateSectionAt(section.Order);
        Block copy = duplicated.Blocks.Single();

        Assert.False(string.IsNullOrEmpty(copy.StableBlockId));
        Assert.NotEqual(original.StableBlockId, copy.StableBlockId);
    }

    [Fact]
    public void CloneToWorkflow_ReMintsStableBlockIds()
    {
        var source = new Workflow("source");
        var section = new Section("s", source) { Title = "s" };
        var original = new Block(BlockTypes.AiQuery, section, "tag");
        var destination = new Workflow("destination");

        Section copied = section.CloneToWorkflow(destination, 0);
        Block copy = copied.Blocks.Single();

        Assert.False(string.IsNullOrEmpty(copy.StableBlockId));
        Assert.NotEqual(original.StableBlockId, copy.StableBlockId);
    }

    private static List<string> ExpandAndCollectStableIds()
    {
        Section section = BuildLoopSection();

        section.ProcessLoops([]);

        return [.. section.Blocks
            .Where(b => !string.IsNullOrEmpty(b.StableBlockId))
            .Select(b => b.StableBlockId!)
            .OrderBy(id => id, StringComparer.Ordinal)];
    }

    private static Section BuildLoopSection()
    {
        var workflow = new Workflow("root");
        var section = new Section("s", workflow) { Title = "s" };

        var loopStart = new Block(BlockTypes.LoopStart, section)
        {
            Properties = new LoopStartProperties
            {
                ListTitle = "items",
                LoopedListTemplate = "alpha\nbravo",
            },
        };
        // Deterministic id so loop-item expectations are stable across test-built sections,
        // mirroring production where baseline and rerun expand the same template.
        loopStart.StableBlockId = "loopstart";

        var withStable = new Block(BlockTypes.AiQuery, section, "body")
        {
            StableBlockId = "srcblock",
        };
        Assert.NotNull(withStable);

        var nullStable = new Block(BlockTypes.Merge, section, "nullbody")
        {
            StableBlockId = null,
        };
        Assert.NotNull(nullStable);

        _ = new Block(BlockTypes.LoopEnd, section);

        return section;
    }
}
