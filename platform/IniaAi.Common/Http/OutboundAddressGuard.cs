using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;

namespace IniaAi.Common.Http;

/// <summary>
/// Keeps a request that a user, a model answer or an email can steer away from the deployment's
/// own network. It refuses every address in <see cref="InternalAddresses"/> unless the
/// deployment's allow-list names the host, the address or a range that holds it.
/// </summary>
public sealed class OutboundAddressGuard
{
    private const int POOLED_CONNECTION_LIFETIME_MINUTES = 5;
    private const char RANGE_SEPARATOR = '/';

    private static readonly ConcurrentDictionary<string, OutboundAddressGuard> _guards = new(StringComparer.Ordinal);

    private readonly HashSet<string> _allowedHostNames;
    private readonly IPNetwork[] _allowedNetworks;
    private readonly IWebProxy _proxy;
    private readonly Func<string, CancellationToken, Task<IPAddress[]>> _resolve;
    private readonly Lazy<HttpMessageHandler> _sharedHandler;

    private OutboundAddressGuard(IEnumerable<string> allowList, IWebProxy proxy, Func<string, CancellationToken, Task<IPAddress[]>> resolve)
    {
        _proxy = proxy;
        _resolve = resolve;
        _allowedHostNames = new(StringComparer.OrdinalIgnoreCase);
        List<IPNetwork> networks = [];

        foreach (string entry in allowList)
        {
            if (TryParseNetwork(entry, out IPNetwork network))
            {
                if (InternalAddresses.CloudMetadataIn(network) is IPAddress metadata)
                {
                    throw new ArgumentException(
                        $"{OutboundAddressRefusedException.ALLOW_LIST_SETTING} holds '{entry}', which covers {metadata}, a cloud metadata service that no workflow may reach; {OutboundAddressRefusedException.PROCESS_POINTER}.",
                        nameof(allowList));
                }

                networks.Add(network);
            }
            else if (Uri.CheckHostName(entry) == UriHostNameType.Dns)
            {
                _ = _allowedHostNames.Add(entry);
            }
            else
            {
                throw new ArgumentException(
                    $"{OutboundAddressRefusedException.ALLOW_LIST_SETTING} holds '{entry}', which is not a host name, an IP address or a CIDR range.",
                    nameof(allowList));
            }
        }

        _allowedNetworks = [.. networks];
        _sharedHandler = new(CreateCheckedHandler);
    }

    /// <summary>
    /// The guard for <paramref name="allowedInternalHosts"/>. One guard serves each distinct
    /// list, so the connection pool of its shared handler lives as long as the process.
    /// Throws <see cref="ArgumentException"/> for an entry that is not a host name, an IP address
    /// or a CIDR range.
    /// </summary>
    public static OutboundAddressGuard For(IEnumerable<string> allowedInternalHosts)
    {
        string[] entries = [.. allowedInternalHosts
            .Where(entry => !string.IsNullOrWhiteSpace(entry))
            .Select(entry => entry.Trim().TrimEnd('.').ToLowerInvariant())
            .Distinct()
            .Order(StringComparer.Ordinal)];

        return _guards.GetOrAdd(
            string.Join('\n', entries),
            static (_, list) => new OutboundAddressGuard(list, HttpClient.DefaultProxy, Dns.GetHostAddressesAsync),
            entries);
    }

    /// <summary>
    /// A guard whose requests leave through <paramref name="proxy"/> instead of the proxy the
    /// platform's environment sets. Not cached.
    /// </summary>
    internal static OutboundAddressGuard For(IEnumerable<string> allowedInternalHosts, IWebProxy proxy)
    {
        return For(allowedInternalHosts, proxy, Dns.GetHostAddressesAsync);
    }

    /// <summary>
    /// A guard that looks a host name up with <paramref name="resolve"/> instead of the system
    /// resolver. Not cached.
    /// </summary>
    internal static OutboundAddressGuard For(IEnumerable<string> allowedInternalHosts, IWebProxy proxy, Func<string, CancellationToken, Task<IPAddress[]>> resolve)
    {
        return new OutboundAddressGuard(allowedInternalHosts, proxy, resolve);
    }

