using System.Net;
using System.Net.Sockets;

namespace IniaAi.Common.Http;

/// <summary>
/// The address ranges that reach a deployment's own network rather than the internet: the
/// loopback, private, carrier-grade NAT, link-local, unique-local and unspecified ranges, in
/// IPv4 and IPv6; the reserved, benchmarking, deprecated IPv4-compatible and local-use NAT64
/// ranges, which no public server uses; and the Azure host platform's own address. An IPv6
/// address that carries an IPv4 address (IPv4-mapped, or NAT64 under 64:ff9b::/96) is judged by
/// the IPv4 address it carries.
/// </summary>
public static class InternalAddresses
{
    public const string LOOPBACK = "loopback";
    public const string PRIVATE = "private";
    public const string CARRIER_GRADE_NAT = "carrier-grade NAT";
    public const string LINK_LOCAL = "link-local";
    public const string UNIQUE_LOCAL = "unique-local";
    public const string UNSPECIFIED = "unspecified";
    public const string RESERVED = "reserved";
    public const string BENCHMARKING = "benchmarking";
    public const string IPV4_COMPATIBLE = "deprecated IPv4-compatible";
    public const string LOCAL_NAT64 = "local-use NAT64";
    public const string CLOUD_PLATFORM = "cloud platform";

    private static readonly (IPNetwork Network, string Rule)[] _ranges =
    [
        (IPNetwork.Parse("0.0.0.0/8"), UNSPECIFIED),
        (IPNetwork.Parse("10.0.0.0/8"), PRIVATE),
        (IPNetwork.Parse("100.64.0.0/10"), CARRIER_GRADE_NAT),
        (IPNetwork.Parse("127.0.0.0/8"), LOOPBACK),
        (IPNetwork.Parse("169.254.0.0/16"), LINK_LOCAL),
        (IPNetwork.Parse("172.16.0.0/12"), PRIVATE),
        (IPNetwork.Parse("192.168.0.0/16"), PRIVATE),
        (IPNetwork.Parse("192.0.0.0/24"), RESERVED),
        (IPNetwork.Parse("198.18.0.0/15"), BENCHMARKING),
        (IPNetwork.Parse("240.0.0.0/4"), RESERVED),
        // Azure's WireServer: a public address that reaches the host platform from every VM.
        (IPNetwork.Parse("168.63.129.16/32"), CLOUD_PLATFORM),
        (IPNetwork.Parse("::/128"), UNSPECIFIED),
        (IPNetwork.Parse("::1/128"), LOOPBACK),
        (IPNetwork.Parse("::/96"), IPV4_COMPATIBLE),
        (IPNetwork.Parse("64:ff9b:1::/48"), LOCAL_NAT64),
        (IPNetwork.Parse("fc00::/7"), UNIQUE_LOCAL),
        (IPNetwork.Parse("fe80::/10"), LINK_LOCAL),
    ];

    private static readonly IPNetwork _nat64 = IPNetwork.Parse("64:ff9b::/96");

    // The services that hand out the host's own credentials: the AWS, Azure and Google instance
    // metadata endpoints, and Azure's WireServer.
    private static readonly IPAddress[] _cloudMetadata =
    [
        IPAddress.Parse("169.254.169.254"),
        IPAddress.Parse("fd00:ec2::254"),
        IPAddress.Parse("168.63.129.16"),
    ];

    /// <summary>
    /// The rule <paramref name="address"/> breaks, such as <see cref="LINK_LOCAL"/>, or null for
    /// an address on the internet.
    /// </summary>
    public static string? RuleFor(IPAddress address)
    {
        IPAddress judged = CarriedIPv4(address) ?? address;

        foreach ((IPNetwork network, string rule) in _ranges)
        {
            if (network.Contains(judged))
            {
                return rule;
            }
        }

        return null;
    }

    /// <summary>
    /// The cloud metadata address that <paramref name="network"/> holds, or null. No allow-list
    /// entry may hold one.
    /// </summary>
    internal static IPAddress? CloudMetadataIn(IPNetwork network)
    {
        return _cloudMetadata.FirstOrDefault(network.Contains);
    }

    internal static bool IsCloudMetadata(IPAddress address)
    {
        IPAddress judged = CarriedIPv4(address) ?? address;
        return _cloudMetadata.Contains(judged);
    }

    /// <summary>
    /// The IPv4 address an IPv6 address carries (IPv4-mapped, or NAT64 under 64:ff9b::/96), or
    /// null for any other address.
    /// </summary>
    internal static IPAddress? CarriedIPv4(IPAddress address)
    {
        if (address.AddressFamily != AddressFamily.InterNetworkV6)
        {
            return null;
        }

        if (address.IsIPv4MappedToIPv6)
        {
            return address.MapToIPv4();
        }

        if (_nat64.Contains(address))
        {
            byte[] bytes = address.GetAddressBytes();
            return new IPAddress(bytes[^4..]);
        }

        return null;
    }
}
