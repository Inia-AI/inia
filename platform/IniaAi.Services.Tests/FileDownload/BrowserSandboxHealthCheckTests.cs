using IniaAi.Common.Http;
using IniaAi.Services.FileDownload;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging.Abstractions;

namespace IniaAi.Services.Tests.FileDownload;

public class BrowserSandboxHealthCheckTests
{
    private const string WITHOUT_SANDBOX = "Chrome runs without its sandbox here.";

    [Fact]
    public async Task BeforeChromeStarts_TheCheckIsHealthy()
    {
        using var pool = new ChromeDriverPool(1);

        HealthCheckResult result = await CheckAsync(pool);

        Assert.Equal(HealthStatus.Healthy, result.Status);
    }

    [Fact]
    public async Task ASandboxedChrome_IsHealthy()
    {
        using var pool = new ChromeDriverPool(1, new BrowserIsolation(true, "app", "Chrome runs as the user app with its sandbox on."));

        HealthCheckResult result = await CheckAsync(pool);

        Assert.Equal(HealthStatus.Healthy, result.Status);
        Assert.Equal("Chrome runs as the user app with its sandbox on.", result.Description);
    }

    [Fact]
    public async Task AChromeWithoutItsSandbox_IsHealthyUntilALinkSkipsTheBrowser_ThenDegraded()
    {
        using var pool = new ChromeDriverPool(1, new BrowserIsolation(false, "app", WITHOUT_SANDBOX));

        HealthCheckResult before = await CheckAsync(pool);
        _ = await Assert.ThrowsAsync<BrowserSandboxUnavailableException>(
            () => pool.AcquirePageDriverAsync("downloads", OutboundAddressGuard.For([]), NullLogger.Instance, TestContext.Current.CancellationToken));
        HealthCheckResult after = await CheckAsync(pool);

        Assert.Equal(HealthStatus.Healthy, before.Status);
        Assert.Equal(HealthStatus.Degraded, after.Status);
        Assert.Contains(WITHOUT_SANDBOX, after.Description);
        Assert.Contains("1 links from emails skipped the browser step", after.Description);
    }

    private static Task<HealthCheckResult> CheckAsync(ChromeDriverPool pool)
    {
        return new BrowserSandboxHealthCheck(pool).CheckHealthAsync(new HealthCheckContext(), TestContext.Current.CancellationToken);
    }
}