    /// <summary>
    /// The handler every caller shares, with one connection pool, sending and keeping cookies in
    /// <paramref name="cookies"/> only, so callers that hold different jars never share a cookie.
    /// The caller does not dispose it. It checks requests as <see cref="CreateHandler"/> does, and
    /// with <paramref name="allowedOrigins"/> it also refuses a request or a redirect to any web
    /// address outside them.
    /// </summary>
    public HttpMessageHandler SharedHandlerWith(CookieContainer cookies, IReadOnlySet<string>? allowedOrigins = null)
    {
        var jar = new CookieJarHandler(cookies, _sharedHandler.Value);
        return allowedOrigins is null ? jar : new AllowedOriginsHandler(allowedOrigins, jar);
    }

    /// <summary>
    /// A new handler with its own cookies, for a caller that owns and disposes it. A direct
    /// connection opens only to an address this guard allows, and a request a proxy carries is
    /// checked by its target before it leaves. Redirects are followed one hop at a time, so each
    /// hop meets the same check. A refusal surfaces as <see cref="OutboundAddressRefusedException"/>.
    /// </summary>
    public HttpMessageHandler CreateHandler()
    {
        return new CookieJarHandler(new CookieContainer(), CreateCheckedHandler());
    }

    /// <summary>
    /// Starts a forward proxy on the loopback interface that applies this guard to every
    /// request a browser sends through it. The caller owns the proxy and disposes it.
    /// </summary>
    public CheckedForwardProxy StartForwardProxy(ILogger logger)
    {
        return new CheckedForwardProxy(this, logger);
    }

    /// <summary>
    /// The addresses of <paramref name="host"/> that a connection may use. Throws
    /// <see cref="OutboundAddressRefusedException"/> when none is allowed.
    /// </summary>
    internal async Task<IReadOnlyList<IPAddress>> ResolveAsync(string host, CancellationToken cancellationToken)
    {
        string name = host.Trim('[', ']').TrimEnd('.');
        bool nameAllowed = _allowedHostNames.Contains(name);

        IPAddress[] addresses = IPAddress.TryParse(name, out IPAddress? literal)
            ? [literal]
            : await _resolve(name, cancellationToken);

        List<IPAddress> allowed = [];
        OutboundAddressRefusedException? refusal = null;

        foreach (IPAddress address in addresses)
        {
            string? rule = InternalAddresses.RuleFor(address);

            bool allowedOnPurpose = (nameAllowed || IsInAllowedNetwork(address)) && !InternalAddresses.IsCloudMetadata(address);

            if (rule is null || allowedOnPurpose)
            {
                allowed.Add(address);
            }
            else
            {
                refusal ??= new OutboundAddressRefusedException(host, address, rule);
            }
        }

        if (allowed.Count == 0 && refusal is not null)
        {
            throw refusal;
        }

        return allowed;
    }

    /// <summary>
    /// Checks a request that a proxy carries, which the connection check cannot see. The proxy
    /// resolves the name, so a name the platform cannot resolve itself is left to the proxy. A
    /// direct request needs no check here: its connection is checked when it opens.
    /// </summary>
    internal async Task CheckProxiedTargetAsync(Uri target, CancellationToken cancellationToken)
    {
        if (UpstreamProxyFor(target) is null)
        {
            return;
        }

        try
        {
            _ = await ResolveAsync(target.IdnHost, cancellationToken);
        }
        catch (SocketException)
        {
        }
    }

    /// <summary>
    /// A connection that carries a request to <paramref name="target"/>: to a checked address of
    /// the target itself, or to the proxy that carries it once the target is checked. The proxy
    /// is null for a direct connection.
    /// </summary>
    internal async Task<(Stream Connection, Uri? Upstream)> OpenAsync(Uri target, CancellationToken cancellationToken)
    {
        if (UpstreamProxyFor(target) is Uri upstream)
        {
            await CheckProxiedTargetAsync(target, cancellationToken);
            return (await ConnectToProxyAsync(upstream, cancellationToken), upstream);
        }

        return (await ConnectAsync(target.IdnHost, target.Port, cancellationToken), null);
    }

