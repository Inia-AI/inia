using System.Net;
using IniaAi.Configuration.Extensions;
using Microsoft.Extensions.Configuration;
using IPNetwork = System.Net.IPNetwork;

namespace IniaAi.Configuration.Security;

/// <summary>
/// The proxies a deployment names under <c>ForwardedHeaders</c>, the only senders whose
/// <c>X-Forwarded-For</c> and <c>X-Forwarded-Proto</c> an app takes.
/// </summary>
public sealed record TrustedProxies(IReadOnlyList<IPNetwork> Networks, IReadOnlyList<IPAddress> Proxies)
{
    /// <summary>The setting that lists the proxy networks, in CIDR form.</summary>
    public const string KNOWN_NETWORKS_KEY = "ForwardedHeaders:KnownNetworks";

    /// <summary>The setting that lists single proxy addresses.</summary>
    public const string KNOWN_PROXIES_KEY = "ForwardedHeaders:KnownProxies";

    /// <summary>
    /// The forwarding hops an app unwinds. The middleware sizes its work to this number, and a
    /// client chooses how many entries its X-Forwarded-For header carries.
    /// </summary>
    public const int MAX_HOPS = 10;

    public bool IsEmpty => Networks.Count == 0 && Proxies.Count == 0;

    /// <summary>
    /// Reads both lists. An entry that is not a CIDR network or an address stops startup.
    /// </summary>
    public static TrustedProxies From(IConfiguration configuration)
    {
        IPNetwork[] networks = [.. configuration.GetList(KNOWN_NETWORKS_KEY)
            .Select(entry => IPNetwork.TryParse(entry, out IPNetwork network) ? network : throw Invalid(KNOWN_NETWORKS_KEY, entry))];
        IPAddress[] proxies = [.. configuration.GetList(KNOWN_PROXIES_KEY)
            .Select(entry => IPAddress.TryParse(entry, out IPAddress? proxy) ? proxy : throw Invalid(KNOWN_PROXIES_KEY, entry))];
        return new TrustedProxies(networks, proxies);
    }

    private static InvalidOperationException Invalid(string key, string entry)
    {
        return new InvalidOperationException($"{key} holds '{entry}', which is not a valid entry.");
    }
}
