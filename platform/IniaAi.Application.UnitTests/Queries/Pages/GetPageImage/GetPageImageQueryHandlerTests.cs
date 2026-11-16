using ErrorOr;
using IniaAi.Application.Queries.Pages.GetPageImage;
using IniaAi.Common.Enums;
using IniaAi.Common.Interfaces.Persistence;
using IniaAi.Domain.Entities.Documents;
using IniaAi.Domain.Entities.Documents.Enums;
using IniaAi.Domain.Enums;
using IniaAi.Domain.UserData.Interfaces;
using IniaAi.Infrastructure.Persistence.UserData.Contexts;
using IniaAi.Testing.DependencyInjection;
using IniaAi.Testing.Documents;
using IniaAi.WebApi.Contracts.Responses.Pages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace IniaAi.Application.UnitTests.Queries.Pages.GetPageImage;

public class GetPageImageQueryHandlerTests : IDisposable
{
    private readonly Microsoft.Extensions.DependencyInjection.ServiceProvider _serviceProvider;
    private readonly UserDataDbContext _context;
    private readonly GetPageImageQueryHandler _handler;
    private readonly IFileStorageManager _fileStorageManager;

    public GetPageImageQueryHandlerTests()
    {
        var services = new ServiceCollection();
        _ = services.AddTestDatabaseServices();

        _serviceProvider = services.BuildServiceProvider();
        _context = _serviceProvider.GetRequiredService<UserDataDbContext>();

        IUserDataUnitOfWork unitOfWork = _serviceProvider.GetRequiredService<IUserDataUnitOfWork>();
        _fileStorageManager = Substitute.For<IFileStorageManager>();

        ILogger<GetPageImageQueryHandler> logger = Substitute.For<ILogger<GetPageImageQueryHandler>>();

        _handler = new GetPageImageQueryHandler(logger, unitOfWork, _fileStorageManager, FileFormatsUnderTest.Pdf);
    }

    [Fact]
    public async Task Handle_WithNonExistentPageId_ShouldReturnNotFound()
    {
        // Arrange
        var query = new GetPageImageQuery("org123", "user456", "nonexistent-page-id");

        // Act
        ErrorOr<PageImageResultDto> result = await _handler.Handle(query, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.IsError);
        Assert.Equal(ErrorType.NotFound, result.FirstError.Type);
        Assert.Contains("Page not found", result.FirstError.Description);
    }

    [Fact]
    public async Task Handle_WithWrongOrganization_ShouldReturnNotFound_AsForAMissingPage()
    {
        // Arrange - Create page in org123
        (Page? page, _, _) = await CreateTestPageWithDocumentAsync("org123");

        // Act - Try to access from different org, and ask for a page that does not exist
        ErrorOr<PageImageResultDto> foreign = await _handler.Handle(new GetPageImageQuery("org999", "user456", page.Id), TestContext.Current.CancellationToken);
        ErrorOr<PageImageResultDto> missing = await _handler.Handle(new GetPageImageQuery("org999", "user456", "nonexistent-page-id"), TestContext.Current.CancellationToken);

        // Assert
        Assert.True(foreign.IsError);
        Assert.Equal(ErrorType.NotFound, foreign.FirstError.Type);
        Assert.Equal(missing.FirstError, foreign.FirstError);
    }

    [Fact]
    public async Task Handle_WithTextDocument_ShouldReturnPageText()
    {
        // Arrange
        (Page? page, _, _) = await CreateTestPageWithDocumentAsync(
            "org123",
            MediaType.ApplicationVndOpenXmlFormatsOfficedocumentWordprocessingmlDocument,
            ".docx");

        var query = new GetPageImageQuery("org123", "user456", page.Id);

        // Act
        ErrorOr<PageImageResultDto> result = await _handler.Handle(query, TestContext.Current.CancellationToken);

        // Assert
        Assert.False(result.IsError);
        Assert.Contains("text/plain", result.Value.ContentType);
        Assert.False(string.IsNullOrEmpty(result.Value.ETag));
    }