    /// <summary>
    /// The proxy that carries a request to <paramref name="target"/>, by default the one the
    /// platform's environment sets (<c>HTTP_PROXY</c>, <c>HTTPS_PROXY</c>, <c>NO_PROXY</c>), or
    /// null for a direct connection.
    /// </summary>
    internal Uri? UpstreamProxyFor(Uri target)
    {
        return _proxy.IsBypassed(target) ? null : _proxy.GetProxy(target);
    }

    internal NetworkCredential? UpstreamProxyCredential(Uri upstream)
    {
        return _proxy.Credentials?.GetCredential(upstream, "Basic");
    }

    /// <summary>
    /// Opens a connection to <paramref name="host"/> on one of the addresses
    /// <see cref="ResolveAsync"/> allows, so a DNS answer that changes between a check and the
    /// connection cannot move it to a refused address.
    /// </summary>
    private async Task<Stream> ConnectAsync(string host, int port, CancellationToken cancellationToken)
    {
        IReadOnlyList<IPAddress> addresses = await ResolveAsync(host, cancellationToken);
        return await ConnectToAsync([.. addresses], port, cancellationToken);
    }

    /// <summary>
    /// The deployment's own proxy is trusted wherever it lives, often on an internal address.
    /// </summary>
    private static async Task<Stream> ConnectToProxyAsync(Uri upstream, CancellationToken cancellationToken)
    {
        IPAddress[] addresses = await Dns.GetHostAddressesAsync(upstream.IdnHost, cancellationToken);
        return await ConnectToAsync(addresses, upstream.Port, cancellationToken);
    }

    private static async Task<Stream> ConnectToAsync(IPAddress[] addresses, int port, CancellationToken cancellationToken)
    {
        var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };

        try
        {
            await socket.ConnectAsync(addresses, port, cancellationToken);
            return new NetworkStream(socket, ownsSocket: true);
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }

    private async ValueTask<Stream> ConnectForHttpAsync(SocketsHttpConnectionContext context, CancellationToken cancellationToken)
    {
        DnsEndPoint endpoint = context.DnsEndPoint;

        if (context.InitialRequestMessage.RequestUri is Uri target
            && UpstreamProxyFor(target) is Uri upstream
            && upstream.IdnHost.Equals(endpoint.Host, StringComparison.OrdinalIgnoreCase)
            && upstream.Port == endpoint.Port)
        {
            return await ConnectToProxyAsync(upstream, cancellationToken);
        }

        return await ConnectAsync(endpoint.Host, endpoint.Port, cancellationToken);
    }

    private HttpMessageHandler CreateCheckedHandler()
    {
        return new CheckedRedirectHandler(this, new HopCookieHandler(new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            UseCookies = false,
            Proxy = _proxy,
            PooledConnectionLifetime = TimeSpan.FromMinutes(POOLED_CONNECTION_LIFETIME_MINUTES),
            ConnectCallback = ConnectForHttpAsync,
        }));
    }

    private bool IsInAllowedNetwork(IPAddress address)
    {
        IPAddress judged = InternalAddresses.CarriedIPv4(address) ?? address;

        return _allowedNetworks.Any(network => network.Contains(judged));
    }

    private static bool TryParseNetwork(string entry, out IPNetwork network)
    {
        if (entry.Contains(RANGE_SEPARATOR))
        {
            return IPNetwork.TryParse(entry, out network);
        }

        if (IPAddress.TryParse(entry, out IPAddress? address))
        {
            // A request is judged on the IPv4 address that an IPv6 address carries, so an entry is too.
            IPAddress judged = InternalAddresses.CarriedIPv4(address) ?? address;
            network = new IPNetwork(judged, judged.AddressFamily == AddressFamily.InterNetwork ? 32 : 128);
            return true;
        }

        network = default;
        return false;
    }
}
