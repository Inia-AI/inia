using IniaAi.Application.Common;
using IniaAi.Testing;
using IniaAi.Testing.DependencyInjection.Mocks;
using IniaAi.WebApp.Components.Account;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace IniaAi.WebApp.Tests.Components.Account;

/// <summary>
/// A link the admin app mails starts at the address the deployment configured. A request's Host
/// header is the sender's to choose, so a link built from it could carry a reset code to another site.
/// </summary>
public class AccountLinksTests
{
    private const string ADMIN = "https://admin.example.com";

    private readonly CapturingLogger<AccountLinks> _log = new();

    [Fact]
    public void ALink_StartsAtTheConfiguredAddress_WithItsQueryEncoded()
    {
        AccountLinks links = Build(ADMIN + "/", mailOn: true);

        string? link = links.For("Account/ResetPassword", new Dictionary<string, string?> { ["code"] = "a+b/c=" });

        Assert.True(links.CanMail);
        Assert.Equal($"{ADMIN}/Account/ResetPassword?code=a%2Bb%2Fc%3D", link);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("admin.example.com")]
    [InlineData("/admin")]
    public void WithoutAnAbsoluteAddress_NoLinkIsMade_AndTheErrorNamesTheSetting(string? address)
    {
        AccountLinks links = Build(address, mailOn: true);

        Assert.False(links.CanMail);
        Assert.Null(links.For("Account/ResetPassword", new Dictionary<string, string?> { ["code"] = "abc" }));
        Assert.Contains(_log.Entries, entry => entry.Level == LogLevel.Error && entry.Message.Contains(AppAddresses.ADMIN_BASE_URL_KEY));
    }

    [Fact]
    public void WithMailOff_NoLinkIsMade()
    {
        AccountLinks links = Build(ADMIN, mailOn: false);

        Assert.False(links.CanMail);
        Assert.Null(links.For("Account/ResetPassword", new Dictionary<string, string?> { ["code"] = "abc" }));
    }

    private AccountLinks Build(string? address, bool mailOn)
    {
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { [AppAddresses.ADMIN_BASE_URL_KEY] = address })
            .Build();
        return new AccountLinks(configuration, new RecordingOutgoingMail(mailOn), _log);
    }
}
