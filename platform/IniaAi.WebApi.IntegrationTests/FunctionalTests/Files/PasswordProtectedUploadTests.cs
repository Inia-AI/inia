using ErrorOr;
using IniaAi.Application.Commands.Files.UploadFile;
using IniaAi.Domain.Entities.Documents;
using IniaAi.Infrastructure.Persistence.UserData.Contexts;
using IniaAi.Testing.DependencyInjection;
using IniaAi.Testing.Documents;
using IniaAi.WebApi.Contracts.Dto;
using IniaAi.WebApi.IntegrationTests.Builders;
using IniaAi.WebApi.IntegrationTests.Fixtures;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace IniaAi.WebApi.IntegrationTests.FunctionalTests.Files;

/// <summary>
/// End-to-end tests for uploading password-protected files: the unlock gate inside
/// <see cref="UploadFileCommandHandler"/>, the typed error codes, and the persisted
/// provenance (Document.WasPasswordProtected + DocumentUnlockAuditLogs) against a real database.
/// </summary>
public class PasswordProtectedUploadTests : IClassFixture<ApiTestFixture>, IAsyncLifetime
{
    private const string ORG_ID = "IniaAI";
    private const string USER_ID = "test-password-upload-user";
    private const string WORKBOOK_PASSWORD = "unlock-me-42";
    private const string WRONG_PASSWORD = "not-the-password";
    private const string PASSWORD_REQUIRED_CODE = "File.Password.Required";
    private const string PASSWORD_INCORRECT_CODE = "File.Password.Incorrect";
    private const string CELL_CONTENT = "protected cell 123";
    private const string XLSX_CONTENT_TYPE = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    private readonly ClientBuilder _apiBuilder;
    private readonly List<string> _seededDocumentIds = [];

    public PasswordProtectedUploadTests(ApiTestFixture fixture)
    {
        _apiBuilder = fixture.ClientBuilder;
    }

    public ValueTask InitializeAsync()
    {
        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        if (_seededDocumentIds.Count == 0)
        {
            return;
        }

        using IServiceScope scope = _apiBuilder.CurrentScope.ServiceProvider.CreateScope();
        UserDataDbContext db = scope.ServiceProvider.GetRequiredService<UserDataDbContext>();

        List<string> fileMetadataIds = await db.Documents
            .IgnoreQueryFilters()
            .Where(d => _seededDocumentIds.Contains(d.Id))
            .Select(d => d.FileMetadataId)
            .ToListAsync(TestContext.Current.CancellationToken);

        _ = await db.DocumentUnlockAuditLogs
            .IgnoreQueryFilters()
            .Where(a => _seededDocumentIds.Contains(a.DocumentId))
            .ExecuteDeleteAsync(TestContext.Current.CancellationToken);

        _ = await db.Documents
            .IgnoreQueryFilters()
            .Where(d => _seededDocumentIds.Contains(d.Id))
            .ExecuteDeleteAsync(TestContext.Current.CancellationToken);

        _ = await db.FileMetadata
            .IgnoreQueryFilters()
            .Where(f => fileMetadataIds.Contains(f.Id))
            .ExecuteDeleteAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task EncryptedXlsx_NoPassword_ReturnsPasswordRequired()
    {
        string fileName = $"locked-{Guid.NewGuid():N}.xlsx";
        var command = new UploadFileCommand(
            FormFileTestHelper.CreateFromBytes(MakeEncryptedXlsx(), fileName, XLSX_CONTENT_TYPE),
            ORG_ID,
            USER_ID);

        ErrorOr<FileDto> result = await _apiBuilder.SendAsync(command);

        Assert.True(result.IsError);
        Assert.Equal(PASSWORD_REQUIRED_CODE, result.FirstError.Code);

        using IServiceScope scope = _apiBuilder.CurrentScope.ServiceProvider.CreateScope();
        UserDataDbContext db = scope.ServiceProvider.GetRequiredService<UserDataDbContext>();
        bool documentCreated = await db.Documents
            .IgnoreQueryFilters()
            .AnyAsync(d => d.Name == Path.GetFileNameWithoutExtension(fileName), TestContext.Current.CancellationToken);
        Assert.False(documentCreated);
    }

    [Fact]
    public async Task EncryptedXlsx_WrongPassword_ReturnsPasswordIncorrect()
    {
        string fileName = $"locked-{Guid.NewGuid():N}.xlsx";
        var command = new UploadFileCommand(
            FormFileTestHelper.CreateFromBytes(MakeEncryptedXlsx(), fileName, XLSX_CONTENT_TYPE),
            ORG_ID,
            USER_ID,
            null,
            WRONG_PASSWORD);

        ErrorOr<FileDto> result = await _apiBuilder.SendAsync(command);

        Assert.True(result.IsError);
        Assert.Equal(PASSWORD_INCORRECT_CODE, result.FirstError.Code);
    }

    [Fact]
    public async Task EncryptedXlsx_CorrectPassword_StoresDecryptedCopyWithProvenance()
    {
        string fileName = $"locked-{Guid.NewGuid():N}.xlsx";
        var command = new UploadFileCommand(
            FormFileTestHelper.CreateFromBytes(MakeEncryptedXlsx(), fileName, XLSX_CONTENT_TYPE),
            ORG_ID,
            USER_ID,
            null,
            WORKBOOK_PASSWORD);

        ErrorOr<FileDto> result = await _apiBuilder.SendAsync(command);

        Assert.False(result.IsError);
        _seededDocumentIds.Add(result.Value.Id);

        using IServiceScope scope = _apiBuilder.CurrentScope.ServiceProvider.CreateScope();
        UserDataDbContext db = scope.ServiceProvider.GetRequiredService<UserDataDbContext>();

        Document document = await db.Documents
            .IgnoreQueryFilters()
            .FirstAsync(d => d.Id == result.Value.Id, TestContext.Current.CancellationToken);
        Assert.True(document.WasPasswordProtected);

        DocumentUnlockAuditLog audit = await db.DocumentUnlockAuditLogs
            .IgnoreQueryFilters()
            .SingleAsync(a => a.DocumentId == document.Id, TestContext.Current.CancellationToken);
        Assert.Equal(ORG_ID, audit.OrganizationId);
        Assert.Equal(USER_ID, audit.ActorUserId);
        Assert.Equal(document.Name, audit.DocumentName);
        Assert.True(audit.PasswordSupplied);
        Assert.False(audit.ContainedSignature);
    }

    private static byte[] MakeEncryptedXlsx()
    {
        return OfficeEncryption.Encrypted(OoxmlFixtures.WorkbookWithCells(("A1", CELL_CONTENT)), WORKBOOK_PASSWORD);
    }
}
