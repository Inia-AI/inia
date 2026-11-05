using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Blocks;
using IniaAi.WebApp.Components.WorkflowEditor.State;
using ContractsState = IniaAi.WebApi.Contracts.Enums.SharedComponentState;
using DomainLinkState = IniaAi.Domain.Entities.SharedComponents.Enums.SharedComponentLinkState;
using static IniaAi.WebApp.Tests.Components.WorkflowEditor.LibraryLinkTestData;

namespace IniaAi.WebApp.Tests.Components.WorkflowEditor.State;

public class LibraryLinkIndexTests
{
    private static readonly string _headHash = new('a', 64);
    private static readonly string _staleHash = new('b', 64);

    private static LibraryLinkIndex BuildIndex()
    {
        return new LibraryLinkIndex(
        [
            Component("comp-active", "Market Data", _headHash),
            Component("comp-retired", "Old Extractor", _headHash, ContractsState.Retired),
            Component("comp-headless", "Draft", headContentHash: null),
        ]);
    }

    [Fact]
    public void For_PlainBlock_None()
    {
        Block block = NewBlock();

        LibraryLinkStatus status = BuildIndex().For(block);

        Assert.Equal(LibraryLinkIndicator.None, status.Indicator);
        Assert.Null(status.ComponentName);
    }

    [Fact]
    public void For_LinkedAtHead_UpToDate()
    {
        Block block = NewBlock("comp-active", DomainLinkState.Linked, _headHash);

        LibraryLinkStatus status = BuildIndex().For(block);

        Assert.Equal(LibraryLinkIndicator.UpToDate, status.Indicator);
        Assert.Equal("Market Data", status.ComponentName);
    }

    [Fact]
    public void For_LinkedBehindHead_UpdateAvailable()
    {
        Block block = NewBlock("comp-active", DomainLinkState.Linked, _staleHash);

        LibraryLinkStatus status = BuildIndex().For(block);

        Assert.Equal(LibraryLinkIndicator.UpdateAvailable, status.Indicator);
    }

    [Fact]
    public void For_UnknownComponentId_Missing()
    {
        Block block = NewBlock("comp-gone", DomainLinkState.Linked, _headHash);

        LibraryLinkStatus status = BuildIndex().For(block);

        Assert.Equal(LibraryLinkIndicator.Missing, status.Indicator);
        Assert.Null(status.ComponentName);
    }

    [Fact]
    public void For_RetiredComponent_Retired()
    {
        Block block = NewBlock("comp-retired", DomainLinkState.Linked, _headHash);

        LibraryLinkStatus status = BuildIndex().For(block);

        Assert.Equal(LibraryLinkIndicator.Retired, status.Indicator);
        Assert.Equal("Old Extractor", status.ComponentName);
    }

    [Fact]
    public void For_BrokenOut_ReadsAsLocalContent_EvenWhenComponentMissing()
    {
        Block known = NewBlock("comp-active", DomainLinkState.BrokenOut, _staleHash);
        Block gone = NewBlock("comp-gone", DomainLinkState.BrokenOut, _staleHash);
        Section section = NewSection("comp-active", DomainLinkState.BrokenOut, _staleHash);

        Assert.Equal(new LibraryLinkStatus(LibraryLinkIndicator.None, null), BuildIndex().For(known));
        Assert.Equal(new LibraryLinkStatus(LibraryLinkIndicator.None, null), BuildIndex().For(gone));
        Assert.Equal(new LibraryLinkStatus(LibraryLinkIndicator.None, null), BuildIndex().For(section));
    }

    [Fact]
    public void For_NullLinkState_ClassifiedLikeLinked()
    {
        Block block = NewBlock("comp-active", linkState: null, _headHash);

        LibraryLinkStatus status = BuildIndex().For(block);

        Assert.Equal(LibraryLinkIndicator.UpToDate, status.Indicator);
    }

    [Fact]
    public void For_HeadlessComponent_UpdateAvailable()
    {
        Block block = NewBlock("comp-headless", DomainLinkState.Linked, _staleHash);

        LibraryLinkStatus status = BuildIndex().For(block);

        Assert.Equal(LibraryLinkIndicator.UpdateAvailable, status.Indicator);
    }

    [Fact]
    public void For_LinkedSection_SameClassification()
    {
        var workflow = new Workflow("wf") { Title = "wf" };
        var section = new Section("s", workflow) { Title = "s" };
        section.SharedComponentId = "comp-active";
        section.SharedComponentLinkState = DomainLinkState.Linked;
        section.SharedComponentVersionHash = _staleHash;

        LibraryLinkStatus status = BuildIndex().For(section);

        Assert.Equal(LibraryLinkIndicator.UpdateAvailable, status.Indicator);
        Assert.Equal("Market Data", status.ComponentName);
    }

