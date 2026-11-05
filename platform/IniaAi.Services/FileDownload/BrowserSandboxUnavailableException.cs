namespace IniaAi.Services.FileDownload;

/// <summary>
/// A page that someone outside the deployment chose may open only in a sandboxed Chrome, and the
/// sandbox does not start on this host. It is not a WebDriverException on purpose: a caller that
/// starts a new Chrome after a crash must not retry it.
/// </summary>
internal sealed class BrowserSandboxUnavailableException(string reason) : Exception(reason);
