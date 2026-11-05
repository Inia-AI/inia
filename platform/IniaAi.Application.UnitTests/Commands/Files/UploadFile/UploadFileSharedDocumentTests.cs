using ErrorOr;
using IniaAi.Application.Commands.Files.UploadFile;
using IniaAi.Application.Common.UploadLimits;
using IniaAi.Application.Documents;
using IniaAi.Common.Enums;
using IniaAi.Common.Interfaces.Persistence;
using IniaAi.Configuration;
using IniaAi.Configuration.Platform;
using IniaAi.Domain.Entities.Documents;
using IniaAi.Domain.Entities.UserManagement;
using IniaAi.Domain.Enums;
using IniaAi.Domain.UserData.Interfaces;
using IniaAi.Infrastructure.Persistence.UserData.Contexts;
using IniaAi.Testing.DependencyInjection;
using IniaAi.Testing.Documents;
using IniaAi.WebApi.Contracts.Dto;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using System.Text;

namespace IniaAi.Application.UnitTests.Commands.Files.UploadFile;

/// <summary>
/// Two uploads of the same file share one document. A second upload that arrives while the
/// first stores the bytes still succeeds, and a failed upload never removes a document that
/// another upload made. SQLite and the database storage, because the primary key on stored
/// files is what a reader that finds a document before its bytes collides with.
/// </summary>
public sealed class UploadFileSharedDocumentTests : IDisposable
{
    private const string USER_ID = "user-upload";
    private const string FILE_NAME = "Fund newsletter.pdf";
    private const string UPLOAD_FAILED = "File.Upload.Failed";

    private const string OWNER_PASSWORD = "owner-pw-1";
    private const string PDF_SIGNATURE = "%PDF";

    private static readonly byte[] _newsletter = PdfFixtures.Minimal("newsletter both uploads carry");

    private readonly Microsoft.Extensions.DependencyInjection.ServiceProvider _provider;
    private readonly InterruptedFileStorageService _storage;
    private readonly Organization _organization = new("Fund administrator");

    public UploadFileSharedDocumentTests()
    {
        var services = new ServiceCollection();
        _ = services.AddSqliteTestDatabaseServices().AddInterruptedDatabaseStorage();
        _provider = services.BuildServiceProvider();
        _storage = _provider.GetRequiredService<InterruptedFileStorageService>();

        using IServiceScope scope = _provider.CreateScope();
        UserDataDbContext context = scope.ServiceProvider.GetRequiredService<UserDataDbContext>();
        _ = context.Organizations.Add(_organization);
        _ = context.SaveChanges();
    }

