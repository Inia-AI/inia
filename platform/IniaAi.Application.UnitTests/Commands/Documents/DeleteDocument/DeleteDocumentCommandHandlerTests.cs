using ErrorOr;
using IniaAi.Application.Commands.Documents.DeleteDocument;
using IniaAi.Application.Errors;
using IniaAi.Common.Enums;
using IniaAi.Common.Interfaces.Persistence;
using IniaAi.Configuration.Storage;
using IniaAi.Domain.Entities.Documents;
using IniaAi.Domain.Entities.WorkflowExecutions;
using IniaAi.Domain.UserData.Interfaces;
using IniaAi.Infrastructure.Persistence.UserData.Contexts;
using IniaAi.Testing.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace IniaAi.Application.UnitTests.Commands.Documents.DeleteDocument;

public class DeleteDocumentCommandHandlerTests : IDisposable
{
    private const string ORG_ID = "org-test";
    private const string OTHER_ORG_ID = "org-other";
    private const string USER_ID = "user-test";
    private const string READ_BY_RUNNING_REPORT = "Document.ReadByRunningReport";

    private readonly Microsoft.Extensions.DependencyInjection.ServiceProvider _serviceProvider;
    private readonly UserDataDbContext _context;
    private readonly TestFileStorageService _fileStorageService;
    private readonly DeleteDocumentCommandHandler _handler;

    public DeleteDocumentCommandHandlerTests()
    {
        var services = new ServiceCollection();
        _ = services.AddTestDatabaseServices();
        _ = services.AddTestFileStorage();

        _serviceProvider = services.BuildServiceProvider();
        _context = _serviceProvider.GetRequiredService<UserDataDbContext>();
        _fileStorageService = _serviceProvider.GetRequiredService<TestFileStorageService>();

        IUserDataUnitOfWork unitOfWork = _serviceProvider.GetRequiredService<IUserDataUnitOfWork>();
        IFileStorageManager fileStorageManager = _serviceProvider.GetRequiredService<IFileStorageManager>();

        _handler = new DeleteDocumentCommandHandler(
            unitOfWork,
            fileStorageManager,
            NullLogger<DeleteDocumentCommandHandler>.Instance);
    }

    [Fact]
    public async Task Handle_ValidId_RemovesDocumentAndBlob()
    {
        Document document = await SeedDocumentAsync(ORG_ID, "Delete Me");
        string storagePath = document.FileMetadata!.ExternalStoragePath!;
        _fileStorageService.AddTestFile(ORG_ID, StorageCategories.Documents, storagePath, [0x01, 0x02, 0x03]);

        var command = new DeleteDocumentCommand(ORG_ID, USER_ID, document.Id);

        ErrorOr<Deleted> result = await _handler.Handle(command, TestContext.Current.CancellationToken);

        Assert.False(result.IsError);

        Document? reloaded = await _context.Documents.AsNoTracking().SingleOrDefaultAsync(
            d => d.Id == document.Id, TestContext.Current.CancellationToken);
        Assert.Null(reloaded);

        Assert.Null(_fileStorageService.GetTestFile(ORG_ID, StorageCategories.Documents, storagePath));
    }

    [Fact]
    public async Task Handle_UnknownId_ReturnsNotFound()
    {
        var command = new DeleteDocumentCommand(ORG_ID, USER_ID, "missing-doc");

        ErrorOr<Deleted> result = await _handler.Handle(command, TestContext.Current.CancellationToken);

        Assert.True(result.IsError);
        Assert.Equal(ErrorType.NotFound, result.FirstError.Type);
    }

    [Fact]
    public async Task Handle_CrossOrgId_ReturnsNotFoundAndLeavesEntity()
    {
        Document document = await SeedDocumentAsync(OTHER_ORG_ID, "Other Org Doc");

        var command = new DeleteDocumentCommand(ORG_ID, USER_ID, document.Id);

        ErrorOr<Deleted> result = await _handler.Handle(command, TestContext.Current.CancellationToken);

        Assert.True(result.IsError);
        Assert.Equal(ErrorType.NotFound, result.FirstError.Type);

        Document? reloaded = await _context.Documents.AsNoTracking().SingleOrDefaultAsync(
            d => d.Id == document.Id, TestContext.Current.CancellationToken);
        Assert.NotNull(reloaded);
    }

