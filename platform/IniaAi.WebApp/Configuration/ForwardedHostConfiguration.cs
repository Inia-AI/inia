using System.Net;
using IniaAi.Configuration.Security;
using Microsoft.AspNetCore.HttpOverrides;

namespace IniaAi.WebApp.Configuration;

/// <summary>
/// Lets the WebApp take its public host name from X-Forwarded-Host, for a reverse proxy that
/// reaches it under another name (e.g. a Teleport application whose address is http://localhost:5118),
/// so URLs the app builds from the request host, such as the OIDC redirect_uri, name the address the
/// browser is on.
/// </summary>
public static class ForwardedHostConfiguration
{
    // The key the ASP.NET host reads ASPNETCORE_FORWARDEDHEADERS_ENABLED under when it decides
    // whether to run the forwarded-headers middleware at all.
    private const string FORWARDED_HEADERS_ENABLED_KEY = "ForwardedHeaders_Enabled";
    private const string ALLOWED_HOSTS_KEY = $"{ForwardedHostSettings.SectionName}:{nameof(ForwardedHostSettings.AllowedHosts)}";

    // ForwardedHeadersMiddleware reads each of these entries as "accept any host".
    private static readonly string[] _anyHostPatterns = ["*", "[::]", "0.0.0.0"];

    /// <summary>
    /// Adds X-Forwarded-Host, restricted to <see cref="ForwardedHostSettings.AllowedHosts"/>, to the
    /// forwarded headers that ASPNETCORE_FORWARDEDHEADERS_ENABLED=true turns on. With no allowed
    /// hosts, X-Forwarded-Host stays ignored.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// The allowed hosts could not take effect as written: ASPNETCORE_FORWARDEDHEADERS_ENABLED is not
    /// true, the list is set as one value, or an entry is not a single host name without a port.
    /// </exception>
    public static IServiceCollection AddForwardedHosts(this IServiceCollection services, IConfiguration configuration)
    {
        if (!string.IsNullOrEmpty(configuration[ALLOWED_HOSTS_KEY]))
        {
            throw new InvalidOperationException(
                $"{ALLOWED_HOSTS_KEY} is set as one value, which binds to an empty list. " +
                "Set each host as an indexed entry, e.g. ForwardedHeaders__AllowedHosts__0.");
        }

        ForwardedHostSettings settings = configuration.GetSection(ForwardedHostSettings.SectionName).Get<ForwardedHostSettings>()
            ?? new ForwardedHostSettings();

        // ForwardedHeadersOptions treats an empty AllowedHosts list as "any host".
        if (settings.AllowedHosts.Count == 0)
        {
            return services;
        }

        if (!ForwardedHeadersEnabled(configuration))
        {
            throw new InvalidOperationException(
                $"{ALLOWED_HOSTS_KEY} is set, but ASPNETCORE_FORWARDEDHEADERS_ENABLED is not true, so no " +
                "forwarded-headers middleware runs and the allowed hosts cannot take effect. " +
                "Set ASPNETCORE_FORWARDEDHEADERS_ENABLED=true on this service.");
        }

        foreach (string allowedHost in settings.AllowedHosts)
        {
            if (!IsSingleHostName(allowedHost))
            {
                throw new InvalidOperationException(
                    $"{ALLOWED_HOSTS_KEY} entry '{allowedHost}' is not a single host name without a port: " +
                    "'*', '[::]' and '0.0.0.0' would accept any forwarded host, and an entry with a port or " +
                    "surrounding whitespace never matches.");
            }
        }

        // PostConfigure, because the host's own ASPNETCORE_FORWARDEDHEADERS_ENABLED setup assigns
        // ForwardedHeaders outright and would erase the flag if it ran after this.
        return services.PostConfigure<ForwardedHeadersOptions>(options =>
        {
            options.ForwardedHeaders |= ForwardedHeaders.XForwardedHost;
            options.AllowedHosts = settings.AllowedHosts;
        });
    }

    /// <summary>
    /// Whether ASPNETCORE_FORWARDEDHEADERS_ENABLED is true, compared exactly as the host compares it,
    /// so the two agree on what "true" means.
    /// </summary>
    public static bool ForwardedHeadersEnabled(IConfiguration configuration)
    {
        return string.Equals(configuration[FORWARDED_HEADERS_ENABLED_KEY], "true", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Once the deployment lists its proxies under <c>ForwardedHeaders:KnownNetworks</c> or
    /// <c>KnownProxies</c>, the forwarded headers that ASPNETCORE_FORWARDEDHEADERS_ENABLED=true turns
    /// on count only from loopback and those senders, up to ten hops, as in the API. With no list, the
    /// host's own setup stays: it trusts any sender for one hop, which a deployment behind one load
    /// balancer relies on for the scheme of its sign-in redirect.
    /// </summary>
    public static IServiceCollection AddTrustedProxies(this IServiceCollection services, IConfiguration configuration)
    {
        TrustedProxies trusted = TrustedProxies.From(configuration);
        if (trusted.IsEmpty)
        {
            return services;
        }

        // PostConfigure, because the host's own ASPNETCORE_FORWARDEDHEADERS_ENABLED setup clears both lists.
        return services.PostConfigure<ForwardedHeadersOptions>(options =>
        {
            options.KnownIPNetworks.Clear();
            options.KnownProxies.Clear();
            options.KnownIPNetworks.Add(new System.Net.IPNetwork(IPAddress.Loopback, 8));
            options.KnownProxies.Add(IPAddress.IPv6Loopback);
            options.ForwardLimit = TrustedProxies.MAX_HOPS;

            foreach (System.Net.IPNetwork network in trusted.Networks)
            {
                options.KnownIPNetworks.Add(network);
            }

            foreach (IPAddress proxy in trusted.Proxies)
            {
                options.KnownProxies.Add(proxy);
            }
        });
    }

    private static bool IsSingleHostName(string allowedHost)
    {
        return !string.IsNullOrWhiteSpace(allowedHost)
            && allowedHost.Trim() == allowedHost
            && !_anyHostPatterns.Contains(allowedHost)
            && new HostString(allowedHost).Port is null;
    }
}
