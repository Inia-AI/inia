using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Configuration;

namespace IniaAi.Configuration.Security;

/// <summary>
/// The size of the sign-in limit: <see cref="PermitLimit"/> requests per client and endpoint in
/// each window of <see cref="WindowSeconds"/>.
/// </summary>
public sealed class SignInRateLimitSettings
{
    /// <summary>The settings section that sizes the sign-in limit.</summary>
    public const string SECTION_NAME = "RateLimiting:SignIn";

    /// <summary>The policy every sign-in and password-mail endpoint names.</summary>
    public const string POLICY_NAME = "sign-in";

    private const int IPV6_NETWORK_BYTES = 8;

    /// <summary>Requests one client may make to one endpoint in a window.</summary>
    public int PermitLimit { get; set; } = 30;

    /// <summary>The window length in seconds.</summary>
    public int WindowSeconds { get; set; } = 60;

    /// <summary>
    /// Reads the limit and refuses a limit or a window under one, which would otherwise fail every
    /// sign-in request when the limiter is first built.
    /// </summary>
    public static SignInRateLimitSettings From(IConfiguration configuration)
    {
        SignInRateLimitSettings settings = configuration.GetSection(SECTION_NAME).Get<SignInRateLimitSettings>()
            ?? new SignInRateLimitSettings();

        if (settings.PermitLimit < 1)
        {
            throw new InvalidOperationException($"{SECTION_NAME}:{nameof(PermitLimit)} must be at least 1.");
        }

        if (settings.WindowSeconds < 1)
        {
            throw new InvalidOperationException($"{SECTION_NAME}:{nameof(WindowSeconds)} must be at least 1.");
        }

        return settings;
    }

    /// <summary>
    /// One bucket per client and endpoint. The endpoint is the one routing matched, so another
    /// spelling of its path counts against the same bucket. An IPv6 client counts by its /64
    /// network, because one subscriber holds a whole /64 and could pick a new address per request.
    /// </summary>
    public static string PartitionKey(IPAddress? client, string? endpoint)
    {
        return $"{ClientNetwork(client)}|{endpoint}";
    }

    private static string? ClientNetwork(IPAddress? client)
    {
        if (client is null)
        {
            return null;
        }

        if (client.IsIPv4MappedToIPv6)
        {
            return client.MapToIPv4().ToString();
        }

        if (client.AddressFamily != AddressFamily.InterNetworkV6)
        {
            return client.ToString();
        }

        byte[] bytes = client.GetAddressBytes();
        Array.Clear(bytes, IPV6_NETWORK_BYTES, bytes.Length - IPV6_NETWORK_BYTES);
        return $"{new IPAddress(bytes)}/64";
    }
}