    [Fact]
    public async Task Handle_OwnOnly_DeletesADocumentTheCallerUploaded()
    {
        Document document = await SeedDocumentAsync(ORG_ID, "Mine", uploadedBy: USER_ID);

        ErrorOr<Deleted> result = await _handler.Handle(new DeleteDocumentCommand(ORG_ID, USER_ID, document.Id, OwnOnly: true), TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        Assert.False(await _context.Documents.AnyAsync(d => d.Id == document.Id, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Handle_OwnOnly_AnotherUsersDocument_ReturnsNotFoundAndLeavesIt()
    {
        Document document = await SeedDocumentAsync(ORG_ID, "Theirs", uploadedBy: "user-other");

        ErrorOr<Deleted> result = await _handler.Handle(new DeleteDocumentCommand(ORG_ID, USER_ID, document.Id, OwnOnly: true), TestContext.Current.CancellationToken);

        Assert.Equal(ErrorType.NotFound, result.FirstError.Type);
        Assert.True(await _context.Documents.AnyAsync(d => d.Id == document.Id, TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData(WorkflowState.Undefined)]
    [InlineData(WorkflowState.Queued)]
    [InlineData(WorkflowState.ProcessingDocuments)]
    [InlineData(WorkflowState.Running)]
    public async Task Handle_DocumentAnUnfinishedReportReads_IsRefusedAndStays(WorkflowState state)
    {
        Document document = await SeedDocumentAsync(ORG_ID, "In Use");
        string storagePath = document.FileMetadata!.ExternalStoragePath!;
        _fileStorageService.AddTestFile(ORG_ID, StorageCategories.Documents, storagePath, [0x01]);
        await SeedReportReadingAsync(document, state);

        ErrorOr<Deleted> result = await _handler.Handle(new DeleteDocumentCommand(ORG_ID, USER_ID, document.Id), TestContext.Current.CancellationToken);

        Assert.Equal(ErrorType.Conflict, result.FirstError.Type);
        Assert.Equal(READ_BY_RUNNING_REPORT, result.FirstError.Code);
        Assert.Equal(DocumentErrorFactory.READ_BY_RUNNING_REPORT_MESSAGE, result.FirstError.Description);
        Assert.True(await _context.Documents.AnyAsync(d => d.Id == document.Id, TestContext.Current.CancellationToken));
        Assert.NotNull(_fileStorageService.GetTestFile(ORG_ID, StorageCategories.Documents, storagePath));
    }

    [Theory]
    [InlineData(WorkflowState.Draft)]
    [InlineData(WorkflowState.Confirmed)]
    [InlineData(WorkflowState.Failed)]
    [InlineData(WorkflowState.Cancelled)]
    public async Task Handle_DocumentOnlyFinishedReportsRead_IsDeleted(WorkflowState state)
    {
        Document document = await SeedDocumentAsync(ORG_ID, "Was Used");
        await SeedReportReadingAsync(document, state);

        ErrorOr<Deleted> result = await _handler.Handle(new DeleteDocumentCommand(ORG_ID, USER_ID, document.Id), TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        Assert.False(await _context.Documents.AnyAsync(d => d.Id == document.Id, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Handle_DocumentARunningReportTookOutOfItsInput_IsDeleted()
    {
        Document document = await SeedDocumentAsync(ORG_ID, "Dropped");
        await SeedReportReadingAsync(document, WorkflowState.Running, excluded: true);

        ErrorOr<Deleted> result = await _handler.Handle(new DeleteDocumentCommand(ORG_ID, USER_ID, document.Id), TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
    }

    [Fact]
    public async Task Handle_OwnOnly_DeletesTheCopiesMadeFromTheDocument_WithTheirFiles()
    {
        Document document = await SeedDocumentAsync(ORG_ID, "Annual report", uploadedBy: USER_ID);
        Document copy = await SeedDocumentAsync(ORG_ID, "Annual report-anon", madeFrom: document.Id);
        string copyPath = copy.FileMetadata!.ExternalStoragePath!;
        _fileStorageService.AddTestFile(ORG_ID, StorageCategories.Documents, copyPath, [0x01]);

        ErrorOr<Deleted> result = await _handler.Handle(new DeleteDocumentCommand(ORG_ID, USER_ID, document.Id, OwnOnly: true), TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        Assert.False(await _context.Documents.AnyAsync(d => d.Id == copy.Id, TestContext.Current.CancellationToken));
        Assert.False(await _context.FileMetadata.AnyAsync(f => f.Id == copy.FileMetadataId, TestContext.Current.CancellationToken));
        Assert.Null(_fileStorageService.GetTestFile(ORG_ID, StorageCategories.Documents, copyPath));
    }

    [Fact]
    public async Task Handle_ACopyAnUnfinishedReportReads_IsRefusedAndBothStay()
    {
        Document document = await SeedDocumentAsync(ORG_ID, "Annual report");
        Document copy = await SeedDocumentAsync(ORG_ID, "Annual report-anon", madeFrom: document.Id);
        await SeedReportReadingAsync(copy, WorkflowState.Running);

        ErrorOr<Deleted> result = await _handler.Handle(new DeleteDocumentCommand(ORG_ID, USER_ID, document.Id), TestContext.Current.CancellationToken);

        Assert.Equal(READ_BY_RUNNING_REPORT, result.FirstError.Code);
        Assert.True(await _context.Documents.AnyAsync(d => d.Id == document.Id, TestContext.Current.CancellationToken));
        Assert.True(await _context.Documents.AnyAsync(d => d.Id == copy.Id, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Handle_LeavesADocumentOfAnotherOrganization_ThatNamesTheDocumentAsItsOriginal()
    {
        Document document = await SeedDocumentAsync(ORG_ID, "Annual report");
        Document elsewhere = await SeedDocumentAsync(OTHER_ORG_ID, "Annual report-anon", madeFrom: document.Id);

        ErrorOr<Deleted> result = await _handler.Handle(new DeleteDocumentCommand(ORG_ID, USER_ID, document.Id), TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        Assert.True(await _context.Documents.AnyAsync(d => d.Id == elsewhere.Id, TestContext.Current.CancellationToken));
    }

    private async Task SeedReportReadingAsync(Document document, WorkflowState state, bool excluded = false)
    {
        string jobId = Guid.NewGuid().ToString();
        _ = await _context.WorkflowExecutions.AddAsync(
            new WorkflowExecution { OrganizationId = document.OrganizationId, JobId = jobId, Title = "Report", WorkflowState = state },
            TestContext.Current.CancellationToken);
        var documentInfo = new DocumentInfo(document.Id) { JobId = jobId };
        if (excluded)
        {
            documentInfo.ExcludeFromRun("NotExpectedDocument", DateTime.UtcNow);
        }

        _ = await _context.DocumentInfos.AddAsync(documentInfo, TestContext.Current.CancellationToken);
        _ = await _context.SaveChangesAsync(TestContext.Current.CancellationToken);
        _context.ChangeTracker.Clear();
    }

    private async Task<Document> SeedDocumentAsync(string organizationId, string name, string? uploadedBy = null, string? madeFrom = null)
    {
        var fileMetadata = new FileMetadata(
            organizationId,
            $"{name}.pdf",
            sizeBytes: 1024,
            storageProvider: Domain.Enums.StorageProvider.Database,
            externalStoragePath: $"{Guid.NewGuid()}.pdf");
        _ = await _context.FileMetadata.AddAsync(fileMetadata, TestContext.Current.CancellationToken);

        var document = new Document(fileMetadata, MediaType.ApplicationPdf)
        {
            OrganizationId = organizationId,
            Name = name,
            UserId = uploadedBy,
            MadeFromDocumentId = madeFrom,
        };
        _ = await _context.Documents.AddAsync(document, TestContext.Current.CancellationToken);
        _ = await _context.SaveChangesAsync(TestContext.Current.CancellationToken);
        _context.ChangeTracker.Clear();
        return document;
    }

    public void Dispose()
    {
        _context?.Dispose();
        _serviceProvider?.Dispose();
        GC.SuppressFinalize(this);
    }
}
