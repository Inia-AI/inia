using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;
using IniaAi.Common.Text;
using Microsoft.Extensions.Logging;

namespace IniaAi.Common.Http;

/// <summary>
/// A forward proxy on the loopback interface that a headless browser must use. It applies an
/// <see cref="OutboundAddressGuard"/> to every request the browser sends, page subrequests and
/// redirects included: a CONNECT tunnel for an HTTPS target, and a plain HTTP request. An offline
/// proxy refuses every request, for a browser that renders the platform's own content. Each
/// connection carries one plain HTTP request: the proxy sends it on with <c>Connection: close</c>
/// and closes the connection when the browser sends another on it. It sends the request on
/// through the proxy that the platform's environment sets, when there is one.
/// </summary>
public sealed class CheckedForwardProxy : IDisposable
{
    private const int MAX_HEAD_BYTES = 64 * 1024;
    private const int BODY_BUFFER_BYTES = 64 * 1024;
    private const string CONNECT_METHOD = "CONNECT";
    private const string OFFLINE_REASON = "This browser has no network access.";
    private const string CONNECTION_HEADER = "Connection";
    private const string CONTENT_LENGTH_HEADER = "Content-Length";
    private const string TRANSFER_ENCODING_HEADER = "Transfer-Encoding";
    private const string HEAD_END = "\r\n\r\n";
    private const string LINE_END = "\r\n";

    private static readonly byte[] _headEndBytes = Encoding.ASCII.GetBytes(HEAD_END);

    private static readonly string[] _hopByHopHeaders =
        [CONNECTION_HEADER, "Keep-Alive", "Proxy-Connection", "Proxy-Authorization", "Proxy-Authenticate", "TE", "Trailer", "Upgrade"];

    private readonly OutboundAddressGuard? _guard;
    private readonly ILogger _logger;
    private readonly TcpListener _listener;
    private readonly CancellationTokenSource _stopping = new();
    private readonly Task _acceptLoop;

    internal CheckedForwardProxy(OutboundAddressGuard? guard, ILogger logger)
    {
        _guard = guard;
        _logger = logger;
        _listener = new TcpListener(IPAddress.Loopback, 0);
        _listener.Start();
        Address = new Uri($"http://{IPAddress.Loopback}:{((IPEndPoint)_listener.LocalEndpoint).Port}");
        _acceptLoop = AcceptLoopAsync();
    }

    public Uri Address { get; }

    /// <summary>
    /// Starts a proxy that refuses every request and logs each one to <paramref name="logger"/>.
    /// The caller owns the proxy and disposes it.
    /// </summary>
    public static CheckedForwardProxy StartOffline(ILogger logger)
    {
        return new CheckedForwardProxy(null, logger);
    }

    public void Dispose()
    {
        if (_stopping.IsCancellationRequested)
        {
            return;
        }

        _stopping.Cancel();
        _listener.Stop();

        try
        {
            _acceptLoop.Wait(TimeSpan.FromSeconds(5));
        }
        catch (AggregateException)
        {
        }

        _stopping.Dispose();
    }

    private async Task AcceptLoopAsync()
    {
        while (!_stopping.IsCancellationRequested)
        {
            TcpClient client;

            try
            {
                client = await _listener.AcceptTcpClientAsync(_stopping.Token);
            }
            catch (Exception ex) when (ex is OperationCanceledException or SocketException or ObjectDisposedException)
            {
                return;
            }

            _ = ServeAsync(client);
        }
    }

    private async Task ServeAsync(TcpClient client)
    {
        using TcpClient owned = client;
        CancellationToken cancellationToken = _stopping.Token;

        try
        {
            NetworkStream browser = client.GetStream();
            RequestHead? head = await RequestHead.ReadAsync(browser, cancellationToken);

            if (head is null)
            {
                return;
            }

            if (_guard is null)
            {
                // Chrome's own background calls (time, updates) land here on every start, so this stays quiet.
                _logger.LogDebug("A browser with no network access asked for {Target}", LogSanitizer.ForLog(head.Target));
                await RespondAsync(browser, HttpStatusCode.Forbidden, OFFLINE_REASON, cancellationToken);
                return;
            }

            try
            {
                if (head.Method.Equals(CONNECT_METHOD, StringComparison.OrdinalIgnoreCase))
                {
                    await TunnelAsync(head, _guard, browser, cancellationToken);
                }
                else
                {
                    await ForwardAsync(head, _guard, browser, cancellationToken);
                }
            }
            catch (OutboundAddressRefusedException refusal)
            {
                _logger.LogWarning("The browser was refused a request ({Address}): {Reason}", refusal.Address, LogSanitizer.ForLog(refusal.Message));
                await RespondAsync(browser, HttpStatusCode.Forbidden, refusal.Message, cancellationToken);
            }
            catch (Exception ex) when (ex is IOException or SocketException or UpstreamProxyException or UriFormatException)
            {
                _logger.LogDebug("A browser request through the proxy failed: {Reason}", LogSanitizer.ForLog(ex.Message));
                await RespondAsync(browser, HttpStatusCode.BadGateway, ex.Message, cancellationToken);
            }
        }
        catch (Exception ex) when (ex is IOException or SocketException or OperationCanceledException or ObjectDisposedException)
        {
            _logger.LogTrace("A browser connection to the proxy closed: {Reason}", LogSanitizer.ForLog(ex.Message));
        }
    }

