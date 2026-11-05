using IniaAi.Common.Enums;
using IniaAi.Core.Workflows.Strategies;
using IniaAi.Domain.Entities.Documents;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties;
using IniaAi.Domain.Enums;

namespace IniaAi.Core.Tests.Workflows.Strategies;

public class AiQueryPageImageSelectionTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void BlockThatIncludesPageImages_GetsAnImageForEveryPage_OnEitherModel(bool useAdvancedModel)
    {
        var props = new AiQueryProperties { ShouldUsePageImages = true, ShouldUseAdvancedModel = useAdvancedModel };
        List<Page> pages = TwoPages();

        List<Page> selected = AiQueryStrategy.SelectPagesNeedingImages(props, pages);

        Assert.Equal(pages, selected);
    }

    [Fact]
    public void BlockWithoutPageImages_GetsImagesOnlyForPagesWhoseOcrWasRejected()
    {
        var props = new AiQueryProperties { ShouldUsePageImages = false, ShouldUseAdvancedModel = true };
        List<Page> pages = TwoPages();

        List<Page> selected = AiQueryStrategy.SelectPagesNeedingImages(props, pages);

        Assert.Equal([pages[1]], selected);
    }

    private static List<Page> TwoPages()
    {
        var document = new Document(new FileMetadata("org", "statement.pdf", 0, StorageProvider.Database), MediaType.ApplicationPdf);

        return
        [
            new Page(document, 1, 1) { RawText = "Page one", Status = PageStatus.OCRSuccessful },
            new Page(document, 2, 2) { RawText = "Page two", Status = PageStatus.OCRNeedsReview },
        ];
    }
}