    [Fact]
    public async Task Handle_WithMissingStoragePath_ShouldReturnNotFound()
    {
        // Arrange - Create page with empty storage path
        var fileMetadata = new FileMetadata(
            "org123",
            "test.pdf",
            1000,
            StorageProvider.Database,
            null); // No storage path

        _ = await _context.FileMetadata.AddAsync(fileMetadata, TestContext.Current.CancellationToken);
        _ = await _context.SaveChangesAsync(TestContext.Current.CancellationToken);

        var document = new Document(fileMetadata, MediaType.ApplicationPdf);

        _ = await _context.Documents.AddAsync(document, TestContext.Current.CancellationToken);
        _ = await _context.SaveChangesAsync(TestContext.Current.CancellationToken);

        var page = new Page(document, 1, 1)
        {
            Location = PageLocation.FullPage,
            Text = "Test page text"
        };

        _ = await _context.Pages.AddAsync(page, TestContext.Current.CancellationToken);
        _ = await _context.SaveChangesAsync(TestContext.Current.CancellationToken);

        var query = new GetPageImageQuery("org123", "user456", page.Id);

        // Act
        ErrorOr<PageImageResultDto> result = await _handler.Handle(query, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.IsError);
        Assert.Equal(ErrorType.NotFound, result.FirstError.Type);
        Assert.Contains("storage path", result.FirstError.Description);
    }

    [Fact]
    public async Task Handle_PageOfADeletedDocument_SaysTheDocumentWasDeleted_ToItsOrganizationOnly()
    {
        (Page page, Document document, _) = await CreateTestPageWithDocumentAsync("org123");
        IUserDataUnitOfWork unitOfWork = _serviceProvider.GetRequiredService<IUserDataUnitOfWork>();
        unitOfWork.Documents.Remove(await _context.Documents.SingleAsync(d => d.Id == document.Id, TestContext.Current.CancellationToken));
        _ = await unitOfWork.CompleteAsync(TestContext.Current.CancellationToken);
        _context.ChangeTracker.Clear();

        ErrorOr<PageImageResultDto> own = await _handler.Handle(new GetPageImageQuery("org123", "user456", page.Id), TestContext.Current.CancellationToken);
        ErrorOr<PageImageResultDto> foreign = await _handler.Handle(new GetPageImageQuery("org999", "user456", page.Id), TestContext.Current.CancellationToken);
        ErrorOr<PageImageResultDto> missing = await _handler.Handle(new GetPageImageQuery("org999", "user456", "nonexistent-page-id"), TestContext.Current.CancellationToken);

        Assert.Equal(ErrorType.NotFound, own.FirstError.Type);
        Assert.Equal("Document.Deleted", own.FirstError.Code);
        Assert.Equal(missing.FirstError, foreign.FirstError);
    }

    private async Task<(Page, Document, FileMetadata)> CreateTestPageWithDocumentAsync(
        string organizationId,
        MediaType mediaType = MediaType.ApplicationPdf,
        string extension = ".pdf")
    {
        var fileMetadata = new FileMetadata(
            organizationId,
            $"test{extension}",
            1000,
            StorageProvider.Database,
            "test-storage-path");

        _ = await _context.FileMetadata.AddAsync(fileMetadata, TestContext.Current.CancellationToken);
        _ = await _context.SaveChangesAsync(TestContext.Current.CancellationToken);

        var document = new Document(fileMetadata, mediaType);

        _ = await _context.Documents.AddAsync(document, TestContext.Current.CancellationToken);
        _ = await _context.SaveChangesAsync(TestContext.Current.CancellationToken);

        var page = new Page(document, 1, 1)
        {
            Location = PageLocation.FullPage,
            Text = "Test page text"
        };

        _ = await _context.Pages.AddAsync(page, TestContext.Current.CancellationToken);
        _ = await _context.SaveChangesAsync(TestContext.Current.CancellationToken);

        return (page, document, fileMetadata);
    }

    public void Dispose()
    {
        _context?.Dispose();
        _serviceProvider?.Dispose();
    }
}