    [Fact]
    public void AttentionCount_CountsUpdateAvailableMissingAndRetiredNodes()
    {
        var workflow = new Workflow("wf") { Title = "wf" };
        var section = new Section("s", workflow) { Title = "s" };
        StampBlock(NewBlockIn(section, "b1"), "comp-active", DomainLinkState.Linked, _headHash);
        StampBlock(NewBlockIn(section, "b2"), "comp-active", DomainLinkState.Linked, _staleHash);
        StampBlock(NewBlockIn(section, "b3"), "comp-gone", DomainLinkState.Linked, _headHash);
        StampBlock(NewBlockIn(section, "b4"), "comp-retired", DomainLinkState.Linked, _headHash);
        StampBlock(NewBlockIn(section, "b5"), "comp-active", DomainLinkState.BrokenOut, _staleHash);
        var linkedSection = new Section("s2", workflow) { Title = "s2" };
        linkedSection.SharedComponentId = "comp-active";
        linkedSection.SharedComponentLinkState = DomainLinkState.Linked;
        linkedSection.SharedComponentVersionHash = _staleHash;

        int count = BuildIndex().AttentionCount(workflow);

        Assert.Equal(4, count);
    }

    [Fact]
    public void MissingCount_CountsOnlyLinkedCopiesWhoseComponentIsAbsent()
    {
        var workflow = new Workflow("wf") { Title = "wf" };
        var section = new Section("s", workflow) { Title = "s" };
        StampBlock(NewBlockIn(section, "b1"), "comp-gone", DomainLinkState.Linked, _headHash);
        StampBlock(NewBlockIn(section, "b2"), "comp-also-gone", DomainLinkState.Linked, _headHash);
        StampBlock(NewBlockIn(section, "b3"), "comp-gone", DomainLinkState.BrokenOut, _headHash);
        StampBlock(NewBlockIn(section, "b4"), "comp-active", DomainLinkState.Linked, _staleHash);
        StampBlock(NewBlockIn(section, "b5"), "comp-retired", DomainLinkState.Linked, _headHash);

        Assert.Equal(2, BuildIndex().MissingCount(workflow));
    }

    [Fact]
    public void For_RefEntryInnerBlock_ClassifiesAgainstItsOwnComponent()
    {
        var index = new LibraryLinkIndex(
        [
            Component("comp-section", "KYC Pack", _headHash),
            Component("comp-active", "Market Data", _headHash),
        ]);
        var workflow = new Workflow("wf") { Title = "wf" };
        var section = new Section("s", workflow) { Title = "s" };
        section.SharedComponentId = "comp-section";
        section.SharedComponentLinkState = DomainLinkState.Linked;
        section.SharedComponentVersionHash = _headHash;
        Block inner = NewBlockIn(section, "kyc");
        StampBlock(inner, "comp-active", DomainLinkState.Linked, _staleHash);

        Assert.Equal(LibraryLinkIndicator.UpToDate, index.For(section).Indicator);
        LibraryLinkStatus innerStatus = index.For(inner);
        Assert.Equal(LibraryLinkIndicator.UpdateAvailable, innerStatus.Indicator);
        Assert.Equal("Market Data", innerStatus.ComponentName);
    }

    [Fact]
    public void AttentionCount_RefEntryBehindItsOwnHead_CountsOnce()
    {
        var index = new LibraryLinkIndex(
        [
            Component("comp-section", "KYC Pack", _headHash),
            Component("comp-active", "Market Data", _headHash),
        ]);
        var workflow = new Workflow("wf") { Title = "wf" };
        var section = new Section("s", workflow) { Title = "s" };
        section.SharedComponentId = "comp-section";
        section.SharedComponentLinkState = DomainLinkState.Linked;
        section.SharedComponentVersionHash = _headHash;
        StampBlock(NewBlockIn(section, "kyc"), "comp-active", DomainLinkState.Linked, _staleHash);
        StampBlock(NewBlockIn(section, "aux"), "comp-active", DomainLinkState.BrokenOut, _staleHash);

        Assert.Equal(1, index.AttentionCount(workflow));
    }

    [Fact]
    public void HeadVersionIdFor_KnownComponent_ReturnsHeadVersionId()
    {
        Assert.Equal("v1", BuildIndex().HeadVersionIdFor("comp-active"));
    }

    [Fact]
    public void HeadVersionIdFor_UnknownOrHeadless_ReturnsNull()
    {
        Assert.Null(BuildIndex().HeadVersionIdFor("comp-gone"));
        Assert.Null(BuildIndex().HeadVersionIdFor("comp-headless"));
    }

}
