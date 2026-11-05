using System.Net;
using System.Net.Sockets;

namespace IniaAi.Common.Tests.Http;

public sealed class RawProxyClientTests
{
    [Fact]
    public async Task ReadUntil_EndsWhenTheProxyResetsTheConnection()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();

        try
        {
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            using RawProxyClient browser = await RawProxyClient.ConnectAsync(new Uri($"http://127.0.0.1:{port}"));
            using (TcpClient proxySide = await listener.AcceptTcpClientAsync(TestContext.Current.CancellationToken))
            {
                proxySide.LingerState = new LingerOption(true, 0);
            }

            string received = await browser.ReadUntilAsync("never-sent");

            Assert.Equal(string.Empty, received);
        }
        finally
        {
            listener.Stop();
        }
    }
}
