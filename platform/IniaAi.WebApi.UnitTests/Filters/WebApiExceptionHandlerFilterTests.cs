using System.Text.Json;
using IniaAi.WebApi.Filters;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;

namespace IniaAi.WebApi.UnitTests.Filters;

public sealed class WebApiExceptionHandlerFilterTests
{
    private const string EXCEPTION_MESSAGE = "The query specified in the URI is not valid.";
    private const string PRODUCTION = "Production";

    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    [InlineData("Development")]
    public async Task AnUnhandledError_OutsideALocalMachine_ShowsNoExceptionDetail(string environmentName)
    {
        JsonElement body = await HandleAsync(environmentName);

        string text = body.GetRawText();
        Assert.Equal(500, body.GetProperty("status").GetInt32());
        Assert.DoesNotContain(EXCEPTION_MESSAGE, text);
        Assert.DoesNotContain(nameof(InvalidOperationException), text);
        Assert.DoesNotContain("   at ", text);
    }

    [Fact]
    public async Task AnUnhandledError_OnALocalMachine_ShowsTheException()
    {
        JsonElement body = await HandleAsync("LocalDevelopment");

        Assert.Contains(EXCEPTION_MESSAGE, body.GetProperty("detail").GetString());
        Assert.Equal(typeof(InvalidOperationException).FullName, body.GetProperty("type").GetString());
    }

    [Fact]
    public async Task ABodyOverTheSizeLimit_Answers413()
    {
        JsonElement body = await HandleAsync(PRODUCTION, new BadHttpRequestException("Request body too large.", StatusCodes.Status413PayloadTooLarge));

        Assert.Equal(StatusCodes.Status413PayloadTooLarge, body.GetProperty("status").GetInt32());
    }

    [Fact]
    public async Task AFormKeyOverTheFormReadersLimit_Answers400()
    {
        JsonElement body = await HandleAsync(PRODUCTION, await FormReaderErrorAsync());

        Assert.Equal(StatusCodes.Status400BadRequest, body.GetProperty("status").GetInt32());
    }

    [Fact]
    public async Task AMultipartFormWithNoBoundary_Answers400()
    {
        JsonElement body = await HandleAsync(PRODUCTION, await MissingBoundaryErrorAsync());

        Assert.Equal(StatusCodes.Status400BadRequest, body.GetProperty("status").GetInt32());
    }

    [Fact]
    public async Task AnInvalidDataExceptionTheAppThrows_StaysA500()
    {
        JsonElement body = await HandleAsync(PRODUCTION, Thrown(() => throw new InvalidDataException(EXCEPTION_MESSAGE)));

        Assert.Equal(StatusCodes.Status500InternalServerError, body.GetProperty("status").GetInt32());
    }

    private static Task<JsonElement> HandleAsync(string environmentName)
    {
        return HandleAsync(environmentName, Thrown(() => throw new InvalidOperationException(EXCEPTION_MESSAGE)));
    }

    private static async Task<InvalidDataException> FormReaderErrorAsync()
    {
        var context = new DefaultHttpContext();
        context.Request.ContentType = "application/x-www-form-urlencoded";
        context.Request.Body = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(new string('k', 2049) + "=v"));

        return await Assert.ThrowsAsync<InvalidDataException>(() => context.Request.ReadFormAsync(TestContext.Current.CancellationToken));
    }

    private static async Task<InvalidDataException> MissingBoundaryErrorAsync()
    {
        var context = new DefaultHttpContext();
        context.Request.ContentType = "multipart/form-data";
        context.Request.Body = new MemoryStream([1]);

        return await Assert.ThrowsAsync<InvalidDataException>(() => context.Request.ReadFormAsync(TestContext.Current.CancellationToken));
    }

    private static Exception Thrown(Action @throw)
    {
        try
        {
            @throw();
        }
        catch (Exception exception)
        {
            return exception;
        }

        throw new InvalidOperationException("The action threw nothing.");
    }

    private static async Task<JsonElement> HandleAsync(string environmentName, Exception exception)
    {
        var services = new ServiceCollection();
        _ = services.AddSingleton<IWebHostEnvironment>(new LocalHostEnvironment(environmentName));
        using ServiceProvider provider = services.BuildServiceProvider();
        var context = new DefaultHttpContext { RequestServices = provider };
        using var responseBody = new MemoryStream();
        context.Response.Body = responseBody;

        var handler = new WebApiExceptionHandlerFilter(NullLogger<WebApiExceptionHandlerFilter>.Instance);
        _ = await handler.TryHandleAsync(context, exception, TestContext.Current.CancellationToken);

        responseBody.Position = 0;
        return await JsonSerializer.DeserializeAsync<JsonElement>(responseBody, cancellationToken: TestContext.Current.CancellationToken);
    }

    private sealed class LocalHostEnvironment(string environmentName) : IWebHostEnvironment
    {
        public string EnvironmentName { get; set; } = environmentName;

        public string ApplicationName { get; set; } = "IniaAi.WebApi";

        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;

        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();

        public string WebRootPath { get; set; } = AppContext.BaseDirectory;

        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
    }
}
