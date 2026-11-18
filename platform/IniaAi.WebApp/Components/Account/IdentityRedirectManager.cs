using Microsoft.AspNetCore.Components;
using System.Diagnostics.CodeAnalysis;
using IniaAi.WebApp.Configuration;

namespace IniaAi.WebApp.Components.Account;

# pragma warning disable CA1812
internal sealed class IdentityRedirectManager(NavigationManager navigationManager, AdminCookies cookies)
# pragma warning restore CA1812
{
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
        context.Response.Cookies.Append(cookies.StatusMessage, message, cookies.StatusMessageCookie.Build(context));
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
