using Bunit;
using IniaAi.WebApp.Components.Account.Shared;
using IniaAi.WebApp.Configuration;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace IniaAi.WebApp.Tests.Components.Account;

/// <summary>
/// A sign-in page shows the status message its redirect carried, and deletes the message's cookie with the
/// attributes it was set with: a browser ignores a change to a <c>__Host-</c> cookie that comes without
/// <c>Secure</c>, so the message would show again on a reload.
/// </summary>
public class StatusMessageTests : BunitContext
{
    private const string MESSAGE = "Your password has been reset.";

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void TheMessage_Shows_AndItsCookieIsDeletedWithItsOwnAttributes(bool httpsOnly)
    {
        AdminCookies cookies = new(httpsOnly);
        _ = Services.AddSingleton(cookies);
        DefaultHttpContext http = new();
        http.Request.Scheme = httpsOnly ? Uri.UriSchemeHttps : Uri.UriSchemeHttp;
        http.Request.Headers.Cookie = $"{cookies.StatusMessage}={Uri.EscapeDataString(MESSAGE)}";

        IRenderedComponent<StatusMessage> message = Render<StatusMessage>(parameters => parameters.AddCascadingValue<HttpContext>(http));

        Assert.Contains(MESSAGE, message.Markup, StringComparison.Ordinal);
        string deletion = Assert.Single(http.Response.Headers.SetCookie.ToArray())!;
        Assert.StartsWith($"{cookies.StatusMessage}=;", deletion, StringComparison.Ordinal);
        Assert.Contains("expires=Thu, 01 Jan 1970", deletion, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("max-age", deletion, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(httpsOnly, deletion.Contains("; secure", StringComparison.OrdinalIgnoreCase));
    }
}
