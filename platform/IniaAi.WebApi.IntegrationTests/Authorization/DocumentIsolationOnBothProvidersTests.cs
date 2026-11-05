using ErrorOr;
using IniaAi.Application.Commands.Documents.BulkDeleteDocuments;
using IniaAi.Application.Commands.Documents.DeleteDocument;
using IniaAi.Application.Commands.Documents.ProcessDocument;
using IniaAi.Application.Commands.Documents.UpdateDocument;
using IniaAi.Application.Common.Dispatching;
using IniaAi.Application.Common.Interfaces.Services;
using IniaAi.Application.Queries.Documents.GetDocument;
using IniaAi.Application.Queries.Documents.GetDocumentPage;
using IniaAi.Application.Queries.Documents.GetDocumentText;
using IniaAi.Application.Queries.Documents.ListDocumentPages;
using IniaAi.Application.Queries.Files.GetFile;
using IniaAi.Application.Queries.Pages.GetPageImage;
using IniaAi.Common.Enums;
using IniaAi.Domain.Entities.Documents;
using IniaAi.Domain.Entities.Documents.Enums;
using IniaAi.Domain.Entities.UserManagement;
using IniaAi.Domain.Enums;
using IniaAi.Infrastructure.Persistence.UserData.Contexts;
using IniaAi.Services.Documents;
using IniaAi.Testing.DependencyInjection;
using IniaAi.Testing.Documents;
using IniaAi.WebApi.Contracts.Responses;
using IniaAi.WebApi.Contracts.Responses.Documents;
using IniaAi.WebApi.Contracts.Responses.Files;
using IniaAi.WebApi.Contracts.Responses.Pages;
using IniaAi.WebApi.IntegrationTests.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace IniaAi.WebApi.IntegrationTests.Authorization;

/// <summary>
/// Every document read and write answers another organization's document exactly as an id that
/// does not exist, on SQL Server and on PostgreSQL alike, so a caller cannot tell a real foreign id
/// from a made-up one. Each handler runs its real queries against a throwaway database.
/// </summary>
public sealed class DocumentIsolationOnBothProvidersTests
{
    private const string TEST_DB_PREFIX = "iniaai_docisolation_";
    private const string OWNER_ORGANIZATION_ID = "org-owner";
    private const string CALLER_ORGANIZATION_ID = "org-caller";
    private const string CALLER_ID = "user-caller";
    private const string UNKNOWN_ID = "no-such-id";

    [Theory]
    [InlineData(BothProvidersUserDataDatabase.MSSQL)]
    [InlineData(BothProvidersUserDataDatabase.POSTGRESQL)]
    public async Task AnotherOrganizationsDocument_AnswersAsAnUnknownId_OnEveryDocumentEndpoint(string provider)
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        DbContextOptions<UserDataDbContext> options = await BothProvidersUserDataDatabase.OptionsOrSkipAsync(
            provider,
            TEST_DB_PREFIX,
            "the document isolation guard",
            cancellationToken);

