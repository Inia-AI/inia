using Microsoft.AspNetCore.Components;
using System.Diagnostics.CodeAnalysis;

namespace IniaAi.WebApp.Components.Account;

# pragma warning disable CA1812
internal sealed class IdentityRedirectManager(NavigationManager navigationManager)
# pragma warning restore CA1812
{
    public const string STATUS_COOKIE_NAME = "Identity.StatusMessage";

    private static readonly CookieBuilder _statusCookieBuilder = new()
    {
        // Strict is withheld on the redirect that follows an external-IdP callback
        // (the chain originates cross-site), silently dropping login status messages.
        SameSite = SameSiteMode.Lax,
        HttpOnly = true,
        IsEssential = true,
        MaxAge = TimeSpan.FromSeconds(5),
    };

    [DoesNotReturn]
    public void RedirectTo(string? uri)
    {
        uri ??= "";

        // Prevent open redirects.
        if (!Uri.IsWellFormedUriString(uri, UriKind.Relative)
            || uri.StartsWith("//", StringComparison.Ordinal))
        {
            uri = navigationManager.ToBaseRelativePath(uri);
        }

        // During static rendering, NavigateTo throws a NavigationException which is handled by the framework as a redirect.
        // So as long as this is called from a statically rendered Identity component, the InvalidOperationException is never thrown.
        navigationManager.NavigateTo(uri);
        throw new InvalidOperationException($"{nameof(IdentityRedirectManager)} can only be used during static rendering.");
    }

    [DoesNotReturn]
    public void RedirectTo(string uri, Dictionary<string, object?> queryParameters)
    {
        string uriWithoutQuery = navigationManager
            .ToAbsoluteUri(uri)
            .GetLeftPart(UriPartial.Path);
        string newUri = navigationManager
            .GetUriWithQueryParameters(uriWithoutQuery, queryParameters);
        RedirectTo(newUri);
    }

    [DoesNotReturn]
    public void RedirectToWithStatus(string uri, string message, HttpContext context)
    {
        context.Response.Cookies.Append(STATUS_COOKIE_NAME, message, _statusCookieBuilder.Build(context));
        RedirectTo(uri);
    }

    private string _currentPath => navigationManager.ToAbsoluteUri(navigationManager.Uri).GetLeftPart(UriPartial.Path);

    [DoesNotReturn]
    public void RedirectToCurrentPage()
    {
        RedirectTo(_currentPath);
    }

    [DoesNotReturn]
    public void RedirectToCurrentPageWithStatus(string message, HttpContext context)
    {
        RedirectToWithStatus(_currentPath, message, context);
    }
}
