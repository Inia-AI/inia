using System.Net;
using IniaAi.Common.Http;
using IniaAi.Testing;

namespace IniaAi.Common.Tests.Http;

public sealed class OutboundCookieTests : IDisposable
{
    private readonly LocalTestHttpServer _site = new();
    private readonly OutboundAddressGuard _guard = OutboundAddressGuard.For(["127.0.0.1"]);

    public void Dispose()
    {
        _site.Dispose();
    }

    [Fact]
    public async Task CallersOfTheSharedHandler_KeepTheirCookiesApart()
    {
        var first = new CookieContainer();
        var second = new CookieContainer();

        _ = await GetAsync(_guard.SharedHandlerWith(first), "/login?setCookie=session%3Dfirst");

        Assert.DoesNotContain("session=first", await GetAsync(_guard.SharedHandlerWith(second), "/echo"));
        Assert.Contains("session=first", await GetAsync(_guard.SharedHandlerWith(first), "/echo"));
    }

    [Fact]
    public async Task ACookieSetOnARedirectHop_IsSentOnTheNextHop()
    {
        string landed = await GetAsync(
            _guard.SharedHandlerWith(new CookieContainer()),
            $"/redirect?setCookie=hop%3D1&to={Uri.EscapeDataString("/landed")}");

        Assert.Contains("/landed", landed);
        Assert.Contains("hop=1", landed);
    }

    [Fact]
    public async Task ACookieForOneHop_IsNotCarriedToTheNext()
    {
        var jar = new CookieContainer();
        jar.Add(new Cookie("scoped", "1", "/redirect", "127.0.0.1"));

        string landed = await GetAsync(_guard.SharedHandlerWith(jar), $"/redirect?to={Uri.EscapeDataString("/landed")}");

        Assert.Contains("/landed", landed);
        Assert.DoesNotContain("scoped=1", landed);
    }

    [Fact]
    public async Task AnOwnedHandler_KeepsItsOwnCookies()
    {
        using HttpMessageHandler owned = _guard.CreateHandler();

        _ = await GetAsync(owned, "/login?setCookie=owned%3D1");

        Assert.Contains("owned=1", await GetAsync(owned, "/echo"));
        Assert.DoesNotContain("owned=1", await GetAsync(_guard.SharedHandlerWith(new CookieContainer()), "/echo"));
    }

    private async Task<string> GetAsync(HttpMessageHandler handler, string pathAndQuery)
    {
        using var client = new HttpClient(handler, disposeHandler: false);
        return await client.GetStringAsync($"{_site.BaseUrl}{pathAndQuery}", TestContext.Current.CancellationToken);
    }
}
