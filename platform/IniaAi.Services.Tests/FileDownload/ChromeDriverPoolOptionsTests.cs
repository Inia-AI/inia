using IniaAi.Services.FileDownload;
using OpenQA.Selenium.Chrome;
using System.Text.Json;

namespace IniaAi.Services.Tests.FileDownload;

public class ChromeDriverPoolOptionsTests
{
    private static readonly Uri _proxy = new("http://127.0.0.1:45678");

    [Theory]
    [InlineData("--no-sandbox")]
    [InlineData("--disable-web-security")]
    [InlineData("--allow-running-insecure-content")]
    [InlineData("--disable-blink-features=AutomationControlled")]
    [InlineData("--disable-client-side-phishing-detection")]
    [InlineData("--disable-automation")]
    [InlineData("--disable-infobars")]
    public void ChromeStartsWithItsSecurityOn(string removedSwitch)
    {
        ChromeOptions options = ChromeDriverPool.BuildOptions("downloads", _proxy, sandboxed: true);

        Assert.DoesNotContain(removedSwitch, options.Arguments);
    }

    [Fact]
    public void AChromeWithoutItsSandbox_IsStartedWithoutIt_AndKeepsTheRestOfItsSecurity()
    {
        ChromeOptions options = ChromeDriverPool.BuildOptions("downloads", _proxy, sandboxed: false);

        Assert.Contains("--no-sandbox", options.Arguments);
        Assert.DoesNotContain("--disable-web-security", options.Arguments);
        Assert.DoesNotContain("--allow-running-insecure-content", options.Arguments);
        Assert.Contains("--proxy-bypass-list=<-loopback>", options.Arguments);
    }

    [Fact]
    public void ChromeCarriesNoBotEvasion()
    {
        string capabilities = JsonSerializer.Serialize(
            ChromeDriverPool.BuildOptions("downloads", _proxy, sandboxed: true).ToCapabilities().GetCapability(new ChromeOptions().CapabilityName));

        Assert.Contains("--headless=new", capabilities);
        Assert.DoesNotContain("--user-agent", capabilities);
        Assert.DoesNotContain("enable-automation", capabilities);
        Assert.DoesNotContain("profile.default_content_settings.popups", capabilities);
    }

    [Fact]
    public void Chrome_SendsEveryRequestThroughTheProxy()
    {
        ChromeOptions options = ChromeDriverPool.BuildOptions("downloads", _proxy, sandboxed: true);

        Assert.Contains("--proxy-server=http://127.0.0.1:45678", options.Arguments);
        Assert.Contains("--proxy-bypass-list=<-loopback>", options.Arguments);
        Assert.Contains("--disable-quic", options.Arguments);
        Assert.Contains("disable_non_proxied_udp", JsonSerializer.Serialize(
            options.ToCapabilities().GetCapability(new ChromeOptions().CapabilityName)));
    }

    [Fact]
    public void Chrome_GivesUpOnAPageAtThePoolsPageLoadTimeout()
    {
        ChromeOptions options = ChromeDriverPool.BuildOptions("downloads", _proxy, sandboxed: true);

        Assert.Equal(ChromeDriverPool.PageLoadTimeout, options.PageLoadTimeout);
    }

    [Fact]
    public void TheServicesAssembly_EmbedsNoBrowserScripts()
    {
        string[] resources = typeof(ChromeDriverPool).Assembly.GetManifestResourceNames();

        Assert.DoesNotContain(resources, name =>
            name.EndsWith("stealth.min.js", StringComparison.Ordinal)
            || name.EndsWith("page-scroller.js", StringComparison.Ordinal)
            || name.EndsWith("page-modifications.js", StringComparison.Ordinal)
            || name.EndsWith("html-processor.js", StringComparison.Ordinal));
        Assert.Contains(resources, name => name.EndsWith("charts.global.js", StringComparison.Ordinal));
    }
}
