using IniaAi.Application.Common.Interfaces.Services;
using IniaAi.Application.Common.Capabilities;
using IniaAi.Common.Interfaces.Persistence;
using IniaAi.Configuration;
using IniaAi.Configuration.Messaging;
using IniaAi.Configuration.Storage;
using IniaAi.Domain.Entities.Documents;
using IniaAi.Domain.Entities.UserManagement;
using IniaAi.Domain.Enums;
using IniaAi.Domain.UserData.Interfaces;
using IniaAi.Testing.DependencyInjection;
using IniaAi.WebApp.Controllers;
using IniaAi.WebApp.Services;
using IniaAi.WebApp.Tests.Helpers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Text;
using MediaType = IniaAi.Common.Enums.MediaType;

namespace IniaAi.WebApp.Tests.Controllers;

/// <summary>
/// Integration tests for <see cref="FileDownloadController"/> using real database and file storage.
/// A caller downloads only from the organizations they belong to: a signed-in member from their own
/// organizations, a super admin from any, and the message processor's API key from the organization
/// the processor is configured for. A file of any other organization answers as if it did not exist.
/// </summary>
public class FileDownloadControllerTests : IDisposable
{
    private const string PROCESSOR_ORG_ID = "processor-org";

    private readonly IServiceProvider _serviceProvider;
    private readonly IUserDataUnitOfWork _unitOfWork;
    private readonly TestFileStorageService _fileStorageService;
    private readonly Organization _testOrganization;
    private readonly Organization _otherOrganization;
    private readonly User _member;
    private readonly FileDownloadController _controller;

    public FileDownloadControllerTests(ITestOutputHelper testOutputHelper)
    {
        var platformSettings = new PlatformSettings
        {
            CustomerOrgId = PROCESSOR_ORG_ID,
            MessageProcessor = new MessageProcessorSettings
            {
                ApiKey = "test-api-key-12345",
            }
        };

        var serviceProviderBuilder = new TestServiceProviderBuilder(platformSettings, testOutputHelper);
        _serviceProvider = serviceProviderBuilder.Build();

        _unitOfWork = _serviceProvider.GetRequiredService<IUserDataUnitOfWork>();
        _fileStorageService = _serviceProvider.GetRequiredService<TestFileStorageService>();
        _testOrganization = TestDatabaseServices.GetTestOrganization(_serviceProvider);

        _otherOrganization = new Organization("Other organization");
        _member = new User(_testOrganization.Id) { UserName = "member", Email = "member@example.test" };
        _unitOfWork.Organizations.AddAsync(_otherOrganization).GetAwaiter().GetResult();
        _unitOfWork.Users.AddAsync(_member).GetAwaiter().GetResult();
        _unitOfWork.Repository<UserOrganization>()
            .AddAsync(new UserOrganization { UserId = _member.Id, OrganizationId = _testOrganization.Id, IsDefault = true })
            .GetAwaiter().GetResult();
        _ = _unitOfWork.CompleteAsync().GetAwaiter().GetResult();

        _controller = ControllerFor(WebAppCallers.SignedInRequest(_member.Id, Roles.USER));
    }

