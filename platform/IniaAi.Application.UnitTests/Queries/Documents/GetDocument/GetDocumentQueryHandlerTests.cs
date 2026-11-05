using ErrorOr;
using IniaAi.Application.Queries.Documents.GetDocument;
using IniaAi.Application.UnitTests.Documents;
using IniaAi.Domain.Entities.Documents;
using IniaAi.WebApi.Contracts.Responses.Documents;
using Microsoft.Extensions.Logging.Abstractions;

namespace IniaAi.Application.UnitTests.Queries.Documents.GetDocument;

public class GetDocumentQueryHandlerTests : DocumentTestHost
{
    private GetDocumentQueryHandler Handler()
    {
        return new GetDocumentQueryHandler(_userData, NullLogger<GetDocumentQueryHandler>.Instance);
    }

    [Fact]
    public async Task Handle_WithValidId_ReturnsDetail()
    {
        Document document = await SeedDocumentAsync(ORG_ID, "My Doc.pdf");

        ErrorOr<DocumentDetailDto> result = await Handler().Handle(new GetDocumentQuery(ORG_ID, USER_ID, document.Id), TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        Assert.Equal(document.Id, result.Value.Id);
        Assert.Equal("My Doc", result.Value.Name);
    }

    [Fact]
    public async Task Handle_UnknownId_ReturnsNotFound()
    {
        ErrorOr<DocumentDetailDto> result = await Handler().Handle(new GetDocumentQuery(ORG_ID, USER_ID, "missing-document"), TestContext.Current.CancellationToken);

        Assert.True(result.IsError);
        Assert.Equal(ErrorType.NotFound, result.FirstError.Type);
    }

    [Fact]
    public async Task Handle_CrossOrgId_ReturnsNotFound()
    {
        Document document = await SeedDocumentAsync(OTHER_ORG_ID, "Other Org Doc.pdf");

        ErrorOr<DocumentDetailDto> result = await Handler().Handle(new GetDocumentQuery(ORG_ID, USER_ID, document.Id), TestContext.Current.CancellationToken);

        Assert.True(result.IsError);
        Assert.Equal(ErrorType.NotFound, result.FirstError.Type);
    }
}
