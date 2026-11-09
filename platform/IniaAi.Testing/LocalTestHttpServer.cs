using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace IniaAi.Testing;

/// <summary>
/// A real in-process HTTP server bound to a free loopback port, used so WebApiCall tests
/// exercise the genuine <see cref="System.Net.Http.HttpClient"/> round-trip deterministically
/// instead of depending on a public endpoint like httpbin.org. It is a real server, not a mock:
/// requests are echoed back as JSON so callers can assert on the method, path, headers and body
/// that actually reached the wire. Special paths drive the error/timeout behaviours.
/// </summary>
public sealed class LocalTestHttpServer : IDisposable
{
    private const string STATUS_500_PATH = "/status/500";
    private const string STATUS_PATH_PREFIX = "/status/";
    private const string DELAY_PATH = "/delay";
    private const string INFLIGHT_PATH = "/inflight";
    private const string REDIRECT_PATH = "/redirect";
    private const string PAGE_PATH = "/page";
    private const string PDF_CONTENT_PATH = "/pdf-content";
    private const string PDF_CONTENT = "%PDF-1.4\n%test\n";
    private const string TYPE_QUERY = "type";
    private const string KEY_QUERY = "key";
    private const string TIMES_QUERY = "times";
    private const string STATUSES_QUERY = "statuses";
    private const string RETRY_AFTER_QUERY = "retryAfter";
    private const string RETRY_AFTER_DATE_SECONDS_QUERY = "retryAfterDateSeconds";
    private const string HOLD_MS_QUERY = "holdMs";
    private const string TO_QUERY = "to";
    private const string STATUS_QUERY = "status";
    private const string HTML_QUERY = "html";
    private const string SET_COOKIE_QUERY = "setCookie";
    private const string JSON_CONTENT_TYPE = "application/json";
    private const string HTML_CONTENT_TYPE = "text/html";
    private const string RETRY_AFTER_HEADER = "Retry-After";
    private const int DEFAULT_HOLD_MS = 200;

    // Per-key counters so several tests can share the one server without seeing each other's
    // state: how many times a scripted status path has been hit, and the in-flight high-water
    // mark of the concurrency probe.
    private readonly ConcurrentDictionary<string, int> _statusHits = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, int> _inFlight = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, int> _maxInFlight = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, int> _pathHits = new(StringComparer.Ordinal);

    private readonly HttpListener _listener;
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _acceptLoop;

    public string BaseUrl { get; }

    public LocalTestHttpServer()
    {
        (_listener, BaseUrl) = StartOnFreeLoopbackPort();
        _acceptLoop = Task.Run(AcceptLoopAsync);
    }

    /// <summary>
    /// A started listener on a free loopback port and its base URL. The IP literal 127.0.0.1 is used
    /// for the reservation, the listener prefix and the client URL so all three share one address
    /// family: "localhost" binds IPv6-only on macOS but resolves IPv4-first on Linux, which would
    /// leave the client connecting to an unbound family. Binding to a freed ephemeral port is racy
    /// (HttpListener cannot bind port 0), so a lost race is retried.
    /// </summary>
    public static (HttpListener Listener, string BaseUrl) StartOnFreeLoopbackPort()
    {
        const int MAX_ATTEMPTS = 5;

        for (int attempt = 1; attempt <= MAX_ATTEMPTS; attempt++)
        {
            int port = ReserveFreeLoopbackPort();
            string baseUrl = $"http://127.0.0.1:{port}";
            var listener = new HttpListener();
            listener.Prefixes.Add($"{baseUrl}/");

            try
            {
                listener.Start();
                return (listener, baseUrl);
            }
            catch (HttpListenerException) when (attempt < MAX_ATTEMPTS)
            {
                listener.Close();
            }
        }

        throw new InvalidOperationException(
            $"Could not bind a loopback HTTP listener after {MAX_ATTEMPTS} attempts.");
    }

    /// <summary>A loopback port that is free now; another process can take it before the caller binds it.</summary>
    public static int ReserveFreeLoopbackPort()
    {
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        try
        {
            return ((IPEndPoint)probe.LocalEndpoint).Port;
        }
        finally
        {
            probe.Stop();
        }
    }