    public void Dispose()
    {
        _serviceProvider?.GetService<IServiceScope>()?.Dispose();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void GetInfo_ReturnsJsonResult()
    {
        IActionResult result = _controller.GetInfo();

        _ = Assert.IsType<JsonResult>(result);
    }

    [Fact]
    public async Task DownloadDocument_NonExistentId_ReturnsNotFound()
    {
        IActionResult result = await _controller.DownloadDocument("non-existent-doc-id");

        _ = Assert.IsType<NotFoundObjectResult>(result);
    }

    [Fact]
    public async Task DownloadDocument_ValidDocument_ReturnsFile()
    {
        byte[] testContent = Encoding.UTF8.GetBytes("Test PDF content");
        Document document = await StoreDocumentAsync(_testOrganization.Id, "test.pdf", "test/path", testContent);

        IActionResult result = await _controller.DownloadDocument(document.Id);

        FileStreamResult file = Assert.IsType<FileStreamResult>(result);
        Assert.Equal("application/pdf", file.ContentType);
        Assert.Equal(testContent, await StoredObjectStream.ReadAllAsync(file.FileStream, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task DownloadDocument_OfAnotherOrganization_AnswersAsIfItDidNotExist()
    {
        Document document = await StoreDocumentAsync(_otherOrganization.Id, "foreign.pdf", "test/foreign", Encoding.UTF8.GetBytes("foreign"));

        IActionResult result = await _controller.DownloadDocument(document.Id);

        NotFoundObjectResult notFound = Assert.IsType<NotFoundObjectResult>(result);
        Assert.Equal("Document not found.", notFound.Value);
    }

    [Fact]
    public async Task DownloadDocument_ASuperAdmin_DownloadsFromAnyOrganization()
    {
        byte[] content = Encoding.UTF8.GetBytes("foreign");
        Document document = await StoreDocumentAsync(_otherOrganization.Id, "foreign-for-admin.pdf", "test/foreign-admin", content);

        IActionResult result = await ControllerFor(WebAppCallers.SignedInRequest(_member.Id, Roles.SUPER_ADMIN)).DownloadDocument(document.Id);

        FileStreamResult file = Assert.IsType<FileStreamResult>(result);
        Assert.Equal(content, await StoredObjectStream.ReadAllAsync(file.FileStream, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task DownloadDocument_TheApiKeyClient_DownloadsOnlyFromTheProcessorsOrganization()
    {
        await _unitOfWork.Organizations.AddAsync(new Organization("Processor", PROCESSOR_ORG_ID));
        byte[] content = Encoding.UTF8.GetBytes("processor");
        Document own = await StoreDocumentAsync(PROCESSOR_ORG_ID, "processor.pdf", "test/processor", content);
        Document foreign = await StoreDocumentAsync(_testOrganization.Id, "member-only.pdf", "test/member-only", Encoding.UTF8.GetBytes("member"));
        FileDownloadController apiKeyClient = ControllerFor(WebAppCallers.ApiKeyRequest(_serviceProvider.GetRequiredService<IOptions<PlatformSettings>>().Value));

        FileStreamResult file = Assert.IsType<FileStreamResult>(await apiKeyClient.DownloadDocument(own.Id));
        Assert.Equal(content, await StoredObjectStream.ReadAllAsync(file.FileStream, TestContext.Current.CancellationToken));
        _ = Assert.IsType<NotFoundObjectResult>(await apiKeyClient.DownloadDocument(foreign.Id));
    }

    [Fact]
    public async Task DownloadFileByName_EmptyFileName_ReturnsBadRequest()
    {
        IActionResult result = await _controller.DownloadFileByName("");

        _ = Assert.IsType<BadRequestObjectResult>(result);
    }

    [Theory]
    [InlineData("folder/statement.pdf")]
    [InlineData("..\\statement.pdf")]
    [InlineData("../statement.pdf")]
    [InlineData("..%2fstatement.pdf")]
    [InlineData("..pdf")]
    public async Task DownloadFileByName_ANameThatIsAPath_IsRefusedOrFindsNothing(string fileName)
    {
        IActionResult result = await _controller.DownloadFileByName(fileName);

        Assert.True(result is BadRequestObjectResult or NotFoundObjectResult, $"The download answered {result.GetType().Name}.");
    }

    [Fact]
    public async Task DownloadFileByName_NonExistentFile_ReturnsNotFound()
    {
        IActionResult result = await _controller.DownloadFileByName("nonexistent.pdf");

        _ = Assert.IsType<NotFoundObjectResult>(result);
    }

    [Fact]
    public async Task DownloadFileByName_ValidFile_ReturnsFile()
    {
        byte[] testContent = Encoding.UTF8.GetBytes("Test file content");
        await StoreFileAsync(_testOrganization.Id, "valid-file.pdf", "test/path", testContent);

        IActionResult result = await _controller.DownloadFileByName("valid-file.pdf");

        FileStreamResult file = Assert.IsType<FileStreamResult>(result);
        Assert.Equal("application/pdf", file.ContentType);
        Assert.Equal(testContent, await StoredObjectStream.ReadAllAsync(file.FileStream, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task DownloadFileByName_DeletedFile_ReturnsNotFound()
    {
        byte[] testContent = Encoding.UTF8.GetBytes("Test file content");
        var fileMetadata = new FileMetadata(_testOrganization.Id, "deleted-file.pdf", testContent.Length, StorageProvider.Database, "test/path")
        {
            IsDeleted = true
        };
        await _unitOfWork.FileMetadata.AddAsync(fileMetadata);
        _ = await _unitOfWork.CompleteAsync(TestContext.Current.CancellationToken);
        _fileStorageService.AddTestFile(_testOrganization.Id, StorageCategories.Documents, "test/path", testContent);

        IActionResult result = await _controller.DownloadFileByName("deleted-file.pdf");

        NotFoundObjectResult notFound = Assert.IsType<NotFoundObjectResult>(result);
        Assert.Equal("File not found.", notFound.Value);
    }

    [Fact]
    public async Task DownloadFileByName_OfAnotherOrganization_AnswersAsIfItDidNotExist()
    {
        await StoreFileAsync(_otherOrganization.Id, "statement.pdf", "test/other-statement", Encoding.UTF8.GetBytes("foreign"));

        IActionResult result = await _controller.DownloadFileByName("statement.pdf");

        NotFoundObjectResult notFound = Assert.IsType<NotFoundObjectResult>(result);
        Assert.Equal("File not found.", notFound.Value);
    }

    [Fact]
    public async Task DownloadFileByName_WhenAnotherOrganizationHasANewerFileOfTheName_ServesTheCallersOwn()
    {
        byte[] own = Encoding.UTF8.GetBytes("own content");
        await StoreFileAsync(_testOrganization.Id, "report.pdf", "test/own-report", own, DateTime.UtcNow.AddDays(-1));
        await StoreFileAsync(_otherOrganization.Id, "report.pdf", "test/other-report", Encoding.UTF8.GetBytes("foreign"));

        IActionResult result = await _controller.DownloadFileByName("report.pdf");

        FileStreamResult file = Assert.IsType<FileStreamResult>(result);
        Assert.Equal(own, await StoredObjectStream.ReadAllAsync(file.FileStream, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task DownloadFileByName_WhenTwoFilesShareTheName_ServesTheNewest()
    {
        byte[] older = Encoding.UTF8.GetBytes("older content");
        byte[] newer = Encoding.UTF8.GetBytes("newer content");
        await StoreFileAsync(_testOrganization.Id, "shared-name.pdf", "test/shared-older", older, DateTime.UtcNow.AddDays(-1));
        await StoreFileAsync(_testOrganization.Id, "shared-name.pdf", "test/shared-newer", newer);

        IActionResult result = await _controller.DownloadFileByName("shared-name.pdf");

        FileStreamResult file = Assert.IsType<FileStreamResult>(result);
        Assert.Equal(newer, await StoredObjectStream.ReadAllAsync(file.FileStream, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task DownloadDocument_APdfStoredUnderAForeignExtension_AnswersAsAPdf()
    {
        Document document = await StoreDocumentAsync(
            _testOrganization.Id, "statement-by-id.dat", "test/statement-by-id", Encoding.UTF8.GetBytes("%PDF-1.7 test content"));

        IActionResult result = await _controller.DownloadDocument(document.Id);

        FileStreamResult fileResult = Assert.IsType<FileStreamResult>(result);
        Assert.Equal("application/pdf", fileResult.ContentType);
        Assert.Equal("statement-by-id.pdf", fileResult.FileDownloadName);
    }

    [Fact]
    public async Task DownloadDocument_ANameWithADotInIt_KeepsEveryPartOfIt()
    {
        Document document = await StoreDocumentAsync(
            _testOrganization.Id, "Q3 results v1.2.pdf", "test/q3-results", Encoding.UTF8.GetBytes("%PDF-1.7 test content"));

        IActionResult result = await _controller.DownloadDocument(document.Id);

        FileStreamResult fileResult = Assert.IsType<FileStreamResult>(result);
        Assert.Equal("Q3 results v1.2.pdf", fileResult.FileDownloadName);
    }

    [Fact]
    public async Task DownloadFileByName_APdfStoredUnderAForeignExtension_AnswersAsAPdf()
    {
        _ = await StoreDocumentAsync(
            _testOrganization.Id, "statement-by-name.dat", "test/statement-by-name", Encoding.UTF8.GetBytes("%PDF-1.7 test content"));

        IActionResult result = await _controller.DownloadFileByName("statement-by-name.dat");

        FileStreamResult fileResult = Assert.IsType<FileStreamResult>(result);
        Assert.Equal("application/pdf", fileResult.ContentType);
        Assert.Equal("statement-by-name.dat", fileResult.FileDownloadName);
    }

    private FileDownloadController ControllerFor(ControllerContext request)
    {
        IOptions<PlatformSettings> platformSettings = _serviceProvider.GetRequiredService<IOptions<PlatformSettings>>();

        return new FileDownloadController(
            _unitOfWork,
            _serviceProvider.GetRequiredService<ILogger<FileDownloadController>>(),
            platformSettings,
            _serviceProvider.GetRequiredService<IFileStorageManager>(),
            new CallerOrganizations(_unitOfWork, platformSettings, new NoOrganizationsClosedToStaff()))
        {
            ControllerContext = request,
        };
    }

    private async Task<FileMetadata> StoreFileAsync(string organizationId, string name, string path, byte[] content, DateTime? createdAt = null)
    {
        var fileMetadata = new FileMetadata(organizationId, name, content.Length, StorageProvider.Database, path);

        if (createdAt is DateTime created)
        {
            fileMetadata.CreatedAt = created;
        }

        await _unitOfWork.FileMetadata.AddAsync(fileMetadata);
        _ = await _unitOfWork.CompleteAsync(TestContext.Current.CancellationToken);
        _fileStorageService.AddTestFile(organizationId, StorageCategories.Documents, path, content);

        return fileMetadata;
    }

    private async Task<Document> StoreDocumentAsync(string organizationId, string name, string path, byte[] content)
    {
        FileMetadata fileMetadata = await StoreFileAsync(organizationId, name, path, content);
        var document = new Document(fileMetadata, MediaType.ApplicationPdf);
        await _unitOfWork.Documents.AddAsync(document);
        _ = await _unitOfWork.CompleteAsync(TestContext.Current.CancellationToken);

        return document;
    }
}
