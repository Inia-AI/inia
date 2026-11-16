using System.Net;
using IniaAi.Common.Http;
using IniaAi.Testing;

namespace IniaAi.Common.Tests.Http;

public sealed class CheckedRedirectHandlerTests : IDisposable
{
    private const string LANDING_PATH = "/landed";
    private const string API_KEY_HEADER = "x-api-key";

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

    [Fact]
    public async Task ARedirectToAnInternalAddress_IsRefused()
    {
        using var client = new HttpClient(OutboundAddressGuard.For(["127.0.0.1"]).CreateHandler());

        OutboundAddressRefusedException refusal = await Assert.ThrowsAsync<OutboundAddressRefusedException>(() => client.GetAsync(
            $"{_site.BaseUrl}/redirect?to={Uri.EscapeDataString("http://127.0.0.2:9/report.pdf")}",
            TestContext.Current.CancellationToken));

        Assert.Equal(InternalAddresses.LOOPBACK, refusal.Rule);
        Assert.Contains("127.0.0.2", refusal.Message);
    }

    [Theory]
    [InlineData("ftp://127.0.0.1/report.pdf")]
    [InlineData("file:///etc/hostname")]
    public async Task ARedirectToAnotherScheme_IsNotFollowed(string location)
    {
        var network = new RedirectingNetwork(new Uri(location));
        using var client = new HttpClient(new CheckedRedirectHandler(OutboundAddressGuard.For([], new WebProxy()), network));

        using HttpResponseMessage response = await client.GetAsync("http://reports.example/start", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        Assert.Equal(["http://reports.example/start"], network.Requests);
    }

    [Theory]
    [InlineData(HttpStatusCode.Found)]
    [InlineData(HttpStatusCode.TemporaryRedirect)]
    public async Task ARedirectToAnotherHost_CarriesNoHeaderTheCallerSet_ButAccept(HttpStatusCode status)
    {
        var network = new OneRedirectNetwork(new Uri("https://elsewhere.example/landed"), status);
        using var client = new HttpClient(new CheckedRedirectHandler(OutboundAddressGuard.For([], new WebProxy()), network));
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://api.example/start");
        request.Headers.Add(API_KEY_HEADER, "stored key");
        request.Headers.Add("X-Trace", "run 7");
        request.Headers.Accept.ParseAdd("application/json");

        using HttpResponseMessage response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(["https://api.example/start", "https://elsewhere.example/landed"], network.Requests);
        Assert.Contains(API_KEY_HEADER, network.HeadersSent[0]);
        Assert.Equal(["Accept"], network.HeadersSent[1]);
    }

    [Fact]
    public async Task ARedirectOnTheSameHost_KeepsTheHeadersTheCallerSet()
    {
        var network = new OneRedirectNetwork(new Uri("https://api.example/landed"), HttpStatusCode.Found);
        using var client = new HttpClient(new CheckedRedirectHandler(OutboundAddressGuard.For([], new WebProxy()), network));
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://api.example/start");
        request.Headers.Add(API_KEY_HEADER, "stored key");

        using HttpResponseMessage response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(API_KEY_HEADER, network.HeadersSent[1]);
    }

    [Fact]
    public async Task ARequestToAnAddressOutsideTheCallersAllowedSet_IsRefused_BeforeItLeaves()
    {
        var network = new OneRedirectNetwork(new Uri("https://api.example/landed"), HttpStatusCode.Found);
        using var client = new HttpClient(new AllowedOriginsHandler(
            new HashSet<string>(["https://api.example"], StringComparer.OrdinalIgnoreCase),
            new CheckedRedirectHandler(OutboundAddressGuard.For([], new WebProxy()), network)));

        OutboundOriginRefusedException refusal = await Assert.ThrowsAsync<OutboundOriginRefusedException>(
            () => client.GetAsync("https://elsewhere.example/start", TestContext.Current.CancellationToken));

        Assert.Equal("https://elsewhere.example", refusal.Origin);
        Assert.Empty(network.Requests);
    }

    [Fact]
    public async Task ARedirectToAnAddressOutsideTheCallersAllowedSet_IsRefused_AtThatHop()
    {
        var network = new OneRedirectNetwork(new Uri("https://elsewhere.example/landed"), HttpStatusCode.TemporaryRedirect);
        using var client = new HttpClient(new AllowedOriginsHandler(
            new HashSet<string>(["https://API.example"], StringComparer.OrdinalIgnoreCase),
            new CheckedRedirectHandler(OutboundAddressGuard.For([], new WebProxy()), network)));

        OutboundOriginRefusedException refusal = await Assert.ThrowsAsync<OutboundOriginRefusedException>(
            () => client.GetAsync("https://api.example/start", TestContext.Current.CancellationToken));

        Assert.Equal("https://elsewhere.example", refusal.Origin);
        Assert.Equal(["https://api.example/start"], network.Requests);
    }

    [Fact]
    public async Task ARedirectWithinTheCallersAllowedSet_IsFollowed()
    {
        var network = new OneRedirectNetwork(new Uri("https://files.example/landed"), HttpStatusCode.Found);
        using var client = new HttpClient(new AllowedOriginsHandler(
            new HashSet<string>(["https://api.example", "https://files.example"], StringComparer.OrdinalIgnoreCase),
            new CheckedRedirectHandler(OutboundAddressGuard.For([], new WebProxy()), network)));

        using HttpResponseMessage response = await client.GetAsync("https://api.example/start", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(["https://api.example/start", "https://files.example/landed"], network.Requests);
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

    /// <summary>
    /// The network: the first request is answered with one redirect, every later one with 200, and
    /// each request's header names are kept.
    /// </summary>
    private sealed class OneRedirectNetwork(Uri location, HttpStatusCode status) : HttpMessageHandler
    {
        public List<string> Requests { get; } = [];

        public List<List<string>> HeadersSent { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request.RequestUri!.AbsoluteUri);
            HeadersSent.Add([.. request.Headers.Select(header => header.Key)]);
            if (Requests.Count > 1)
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
            }

            var response = new HttpResponseMessage(status);
            response.Headers.Location = location;
            return Task.FromResult(response);
        }
    }
}