    private static async Task TunnelAsync(RequestHead head, OutboundAddressGuard guard, NetworkStream browser, CancellationToken cancellationToken)
    {
        var target = new Uri($"https://{head.Target}");
        (Stream server, Uri? upstream) = await guard.OpenAsync(target, cancellationToken);

        await using (server)
        {
            if (upstream is not null)
            {
                await ConnectThroughAsync(server, guard, upstream, target, cancellationToken);
            }

            await WriteAsciiAsync(browser, $"HTTP/1.1 200 Connection Established{HEAD_END}", cancellationToken);
            await server.WriteAsync(head.Leftover, cancellationToken);
            await RelayAsync(browser.CopyToAsync(server, cancellationToken), server, browser, cancellationToken);
        }
    }

    private static async Task ForwardAsync(RequestHead head, OutboundAddressGuard guard, NetworkStream browser, CancellationToken cancellationToken)
    {
        var target = new Uri(head.Target, UriKind.Absolute);

        if (target.Scheme != Uri.UriSchemeHttp)
        {
            throw new UriFormatException($"The proxy forwards only http requests, not {target.Scheme}.");
        }

        long bodyLength = head.BodyLength();
        (Stream server, Uri? upstream) = await guard.OpenAsync(target, cancellationToken);

        await using (server)
        {
            string requestTarget = upstream is null ? target.PathAndQuery : target.AbsoluteUri;
            string proxyAuthorization = upstream is null ? string.Empty : ProxyAuthorizationLine(guard, upstream);
            await WriteAsciiAsync(server, head.Rewrite(requestTarget, proxyAuthorization), cancellationToken);
            await SendBodyAsync(head.Leftover, browser, server, bodyLength, cancellationToken);

            byte[] nextRequest = new byte[1];
            await RelayAsync(browser.ReadAsync(nextRequest, cancellationToken).AsTask(), server, browser, cancellationToken);
        }
    }

    private static async Task SendBodyAsync(byte[] leftover, Stream browser, Stream server, long length, CancellationToken cancellationToken)
    {
        int buffered = (int)Math.Min(leftover.Length, length);
        await server.WriteAsync(leftover.AsMemory(0, buffered), cancellationToken);

        byte[] buffer = new byte[BODY_BUFFER_BYTES];

        for (long remaining = length - buffered; remaining > 0;)
        {
            int read = await browser.ReadAsync(buffer.AsMemory(0, (int)Math.Min(buffer.Length, remaining)), cancellationToken);

            if (read == 0)
            {
                throw new IOException("The browser closed the connection before it sent the whole request body.");
            }

            await server.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            remaining -= read;
        }
    }

    private static async Task ConnectThroughAsync(Stream proxy, OutboundAddressGuard guard, Uri upstream, Uri target, CancellationToken cancellationToken)
    {
        string host = target.HostNameType == UriHostNameType.IPv6 ? $"[{target.IdnHost}]" : target.IdnHost;
        string authority = $"{host}:{target.Port}";
        await WriteAsciiAsync(
            proxy,
            $"CONNECT {authority} HTTP/1.1{LINE_END}Host: {authority}{LINE_END}{ProxyAuthorizationLine(guard, upstream)}{LINE_END}",
            cancellationToken);

        int? statusCode = await ReadStatusCodeAsync(proxy, cancellationToken)
            ?? throw new UpstreamProxyException($"The proxy {upstream.Authority} closed the connection.");

        if (statusCode != (int)HttpStatusCode.OK)
        {
            throw new UpstreamProxyException($"The proxy {upstream.Authority} answered CONNECT with {statusCode}.");
        }
    }

    private static string ProxyAuthorizationLine(OutboundAddressGuard guard, Uri upstream)
    {
        NetworkCredential? credential = guard.UpstreamProxyCredential(upstream);

        if (credential is null || string.IsNullOrEmpty(credential.UserName))
        {
            return string.Empty;
        }

        string token = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{credential.UserName}:{credential.Password}"));
        return $"Proxy-Authorization: Basic {token}{LINE_END}";
    }

    /// <summary>
    /// Copies the server's bytes to the browser until either side is done: the server closes, or
    /// <paramref name="fromBrowser"/> ends. Ending the relay closes both connections.
    /// </summary>
    private static async Task RelayAsync(Task fromBrowser, Stream server, Stream browser, CancellationToken cancellationToken)
    {
        using var relay = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        Task toBrowser = server.CopyToAsync(browser, relay.Token);

        _ = await Task.WhenAny(fromBrowser, toBrowser);
        relay.Cancel();
        server.Close();
        browser.Close();

        try
        {
            await Task.WhenAll(fromBrowser, toBrowser);
        }
        catch (Exception ex) when (ex is IOException or SocketException or OperationCanceledException or ObjectDisposedException)
        {
        }
    }

