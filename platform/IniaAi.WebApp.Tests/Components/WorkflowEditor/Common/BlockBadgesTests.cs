using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Blocks;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties;
using IniaAi.Domain.Entities.Workflows.Enums;
using IniaAi.WebApp.Components.WorkflowEditor.Common;
using IniaAi.WebApp.Components.WorkflowEditor.State;
using IniaAi.WebApi.Contracts.Responses.SharedComponents;
using Color = MudBlazor.Color;
using ContractsKind = IniaAi.WebApi.Contracts.Enums.SharedComponentKind;
using ContractsState = IniaAi.WebApi.Contracts.Enums.SharedComponentState;
using DomainLinkState = IniaAi.Domain.Entities.SharedComponents.Enums.SharedComponentLinkState;

namespace IniaAi.WebApp.Tests.Components.WorkflowEditor.Common;

public class BlockBadgesTests
{
    private static readonly string _headHash = new('a', 64);
    private static readonly string _staleHash = new('b', 64);

    private static Block NewBlock()
    {
        var workflow = new Workflow("wf") { Title = "wf" };
        var section = new Section("section", workflow);
        return new Block(BlockTypes.AiQuery, section, "tag");
    }

    private static (Workflow Workflow, Block Block) NewWiredBlock(BlockTypes type)
    {
        var workflow = new Workflow("wf") { Title = "wf" };
        var section = new Section("section", workflow);
        return (workflow, new Block(type, section, "tag"));
    }

    private static Block LinkedBlock(string componentId, DomainLinkState linkState, string versionHash)
    {
        Block block = NewBlock();
        block.SharedComponentId = componentId;
        block.SharedComponentLinkState = linkState;
        block.SharedComponentVersionHash = versionHash;
        return block;
    }

    private static LibraryLinkIndex Index(ContractsState state = ContractsState.Active)
    {
        return new LibraryLinkIndex(
        [
            new SharedComponentSummaryDto(
                "comp-1", ContractsKind.Block, "Market Data", null, null, null,
                state, "v1", _headHash, null, 0, []),
        ]);
    }

    [Fact]
    public void For_BlockWithRegressionSchema_HasNoRegressionBadge()
    {
        Block block = NewBlock();
        block.RegressionSchema = "{\"Revenue\":{\"Source\":\"xml\"}}";

        List<BlockBadge> badges = BlockBadges.For(block).ToList();

        Assert.DoesNotContain(badges, badge => badge.Key == "regression");
    }

    [Fact]
    public void For_OutputSourceBlock_IncludesOutputBadge()
    {
        Block block = NewBlock();
        block.IsOutputSource = true;

        List<BlockBadge> badges = BlockBadges.For(block).ToList();

        Assert.Contains(badges, badge => badge.Key == "output");
    }

    [Fact]
    public void For_PlainBlock_HasNoBadges()
    {
        Block block = NewBlock();

        Assert.Empty(BlockBadges.For(block));
    }

    [Fact]
    public void For_BlockWithSeveralFlags_ReturnsEachOnce()
    {
        (Workflow workflow, Block block) = NewWiredBlock(BlockTypes.AiQuery);
        _ = new WorkflowSignatureField(workflow, "signed_by");
        ((AiQueryProperties)block.Properties).UserPromptTemplate = "Sign here: {signed_by}";
        block.IsOutputSource = true;

        List<BlockBadge> badges = BlockBadges.For(block).ToList();

        Assert.Equal(badges.Count, badges.Select(badge => badge.Key).Distinct().Count());
        Assert.Contains(badges, badge => badge.Key == "signature");
        Assert.Contains(badges, badge => badge.Key == "output");
    }

    [Fact]
    public void For_BearingBlockReferencingWorkflowField_IncludesSignatureBadge()
    {
        (Workflow workflow, Block block) = NewWiredBlock(BlockTypes.AiQuery);
        _ = new WorkflowSignatureField(workflow, "signed_by");
        ((AiQueryProperties)block.Properties).UserPromptTemplate = "Sign here: {signed_by}";

        List<BlockBadge> badges = BlockBadges.For(block).ToList();

        Assert.Contains(badges, badge => badge.Key == "signature");
    }

