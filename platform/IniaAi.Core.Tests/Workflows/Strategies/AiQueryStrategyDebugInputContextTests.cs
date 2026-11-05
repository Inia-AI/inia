using IniaAi.Common.Enums;
using IniaAi.Core.Workflows.Strategies;
using IniaAi.Domain.Entities.Documents;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties;
using IniaAi.Domain.Enums;

namespace IniaAi.Core.Tests.Workflows.Strategies;

public class AiQueryStrategyDebugInputContextTests
{
    [Fact]
    public void GetDebugInputContext_ImageOnlyPage_RendersOverviewNotEmptyTag()
    {
        Document document = new(new FileMetadata("org", "scan.pdf", [0]), MediaType.ApplicationPdf);
        Page page = new(document, 1, 1)
        {
            Status = PageStatus.OCRNeedsReview,
            Overview = "page summary"
        };
        var pages = new Dictionary<int, Page> { [0] = page };
        var props = new AiQueryProperties { ShouldUseOverviews = false };

        string result = AiQueryStrategy.GetDebugInputContext(props, pages);

        Assert.Contains("page summary", result);
    }
}
