using ErrorOr;
using IniaAi.Application.Commands.Documents.UpdateDocument;
using IniaAi.Application.UnitTests.Documents;
using IniaAi.Domain.Entities.Documents;
using IniaAi.WebApi.Contracts.Responses.Documents;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace IniaAi.Application.UnitTests.Commands.Documents.UpdateDocument;

public class UpdateDocumentCommandHandlerTests : DocumentTestHost
{
    private const string WORKFLOW_TYPE_ID = "AssetManagerCreditAnalysis";
    private const string STATIC_CLASS = "Regulatory_Fmk_Updated";

    private UpdateDocumentCommandHandler Handler()
    {
        return new UpdateDocumentCommandHandler(_userData, NullLogger<UpdateDocumentCommandHandler>.Instance);
    }

    [Fact]
    public async Task Handle_WithValidId_UpdatesProvidedFields()
    {
        Document document = await SeedDocumentAsync(ORG_ID, "Original Name.pdf");

        var command = new UpdateDocumentCommand(
            OrganizationId: ORG_ID,
            UserId: USER_ID,
            DocumentId: document.Id,
            Name: "Updated Name",
            Description: "Updated description",
            DocumentCategoryId: null,
            WorkflowTypeId: null,
            Class: "Contract",
            Topics: null,
            TableOfContents: null);

        ErrorOr<DocumentDetailDto> result = await Handler().Handle(command, TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        Assert.Equal("Updated Name", result.Value.Name);
        Assert.Equal("Updated description", result.Value.Description);
        Assert.Equal("Contract", result.Value.Class);

        Document? reloaded = await _context.Documents.AsNoTracking().SingleOrDefaultAsync(
            d => d.Id == document.Id, TestContext.Current.CancellationToken);
        Assert.NotNull(reloaded);
        Assert.Equal("Updated Name", reloaded!.Name);
        Assert.Equal(USER_ID, reloaded.UpdatedByUserId);
    }

    [Fact]
    public async Task Handle_WithAllNullFields_LeavesDocumentUnchanged()
    {
        Document document = await SeedDocumentAsync(ORG_ID, "Unchanged.pdf");

        var command = new UpdateDocumentCommand(
            OrganizationId: ORG_ID,
            UserId: USER_ID,
            DocumentId: document.Id,
            Name: null,
            Description: null,
            DocumentCategoryId: null,
            WorkflowTypeId: null,
            Class: null,
            Topics: null,
            TableOfContents: null);

        ErrorOr<DocumentDetailDto> result = await Handler().Handle(command, TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        Assert.Equal("Unchanged", result.Value.Name);
    }

    [Fact]
    public async Task Handle_MarkingADocumentStatic_SetsItsWorkflowTypeAndClass()
    {
        Document document = await SeedDocumentAsync(ORG_ID, "Regulatory framework.pdf");

        ErrorOr<DocumentDetailDto> result = await Handler().Handle(
            StaticCommand(document.Id, isStatic: true, WORKFLOW_TYPE_ID, STATIC_CLASS),
            TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        Document reloaded = await _context.Documents.AsNoTracking().SingleAsync(d => d.Id == document.Id, TestContext.Current.CancellationToken);
        Assert.Equal(WORKFLOW_TYPE_ID, reloaded.WorkflowTypeId);
        Assert.Equal(STATIC_CLASS, reloaded.Class);
    }

    [Fact]
    public async Task Handle_UnmarkingAStaticDocument_ClearsItsWorkflowTypeAndClass()
    {
        Document document = await SeedDocumentAsync(ORG_ID, "Regulatory framework.pdf");
        _ = await Handler().Handle(StaticCommand(document.Id, isStatic: true, WORKFLOW_TYPE_ID, STATIC_CLASS), TestContext.Current.CancellationToken);
        _context.ChangeTracker.Clear();

        ErrorOr<DocumentDetailDto> result = await Handler().Handle(
            StaticCommand(document.Id, isStatic: false, workflowTypeId: null, documentClass: null),
            TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        Assert.Null(result.Value.WorkflowTypeId);
        Assert.Null(result.Value.Class);
        Document reloaded = await _context.Documents.AsNoTracking().SingleAsync(d => d.Id == document.Id, TestContext.Current.CancellationToken);
        Assert.Null(reloaded.WorkflowTypeId);
        Assert.Null(reloaded.Class);
    }

    [Fact]
    public async Task Handle_ANameWithALineBreak_IsStoredOnOneLine()
    {
        Document document = await SeedDocumentAsync(ORG_ID, "Original Name.pdf");
        var command = new UpdateDocumentCommand(
            ORG_ID, USER_ID, document.Id, "First line\nSecond line", null, null, null, null, null, null);

        ErrorOr<DocumentDetailDto> result = await Handler().Handle(command, TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        Document reloaded = await _context.Documents.AsNoTracking().SingleAsync(
            d => d.Id == document.Id, TestContext.Current.CancellationToken);
        Assert.Equal("First line Second line", reloaded.Name);
    }

    [Fact]
    public async Task Handle_UnknownId_ReturnsNotFound()
    {
        var command = new UpdateDocumentCommand(
            ORG_ID, USER_ID, "missing-doc", "anything", null, null, null, null, null, null);

        ErrorOr<DocumentDetailDto> result = await Handler().Handle(command, TestContext.Current.CancellationToken);

        Assert.True(result.IsError);
        Assert.Equal(ErrorType.NotFound, result.FirstError.Type);
    }

    [Fact]
    public async Task Handle_CrossOrgId_ReturnsNotFound()
    {
        Document document = await SeedDocumentAsync(OTHER_ORG_ID, "Other Org Doc.pdf");

        var command = new UpdateDocumentCommand(
            ORG_ID, USER_ID, document.Id, "Hack", null, null, null, null, null, null);

        ErrorOr<DocumentDetailDto> result = await Handler().Handle(command, TestContext.Current.CancellationToken);

        Assert.True(result.IsError);
        Assert.Equal(ErrorType.NotFound, result.FirstError.Type);
    }

    private static UpdateDocumentCommand StaticCommand(string documentId, bool isStatic, string? workflowTypeId, string? documentClass)
    {
        return new UpdateDocumentCommand(
            OrganizationId: ORG_ID,
            UserId: USER_ID,
            DocumentId: documentId,
            Name: null,
            Description: null,
            DocumentCategoryId: null,
            WorkflowTypeId: workflowTypeId,
            Class: documentClass,
            Topics: null,
            TableOfContents: null,
            IsStatic: isStatic);
    }
}