    private static async Task RespondAsync(Stream browser, HttpStatusCode status, string reason, CancellationToken cancellationToken)
    {
        byte[] body = Encoding.UTF8.GetBytes(reason);
        await WriteAsciiAsync(
            browser,
            $"HTTP/1.1 {(int)status} {status}{LINE_END}Content-Type: text/plain; charset=utf-8{LINE_END}Content-Length: {body.Length}{LINE_END}Connection: close{HEAD_END}",
            cancellationToken);
        await browser.WriteAsync(body, cancellationToken);
    }

    private static Task WriteAsciiAsync(Stream stream, string text, CancellationToken cancellationToken)
    {
        return stream.WriteAsync(Encoding.ASCII.GetBytes(text), cancellationToken).AsTask();
    }

    private static async Task<(string Head, byte[] Leftover)?> ReadHeadAsync(Stream stream, CancellationToken cancellationToken)
    {
        byte[] buffer = new byte[MAX_HEAD_BYTES];
        int filled = 0;

        while (filled < buffer.Length)
        {
            int read = await stream.ReadAsync(buffer.AsMemory(filled), cancellationToken);

            if (read == 0)
            {
                return null;
            }

            int scanFrom = Math.Max(0, filled - _headEndBytes.Length + 1);
            filled += read;
            int end = buffer.AsSpan(scanFrom, filled - scanFrom).IndexOf(_headEndBytes);

            if (end >= 0)
            {
                end += scanFrom;
                return (Encoding.Latin1.GetString(buffer, 0, end), buffer[(end + _headEndBytes.Length)..filled]);
            }
        }

        throw new IOException($"The request head is longer than {MAX_HEAD_BYTES} bytes.");
    }

    private static async Task<int?> ReadStatusCodeAsync(Stream stream, CancellationToken cancellationToken)
    {
        if (await ReadHeadAsync(stream, cancellationToken) is not { } read)
        {
            return null;
        }

        string[] statusLine = read.Head.Split(LINE_END)[0].Split(' ', 3);

        return statusLine.Length >= 2 && int.TryParse(statusLine[1], out int statusCode)
            ? statusCode
            : throw new IOException("The proxy's status line is malformed.");
    }

    private sealed class UpstreamProxyException(string message) : Exception(message);

    private sealed record RequestHead(string Method, string Target, string Version, string[] Headers, byte[] Leftover)
    {
        public static async Task<RequestHead?> ReadAsync(Stream stream, CancellationToken cancellationToken)
        {
            if (await ReadHeadAsync(stream, cancellationToken) is not { } read)
            {
                return null;
            }

            string[] lines = read.Head.Split(LINE_END);
            string[] requestLine = lines[0].Split(' ');

            if (requestLine.Length != 3)
            {
                throw new IOException("The request line is malformed.");
            }

            return new RequestHead(requestLine[0], requestLine[1], requestLine[2], lines[1..], read.Leftover);
        }

        /// <summary>
        /// The length of the body that follows the head. A chunked body is refused: a browser
        /// sends one only to an HTTP/2 server, which a proxied plain request never reaches.
        /// </summary>
        public long BodyLength()
        {
            if (Values(TRANSFER_ENCODING_HEADER).Any())
            {
                throw new IOException("The proxy forwards a request body only with a Content-Length.");
            }

            string? contentLength = Values(CONTENT_LENGTH_HEADER).FirstOrDefault();

            if (contentLength is null)
            {
                return 0;
            }

            return long.TryParse(contentLength, NumberStyles.None, CultureInfo.InvariantCulture, out long length)
                ? length
                : throw new IOException("The request's Content-Length is malformed.");
        }

        public string Rewrite(string requestTarget, string proxyAuthorization)
        {
            var builder = new StringBuilder();
            _ = builder.Append($"{Method} {requestTarget} {Version}{LINE_END}");

            HashSet<string> dropped = new(_hopByHopHeaders, StringComparer.OrdinalIgnoreCase);
            dropped.UnionWith(Values(CONNECTION_HEADER)
                .SelectMany(value => value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)));

            foreach (string header in Headers)
            {
                if (!dropped.Contains(NameOf(header)))
                {
                    _ = builder.Append(header).Append(LINE_END);
                }
            }

            _ = builder.Append(proxyAuthorization);
            _ = builder.Append($"{CONNECTION_HEADER}: close{HEAD_END}");
            return builder.ToString();
        }

        private IEnumerable<string> Values(string name)
        {
            return Headers
                .Where(header => NameOf(header).Equals(name, StringComparison.OrdinalIgnoreCase))
                .Select(header => header.Split(':', 2) is [_, string value] ? value.Trim() : string.Empty);
        }

        private static string NameOf(string header)
        {
            return header.Split(':', 2)[0].Trim();
        }
    }
}