    public void Dispose()
    {
        _provider.Dispose();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SecondUploadArrivesWhileTheFirstStoresTheSameFile_BothUploadsKeepAStoredDocument(bool ownerPasswordProtected)
    {
        byte[] newsletter = ownerPasswordProtected
            ? PdfFixtures.Encrypted(PdfFixtures.Plain("Fund newsletter both uploads carry"), userPassword: string.Empty, OWNER_PASSWORD)
            : _newsletter;
        ErrorOr<FileDto> second = default;
        _storage.InterruptFirstUploadWith(async () => second = await UploadAsync(newsletter));

        ErrorOr<FileDto> first = await UploadAsync(newsletter);

        Assert.False(first.IsError, Describe(first));
        Assert.False(second.IsError, Describe(second));
        using IServiceScope check = _provider.CreateScope();
        UserDataDbContext context = check.ServiceProvider.GetRequiredService<UserDataDbContext>();
        List<Document> documents = await context.Documents
            .IgnoreQueryFilters()
            .Include(document => document.FileMetadata)
            .Where(document => document.Id == first.Value.Id || document.Id == second.Value.Id)
            .ToListAsync(TestContext.Current.CancellationToken);
        Assert.NotEmpty(documents);
        foreach (Document document in documents)
        {
            Assert.False(document.IsDeleted);
            Domain.Entities.Documents.File file = await context.Files.SingleAsync(
                row => row.Id == document.FileMetadata!.ExternalStoragePath, TestContext.Current.CancellationToken);
            Assert.StartsWith(PDF_SIGNATURE, Encoding.ASCII.GetString(file.Bytes, 0, PDF_SIGNATURE.Length));
        }
    }

    [Fact]
    public async Task UploadThatFailsToStoreAnotherUploadsDocument_LeavesThatDocument()
    {
        string sharedDocumentId;
        using (IServiceScope earlier = _provider.CreateScope())
        {
            IUserDataUnitOfWork unitOfWork = earlier.ServiceProvider.GetRequiredService<IUserDataUnitOfWork>();
            Document shared = await unitOfWork.Documents.UploadNewFileCreateNewDocumentAsync(
                _newsletter, _organization.Id, USER_ID, FILE_NAME, StorageProvider.Database, MediaType.ApplicationPdf);
            _ = await unitOfWork.CompleteAsync(TestContext.Current.CancellationToken);
            sharedDocumentId = shared.Id;
        }

        _storage.InterruptFirstUploadWith(() => throw new IOException("storage is unreachable"));

        ErrorOr<FileDto> result = await UploadAsync();

        Assert.True(result.IsError);
        Assert.Equal(UPLOAD_FAILED, result.FirstError.Code);
        using IServiceScope check = _provider.CreateScope();
        UserDataDbContext context = check.ServiceProvider.GetRequiredService<UserDataDbContext>();
        Document document = await context.Documents
            .IgnoreQueryFilters()
            .SingleAsync(row => row.Id == sharedDocumentId, TestContext.Current.CancellationToken);
        Assert.False(document.IsDeleted);
    }

    [Fact]
    public async Task NewUploadThatFailsToStore_ReturnsTheUploadFailureAndLeavesNoLiveDocument()
    {
        _storage.InterruptFirstUploadWith(() => throw new IOException("storage is unreachable"));

        ErrorOr<FileDto> result = await UploadAsync();

        Assert.True(result.IsError);
        Assert.Equal(UPLOAD_FAILED, result.FirstError.Code);
        Assert.Equal("The file could not be uploaded. Try again.", result.FirstError.Description);
        using IServiceScope check = _provider.CreateScope();
        UserDataDbContext context = check.ServiceProvider.GetRequiredService<UserDataDbContext>();
        Assert.False(await context.Documents
            .IgnoreQueryFilters()
            .AnyAsync(document => !document.IsDeleted, TestContext.Current.CancellationToken));
    }

    private async Task<ErrorOr<FileDto>> UploadAsync()
    {
        return await UploadAsync(_newsletter);
    }

    private async Task<ErrorOr<FileDto>> UploadAsync(byte[] content)
    {
        using IServiceScope scope = _provider.CreateScope();
        var handler = new UploadFileCommandHandler(
            scope.ServiceProvider.GetRequiredService<IUserDataUnitOfWork>(),
            NullLogger<UploadFileCommandHandler>.Instance,
            scope.ServiceProvider.GetRequiredService<IFileStorageManager>(),
            new DocumentIntakeGate(
                new LoggingIntakeRefusalRecorder(NullLogger<LoggingIntakeRefusalRecorder>.Instance),
                FileFormatsUnderTest.Unlocker,
                FileFormatsUnderTest.ReadableFormats,
                Options.Create(new PlatformSettings { IniaAi = new IniaAiSettings { MaxFileSizeMb = 1 } })),
            new NoUploadLimits());
        var file = new FormFile(new MemoryStream(content), 0, content.Length, "file", FILE_NAME)
        {
            Headers = new HeaderDictionary(),
            ContentType = "application/pdf",
        };

        return await handler.Handle(
            new UploadFileCommand(file, _organization.Id, USER_ID), TestContext.Current.CancellationToken);
    }

    private static string Describe(ErrorOr<FileDto> result)
    {
        return string.Join("; ", result.ErrorsOrEmptyList.Select(error => error.Description));
    }
}