    private async Task AcceptLoopAsync()
    {
        while (!_cts.IsCancellationRequested)
        {
            HttpListenerContext context;
            try
            {
                context = await _listener.GetContextAsync();
            }
            catch (Exception) when (_cts.IsCancellationRequested)
            {
                return;
            }

            _ = Task.Run(() => RespondAsync(context));
        }
    }

    private async Task RespondAsync(HttpListenerContext context)
    {
        try
        {
            string path = context.Request.Url?.AbsolutePath ?? "/";
            _ = _pathHits.AddOrUpdate(path, 1, static (_, previous) => previous + 1);

            // ?setCookie=C on any path answers with a Set-Cookie header of C.
            if (context.Request.QueryString[SET_COOKIE_QUERY] is string cookie)
            {
                context.Response.AppendHeader("Set-Cookie", cookie);
            }

            if (string.Equals(path, REDIRECT_PATH, StringComparison.Ordinal))
            {
                RespondRedirect(context);
                return;
            }

            if (string.Equals(path, PDF_CONTENT_PATH, StringComparison.Ordinal))
            {
                await WriteAsync(context, 200, PDF_CONTENT, context.Request.QueryString[TYPE_QUERY] ?? "application/pdf");
                return;
            }

            if (string.Equals(path, PAGE_PATH, StringComparison.Ordinal))
            {
                await WriteAsync(context, 200, context.Request.QueryString[HTML_QUERY] ?? string.Empty, HTML_CONTENT_TYPE);
                return;
            }

            if (string.Equals(path, STATUS_500_PATH, StringComparison.Ordinal)
                && context.Request.QueryString[TIMES_QUERY] is null)
            {
                await WriteAsync(context, 500, "Internal Server Error");
                return;
            }

            if (path.StartsWith(STATUS_PATH_PREFIX, StringComparison.Ordinal)
                && int.TryParse(path[STATUS_PATH_PREFIX.Length..], out int scriptedStatus))
            {
                await RespondScriptedStatusAsync(context, scriptedStatus);
                return;
            }

            if (string.Equals(path, INFLIGHT_PATH, StringComparison.Ordinal))
            {
                await RespondInFlightProbeAsync(context);
                return;
            }

            if (string.Equals(path, DELAY_PATH, StringComparison.Ordinal))
            {
                int delayMs = int.TryParse(context.Request.QueryString["ms"], out int parsed) ? parsed : 1000;
                await SimulatedWork.TakesAsync(TimeSpan.FromMilliseconds(delayMs), _cts.Token);
                await WriteAsync(context, 200, "{}");
                return;
            }

            await WriteAsync(context, 200, await EchoJsonAsync(context.Request));
        }
        catch (Exception)
        {
            // The client (or the server itself, on shutdown) may abandon the request mid-flight;
            // a dead connection here is expected and must not crash the accept loop.
        }
    }

    /// <summary>
    /// The highest number of simultaneous requests the <c>/inflight</c> probe has seen under
    /// <paramref name="key"/>; zero when the key was never hit.
    /// </summary>
    public int MaxInFlight(string key)
    {
        return _maxInFlight.TryGetValue(key, out int max) ? max : 0;
    }

    /// <summary>
    /// How many requests reached <paramref name="path"/>, whatever the query string.
    /// </summary>
    public int PathHits(string path)
    {
        return _pathHits.TryGetValue(path, out int hits) ? hits : 0;
    }

    /// <summary>
    /// How many requests a scripted <c>/status/{code}</c> path has received under <paramref name="key"/>.
    /// </summary>
    public int StatusHits(string key)
    {
        return _statusHits.TryGetValue(key, out int hits) ? hits : 0;
    }

