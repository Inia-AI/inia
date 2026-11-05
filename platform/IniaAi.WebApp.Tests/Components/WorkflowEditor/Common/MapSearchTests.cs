using IniaAi.WebApp.Components.WorkflowEditor.Common;

namespace IniaAi.WebApp.Tests.Components.WorkflowEditor.Common;

public class MapSearchTests
{
    [Theory]
    [InlineData("DDQ -  Load Document", "DDQ - Load Document")]
    [InlineData("DDQ - Load Document", "DDQ -  Load Document")]
    [InlineData("Extract\tCompany  Org Chart", "Extract Company Org Chart")]
    public void Matches_WhitespaceRunsDifferFromTheTypedQuery_StillMatches(string text, string query)
    {
        Assert.True(MapSearch.Matches(text, query));
    }

    [Theory]
    [InlineData("DDQ -  Load Document", "ddq")]
    [InlineData("DDQ -  Load Document", "  load document  ")]
    [InlineData("Rate Summary", "RATE")]
    public void Matches_PartialAndDifferentlyCasedQueries_StillMatch(string text, string query)
    {
        Assert.True(MapSearch.Matches(text, query));
    }

    [Theory]
    [InlineData("DDQ - Load Document", "annual report")]
    [InlineData(null, "ddq")]
    [InlineData("", "ddq")]
    public void Matches_UnrelatedOrAbsentText_DoesNotMatch(string? text, string query)
    {
        Assert.False(MapSearch.Matches(text, query));
    }

    [Fact]
    public void Matches_BlankQuery_MatchesNothing()
    {
        Assert.False(MapSearch.Matches("DDQ - Load Document", "   "));
    }
}
