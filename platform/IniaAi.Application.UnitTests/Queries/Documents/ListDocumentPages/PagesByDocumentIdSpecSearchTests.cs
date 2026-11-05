using IniaAi.Application.UnitTests.Documents;
using IniaAi.Domain.Entities.Documents;
using IniaAi.Domain.Specifications.Documents;
using Microsoft.EntityFrameworkCore;

namespace IniaAi.Application.UnitTests.Queries.Documents.ListDocumentPages;

/// <summary>
/// The page search matches a page number exactly, and the page text or overview anywhere, ignoring
/// case. It runs in the database, so it reads the whole text, not the preview.
/// </summary>
public class PagesByDocumentIdSpecSearchTests : DocumentTestHost
{
    [Theory]
    [InlineData(null, new[] { 1, 2, 3 })]
    [InlineData("", new[] { 1, 2, 3 })]
    [InlineData("2", new[] { 2 })]
    [InlineData("EBITDA", new[] { 1 })]
    [InlineData("covenant", new[] { 3 })]
    [InlineData("nothing like this", new int[0])]
    public async Task TheSearch_MatchesTheNumberTheTextOrTheOverview(string? search, int[] expectedPages)
    {
        Document document = await SeedDocumentWithPagesAsync(
            ORG_ID,
            "facility.pdf",
            first => first.Text = new string('x', 500) + " ebitda margin",
            second => second.Text = "Signatures",
            third => third.Overview = "The Covenant schedule");

        List<int> pages = await _userData.Repository<Page>()
            .GetAll(new PagesByDocumentIdSpec(document.Id, search))
            .Select(p => p.PageNumber)
            .ToListAsync(TestContext.Current.CancellationToken);

        Assert.Equal(expectedPages, pages);
    }
}
