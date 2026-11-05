using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace IniaAi.Services.FileDownload;

/// <summary>
/// Reports how Chrome runs here. It is Degraded only once a link from an email has skipped the
/// browser step because the sandbox does not start, so a host that never opens such a link stays
/// Healthy. Degraded still answers HTTP 200, so no container probe fails on it.
/// </summary>
public sealed class BrowserSandboxHealthCheck : IHealthCheck
{
    private const string NOT_STARTED = "Chrome has not started yet.";

    private readonly ChromeDriverPool _pool;

    public BrowserSandboxHealthCheck()
        : this(ChromeDriverPoolProvider.Instance)
    {
    }

    internal BrowserSandboxHealthCheck(ChromeDriverPool pool)
    {
        _pool = pool;
    }

    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        BrowserIsolation? isolation = _pool.KnownIsolation;
        int refusedPages = _pool.RefusedPages;

        HealthCheckResult result = isolation switch
        {
            null => HealthCheckResult.Healthy(NOT_STARTED),
            { Sandboxed: false } when refusedPages > 0 => HealthCheckResult.Degraded(
                $"{isolation.Description} {refusedPages} links from emails skipped the browser step."),
            _ => HealthCheckResult.Healthy(isolation.Description),
        };

        return Task.FromResult(result);
    }
}
