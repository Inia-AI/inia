using IniaAi.Application;
using IniaAi.Application.Common.Dispatching;
using IniaAi.Application.Common.Interfaces.Services;
using IniaAi.Common.Enums;
using IniaAi.Configuration.Storage;
using IniaAi.Domain.Entities.Documents;
using IniaAi.Domain.Enums;
using IniaAi.Testing.DependencyInjection;
using IniaAi.WebApi.Controllers.v1;
using IniaAi.WebApi.Services;
using IniaAi.WebApi.UnitTests.ReadPaths;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace IniaAi.WebApi.UnitTests.Controllers;

/// <summary>
/// The file endpoint's disposition: a preview is served inline and a download as an attachment
/// named after the file, both under the stored content type.
/// </summary>
public sealed class FilesControllerTests : ReadPathTests
{
    private const string ORGANIZATION_ID = "org-1";
    private const string USER_ID = "user-1";
    private const string DOCUMENT_ID = "document-1";
    private const string FILE_NAME = "statement.pdf";
    private const string CONTENT_TYPE = "application/pdf";
    private static readonly byte[] _content = [1, 2, 3];

    protected override void ConfigureServices(IServiceCollection services)
    {
        base.ConfigureServices(services);
        _ = services.AddTestFileStorage();
        _ = services.AddApplication();
    }

    [Fact]
    public async Task GetFile_ForPreview_ServesInline()
    {
        await SeedFileAsync();
        FilesController controller = CreateController();

        IActionResult result = await controller.GetFileAsync(DOCUMENT_ID, "preview");

        FileStreamResult file = Assert.IsType<FileStreamResult>(result);
        Assert.Equal(CONTENT_TYPE, file.ContentType);
        Assert.Equal(_content, await StoredObjectStream.ReadAllAsync(file.FileStream, TestContext.Current.CancellationToken));
        Assert.Empty(file.FileDownloadName);
        Assert.Equal("inline", controller.Response.Headers.ContentDisposition.ToString());
        Assert.Equal(_content.Length, controller.Response.ContentLength);
    }

    [Fact]
    public async Task GetFile_ForDownload_ServesAnAttachmentNamedAfterTheFile()
    {
        await SeedFileAsync();
        FilesController controller = CreateController();

        IActionResult result = await controller.GetFileAsync(DOCUMENT_ID);

        FileStreamResult file = Assert.IsType<FileStreamResult>(result);
        Assert.Equal(CONTENT_TYPE, file.ContentType);
        Assert.Equal(FILE_NAME, file.FileDownloadName);
        Assert.Equal(_content, await StoredObjectStream.ReadAllAsync(file.FileStream, TestContext.Current.CancellationToken));
    }

    private async Task SeedFileAsync()
    {
        string storagePath = await Service<TestFileStorageService>().UploadAsync(
            ORGANIZATION_ID, StorageCategories.Documents, FILE_NAME, new MemoryStream(_content), CONTENT_TYPE);
        var fileMetadata = new FileMetadata(ORGANIZATION_ID, FILE_NAME, _content.Length, StorageProvider.Database, storagePath)
        {
            CreatedAt = Created,
            UpdatedAt = Created,
        };
        _ = UserData.Add(new Document(fileMetadata, MediaType.ApplicationPdf)
        {
            Id = DOCUMENT_ID,
            OrganizationId = ORGANIZATION_ID,
            UserId = USER_ID,
            CreatedAt = Created,
            UpdatedAt = Created,
        });
        _ = await UserData.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    private FilesController CreateController()
    {
        RequestContext requestContext = new() { OrganizationId = ORGANIZATION_ID, UserId = USER_ID };

        return new FilesController(requestContext, NullLogger<FilesController>.Instance, Service<ISender>())
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
        };
    }
}
