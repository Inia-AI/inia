namespace IniaAi.Configuration.Messaging;

public class EmailExpansionSettings
{
    /// <summary>
    /// Whether expansion should try to download PDFs from URLs found in the email body.
    /// Off by default: fetching caller-controlled URLs server-side is a deployment-level
    /// decision, not something every API caller should be able to trigger. When on, a /submit
    /// request that expands an email waits synchronously for every URL download (page-load delay
    /// plus retry budget, per URL); the start path pays that inside the execution job instead.
    /// </summary>
    public bool ShouldTryDownloadUrls { get; set; } = false;

    /// <summary>
    /// Delay in milliseconds to wait for Chrome page load before checking for PDF content.
    /// </summary>
    public int ChromePageLoadDelayMs { get; set; } = 30000;

    /// <summary>
    /// Maximum number of entries a ZIP attachment may contain. Archives over the limit are
    /// skipped whole and reported.
    /// </summary>
    public int MaxArchiveEntries { get; set; } = 100;

    /// <summary>
    /// Hosts whose links expansion never downloads, each with its subdomains. Configured hosts add to the default.
    /// </summary>
    public List<string> SkippedLinkHosts { get; set; } = ["linkedin.com"];
}
