using ErrorOr;
using IniaAi.Application.Queries.Documents.ListDocumentPages;
using IniaAi.Application.UnitTests.Documents;
using IniaAi.Domain.Entities.Documents;
using IniaAi.Testing.DependencyInjection;
using IniaAi.WebApi.Contracts.Responses;
using IniaAi.WebApi.Contracts.Responses.Documents;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace IniaAi.Application.UnitTests.Queries.Documents.ListDocumentPages;

/// <summary>
/// The page search arrives in the same query string as the OData options, so it must carry a name
/// OData does not claim for itself.
/// </summary>
public class PaginatedResponseDocumentPagesQueryHandlerTests : DocumentTestHost
{
    [Fact]
    public async Task Handle_SearchInTheQueryString_NarrowsThePagesBesideTheODataOptions()
    {
        Document document = await SeedDocumentWithPagesAsync(
            ORG_ID,
            "annual-report.pdf",
            first => first.Text = "Revenue grew.",
            second => second.Text = "Net income fell.");
        string queryString = $"?$top=10&$orderby=pageNumber asc&{PaginatedResponseDocumentPagesQuery.SEARCH_PARAMETER}=income";

        var handler = new PaginatedResponseDocumentPagesQueryHandler(
            _userData,
            NullLogger<PaginatedResponseDocumentPagesQueryHandler>.Instance);
        ErrorOr<ODataPaginatedResponse<PageSummaryDto>> result = await handler.Handle(
            new PaginatedResponseDocumentPagesQuery(
                ORG_ID,
                USER_ID,
                document.Id,
                ODataTestOptions.For<PageSummaryDto>(queryString),
                "income"),
            TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        Assert.Equal([2], result.Value.Value.Select(page => page.PageNumber));
    }
}
