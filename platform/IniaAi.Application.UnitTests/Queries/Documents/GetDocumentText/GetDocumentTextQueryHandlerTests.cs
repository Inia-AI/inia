using ErrorOr;
using IniaAi.Application.Queries.Documents.GetDocumentText;
using IniaAi.Application.UnitTests.Documents;
using IniaAi.Domain.Entities.Documents;
using Microsoft.Extensions.Logging.Abstractions;

namespace IniaAi.Application.UnitTests.Queries.Documents.GetDocumentText;

/// <summary>
/// The text of a document is every page's text in page order, each page wrapped in a numbered tag,
/// named after the document with a .txt extension.
/// </summary>
public class GetDocumentTextQueryHandlerTests : DocumentTestHost
{
    private GetDocumentTextQueryHandler Handler()
    {
        return new GetDocumentTextQueryHandler(_userData, NullLogger<GetDocumentTextQueryHandler>.Instance);
    }

    [Fact]
    public async Task Handle_WrapsEachPageInItsNumberedTag_InPageOrder()
    {
        Document document = await SeedDocumentWithPagesAsync(
            ORG_ID,
            "annual-report.pdf",
            first => first.Text = "Revenue grew.",
            second => second.Text = null,
            third => third.Text = "Net income fell.");

        ErrorOr<DocumentText> result = await Handler().Handle(new GetDocumentTextQuery(ORG_ID, USER_ID, document.Id), TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        Assert.Equal("annual-report.txt", result.Value.FileName);
        Assert.Equal(
            "<page1>\nRevenue grew.\n</page1>\n\n<page2>\n\n</page2>\n\n<page3>\nNet income fell.\n</page3>\n\n",
            result.Value.Text.ReplaceLineEndings("\n"));
    }

    [Fact]
    public async Task Handle_KeepsEveryDotInTheDocumentsName()
    {
        Document document = await SeedDocumentWithPagesAsync(ORG_ID, "Acme Inc. 10-K.pdf", page => page.Text = "Revenue grew.");

        ErrorOr<DocumentText> result = await Handler().Handle(new GetDocumentTextQuery(ORG_ID, USER_ID, document.Id), TestContext.Current.CancellationToken);

        Assert.Equal("Acme Inc. 10-K.txt", result.Value.FileName);
    }

    [Fact]
    public async Task Handle_ForeignDocument_AnswersAsAnUnknownOne()
    {
        Document foreign = await SeedDocumentWithPagesAsync(OTHER_ORG_ID, "foreign.pdf", page => page.Text = "secret");

        ErrorOr<DocumentText> unknown = await Handler().Handle(new GetDocumentTextQuery(ORG_ID, USER_ID, "missing"), TestContext.Current.CancellationToken);
        ErrorOr<DocumentText> foreignResult = await Handler().Handle(new GetDocumentTextQuery(ORG_ID, USER_ID, foreign.Id), TestContext.Current.CancellationToken);

        Assert.Equal(ErrorType.NotFound, unknown.FirstError.Type);
        Assert.Equal(ErrorType.NotFound, foreignResult.FirstError.Type);
    }
}
