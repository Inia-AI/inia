using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using IniaAi.Common.Http;
using IniaAi.Testing;

namespace IniaAi.Common.Tests.Http;

public sealed class CheckedForwardProxyTests : IDisposable
{
    private const string PAGE_PATH = "/through-proxy";

    private readonly LocalTestHttpServer _site = new();

    public void Dispose()
    {
        _site.Dispose();
    }

    [Fact]
    public async Task AnHttpRequestToARefusedAddress_IsAnsweredWithTheRefusalAndLogged()
    {
        var logger = new CapturingLogger<CheckedForwardProxyTests>();
        using CheckedForwardProxy proxy = OutboundAddressGuard.For([]).StartForwardProxy(logger);
        using HttpClient client = ClientThrough(proxy);

        using HttpResponseMessage response = await client.GetAsync($"{_site.BaseUrl}{PAGE_PATH}", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Contains(InternalAddresses.LOOPBACK, await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Equal(0, _site.PathHits(PAGE_PATH));
        Assert.Contains(logger.Entries, entry => entry.Message.Contains("127.0.0.1") && entry.Message.Contains(InternalAddresses.LOOPBACK));
    }

    [Fact]
    public async Task AnHttpRequestToAnAllowedHost_IsForwarded()
    {
        using CheckedForwardProxy proxy = OutboundAddressGuard.For(["127.0.0.1"])
            .StartForwardProxy(new CapturingLogger<CheckedForwardProxyTests>());
        using HttpClient client = ClientThrough(proxy);

        string body = await client.GetStringAsync($"{_site.BaseUrl}{PAGE_PATH}", TestContext.Current.CancellationToken);

        Assert.Contains(PAGE_PATH, body);
        Assert.Equal(1, _site.PathHits(PAGE_PATH));
    }

    [Fact]
    public async Task AnOfflineProxy_RefusesEveryRequest_EvenToAnAllowedHost()
    {
        var logger = new CapturingLogger<CheckedForwardProxyTests>();
        using CheckedForwardProxy proxy = CheckedForwardProxy.StartOffline(logger);
        using HttpClient client = ClientThrough(proxy);

        using HttpResponseMessage response = await client.GetAsync($"{_site.BaseUrl}{PAGE_PATH}", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Contains("no network access", await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Equal(0, _site.PathHits(PAGE_PATH));
        Assert.Contains(logger.Entries, entry => entry.Message.Contains("no network access") && entry.Message.Contains(PAGE_PATH));
    }

    [Fact]
    public async Task ATunnelToARefusedAddress_IsRefused()
    {
        using CheckedForwardProxy proxy = OutboundAddressGuard.For([]).StartForwardProxy(new CapturingLogger<CheckedForwardProxyTests>());
        using RawProxyClient browser = await RawProxyClient.ConnectAsync(proxy.Address);

        await browser.SendConnectAsync("169.254.169.254:443");
        string answer = await browser.ReadToEndAsync();

        Assert.StartsWith("HTTP/1.1 403", answer);
        Assert.Contains(InternalAddresses.LINK_LOCAL, answer);
    }

    [Fact]
    public async Task ATunnelToAnAllowedHost_CarriesBytesBothWays()
    {
        using var echo = new TcpListener(IPAddress.Loopback, 0);
        echo.Start();
        int echoPort = ((IPEndPoint)echo.LocalEndpoint).Port;
        Task echoing = EchoOnceAsync(echo);
        using CheckedForwardProxy proxy = OutboundAddressGuard.For(["127.0.0.1"])
            .StartForwardProxy(new CapturingLogger<CheckedForwardProxyTests>());
        using RawProxyClient browser = await RawProxyClient.ConnectAsync(proxy.Address);

        await browser.SendConnectAsync($"127.0.0.1:{echoPort}");
        string established = await browser.ReadUntilAsync("\r\n\r\n");
        await browser.WriteAsync("ping");
        string echoed = await browser.ReadUntilAsync("ping");

        Assert.StartsWith("HTTP/1.1 200", established);
        Assert.Equal("ping", echoed);
        await echoing;
    }

    [Fact]
    public async Task AConnection_CarriesOneRequest_EvenWhenTheServerKeepsItOpen()
    {
        using var server = new KeepAliveServer();
        using CheckedForwardProxy proxy = OutboundAddressGuard.For(["127.0.0.1"])
            .StartForwardProxy(new CapturingLogger<CheckedForwardProxyTests>());
        using RawProxyClient browser = await RawProxyClient.ConnectAsync(proxy.Address);

        await browser.WriteAsync($"GET {server.BaseUrl}/first HTTP/1.1\r\nHost: {server.Authority}\r\n\r\n");
        string first = await browser.ReadUntilAsync("answer-1");
        await browser.WriteAsync("GET http://169.254.169.254/second HTTP/1.1\r\nHost: 169.254.169.254\r\n\r\n");
        string afterSecond = await browser.ReadUntilAsync("answer-2");

        Assert.Contains("answer-1", first);
        Assert.DoesNotContain("answer-2", afterSecond);
        Assert.Equal(["GET /first HTTP/1.1"], server.Heads.Select(head => head.Split("\r\n")[0]));
    }

    [Fact]
    public async Task ARequestBody_IsForwarded()
    {
        using var server = new KeepAliveServer();
        using CheckedForwardProxy proxy = OutboundAddressGuard.For(["127.0.0.1"])
            .StartForwardProxy(new CapturingLogger<CheckedForwardProxyTests>());
        using RawProxyClient browser = await RawProxyClient.ConnectAsync(proxy.Address);

        await browser.WriteAsync($"POST {server.BaseUrl}/form HTTP/1.1\r\nHost: {server.Authority}\r\nContent-Length: 11\r\n\r\nhello");
        await browser.WriteAsync(" world");
        _ = await browser.ReadUntilAsync("answer-1");

        Assert.Equal(["hello world"], server.Bodies);
    }

    [Fact]
    public async Task AHeaderThatTheConnectionHeaderNames_IsNotForwarded()
    {
        using var server = new KeepAliveServer();
        using CheckedForwardProxy proxy = OutboundAddressGuard.For(["127.0.0.1"])
            .StartForwardProxy(new CapturingLogger<CheckedForwardProxyTests>());
        using RawProxyClient browser = await RawProxyClient.ConnectAsync(proxy.Address);

        await browser.WriteAsync(
            $"GET {server.BaseUrl}/page HTTP/1.1\r\nHost: {server.Authority}\r\nConnection: keep-alive, X-Hop\r\nX-Hop: secret\r\nX-Kept: yes\r\n\r\n");
        _ = await browser.ReadUntilAsync("answer-1");

        string head = Assert.Single(server.Heads);
        Assert.DoesNotContain("X-Hop", head);
        Assert.Contains("X-Kept: yes", head);
    }

    private static HttpClient ClientThrough(CheckedForwardProxy proxy)
    {
        return new HttpClient(new SocketsHttpHandler
        {
            Proxy = new WebProxy(proxy.Address) { BypassProxyOnLocal = false },
            UseProxy = true,
        });
    }

    private static async Task EchoOnceAsync(TcpListener listener)
    {
        using TcpClient accepted = await listener.AcceptTcpClientAsync(TestContext.Current.CancellationToken);
        NetworkStream stream = accepted.GetStream();
        byte[] buffer = new byte[4];
        int read = await stream.ReadAtLeastAsync(buffer, buffer.Length, cancellationToken: TestContext.Current.CancellationToken);
        await stream.WriteAsync(buffer.AsMemory(0, read), TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// A server that keeps every connection open and answers each request on it, whatever the
    /// request's <c>Connection</c> header says.
    /// </summary>
    private sealed class KeepAliveServer : IDisposable
    {
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
        private readonly ConcurrentQueue<string> _heads = new();
        private readonly ConcurrentQueue<string> _bodies = new();
        private int _answers;

        public KeepAliveServer()
        {
            _listener.Start();
            Authority = $"127.0.0.1:{((IPEndPoint)_listener.LocalEndpoint).Port}";
            _ = AcceptAsync();
        }

        public string Authority { get; }

        public string BaseUrl => $"http://{Authority}";

        public IReadOnlyCollection<string> Heads => _heads;

        public IReadOnlyCollection<string> Bodies => _bodies;

        public void Dispose()
        {
            _listener.Stop();
        }

        private async Task AcceptAsync()
        {
            while (true)
            {
                TcpClient client;

                try
                {
                    client = await _listener.AcceptTcpClientAsync();
                }
                catch (Exception)
                {
                    return;
                }

                _ = AnswerEveryRequestAsync(client);
            }
        }

        private async Task AnswerEveryRequestAsync(TcpClient client)
        {
            using TcpClient owned = client;
            NetworkStream stream = client.GetStream();
            var received = new List<byte>();
            byte[] buffer = new byte[1024];

            try
            {
                while (true)
                {
                    int headEnd;

                    while ((headEnd = IndexOfHeadEnd(received)) < 0)
                    {
                        int read = await stream.ReadAsync(buffer);

                        if (read == 0)
                        {
                            return;
                        }

                        received.AddRange(buffer.AsSpan(0, read));
                    }

                    string head = Encoding.ASCII.GetString([.. received.Take(headEnd)]);
                    received.RemoveRange(0, headEnd + 4);
                    int bodyLength = ContentLength(head);

                    while (received.Count < bodyLength)
                    {
                        int read = await stream.ReadAsync(buffer);

                        if (read == 0)
                        {
                            return;
                        }

                        received.AddRange(buffer.AsSpan(0, read));
                    }

                    _heads.Enqueue(head);

                    if (bodyLength > 0)
                    {
                        _bodies.Enqueue(Encoding.ASCII.GetString([.. received.Take(bodyLength)]));
                        received.RemoveRange(0, bodyLength);
                    }

                    string body = $"answer-{Interlocked.Increment(ref _answers)}";
                    await stream.WriteAsync(Encoding.ASCII.GetBytes($"HTTP/1.1 200 OK\r\nContent-Length: {body.Length}\r\n\r\n{body}"));
                }
            }
            catch (IOException)
            {
            }
        }

        private static int IndexOfHeadEnd(List<byte> received)
        {
            return CollectionsMarshal.AsSpan(received).IndexOf("\r\n\r\n"u8);
        }

        private static int ContentLength(string head)
        {
            string? line = head.Split("\r\n").FirstOrDefault(line => line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase));

            return line is null ? 0 : int.Parse(line.Split(':', 2)[1].Trim());
        }
    }
}