    // /status/{code}?key=k&times=N&statuses=a,b,c&retryAfter=V&retryAfterDateSeconds=S
    // Returns {code} for the first N requests under key (every request when times is absent),
    // then 200. statuses overrides {code} per request in order (request n gets the nth entry,
    // 200 once the list is used up). retryAfter is sent verbatim as the Retry-After header;
    // retryAfterDateSeconds sends an RFC 1123 date that many seconds from now instead
    // (negative for a past date).
    private async Task RespondScriptedStatusAsync(HttpListenerContext context, int statusCode)
    {
        string key = context.Request.QueryString[KEY_QUERY] ?? string.Empty;
        int hits = _statusHits.AddOrUpdate(key, 1, static (_, previous) => previous + 1);
        string[] scriptedStatuses = (context.Request.QueryString[STATUSES_QUERY] ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (scriptedStatuses.Length > 0)
        {
            if (hits > scriptedStatuses.Length || !int.TryParse(scriptedStatuses[hits - 1], out statusCode))
            {
                await WriteAsync(context, 200, await EchoJsonAsync(context.Request));
                return;
            }
        }
        else if (int.TryParse(context.Request.QueryString[TIMES_QUERY], out int times) && hits > times)
        {
            await WriteAsync(context, 200, await EchoJsonAsync(context.Request));
            return;
        }

        string? retryAfter = context.Request.QueryString[RETRY_AFTER_QUERY];

        if (retryAfter is not null)
        {
            context.Response.Headers[RETRY_AFTER_HEADER] = retryAfter;
        }
        else if (int.TryParse(context.Request.QueryString[RETRY_AFTER_DATE_SECONDS_QUERY], out int dateSeconds))
        {
            context.Response.Headers[RETRY_AFTER_HEADER] =
                DateTimeOffset.UtcNow.AddSeconds(dateSeconds).ToString("R", CultureInfo.InvariantCulture);
        }

        await WriteAsync(context, statusCode, $$"""{"status":{{statusCode}}}""");
    }

    // /redirect?to=URL&status=N answers N (302 when absent) with a Location header of URL.
    // /page?html=H answers H as an HTML page.
    // /pdf-content?type=T answers the start of a PDF, labelled T (application/pdf when absent).
    private static void RespondRedirect(HttpListenerContext context)
    {
        context.Response.StatusCode = int.TryParse(context.Request.QueryString[STATUS_QUERY], out int status)
            ? status
            : (int)HttpStatusCode.Found;
        context.Response.RedirectLocation = context.Request.QueryString[TO_QUERY];
        context.Response.Close();
    }

    // /inflight?key=k&holdMs=N holds the request open for N ms while counting how many requests
    // under key are open at once, so a test can read the high-water mark.
    private async Task RespondInFlightProbeAsync(HttpListenerContext context)
    {
        string key = context.Request.QueryString[KEY_QUERY] ?? string.Empty;
        int holdMs = int.TryParse(context.Request.QueryString[HOLD_MS_QUERY], out int parsed) ? parsed : DEFAULT_HOLD_MS;
        int now = _inFlight.AddOrUpdate(key, 1, static (_, previous) => previous + 1);
        _ = _maxInFlight.AddOrUpdate(key, now, (_, previous) => Math.Max(previous, now));

        try
        {
            await SimulatedWork.TakesAsync(TimeSpan.FromMilliseconds(holdMs), _cts.Token);
        }
        finally
        {
            // Leave before the response is flushed: once the client has the response it may
            // release its slot and the next request must not overlap this one's count.
            _ = _inFlight.AddOrUpdate(key, 0, static (_, previous) => previous - 1);
        }

        await WriteAsync(context, 200, $$"""{"inFlight":{{now}}}""");
    }

    private static async Task<string> EchoJsonAsync(HttpListenerRequest request)
    {
        using var reader = new StreamReader(request.InputStream, request.ContentEncoding);
        string body = await reader.ReadToEndAsync();

        var headers = request.Headers.AllKeys
            .Where(key => key is not null)
            .ToDictionary(key => key!, key => request.Headers[key] ?? string.Empty, StringComparer.Ordinal);

        return JsonSerializer.Serialize(new
        {
            method = request.HttpMethod,
            path = request.Url?.AbsolutePath ?? "/",
            headers,
            body,
        });
    }

    private static async Task WriteAsync(HttpListenerContext context, int statusCode, string body, string contentType = JSON_CONTENT_TYPE)
    {
        byte[] payload = Encoding.UTF8.GetBytes(body);
        context.Response.StatusCode = statusCode;
        context.Response.ContentType = contentType;
        context.Response.ContentLength64 = payload.Length;
        await context.Response.OutputStream.WriteAsync(payload);
        context.Response.Close();
    }

    public void Dispose()
    {
        _cts.Cancel();
        _listener.Close();

        try
        {
            _ = _acceptLoop.Wait(TimeSpan.FromSeconds(2));
        }
        catch (Exception)
        {
            // The accept loop unwinds via the cancellation above; nothing to recover here.
        }

        _cts.Dispose();
    }
}
