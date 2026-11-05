using IniaAi.Application.Workflows.EditorAgent;
using IniaAi.Domain.Proposals;
using Newtonsoft.Json;

namespace IniaAi.Application.UnitTests.Workflows.EditorAgent;

public class EditorAgentSummaryTests
{
    [Fact]
    public void Read_ReadsEntries_AndRejectsAnEntryWithoutWhat()
    {
        List<ProposalSummaryEntry> entries = EditorAgentSummary.Read(
            """{ "changes": [ { "stableBlockId": "abc", "what": "Asks for regions too." }, { "section": "Report", "what": "Adds a section." } ] }""").Changes;

        Assert.Equal(2, entries.Count);
        Assert.Equal("abc", entries[0].StableBlockId);
        Assert.Equal("Report", entries[1].Section);
        _ = Assert.ThrowsAny<JsonException>(() => EditorAgentSummary.Read("""{ "changes": [ { "stableBlockId": "abc" } ] }"""));
    }

    [Fact]
    public void Read_ReadsTheFeedbackItemsAnEntryFixes_AndRejectsFeedbackThatIsNotAList()
    {
        List<ProposalSummaryEntry> entries = EditorAgentSummary.Read(
            """{ "changes": [ { "stableBlockId": "abc", "what": "Reads totals in millions.", "feedback": ["f-1", "f-2", "f-1"] }, { "section": "Report", "what": "Adds a section." } ] }""").Changes;

        Assert.Equal(["f-1", "f-2"], entries[0].FeedbackIds);
        Assert.Null(entries[1].FeedbackIds);
        _ = Assert.ThrowsAny<JsonException>(() => EditorAgentSummary.Read(
            """{ "changes": [ { "stableBlockId": "abc", "what": "Reads totals in millions.", "feedback": "f-1" } ] }"""));
    }

    [Fact]
    public void Read_ReadsTheOverview_AndLeavesItNullWhenBlank()
    {
        EditorAgentSummaryFile file = EditorAgentSummary.Read(
            """{ "overview": " Adds a liquidity section. ", "changes": [ { "section": "Report", "what": "Adds a section." } ] }""");

        Assert.Equal("Adds a liquidity section.", file.Overview);
        Assert.Null(EditorAgentSummary.Read("""{ "overview": " ", "changes": [] }""").Overview);
    }
}
