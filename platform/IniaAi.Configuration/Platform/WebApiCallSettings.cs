using System.ComponentModel.DataAnnotations;

namespace IniaAi.Configuration.Platform;

/// <summary>
/// Deployment-static limits for the WebApiCall block's outbound HTTP calls. The concurrency
/// values bound a per-process gate: every WebApiCall block in this process that targets the
/// same host (and, when the request carries a credential header, the same credential) shares
/// one semaphore of this size, so parallel blocks stop manufacturing their own 429s. Several
/// engine processes (WebApp and WebApi, scaled-out instances) each carry their own gate.
/// </summary>
public class WebApiCallSettings
{
    public const string SECTION_NAME = "PlatformSettings:WebApiCall";

    public const string PERPLEXITY_HOST = "api.perplexity.ai";

    public const int DEFAULT_MAX_CONCURRENT_REQUESTS_PER_HOST = 8;

    /// <summary>
    /// Perplexity publishes per-key QPS by usage tier (1 at Tier 0, 3 at Tier 1, 8 at Tier 2)
    /// and no explicit concurrency figure; a chat completion takes several seconds, so three
    /// in-flight requests stay under the Tier 1 rate.
    /// </summary>
    public const int PERPLEXITY_MAX_CONCURRENT_REQUESTS = 3;

    /// <summary>
    /// In-flight requests allowed per host (per credential) when the host has no entry in
    /// <see cref="MaxConcurrentRequestsByHost"/>.
    /// </summary>
    [Range(1, int.MaxValue)]
    public int MaxConcurrentRequestsPerHost { get; set; } = DEFAULT_MAX_CONCURRENT_REQUESTS_PER_HOST;

    /// <summary>
    /// Per-host overrides, keyed by host name (case-insensitive). Perplexity is seeded so a
    /// deployment with no configuration still protects the one key its corporate workflows share.
    /// </summary>
    public Dictionary<string, int> MaxConcurrentRequestsByHost { get; set; } =
        new(StringComparer.OrdinalIgnoreCase)
        {
            [PERPLEXITY_HOST] = PERPLEXITY_MAX_CONCURRENT_REQUESTS,
        };

    /// <summary>
    /// The concurrency limit that applies to <paramref name="host"/>: its override when one is
    /// configured and positive, else <see cref="MaxConcurrentRequestsPerHost"/>, never below 1.
    /// </summary>
    public int ResolveMaxConcurrentRequests(string host)
    {
        // Scanned rather than looked up so a dictionary the configuration binder rebuilt with
        // the default (case-sensitive) comparer still matches a host written in another case.
        foreach (KeyValuePair<string, int> entry in MaxConcurrentRequestsByHost)
        {
            if (entry.Key.Equals(host, StringComparison.OrdinalIgnoreCase) && entry.Value > 0)
            {
                return entry.Value;
            }
        }

        return Math.Max(1, MaxConcurrentRequestsPerHost);
    }
}
