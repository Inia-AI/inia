using System.Net;
using System.Net.Sockets;
using IniaAi.Common.Http;
using IniaAi.Services.FileDownload;
using IniaAi.Testing;
using IniaAi.Testing.XunitAttributes;
using OpenQA.Selenium.Chrome;

namespace IniaAi.Services.Tests.FileDownload;

public sealed class PdfDownloadLegsTests : IDisposable
{
    private const string PAGE_PATH = "/page";
    private const string PDF_PATH = "/report.pdf";
    private const string PDF_CONTENT_PATH = "/pdf-content";

    private static readonly BrowserIsolation _unsandboxed = new(false, null, "Chrome runs without its sandbox here.");

    private readonly LocalTestHttpServer _site = new();
    private readonly string _downloads = Path.Combine(Path.GetTempPath(), "Inia", "pdf-download-legs", Path.GetRandomFileName());

    public PdfDownloadLegsTests()
    {
        _ = Directory.CreateDirectory(_downloads);
    }

    public void Dispose()
    {
        _site.Dispose();
        Directory.Delete(_downloads, recursive: true);
    }

    [Fact]
    public async Task ThePlainLeg_RefusesADirectLinkToAnInternalAddress()
    {
        var logger = new CapturingLogger<PdfDownloadLegsTests>();
        using var client = new PdfDownloadClient(_downloads, logger, pageLoadDelayMs: 0, OutboundAddressGuard.For([]));

        HttpResponseMessage? response = await client.TryDownloadPdfWithRetryAsync(new Uri($"{_site.BaseUrl}{PDF_PATH}"));

        Assert.Null(response);
        Assert.Equal(0, _site.PathHits(PDF_PATH));
        Assert.Contains(logger.Entries, entry => entry.Message.Contains("Skipped a link") && entry.Message.Contains(InternalAddresses.LOOPBACK));
    }

    [Fact]
    public async Task ThePlainLeg_LogsTheAddressARefusedNameResolvesTo()
    {
        var logger = new CapturingLogger<PdfDownloadLegsTests>();
        using var client = new PdfDownloadClient(_downloads, logger, pageLoadDelayMs: 0, OutboundAddressGuard.For([]));
        string linkOnLocalhost = $"{_site.BaseUrl.Replace("127.0.0.1", "localhost")}{PDF_PATH}";

        HttpResponseMessage? response = await client.TryDownloadPdfWithRetryAsync(new Uri(linkOnLocalhost));

        Assert.Null(response);
        Assert.Contains(logger.Entries, entry =>
            entry.Message.Contains("Skipped a link") && entry.Message.Contains("localhost")
            && (entry.Message.Contains("127.0.0.1") || entry.Message.Contains("::1")));
    }

    [Fact]
    public async Task ThePageLeg_WithoutTheSandbox_SkipsTheBrowser()
    {
        var logger = new CapturingLogger<PdfDownloadLegsTests>();
        using var pool = new ChromeDriverPool(1, _unsandboxed);
        using var client = new PdfDownloadClient(_downloads, logger, pageLoadDelayMs: 0, OutboundAddressGuard.For(["127.0.0.1"]), pool);

        HttpResponseMessage? response = await client.TryDownloadPdfWithRetryAsync(PageUri("<p>no pdf here</p>"));

        Assert.Equal(HttpStatusCode.NoContent, response?.StatusCode);
        Assert.Equal(1, _site.PathHits(PAGE_PATH));
        Assert.Equal(1, pool.RefusedPages);
        Assert.Contains(logger.Entries, entry => entry.Message.Contains("Skipped the browser step") && entry.Message.Contains(_unsandboxed.Description));
    }

