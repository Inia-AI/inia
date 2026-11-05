using System.Net;
using IniaAi.Common.Http;
using IniaAi.Testing;

namespace IniaAi.Common.Tests.Http;

public sealed class CheckedRedirectHandlerTests : IDisposable
{
    private const string LANDING_PATH = "/landed";

    private readonly LocalTestHttpServer _site = new();

    public void Dispose()
    {
        _site.Dispose();
    }

    [Fact]
    public async Task ARelativeLocation_IsFollowedOnTheSameHost()
    {
        using var client = new HttpClient(OutboundAddressGuard.For(["127.0.0.1"]).CreateHandler());

        using HttpResponseMessage response = await client.GetAsync(
            $"{_site.BaseUrl}/redirect?to={Uri.EscapeDataString(LANDING_PATH)}",
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1, _site.PathHits(LANDING_PATH));
    }

    [Fact]
    public async Task ARedirectFromHttpsToHttp_IsNotFollowed()
    {
        var network = new RedirectingNetwork(new Uri("http://reports.example/plain"));
        using var client = new HttpClient(new CheckedRedirectHandler(OutboundAddressGuard.For([], new WebProxy()), network));

        using HttpResponseMessage response = await client.GetAsync("https://reports.example/secure", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        Assert.Equal(["https://reports.example/secure"], network.Requests);
    }

    /// <summary>
    /// The network: every request is answered with a redirect to one location. An HTTPS server
    /// is out of reach of a test, so the network stands in for it.
    /// </summary>
    private sealed class RedirectingNetwork(Uri location) : HttpMessageHandler
    {
        public List<string> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request.RequestUri!.AbsoluteUri);
            var response = new HttpResponseMessage(HttpStatusCode.Found);
            response.Headers.Location = location;
            return Task.FromResult(response);
        }
    }
}
