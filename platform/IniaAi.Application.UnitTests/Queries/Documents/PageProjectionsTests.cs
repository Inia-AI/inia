using IniaAi.Application.Queries.Documents;
using IniaAi.Common.Enums;
using IniaAi.Domain.Entities.Documents;
using IniaAi.Domain.Enums;
using IniaAi.WebApi.Contracts.Responses.Documents;

namespace IniaAi.Application.UnitTests.Queries.Documents;

public class PageProjectionsTests
{
    [Theory]
    [InlineData(PageStatus.OCRNeedsReview, true)]
    [InlineData(PageStatus.OCRSuccessful, false)]
    [InlineData(PageStatus.OCRFailed, false)]
    [InlineData(PageStatus.ProcessedSuccessfully, false)]
    [InlineData(PageStatus.NotProcessed, false)]
    public void ToSummary_SetsOcrNeedsReviewFromStatus(PageStatus status, bool expected)
    {
        var document = new Document(new FileMetadata(
            "IniaAi",
            "Doc.pdf",
            123,
            StorageProvider.Database,
            Guid.NewGuid().ToString()), MediaType.ApplicationPdf);
        var page = new Page(document, 1, 1) { Status = status };

        PageSummaryDto dto = PageProjections.ToSummary.Compile()(page);

        Assert.Equal(expected, dto.OcrNeedsReview);
        Assert.Equal(status.ToString(), dto.Status);
    }
}
