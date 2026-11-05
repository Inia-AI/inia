using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Blocks;
using IniaAi.Domain.Entities.Workflows.Enums;
using IniaAi.Domain.Replay;

namespace IniaAi.Domain.Tests.Replay;

public class BlockIdentityMatcherTests
{
    [Fact]
    public void Resolve_UniqueStableId_ReturnsMatched()
    {
        var workflow = new Workflow("root");
        var section = new Section("s", workflow) { Title = "Section One" };
        var target = new Block(BlockTypes.AiQuery, section, "alpha") { StableBlockId = "stableA" };

        IReadOnlyDictionary<string, List<Block>> stableIndex = BlockIdentityMatcher.BuildStableIndex(workflow);

        BlockMatchResult result = BlockIdentityMatcher.Resolve("stableA", stableIndex);

        Assert.Equal(BlockMatchKind.Matched, result.Kind);
        Assert.Same(target, result.Block);
        Assert.Equal(1, result.CandidateCount);
    }

    [Fact]
    public void Resolve_StableIdSharedByTwoTargetBlocks_ReturnsAmbiguousWithCandidates()
    {
        var workflow = new Workflow("root");
        var section = new Section("s", workflow) { Title = "Section One" };
        var first = new Block(BlockTypes.AiQuery, section, "alpha") { StableBlockId = "dup" };
        var second = new Block(BlockTypes.Merge, section, "bravo") { StableBlockId = "dup" };

        IReadOnlyDictionary<string, List<Block>> stableIndex = BlockIdentityMatcher.BuildStableIndex(workflow);

        BlockMatchResult result = BlockIdentityMatcher.Resolve("dup", stableIndex);

        Assert.Equal(BlockMatchKind.Ambiguous, result.Kind);
        Assert.Null(result.Block);
        Assert.Equal(2, result.CandidateCount);
        Assert.NotNull(result.Candidates);
        Assert.Equal(2, result.Candidates!.Count);
        Assert.Contains(first, result.Candidates);
        Assert.Contains(second, result.Candidates);
    }

    [Fact]
    public void BuildStableIndex_SkipsNullAndEmptyStableBlockIds()
    {
        var workflow = new Workflow("root");
        var section = new Section("s", workflow) { Title = "Section One" };
        var withId = new Block(BlockTypes.AiQuery, section, "alpha") { StableBlockId = "stableA" };
        _ = new Block(BlockTypes.Merge, section, "bravo") { StableBlockId = null };
        _ = new Block(BlockTypes.CalculateNumber, section, "charlie") { StableBlockId = string.Empty };

        IReadOnlyDictionary<string, List<Block>> stableIndex = BlockIdentityMatcher.BuildStableIndex(workflow);

        _ = Assert.Single(stableIndex);
        Assert.True(stableIndex.ContainsKey("stableA"));
        Assert.Same(withId, stableIndex["stableA"].Single());
    }

    [Fact]
    public void Resolve_StableIdAbsentInTarget_ReturnsNotFound()
    {
        var workflow = new Workflow("root");
        var section = new Section("s", workflow) { Title = "Section One" };
        _ = new Block(BlockTypes.AiQuery, section, "alpha") { StableBlockId = "stableA" };

        IReadOnlyDictionary<string, List<Block>> stableIndex = BlockIdentityMatcher.BuildStableIndex(workflow);

        BlockMatchResult result = BlockIdentityMatcher.Resolve("missing-stable-id", stableIndex);

        Assert.Equal(BlockMatchKind.NotFound, result.Kind);
        Assert.Null(result.Block);
        Assert.Equal(0, result.CandidateCount);
    }

    [Fact]
    public void Resolve_NullOrEmptyStableId_ReturnsNotFound()
    {
        var workflow = new Workflow("root");
        var section = new Section("s", workflow) { Title = "Section One" };
        _ = new Block(BlockTypes.AiQuery, section, "alpha") { StableBlockId = "stableA" };

        IReadOnlyDictionary<string, List<Block>> stableIndex = BlockIdentityMatcher.BuildStableIndex(workflow);

        Assert.Equal(BlockMatchKind.NotFound, BlockIdentityMatcher.Resolve(null, stableIndex).Kind);
        Assert.Equal(BlockMatchKind.NotFound, BlockIdentityMatcher.Resolve(string.Empty, stableIndex).Kind);
    }
}
