using ErrorOr;
using IniaAi.Application.Commands.Documents.BulkDeleteDocuments;
using IniaAi.Application.Commands.Documents.DeleteDocument;
using IniaAi.Application.Errors;
using IniaAi.Common.Enums;
using IniaAi.Domain.Entities.Documents;
using IniaAi.Domain.Entities.UserManagement;
using IniaAi.Domain.Entities.WorkflowExecutions;
using IniaAi.Infrastructure.Persistence.UserData.Contexts;
using IniaAi.Testing.DependencyInjection;
using IniaAi.WebApi.Contracts.Responses.Documents;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ServiceProvider = Microsoft.Extensions.DependencyInjection.ServiceProvider;

namespace IniaAi.WebApi.IntegrationTests.Persistence;

/// <summary>
/// A delete of a document waits while a running report reads a copy a confidential run made of it, and then takes the
/// copy with the document, on SQL Server and on PostgreSQL alike. The handlers run their real queries against a
/// throwaway database.
/// </summary>
public sealed class DocumentCopiesOnBothProvidersTests
{
    private const string TEST_DB_PREFIX = "iniaai_doccopies_";
    private const string ORGANIZATION_ID = "org-copies";
    private const string USER_ID = "user-copies";
    private const string JOB_ID = "job-reads-the-copy";

    [Theory]
    [InlineData(BothProvidersUserDataDatabase.MSSQL)]
    [InlineData(BothProvidersUserDataDatabase.POSTGRESQL)]
    public async Task ADelete_WaitsForTheReportThatReadsTheCopy_ThenTakesTheCopy(string provider)
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        DbContextOptions<UserDataDbContext> options = await BothProvidersUserDataDatabase.OptionsOrSkipAsync(
            provider,
            TEST_DB_PREFIX,
            "deleting a document with its copies",
            cancellationToken);

        try
        {
            (Document original, Document copy) = await SeedACopyThatARunningReportReadsAsync(options, cancellationToken);
            using ServiceProvider services = BothProvidersHandlers.BuildServices(options, configure: added => added.AddTestFileStorage());

            ErrorOr<Deleted> refused = await DeleteAsync(services, original.Id);
            BulkDeleteDocumentsResultDto bulkRefused = await BothProvidersHandlers.HandleAsync<BulkDeleteDocumentsCommandHandler, BulkDeleteDocumentsResultDto>(
                services,
                handler => handler.Handle(new BulkDeleteDocumentsCommand(ORGANIZATION_ID, USER_ID, [original.Id]), cancellationToken));
            await FinishTheReportAsync(options, cancellationToken);
            ErrorOr<Deleted> deleted = await DeleteAsync(services, original.Id);

            Assert.Equal(DocumentErrorFactory.READ_BY_RUNNING_REPORT_MESSAGE, refused.FirstError.Description);
            Assert.Equal(DocumentErrorFactory.READ_BY_RUNNING_REPORT_MESSAGE, bulkRefused.Failed[original.Id]);
            Assert.False(deleted.IsError);
            await using UserDataDbContext check = new(options);
            List<string> deletedIds = await check.Documents.IgnoreQueryFilters().Where(document => document.IsDeleted).Select(document => document.Id).ToListAsync(cancellationToken);
            Assert.Equal(new[] { original.Id, copy.Id }.Order(), deletedIds.Order());
        }
        finally
        {
            await using UserDataDbContext cleanup = new(options);
            _ = await cleanup.Database.EnsureDeletedAsync(cancellationToken);
        }
    }

    private static async Task<ErrorOr<Deleted>> DeleteAsync(ServiceProvider services, string documentId)
    {
        using IServiceScope scope = services.CreateScope();
        DeleteDocumentCommandHandler handler = ActivatorUtilities.CreateInstance<DeleteDocumentCommandHandler>(scope.ServiceProvider);
        return await handler.Handle(new DeleteDocumentCommand(ORGANIZATION_ID, USER_ID, documentId), TestContext.Current.CancellationToken);
    }

    private static async Task<(Document Original, Document Copy)> SeedACopyThatARunningReportReadsAsync(
        DbContextOptions<UserDataDbContext> options,
        CancellationToken cancellationToken)
    {
        await using UserDataDbContext db = new(options);
        await db.Database.MigrateAsync(cancellationToken);

        _ = db.Organizations.Add(new Organization(ORGANIZATION_ID, ORGANIZATION_ID));
        FileMetadata originalFile = new(ORGANIZATION_ID, "report.pdf", [0x25, 0x50, 0x44, 0x46]);
        Document original = new(originalFile, MediaType.ApplicationPdf) { UserId = USER_ID };
        FileMetadata copyFile = new(ORGANIZATION_ID, "report.pdf", [0x25, 0x50, 0x44, 0x46]);
        Document copy = new(copyFile, MediaType.ApplicationPdf) { MadeFromDocumentId = original.Id };
        db.Documents.AddRange(original, copy);
        _ = db.WorkflowExecutions.Add(new WorkflowExecution { OrganizationId = ORGANIZATION_ID, JobId = JOB_ID, Title = "Report", WorkflowState = WorkflowState.Running });
        _ = db.DocumentInfos.Add(new DocumentInfo(copy.Id) { JobId = JOB_ID });
        _ = await db.SaveChangesAsync(cancellationToken);

        return (original, copy);
    }

    private static async Task FinishTheReportAsync(DbContextOptions<UserDataDbContext> options, CancellationToken cancellationToken)
    {
        await using UserDataDbContext db = new(options);
        _ = await db.WorkflowExecutions
            .Where(run => run.JobId == JOB_ID)
            .ExecuteUpdateAsync(row => row.SetProperty(run => run.WorkflowState, WorkflowState.Draft), cancellationToken);
    }
}
