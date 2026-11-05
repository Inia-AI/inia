using System.Net;

namespace IniaAi.Common.Http;

/// <summary>
/// An outbound request aimed at an internal address that the deployment does not allow. It is
/// not an <see cref="HttpRequestException"/> on purpose: a caller that retries network errors
/// must not retry a refusal.
/// </summary>
public sealed class OutboundAddressRefusedException : Exception
{
    public const string ALLOW_LIST_SETTING = "PlatformSettings:OutboundAccess:AllowedInternalHosts";

    /// <summary>
    /// The documented process that every entry in <see cref="ALLOW_LIST_SETTING"/> goes through.
    /// </summary>
    public const string PROCESS_PAGE = "Calling an Internal Service from a Workflow";

    internal const string PROCESS_POINTER = $"see \"{PROCESS_PAGE}\" in the platform documentation";

    public OutboundAddressRefusedException(string host, IPAddress address, string rule)
        : base(BuildMessage(host, address, rule))
    {
        Rule = rule;
        Address = address;
    }

    public string Rule { get; }

    /// <summary>
    /// The refused address. The message leaves out an address that a host name resolves to, so a
    /// workflow author cannot map internal names; the server log carries it.
    /// </summary>
    public IPAddress Address { get; }

    /// <summary>
    /// The refusal inside <paramref name="exception"/>, or null. The HTTP stack wraps an exception
    /// thrown while it connects, so a refusal at connect time arrives as an inner exception.
    /// </summary>
    internal static OutboundAddressRefusedException? FindIn(Exception exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (current is OutboundAddressRefusedException refusal)
            {
                return refusal;
            }
        }

        return null;
    }

    private static string BuildMessage(string host, IPAddress address, string rule)
    {
        string kind = $"{ArticleFor(rule)} {rule} address";
        string resolution = host.Trim('[', ']') == address.ToString()
            ? $"{host} is {kind}"
            : $"{host} resolves to {kind}";

        return $"Refused to connect: {resolution}. A workflow reaches an internal service only through an approved exception: {PROCESS_POINTER}.";
    }

    private static string ArticleFor(string rule)
    {
        bool vowelSound = "aeiou".Contains(char.ToLowerInvariant(rule[0]))
            && !rule.StartsWith("uni", StringComparison.OrdinalIgnoreCase);

        return vowelSound ? "an" : "a";
    }
}
