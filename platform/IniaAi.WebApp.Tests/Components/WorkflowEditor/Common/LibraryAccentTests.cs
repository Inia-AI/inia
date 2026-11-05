using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Blocks;
using IniaAi.WebApp.Components.WorkflowEditor.Common;
using IniaAi.WebApp.Components.WorkflowEditor.State;
using ContractsState = IniaAi.WebApi.Contracts.Enums.SharedComponentState;
using DomainLinkState = IniaAi.Domain.Entities.SharedComponents.Enums.SharedComponentLinkState;
using static IniaAi.WebApp.Tests.Components.WorkflowEditor.LibraryLinkTestData;

namespace IniaAi.WebApp.Tests.Components.WorkflowEditor.Common;

public class LibraryAccentTests
{
    private static readonly string _headHash = new('a', 64);
    private static readonly string _staleHash = new('b', 64);

    private static LibraryLinkIndex Index(ContractsState state = ContractsState.Active)
    {
        return new LibraryLinkIndex([Component("comp-1", "Market Data", _headHash, state)]);
    }

    [Fact]
    public void CssClass_Unlinked_IsBlank()
    {
        Assert.Equal(string.Empty, LibraryAccent.CssClass(LibraryLinkIndicator.None));
    }

    [Theory]
    [InlineData(LibraryLinkIndicator.UpToDate)]
    [InlineData(LibraryLinkIndicator.UpdateAvailable)]
    [InlineData(LibraryLinkIndicator.Retired)]
    public void CssClass_LiveLink_IsTheBaseAccentAlone(LibraryLinkIndicator indicator)
    {
        Assert.Equal(LibraryAccent.BASE_CLASS, LibraryAccent.CssClass(indicator));
    }

    [Fact]
    public void CssClass_MissingComponent_AddsTheBrokenModifier()
    {
        string css = LibraryAccent.CssClass(LibraryLinkIndicator.Missing);

        Assert.Contains(LibraryAccent.BASE_CLASS, css);
        Assert.Contains(LibraryAccent.BROKEN_CLASS, css);
    }

    [Theory]
    [InlineData(LibraryLinkIndicator.UpToDate)]
    [InlineData(LibraryLinkIndicator.UpdateAvailable)]
    [InlineData(LibraryLinkIndicator.Retired)]
    [InlineData(LibraryLinkIndicator.Missing)]
    public void CssClass_EveryLinkedState_CarriesTheBaseAccent(LibraryLinkIndicator indicator)
    {
        Assert.StartsWith(LibraryAccent.BASE_CLASS, LibraryAccent.CssClass(indicator));
    }

    [Theory]
    [InlineData(LibraryLinkIndicator.UpToDate, LibraryAccent.BADGE_CLASS)]
    [InlineData(LibraryLinkIndicator.Retired, LibraryAccent.BADGE_CLASS)]
    [InlineData(LibraryLinkIndicator.UpdateAvailable, null)]
    [InlineData(LibraryLinkIndicator.Missing, null)]
    public void BadgeAccent_OnlyQuietLinkedStatesCarryTheBadgeHue(LibraryLinkIndicator indicator, string? expected)
    {
        Assert.Equal(expected, LibraryAccent.BadgeAccent(indicator));
    }

    [Fact]
    public void ClassSuffix_WithoutRegistrySnapshot_StaysBlank()
    {
        Block block = NewBlock("comp-1", DomainLinkState.Linked, _headHash);

        Assert.Equal(string.Empty, LibraryAccent.ClassSuffix(linkIndex: null, block));
    }

    [Fact]
    public void ClassSuffix_PlainBlock_StaysBlank()
    {
        Assert.Equal(string.Empty, LibraryAccent.ClassSuffix(Index(), NewBlock()));
    }

    [Fact]
    public void ClassSuffix_PlainSection_StaysBlank()
    {
        Assert.Equal(string.Empty, LibraryAccent.ClassSuffix(Index(), NewSection()));
    }

    [Fact]
    public void ClassSuffix_LinkedBlock_IsSpacePrefixedForConcatenation()
    {
        Block block = NewBlock("comp-1", DomainLinkState.Linked, _headHash);

        Assert.Equal($" {LibraryAccent.BASE_CLASS}", LibraryAccent.ClassSuffix(Index(), block));
    }

    [Fact]
    public void ClassSuffix_LinkedSection_IsSpacePrefixedForConcatenation()
    {
        Section section = NewSection("comp-1", DomainLinkState.Linked, _headHash);

        Assert.Equal($" {LibraryAccent.BASE_CLASS}", LibraryAccent.ClassSuffix(Index(), section));
    }

    [Fact]
    public void ClassSuffix_StaleBlock_StillReadsAsLive()
    {
        Block block = NewBlock("comp-1", DomainLinkState.Linked, _staleHash);

        Assert.Equal($" {LibraryAccent.BASE_CLASS}", LibraryAccent.ClassSuffix(Index(), block));
    }

    [Fact]
    public void ClassSuffix_BrokenOutSection_StaysBlankLikeLocalContent()
    {
        Section section = NewSection("comp-1", DomainLinkState.BrokenOut, _staleHash);

        Assert.Equal(string.Empty, LibraryAccent.ClassSuffix(Index(), section));
    }

    [Fact]
    public void ClassSuffix_BrokenOutBlock_StaysBlankLikeLocalContent()
    {
        Block block = NewBlock("comp-1", DomainLinkState.BrokenOut, _staleHash);

        Assert.Equal(string.Empty, LibraryAccent.ClassSuffix(Index(), block));
    }

    [Fact]
    public void ClassSuffix_BlockPointingAtAVanishedComponent_CarriesTheBrokenModifier()
    {
        Block block = NewBlock("comp-gone", DomainLinkState.Linked, _headHash);

        Assert.Contains(LibraryAccent.BROKEN_CLASS, LibraryAccent.ClassSuffix(Index(), block));
    }

    [Fact]
    public void ClassSuffix_RetiredComponent_StillReadsAsLive()
    {
        Block block = NewBlock("comp-1", DomainLinkState.Linked, _headHash);

        Assert.Equal($" {LibraryAccent.BASE_CLASS}", LibraryAccent.ClassSuffix(Index(ContractsState.Retired), block));
    }
}
