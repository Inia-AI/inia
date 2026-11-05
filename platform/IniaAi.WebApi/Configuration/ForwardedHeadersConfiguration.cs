using IniaAi.Configuration.Security;
using System.Net;
using Microsoft.AspNetCore.HttpOverrides;
using IPNetwork = System.Net.IPNetwork;

namespace IniaAi.WebApi.Configuration;

/// <summary>
/// Lets the API see the client's address and scheme behind the proxies a deployment names, so the
/// sign-in limits, the second-factor token and the trusted-device record count the client and not
/// the proxy. A forwarded header from any other sender is ignored, so a client cannot pick the
/// address its limits count against.
/// </summary>
public static class ForwardedHeadersConfiguration
{
    /// <summary>The setting that lists the proxy networks, in CIDR form.</summary>
    public const string KNOWN_NETWORKS_KEY = TrustedProxies.KNOWN_NETWORKS_KEY;
    /// <summary>The setting that lists single proxy addresses.</summary>
    public const string KNOWN_PROXIES_KEY = TrustedProxies.KNOWN_PROXIES_KEY;

    /// <summary>
    /// Trusts <c>X-Forwarded-For</c> and <c>X-Forwarded-Proto</c> from loopback and from the
    /// networks (CIDR) and addresses listed under <c>ForwardedHeaders</c>. Trusted hops are
    /// unwound, up to ten, so a load balancer in front of a second proxy still yields the client.
    /// </summary>
    public static IServiceCollection AddClientForwardedHeaders(this IServiceCollection services, IConfiguration configuration)
    {
        TrustedProxies trusted = TrustedProxies.From(configuration);

        _ = services.Configure<ForwardedHeadersOptions>(options =>
        {
            options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
            options.ForwardLimit = TrustedProxies.MAX_HOPS;

            foreach (IPNetwork network in trusted.Networks)
            {
                options.KnownIPNetworks.Add(network);
            }

            foreach (IPAddress proxy in trusted.Proxies)
            {
                options.KnownProxies.Add(proxy);
            }
        });

        return services;
    }
}
