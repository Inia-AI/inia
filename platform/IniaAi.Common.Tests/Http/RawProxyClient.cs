using System.Net.Sockets;
using System.Text;

namespace IniaAi.Common.Tests.Http;

/// <summary>
/// Speaks to a forward proxy the way a browser opens a tunnel: a raw <c>CONNECT</c>, then bytes.
/// </summary>
internal sealed class RawProxyClient : IDisposable
{
    private readonly TcpClient _client = new();
    private NetworkStream? _stream;

    public static async Task<RawProxyClient> ConnectAsync(Uri proxy)
    {
        var client = new RawProxyClient();
        await client._client.ConnectAsync(proxy.Host, proxy.Port, TestContext.Current.CancellationToken);
        client._stream = client._client.GetStream();
        return client;
    }

    public void Dispose()
    {
        _client.Dispose();
    }

    public Task SendConnectAsync(string authority)
    {
        return WriteAsync($"CONNECT {authority} HTTP/1.1\r\nHost: {authority}\r\n\r\n");
    }

    public Task WriteAsync(string text)
    {
        return _stream!.WriteAsync(Encoding.ASCII.GetBytes(text), TestContext.Current.CancellationToken).AsTask();
    }

    public async Task<string> ReadToEndAsync()
    {
        using var reader = new StreamReader(_stream!, Encoding.UTF8, leaveOpen: true);
        return await reader.ReadToEndAsync(TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// What arrives until <paramref name="terminator"/> has been read, or until the proxy closes.
    /// </summary>
    public async Task<string> ReadUntilAsync(string terminator)
    {
        var received = new StringBuilder();
        byte[] buffer = new byte[256];

        while (!received.ToString().Contains(terminator, StringComparison.Ordinal))
        {
            int read = await ReadOrEndAsync(buffer);

            if (read == 0)
            {
                break;
            }

            _ = received.Append(Encoding.ASCII.GetString(buffer, 0, read));
        }

        return received.ToString();
    }

    /// <summary>
    /// A write after the proxy closed draws a reset. Windows reports that reset to the next read,
    /// where Linux and macOS report the end of the stream first, so a reset counts as the close.
    /// </summary>
    private async Task<int> ReadOrEndAsync(byte[] buffer)
    {
        try
        {
            return await _stream!.ReadAsync(buffer, TestContext.Current.CancellationToken);
        }
        catch (IOException exception) when (exception.InnerException is SocketException { SocketErrorCode: SocketError.ConnectionReset })
        {
            return 0;
        }
    }
}
