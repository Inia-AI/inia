using ErrorOr;
using IniaAi.Application.Common.Interfaces.Services;
using IniaAi.Application.Common.Reports;
using IniaAi.Application.Queries.Files.GetFile;
using IniaAi.Common.Interfaces.Persistence;
using IniaAi.Configuration.Storage;
using IniaAi.Domain.Entities.Documents;
using IniaAi.Domain.Enums;
using IniaAi.Domain.UserData.Interfaces;
using IniaAi.Testing.DependencyInjection;
using IniaAi.Testing.Documents;
using IniaAi.WebApi.Contracts.Responses.Files;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using MediaType = IniaAi.Common.Enums.MediaType;

namespace IniaAi.Application.UnitTests.Queries.Files.GetFile;

/// <summary>
/// The download answers with the type the gate detected at intake, whatever the file name
/// claims, so a PDF uploaded under a foreign extension opens as a PDF.
/// </summary>
public sealed class GetFileQueryHandlerTests : IDisposable
{
    private const string ORGANIZATION_ID = "org-download";
    private const string USER_ID = "user-download";
    private const string STORAGE_KEY = "blob-download-1";

    private readonly Microsoft.Extensions.DependencyInjection.ServiceProvider _serviceProvider;
    private readonly IFileStorageManager _fileStorageManager = Substitute.For<IFileStorageManager>();

    public GetFileQueryHandlerTests()
    {
        var services = new ServiceCollection();
        _ = services.AddTestDatabaseServices();
        _serviceProvider = services.BuildServiceProvider();
    }

    public void Dispose()
    {
        _serviceProvider.Dispose();
    }

    [Fact]
    public async Task APdfStoredUnderAForeignExtension_DownloadsAsAPdf()
    {
        byte[] pdf = PdfFixtures.Plain("Download test content");
        var metadata = new FileMetadata(ORGANIZATION_ID, "statement.dat", pdf.Length, StorageProvider.Database, STORAGE_KEY);
        var document = new Document(metadata, MediaType.ApplicationPdf);
        IUserDataUnitOfWork unitOfWork = _serviceProvider.GetRequiredService<IUserDataUnitOfWork>();
        await unitOfWork.Documents.AddAsync(document);
        _ = await unitOfWork.CompleteAsync(TestContext.Current.CancellationToken);
        _ = _fileStorageManager.RetrieveFileAsync(Arg.Any<string>(), ORGANIZATION_ID, StorageCategories.Documents, STORAGE_KEY)
            .Returns(new StoredObjectStream(new MemoryStream(pdf), pdf.Length));
        var handler = new GetFileQueryHandler(NullLogger<GetFileQueryHandler>.Instance, unitOfWork, _fileStorageManager, new EveryReportContent());

        ErrorOr<FileResult> result = await handler.Handle(new GetFileQuery(ORGANIZATION_ID, USER_ID, document.Id), TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        Assert.Equal("application/pdf", result.Value.ContentType);
        Assert.Equal("statement.pdf", result.Value.FileName);
        Assert.Equal(pdf.Length, result.Value.Length!.Value);
        Assert.Equal(pdf, await StoredObjectStream.ReadAllAsync(result.Value.Content, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task AnotherOrganizationsDocument_AnswersNotFound_AsAMissingOneDoes_AndReadsNoBytes()
    {
        byte[] pdf = PdfFixtures.Plain("Another organization's statement");
        var metadata = new FileMetadata("org-other", "statement.pdf", pdf.Length, StorageProvider.Database, STORAGE_KEY);
        var document = new Document(metadata, MediaType.ApplicationPdf);
        IUserDataUnitOfWork unitOfWork = _serviceProvider.GetRequiredService<IUserDataUnitOfWork>();
        await unitOfWork.Documents.AddAsync(document);
        _ = await unitOfWork.CompleteAsync(TestContext.Current.CancellationToken);
        var handler = new GetFileQueryHandler(NullLogger<GetFileQueryHandler>.Instance, unitOfWork, _fileStorageManager, new EveryReportContent());

        ErrorOr<FileResult> foreign = await handler.Handle(new GetFileQuery(ORGANIZATION_ID, USER_ID, document.Id), TestContext.Current.CancellationToken);
        ErrorOr<FileResult> missing = await handler.Handle(new GetFileQuery(ORGANIZATION_ID, USER_ID, "no-such-document"), TestContext.Current.CancellationToken);

        Assert.Equal(ErrorType.NotFound, foreign.FirstError.Type);
        Assert.Equal(missing.FirstError.Description, foreign.FirstError.Description);
        _ = await _fileStorageManager.DidNotReceiveWithAnyArgs().RetrieveFileAsync(default!, default!, default!, default!);
    }
}