    [Fact]
    public void For_BearingBlockWithoutReferencedField_OmitsSignatureBadge()
    {
        (Workflow workflow, Block block) = NewWiredBlock(BlockTypes.AiQuery);
        _ = new WorkflowSignatureField(workflow, "signed_by");
        ((AiQueryProperties)block.Properties).UserPromptTemplate = "No tokens here";

        List<BlockBadge> badges = BlockBadges.For(block).ToList();

        Assert.DoesNotContain(badges, badge => badge.Key == "signature");
    }

    [Fact]
    public void For_NonBearingBlock_OmitsSignatureBadge()
    {
        (Workflow workflow, Block block) = NewWiredBlock(BlockTypes.CalculateNumber);
        _ = new WorkflowSignatureField(workflow, "signed_by");

        List<BlockBadge> badges = BlockBadges.For(block).ToList();

        Assert.DoesNotContain(badges, badge => badge.Key == "signature");
    }

    [Fact]
    public void AllBadges_HaveCompleteCopyWithoutDashes()
    {
        Block block = NewBlock();
        block.IsOutputSource = true;
        block.RegressionSchema = "{\"Revenue\":{\"Source\":\"xml\"}}";

        foreach (BlockBadge badge in BlockBadges.For(block))
        {
            Assert.False(string.IsNullOrWhiteSpace(badge.Icon));
            Assert.False(string.IsNullOrWhiteSpace(badge.Label));
            Assert.False(string.IsNullOrWhiteSpace(badge.Tooltip));
            Assert.DoesNotContain('—', badge.Tooltip);
            Assert.DoesNotContain('–', badge.Tooltip);
        }
    }

    [Fact]
    public void For_LinkedAtHead_WithIndex_YieldsQuietLibraryBadge()
    {
        Block block = LinkedBlock("comp-1", DomainLinkState.Linked, _headHash);

        BlockBadge badge = Assert.Single(BlockBadges.For(block, Index()));

        Assert.Equal("library", badge.Key);
        Assert.Equal(Color.Default, badge.Color);
        Assert.Equal("Library", badge.Label);
        Assert.Contains("Market Data", badge.Tooltip);
    }

    [Fact]
    public void For_LinkedBehindHead_WithIndex_YieldsUpdateAvailable()
    {
        Block block = LinkedBlock("comp-1", DomainLinkState.Linked, _staleHash);

        BlockBadge badge = Assert.Single(BlockBadges.For(block, Index()));

        Assert.Equal("library-update", badge.Key);
        Assert.Equal(Color.Warning, badge.Color);
        Assert.Equal("Update available", badge.Label);
    }

    [Fact]
    public void For_UnknownComponent_WithIndex_YieldsBrokenLink()
    {
        Block block = LinkedBlock("comp-gone", DomainLinkState.Linked, _headHash);

        BlockBadge badge = Assert.Single(BlockBadges.For(block, Index()));

        Assert.Equal("library-missing", badge.Key);
        Assert.Equal(Color.Error, badge.Color);
    }

    [Fact]
    public void For_RetiredComponent_WithIndex_YieldsRetiredBadge()
    {
        Block block = LinkedBlock("comp-1", DomainLinkState.Linked, _headHash);

        BlockBadge badge = Assert.Single(BlockBadges.For(block, Index(ContractsState.Retired)));

        Assert.Equal("library-retired", badge.Key);
        Assert.Equal(Color.Default, badge.Color);
    }

    [Fact]
    public void For_BrokenOutBlock_WithIndex_YieldsNoLibraryBadge()
    {
        Block block = LinkedBlock("comp-1", DomainLinkState.BrokenOut, _staleHash);

        Assert.Empty(BlockBadges.For(block, Index()));
    }

    [Fact]
    public void For_LinkedBlock_WithoutIndex_YieldsNoLibraryBadge()
    {
        Block block = LinkedBlock("comp-1", DomainLinkState.Linked, _headHash);

        Assert.Empty(BlockBadges.For(block, linkIndex: null));
        Assert.Empty(BlockBadges.For(block));
    }

    [Fact]
    public void For_LinkedOutputBlock_AppendsLibraryBadgeAfterCatalogBadges()
    {
        Block block = LinkedBlock("comp-1", DomainLinkState.Linked, _headHash);
        block.IsOutputSource = true;

        List<BlockBadge> badges = [.. BlockBadges.For(block, Index())];

        Assert.Equal(["output", "library"], badges.Select(badge => badge.Key));
    }
}