        try
        {
            (Document document, Page page) = await SeedForeignDocumentAsync(options, cancellationToken);
            using Microsoft.Extensions.DependencyInjection.ServiceProvider services = BuildServices(options);

            await AssertAnswersAsUnknownAsync<GetDocumentQueryHandler, GetDocumentQuery, DocumentDetailDto>(
                services, id => new GetDocumentQuery(CALLER_ORGANIZATION_ID, CALLER_ID, id), document.Id);
            await AssertAnswersAsUnknownAsync<UpdateDocumentCommandHandler, UpdateDocumentCommand, DocumentDetailDto>(
                services, id => new UpdateDocumentCommand(CALLER_ORGANIZATION_ID, CALLER_ID, id, "Renamed", null, null, null, null, null, null), document.Id);
            await AssertAnswersAsUnknownAsync<DeleteDocumentCommandHandler, DeleteDocumentCommand, Deleted>(
                services, id => new DeleteDocumentCommand(CALLER_ORGANIZATION_ID, CALLER_ID, id), document.Id);
            await AssertAnswersAsUnknownAsync<ProcessDocumentCommandHandler, ProcessDocumentCommand, DocumentSummaryDto>(
                services, id => new ProcessDocumentCommand(CALLER_ORGANIZATION_ID, CALLER_ID, id), document.Id);
            await AssertAnswersAsUnknownAsync<GetDocumentPageQueryHandler, GetDocumentPageQuery, PageDetailDto>(
                services, id => new GetDocumentPageQuery(CALLER_ORGANIZATION_ID, CALLER_ID, id, PageNumber: 1), document.Id);
            await AssertAnswersAsUnknownAsync<GetDocumentTextQueryHandler, GetDocumentTextQuery, DocumentText>(
                services, id => new GetDocumentTextQuery(CALLER_ORGANIZATION_ID, CALLER_ID, id), document.Id);
            await AssertAnswersAsUnknownAsync<PaginatedResponseDocumentPagesQueryHandler, PaginatedResponseDocumentPagesQuery, ODataPaginatedResponse<PageSummaryDto>>(
                services, id => new PaginatedResponseDocumentPagesQuery(CALLER_ORGANIZATION_ID, CALLER_ID, id, ODataTestOptions.For<PageSummaryDto>("?$top=10")), document.Id);
            await AssertAnswersAsUnknownAsync<GetFileQueryHandler, GetFileQuery, FileResult>(
                services, id => new GetFileQuery(CALLER_ORGANIZATION_ID, CALLER_ID, id), document.FileMetadataId);
            await AssertAnswersAsUnknownAsync<GetPageImageQueryHandler, GetPageImageQuery, PageImageResultDto>(
                services, id => new GetPageImageQuery(CALLER_ORGANIZATION_ID, CALLER_ID, id), page.Id);

            BulkDeleteDocumentsResultDto bulk = (await HandleAsync<BulkDeleteDocumentsCommandHandler, BulkDeleteDocumentsCommand, BulkDeleteDocumentsResultDto>(
                services, new BulkDeleteDocumentsCommand(CALLER_ORGANIZATION_ID, CALLER_ID, [document.Id, UNKNOWN_ID]))).Value;
            Assert.Empty(bulk.Deleted);
            Assert.Equal(bulk.Failed[UNKNOWN_ID], bulk.Failed[document.Id]);

            await using UserDataDbContext check = new(options);
            Document kept = await check.Documents.AsNoTracking().SingleAsync(cancellationToken);
            Assert.NotEqual("Renamed", kept.Name);
        }
        finally
        {
            await using UserDataDbContext cleanup = new(options);
            _ = await cleanup.Database.EnsureDeletedAsync(cancellationToken);
        }
    }

    private static async Task AssertAnswersAsUnknownAsync<THandler, TRequest, TResult>(
        IServiceProvider services,
        Func<string, TRequest> request,
        string foreignId)
        where THandler : IRequestHandler<TRequest, ErrorOr<TResult>>
        where TRequest : IRequest<ErrorOr<TResult>>
    {
        ErrorOr<TResult> foreign = await HandleAsync<THandler, TRequest, TResult>(services, request(foreignId));
        ErrorOr<TResult> unknown = await HandleAsync<THandler, TRequest, TResult>(services, request(UNKNOWN_ID));

        Assert.True(foreign.IsError, $"{typeof(THandler).Name} served another organization's row.");
        Assert.Equal(ErrorType.NotFound, foreign.FirstError.Type);
        Assert.Equal(unknown.FirstError.Description, foreign.FirstError.Description);
    }

    private static async Task<ErrorOr<TResult>> HandleAsync<THandler, TRequest, TResult>(IServiceProvider services, TRequest request)
        where THandler : IRequestHandler<TRequest, ErrorOr<TResult>>
        where TRequest : IRequest<ErrorOr<TResult>>
    {
        using IServiceScope scope = services.CreateScope();
        THandler handler = ActivatorUtilities.CreateInstance<THandler>(scope.ServiceProvider);

        return await handler.Handle(request, TestContext.Current.CancellationToken);
    }

    private static Microsoft.Extensions.DependencyInjection.ServiceProvider BuildServices(DbContextOptions<UserDataDbContext> options)
    {
        ServiceCollection services = new();
        _ = services.AddLogging();
        _ = services.AddScoped(_ => new UserDataDbContext(options));
        _ = services.AddTestDatabaseServicesWithoutDbContext();
        _ = services.AddTestFileStorage();
        _ = services.AddSingleton<IWorkQueue<DocumentProcessingRequest>, DocumentProcessingQueue>();
        _ = services.AddSingleton(FileFormatsUnderTest.Pdf);

        return services.BuildServiceProvider();
    }

    private static async Task<(Document Document, Page Page)> SeedForeignDocumentAsync(
        DbContextOptions<UserDataDbContext> options,
        CancellationToken cancellationToken)
    {
        await using UserDataDbContext db = new(options);
        await db.Database.MigrateAsync(cancellationToken);

        _ = db.Organizations.Add(new Organization(OWNER_ORGANIZATION_ID, OWNER_ORGANIZATION_ID));
        _ = db.Organizations.Add(new Organization(CALLER_ORGANIZATION_ID, CALLER_ORGANIZATION_ID));
        FileMetadata file = new(OWNER_ORGANIZATION_ID, "owner-report.pdf", [0x25, 0x50, 0x44, 0x46]);
        Document document = new(file, MediaType.ApplicationPdf) { Name = "Owner report" };
        Page page = new(document, 1, 1) { Location = PageLocation.FullPage, Text = "Owner text" };
        _ = db.Documents.Add(document);
        _ = db.Pages.Add(page);
        _ = await db.SaveChangesAsync(cancellationToken);

        return (document, page);
    }
}
