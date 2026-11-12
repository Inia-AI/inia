using System.Net;
using System.Reflection;
using IniaAi.Configuration;
using IniaAi.Configuration.Platform;
using IniaAi.WebApi.Attributes;
using IniaAi.WebApi.Configuration;
using IniaAi.WebApi.Controllers.v1;
using IniaAi.WebApi.UnitTests.Common;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace IniaAi.WebApi.UnitTests.Controllers;

/// <summary>
/// An upload's request body is bounded by the deployment's file size limit before the form is read: a body over the
/// limit is refused while it streams in, and the server reads little more than the limit of it.
/// </summary>
public sealed class FileUploadSizeLimitTests
{
    private const int MAX_FILE_SIZE_MB = 1;
    private const long ONE_MEGABYTE = 1024 * 1024;

    [Fact]
    public async Task AFileUnderTheLimit_IsTaken()
    {
        await using WebApplication app = await StartAsync();
        var body = new CountingContent(ONE_MEGABYTE / 2);

        using HttpResponseMessage response = await PostAsync(app, body);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal((ONE_MEGABYTE / 2).ToString(), await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ABodyThreeTimesTheLimit_IsRefused_BeforeTheServerReadsMuchPastTheLimit()
    {
        await using WebApplication app = await StartAsync();
        var body = new CountingContent(3 * ONE_MEGABYTE);

        using HttpResponseMessage response = await PostAsync(app, body);

        Assert.Contains(response.StatusCode, new[] { HttpStatusCode.BadRequest, HttpStatusCode.RequestEntityTooLarge });
        Assert.True(body.BytesRead < (MAX_FILE_SIZE_MB * ONE_MEGABYTE) + (ONE_MEGABYTE / 2), $"The server read {body.BytesRead} bytes of the body.");
    }

    [Fact]
    public void TheUploadEndpoint_IsBoundedByTheFileSizeLimit_NotLeftUnlimited()
    {
        MethodInfo upload = typeof(FilesController).GetMethod(nameof(FilesController.UploadAsync))!;

        Assert.NotNull(upload.GetCustomAttribute<FileUploadSizeLimitAttribute>());
        Assert.Null(upload.GetCustomAttribute<DisableRequestSizeLimitAttribute>());
    }

    private static async Task<WebApplication> StartAsync()
    {
        WebApplicationBuilder builder = TestWebApplication.CreateBuilder();
        _ = builder.Services.AddSingleton(Options.Create(new PlatformSettings { IniaAi = new IniaAiSettings { MaxFileSizeMb = MAX_FILE_SIZE_MB } }));
        _ = builder.Services.AddControllers().AddApplicationPart(typeof(FileUploadSizeLimitTests).Assembly);
        _ = builder.Services.AddConfiguredApiVersioning();
        WebApplication app = builder.Build();
        _ = app.MapControllers();
        await app.StartAsync(TestContext.Current.CancellationToken);
        return app;
    }

    private static Task<HttpResponseMessage> PostAsync(WebApplication app, CountingContent file)
    {
        var form = new MultipartFormDataContent { { file, "file", "statement.pdf" } };
        return app.GetTestClient().PostAsync("upload-size-probe", form, TestContext.Current.CancellationToken);
    }

    /// <summary>A file part of zero bytes that counts how much of it the server pulled.</summary>
    private sealed class CountingContent(long length) : HttpContent
    {
        public long BytesRead { get; private set; }

        protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context)
        {
            byte[] chunk = new byte[64 * 1024];
            for (long sent = 0; sent < length; sent += chunk.Length)
            {
                int size = (int)Math.Min(chunk.Length, length - sent);
                await stream.WriteAsync(chunk.AsMemory(0, size));
                BytesRead += size;
            }
        }

        protected override bool TryComputeLength(out long size)
        {
            size = length;
            return true;
        }
    }
}

[ApiController]
[Route("upload-size-probe")]
public sealed class UploadSizeProbeController : ControllerBase
{
    [HttpPost]
    [FileUploadSizeLimit]
    [Consumes("multipart/form-data")]
    public IActionResult Post([FromForm] IFormFile file)
    {
        return Ok(file.Length);
    }
}
