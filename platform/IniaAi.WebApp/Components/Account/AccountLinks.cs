using IniaAi.Application.Common;
using IniaAi.Application.Common.Interfaces.Services;
using Microsoft.AspNetCore.WebUtilities;

namespace IniaAi.WebApp.Components.Account;

/// <summary>
/// Builds the links the admin app mails (password reset, email confirmation) from
/// <c>AdminBaseUrl</c>, never from the request: a request's Host header is the sender's to choose,
/// so a link built from it could carry a reset code to another site. Without mail or an absolute
/// address, no link is made.
/// </summary>
public sealed class AccountLinks(IConfiguration configuration, IOutgoingMail mail, ILogger<AccountLinks> logger)
{
    private readonly string? _baseUrl = Uri.TryCreate(configuration[AppAddresses.ADMIN_BASE_URL_KEY], UriKind.Absolute, out Uri? address)
        && (address.Scheme == Uri.UriSchemeHttps || address.Scheme == Uri.UriSchemeHttp)
            ? address.AbsoluteUri.TrimEnd('/')
            : null;

    public bool CanMail => mail.IsConfigured && _baseUrl is not null;

    public string? For(string path, IDictionary<string, string?> query)
    {
        if (!CanMail)
        {
            logger.LogError(
                "Account mail not sent: it needs a mail provider and {AdminBaseUrlKey}, the admin app's public address",
                AppAddresses.ADMIN_BASE_URL_KEY);
            return null;
        }

        return QueryHelpers.AddQueryString($"{_baseUrl}/{path}", query);
    }
}
