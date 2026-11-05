using IniaAi.Common.Http;
using IniaAi.Configuration.Extensions;
using Microsoft.Extensions.Configuration;

namespace IniaAi.Configuration.Platform;

/// <summary>
/// Where the requests that a user, a model answer or an email can steer may go: the WebApiCall
/// block and the email link downloader. Both refuse every address in
/// <see cref="InternalAddresses"/> unless <see cref="AllowedInternalHosts"/> names it. The
/// platform's own clients (models, storage, the agent runtime) are not limited.
/// </summary>
public class OutboundAccessSettings
{
    /// <summary>
    /// The internal hosts these requests may reach on purpose: each entry a host name, an IP
    /// address or a CIDR range. Empty by default. Every entry opens the host to every workflow of
    /// every organization on the deployment, so an entry is added only through the process in
    /// "Calling an Internal Service from a Workflow" in the platform documentation. An entry that
    /// covers a cloud metadata service stops startup.
    /// </summary>
    public List<string> AllowedInternalHosts { get; set; } = [];

    public OutboundAddressGuard Guard => OutboundAddressGuard.For(AllowedInternalHosts);

    /// <summary>
    /// Stops startup on an entry that is not a host name, an address or a range, and on a list
    /// set as one value, which would bind to an empty list.
    /// </summary>
    public static void ThrowIfAllowListInvalid(IConfiguration configuration)
    {
        _ = OutboundAddressGuard.For(configuration.GetList(OutboundAddressRefusedException.ALLOW_LIST_SETTING));
    }
}
