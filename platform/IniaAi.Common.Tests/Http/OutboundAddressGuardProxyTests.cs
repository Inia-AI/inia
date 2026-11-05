using System.Net;
using System.Net.Sockets;
using System.Text;
using IniaAi.Common.Http;
using IniaAi.Testing;

namespace IniaAi.Common.Tests.Http;

/// <summary>
/// A deployment whose egress goes through a proxy (HTTP_PROXY): the proxy resolves the target, so
/// the guard judges the target before the request leaves, and connects to the proxy itself even
/// when the proxy has an internal address.
/// </summary>
public sealed class OutboundAddressGuardProxyTests : IDisposable
{
    private const string UNRESOLVABLE_HOST = "reports.eng718.invalid";

    private readonly TcpListener _upstream = new(IPAddress.Loopback, 0);
    private readonly List<string> _requestLines = [];
    private readonly Uri _upstreamUri;

    public OutboundAddressGuardProxyTests()
    {
        _upstream.Start();
        _upstreamUri = new Uri($"http://127.0.0.1:{((IPEndPoint)_upstream.LocalEndpoint).Port}");
        _ = ServeAsync();
    }

    public void Dispose()
    {
        _upstream.Stop();
    }

    [Fact]
    public async Task BehindAProxy_ANameThePlatformCannotResolve_IsLeftToTheProxy()
    {
        using var client = new HttpClient(Guard().CreateHandler());

        string body = await client.GetStringAsync($"http://{UNRESOLVABLE_HOST}/report.pdf", TestContext.Current.CancellationToken);

        Assert.Equal("from-upstream", body);
        Assert.Contains($"GET http://{UNRESOLVABLE_HOST}/report.pdf HTTP/1.1", _requestLines);
    }

    [Fact]
    public async Task BehindAProxy_AnInternalAddress_IsRefusedBeforeTheProxySeesIt()
    {
        using var client = new HttpClient(Guard().CreateHandler());

        OutboundAddressRefusedException refusal = await Assert.ThrowsAsync<OutboundAddressRefusedException>(
            () => client.GetStringAsync("http://10.20.30.40/report.pdf", TestContext.Current.CancellationToken));

        Assert.Equal(InternalAddresses.PRIVATE, refusal.Rule);
        Assert.Empty(_requestLines);
    }

    [Fact]
    public async Task WithoutAProxy_ANameThatDoesNotResolve_FailsAsANetworkError()
    {
        using var client = new HttpClient(OutboundAddressGuard.For([]).CreateHandler());

        _ = await Assert.ThrowsAnyAsync<Exception>(
            () => client.GetStringAsync($"http://{UNRESOLVABLE_HOST}/report.pdf", TestContext.Current.CancellationToken));

        Assert.Empty(_requestLines);
    }

    [Fact]
    public async Task ABrowserTunnel_GoesOnThroughTheUpstreamProxy()
    {
        using CheckedForwardProxy proxy = Guard().StartForwardProxy(new CapturingLogger<OutboundAddressGuardProxyTests>());
        using RawProxyClient browser = await RawProxyClient.ConnectAsync(proxy.Address);

        await browser.SendConnectAsync($"{UNRESOLVABLE_HOST}:443");
        string answer = await browser.ReadUntilAsync("\r\n\r\n");

        Assert.StartsWith("HTTP/1.1 200", answer);
        Assert.Contains($"CONNECT {UNRESOLVABLE_HOST}:443 HTTP/1.1", _requestLines);
    }

    [Fact]
    public async Task ABrowserTunnelToAnIPv6Address_NamesTheAddressInBrackets()
    {
        using CheckedForwardProxy proxy = Guard().StartForwardProxy(new CapturingLogger<OutboundAddressGuardProxyTests>());
        using RawProxyClient browser = await RawProxyClient.ConnectAsync(proxy.Address);

        await browser.SendConnectAsync("[2606:4700:4700::1111]:443");
        string answer = await browser.ReadUntilAsync("\r\n\r\n");

        Assert.StartsWith("HTTP/1.1 200", answer);
        Assert.Contains("CONNECT [2606:4700:4700::1111]:443 HTTP/1.1", _requestLines);
    }

    private OutboundAddressGuard Guard()
    {
        return OutboundAddressGuard.For([], new WebProxy(_upstreamUri));
    }

    private async Task ServeAsync()
    {
        while (true)
        {
            TcpClient client;

            try
            {
                client = await _upstream.AcceptTcpClientAsync();
            }
            catch (Exception)
            {
                return;
            }

            _ = AnswerAsync(client);
        }
    }

    private async Task AnswerAsync(TcpClient client)
    {
        using TcpClient owned = client;
        NetworkStream stream = client.GetStream();
        var head = new StringBuilder();
        byte[] buffer = new byte[1024];

        while (!head.ToString().Contains("\r\n\r\n", StringComparison.Ordinal))
        {
            int read = await stream.ReadAsync(buffer);

            if (read == 0)
            {
                return;
            }

            _ = head.Append(Encoding.ASCII.GetString(buffer, 0, read));
        }

        string requestLine = head.ToString().Split("\r\n")[0];

        lock (_requestLines)
        {
            _requestLines.Add(requestLine);
        }

        string answer = requestLine.StartsWith("CONNECT", StringComparison.Ordinal)
            ? "HTTP/1.1 200 Connection Established\r\n\r\n"
            : "HTTP/1.1 200 OK\r\nContent-Length: 13\r\nConnection: close\r\n\r\nfrom-upstream";
        await stream.WriteAsync(Encoding.ASCII.GetBytes(answer));
        await stream.FlushAsync();
    }
}