    [Fact]
    public async Task ThePlainLeg_TakesAPdfThatTheServerLabelsAsSomethingElse()
    {
        using var pool = new ChromeDriverPool(1, _unsandboxed);
        using var client = new PdfDownloadClient(_downloads, new CapturingLogger<PdfDownloadLegsTests>(), pageLoadDelayMs: 0, OutboundAddressGuard.For(["127.0.0.1"]), pool);

        HttpResponseMessage? response = await client.TryDownloadPdfWithRetryAsync(new Uri($"{_site.BaseUrl}{PDF_CONTENT_PATH}?type=application/octet-stream"));

        Assert.True(response?.IsSuccessStatusCode);
        Assert.StartsWith("%PDF", await response!.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Equal(0, pool.RefusedPages);
    }

    [RequiresChromeTheory]
    [InlineData("""<meta http-equiv="refresh" content="0;url=http://169.254.169.254/latest/report.pdf">""", "169.254.169.254", InternalAddresses.LINK_LOCAL)]
    [InlineData("""<script>location.href = "http://10.0.0.1/report.pdf";</script>""", "10.0.0.1", InternalAddresses.PRIVATE)]
    public async Task TheChromeLeg_RefusesAPageThatSendsChromeOnToAnInternalAddress(string html, string address, string rule)
    {
        var logger = new CapturingLogger<PdfDownloadLegsTests>();
        using var client = new PdfDownloadClient(_downloads, logger, pageLoadDelayMs: 2000, OutboundAddressGuard.For(["127.0.0.1"]));

        HttpResponseMessage? response = await client.TryDownloadPdfWithRetryAsync(PageUri(html));

        Assert.Equal(HttpStatusCode.NoContent, response?.StatusCode);
        Assert.Equal(2, _site.PathHits(PAGE_PATH));
        Assert.Empty(Directory.GetFiles(_downloads));
        Assert.Contains(logger.Entries, entry =>
            entry.Message.Contains("The browser was refused a request") && entry.Message.Contains(address) && entry.Message.Contains(rule));
    }

    [Fact]
    public async Task ALinksQuery_AndTheLineBreaksItCarries_NeverReachTheLog()
    {
        var logger = new CapturingLogger<PdfDownloadLegsTests>();
        using var pool = new ChromeDriverPool(1, _unsandboxed);
        using var client = new PdfDownloadClient(_downloads, logger, pageLoadDelayMs: 0, OutboundAddressGuard.For(["127.0.0.1"]), pool);

        HttpResponseMessage? response = await client.TryDownloadPdfWithRetryAsync(new Uri($"{_site.BaseUrl}{PDF_PATH}?token=secret-marker%0d%0aFAKE"));

        Assert.True(response?.IsSuccessStatusCode);
        Assert.Contains(logger.Entries, entry => entry.Message.Contains(PDF_PATH, StringComparison.Ordinal));
        Assert.DoesNotContain(logger.Entries, entry =>
            entry.Message.Contains("secret-marker", StringComparison.Ordinal) || entry.Message.Contains('\n') || entry.Message.Contains('\r'));
    }

    [Theory]
    [InlineData("file")]
    [InlineData("ftp")]
    public async Task ALinkThatIsNotHttp_IsSkipped_AndNeverReachesChrome(string scheme)
    {
        string marker = Path.Combine(Path.GetTempPath(), $"sentinel-{Guid.NewGuid():N}.txt");
        await File.WriteAllTextAsync(marker, marker, TestContext.Current.CancellationToken);
        using var pool = new ChromeDriverPool(1, _unsandboxed);
        using var client = new PdfDownloadClient(_downloads, new CapturingLogger<PdfDownloadLegsTests>(), pageLoadDelayMs: 0, OutboundAddressGuard.For(["127.0.0.1"]), pool);
        Uri link = scheme == "file" ? new Uri(marker) : new Uri($"ftp://127.0.0.1:{new Uri(_site.BaseUrl).Port}/report.txt");

        try
        {
            HttpResponseMessage? response = await client.TryDownloadPdfWithRetryAsync(link);

            Assert.Null(response);
            Assert.Equal(0, pool.RefusedPages);
            Assert.Empty(Directory.GetFiles(_downloads));
        }
        finally
        {
            File.Delete(marker);
        }
    }

    [RequiresChromeFact]
    public async Task TheChromeLeg_LoadsAnAllowedPage_ButRefusesItsImageFromAnInternalAddress()
    {
        using var internalHost = new TcpListener(IPAddress.IPv6Loopback, 0);
        internalHost.Start();
        int port = ((IPEndPoint)internalHost.LocalEndpoint).Port;
        var logger = new CapturingLogger<PdfDownloadLegsTests>();
        using var client = new PdfDownloadClient(_downloads, logger, pageLoadDelayMs: 2000, OutboundAddressGuard.For(["127.0.0.1"]));

        HttpResponseMessage? response = await client.TryDownloadPdfWithRetryAsync(PageUri($"<img src=\"http://[::1]:{port}/probe.png\">"));

        Assert.Equal(HttpStatusCode.NoContent, response?.StatusCode);
        Assert.Equal(2, _site.PathHits(PAGE_PATH));
        Assert.False(internalHost.Pending());
        Assert.Contains(logger.Entries, entry =>
            entry.Message.Contains("The browser was refused a request") && entry.Message.Contains("::1") && entry.Message.Contains(InternalAddresses.LOOPBACK));
    }

    [RequiresChromeFact]
    public async Task APooledChrome_SendsLoopbackRequestsThroughItsProxy()
    {
        var logger = new CapturingLogger<PdfDownloadLegsTests>();
        ChromeDriver driver = await ChromeDriverPoolProvider.Instance.AcquirePageDriverAsync(
            _downloads,
            OutboundAddressGuard.For([]),
            logger,
            TestContext.Current.CancellationToken);

        try
        {
            await driver.Navigate().GoToUrlAsync(PageUri("<p>plain</p>"));
        }
        finally
        {
            ChromeDriverPoolProvider.Instance.ReleaseDriver(driver);
        }

        Assert.Equal(0, _site.PathHits(PAGE_PATH));
        Assert.Contains(logger.Entries, entry => entry.Message.Contains("127.0.0.1") && entry.Message.Contains(InternalAddresses.LOOPBACK));
    }

    private Uri PageUri(string html)
    {
        return new Uri($"{_site.BaseUrl}{PAGE_PATH}?html={Uri.EscapeDataString(html)}");
    }
}
