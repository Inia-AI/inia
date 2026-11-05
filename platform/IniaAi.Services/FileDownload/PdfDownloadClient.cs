using IniaAi.Common.Http;
using IniaAi.Common.Text;
using Microsoft.Extensions.Logging;
using OpenQA.Selenium;
using OpenQA.Selenium.Chrome;

namespace IniaAi.Services.FileDownload;

/// <summary>
/// A wrapper around <see cref="PdfDownloadClientInternal"/>, which tries to download PDF files and skip other files.
/// <para>
/// A link that answers with a PDF is downloaded over HTTP. Any other page opens in Chrome, which
/// saves the PDF the page downloads into the storage directory. Chrome starts only for the first
/// link that needs it, and only where its sandbox starts: elsewhere such a link is skipped.
/// </para>
/// <para>
/// Both legs, the HTTP client and Chrome, reach a link only through <see cref="OutboundAddressGuard"/>:
/// the HTTP client through the guard's handler, and Chrome through the checked proxy the pool
/// gives each driver.
/// </para>
/// <para>
/// A failed download returns null, assuming most often it comes from an unreachable URL. A
/// Chrome failure is retried once on a new driver, and a second one is thrown.
/// </para>
/// </summary>
internal class PdfDownloadClient : IDisposable
{
    // The Chrome leg waits for the page to load, then for the page-load delay; the margin covers
    // starting Chrome and the download the page starts.
    private const int CLIENT_TIMEOUT_MARGIN_MINUTES = 1;

    private readonly ILogger _logger;
    private readonly string _storageDir;
    private readonly OutboundAddressGuard _guard;
    private readonly ChromeDriverPool _pool;
    private readonly PdfDownloadClientInternal _pdfDownloadClient;
    private bool _disposed;
    private ChromeDriver? _chromeDriver;

    public PdfDownloadClient(string storageDir, ILogger logger, int pageLoadDelayMs, OutboundAddressGuard guard)
        : this(storageDir, logger, pageLoadDelayMs, guard, ChromeDriverPoolProvider.Instance)
    {
    }

    internal PdfDownloadClient(string storageDir, ILogger logger, int pageLoadDelayMs, OutboundAddressGuard guard, ChromeDriverPool pool)
    {
        _logger = logger;
        _storageDir = storageDir;
        _guard = guard;
        _pool = pool;
        _pdfDownloadClient = new PdfDownloadClientInternal(ChromeAsync, storageDir, logger, pageLoadDelayMs, guard);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _pdfDownloadClient.Dispose();
        ReleaseChrome();

        GC.SuppressFinalize(this);
    }

    public async Task<HttpResponseMessage?> TryDownloadPdfWithRetryAsync
        (Uri url)
    {
        try
        {
            return await TryDownloadPdfAsync(url);
        }
        catch (WebDriverException)
        {
            _logger.LogWarning("ChromeDriver failed, attempting to recreate...");
            ReleaseChrome();

            return await TryDownloadPdfAsync(url);
        }
    }

    public async Task<HttpResponseMessage?> TryDownloadPdfAsync(Uri url)
    {
        try
        {
            return await _pdfDownloadClient.GetAsync(url);
        }
        catch (OutboundAddressRefusedException refusal)
        {
            _logger.LogWarning("Skipped a link ({Address}): {Reason}", refusal.Address, LogSanitizer.ForLog(refusal.Message));

            return null;
        }
        catch (Exception e) when (e is not WebDriverException)
        {
            _logger.LogError("Failed to download PDF: {ErrorMessage}", e.Message);

            return null;
        }
    }

    private async Task<ChromeDriver> ChromeAsync(CancellationToken cancellationToken)
    {
        return _chromeDriver ??= await _pool.AcquirePageDriverAsync(_storageDir, _guard, _logger, cancellationToken);
    }

    private void ReleaseChrome()
    {
        if (_chromeDriver is ChromeDriver driver)
        {
            _chromeDriver = null;
            _pool.ReleaseDriver(driver);
        }
    }

    private class PdfDownloadClientInternal : HttpClient
    {
        public PdfDownloadClientInternal(
            Func<CancellationToken, Task<ChromeDriver>> chrome,
            string storageDir,
            ILogger logger,
            int pageLoadDelayMs,
            OutboundAddressGuard guard)
            : base(new PdfDownloadHandler(chrome, storageDir, logger, pageLoadDelayMs, guard))
        {
            Timeout = ChromeDriverPool.PageLoadTimeout
                + TimeSpan.FromMilliseconds(pageLoadDelayMs)
                + TimeSpan.FromMinutes(CLIENT_TIMEOUT_MARGIN_MINUTES);
        }
    }
}
