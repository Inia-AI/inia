using IniaAi.Common.Http;
using IniaAi.FileFormats.Pdf;
using Microsoft.Extensions.Logging;
using OpenQA.Selenium;
using OpenQA.Selenium.Chrome;
using System.Net;
using System.Net.Http.Headers;

namespace IniaAi.Services.FileDownload;

internal class PdfDownloadHandler : DelegatingHandler
{
    private readonly ILogger _logger;
    private readonly Func<CancellationToken, Task<ChromeDriver>> _chrome;
    private readonly string _storageDir;
    private readonly int _pageLoadDelayMs;

    public PdfDownloadHandler(
        Func<CancellationToken, Task<ChromeDriver>> chrome,
        string storageDir,
        ILogger logger,
        int pageLoadDelayMs,
        OutboundAddressGuard guard)
        : base(guard.CreateHandler())
    {
        _chrome = chrome;
        _storageDir = storageDir;
        _logger = logger;
        _pageLoadDelayMs = pageLoadDelayMs;
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        return request.RequestUri is null
            ? new HttpResponseMessage(HttpStatusCode.BadRequest)
            : await HandleRequestAsync(request, cancellationToken);
    }

    private async Task<HttpResponseMessage> HandleRequestAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        Uri requestUrl = request.RequestUri!;
        string extension = Path.GetExtension(requestUrl.PathAndQuery);

        return IsPdfExtension(extension)
            ? await HandlePdfFileDownloadAsync(request, requestUrl, cancellationToken)
            : await HandleNonPdfFileAsync(request, requestUrl, cancellationToken);
    }

    private async Task<HttpResponseMessage> HandlePdfFileDownloadAsync(
        HttpRequestMessage request,
        Uri requestUrl,
        CancellationToken cancellationToken)
    {
        _logger.LogDebug("PDF file found at URL: {Url}", requestUrl);

        HttpResponseMessage response = await base.SendAsync(request, cancellationToken);

        HeaderLogger.LogHeaders(requestUrl, response.Headers, _logger);

        if (response.IsSuccessStatusCode)
        {
            _logger.LogInformation("PDF file downloaded: {Url}", requestUrl);

            return response;
        }

        _logger.LogWarning("PDF file download failed: {Url} {StatusCode}", requestUrl, response.StatusCode);

        return new HttpResponseMessage(response.StatusCode);
    }

    private async Task<HttpResponseMessage> HandleNonPdfFileAsync(
        HttpRequestMessage request,
        Uri requestUrl,
        CancellationToken cancellationToken)
    {
        _logger.LogDebug("No PDF file found at URL: {Url}, trying HttpClient", requestUrl);

        HttpResponseMessage response = await base.SendAsync(request, cancellationToken);
        HeaderLogger.LogHeaders(requestUrl, response.Headers, _logger);

        if (ContentTypeChecker.IsPdfContent(response.Content)
            || await ContentTypeChecker.StartsWithPdfSignatureAsync(response.Content, cancellationToken))
        {
            _logger.LogInformation("PDF content found at URL: {Url}", requestUrl);

            return response;
        }

        return await HandleChromeDownloadAsync(requestUrl, cancellationToken);
    }

    private async Task<HttpResponseMessage> HandleChromeDownloadAsync(
        Uri requestUrl,
        CancellationToken cancellationToken)
    {
        _logger.LogWarning("No PDF content found at URL: {Url}, trying Chrome", requestUrl);

        ChromeDriver chrome;

        try
        {
            chrome = await _chrome(cancellationToken);
        }
        catch (BrowserSandboxUnavailableException unavailable)
        {
            _logger.LogWarning("Skipped the browser step for {Url}: {Reason}", requestUrl, unavailable.Message);
            return new HttpResponseMessage(HttpStatusCode.NoContent);
        }

        try
        {
            await chrome.Navigate().GoToUrlAsync(requestUrl);
        }
        catch (WebDriverTimeoutException)
        {
            _logger.LogWarning("The page did not finish loading within {Timeout}: {Url}", ChromeDriverPool.PageLoadTimeout, requestUrl);
        }

        await Task.Delay(_pageLoadDelayMs, cancellationToken);

        _ = await DownloadManager.EnsureDownloadFinishedAsync(_storageDir, _logger);

        return new HttpResponseMessage(HttpStatusCode.NoContent);
    }

    private static bool IsPdfExtension(string extension)
    {
        return extension.Length == 4 && extension.Equals(".pdf", StringComparison.OrdinalIgnoreCase);
    }

    private static class HeaderLogger
    {
        public static void LogHeaders(Uri url, HttpHeaders headers, ILogger logger)
        {
            logger.LogTrace("Headers for: {Url}", url);
            foreach ((string key, IEnumerable<string> value) in headers)
            {
                logger.LogTrace("\t{HeaderKey} = {HeaderValue}", key, value);
            }
        }
    }

    private static class ContentTypeChecker
    {
        private const string TEXT_MEDIA_TYPE_PREFIX = "text/";

        public static bool IsPdfContent(HttpContent httpContent)
        {
            return httpContent.Headers.TryGetValues("Content-Type", out IEnumerable<string>? values) &&
                   values.Contains("application/pdf");
        }

        /// <summary>
        /// A PDF that the server labels as something else, such as <c>application/octet-stream</c>.
        /// A text answer is a page for Chrome and is not read here.
        /// </summary>
        public static async Task<bool> StartsWithPdfSignatureAsync(HttpContent httpContent, CancellationToken cancellationToken)
        {
            if (httpContent.Headers.ContentType?.MediaType?.StartsWith(TEXT_MEDIA_TYPE_PREFIX, StringComparison.OrdinalIgnoreCase) == true)
            {
                return false;
            }

            await httpContent.LoadIntoBufferAsync(cancellationToken);
            using Stream content = await httpContent.ReadAsStreamAsync(cancellationToken);
            byte[] head = new byte[PdfUtils.Signature.Length];
            int read = await content.ReadAtLeastAsync(head, head.Length, throwOnEndOfStream: false, cancellationToken);

            return head.AsSpan(0, read).SequenceEqual(PdfUtils.Signature);
        }
    }
}
