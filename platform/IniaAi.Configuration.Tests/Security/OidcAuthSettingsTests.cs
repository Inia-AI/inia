using IniaAi.Configuration.Security;
using Microsoft.Extensions.Configuration;

namespace IniaAi.Configuration.Tests.Security;

public class OidcAuthSettingsTests
{
    [Fact]
    public void SectionName_IsAuthenticationOidc()
    {
        Assert.Equal("Authentication:Oidc", OidcAuthSettings.SectionName);
    }

    [Fact]
    public void DefaultValues_AreCorrect()
    {
        var settings = new OidcAuthSettings();

        Assert.False(settings.Enabled);
        Assert.Equal(string.Empty, settings.ClientId);
        Assert.Equal(string.Empty, settings.ClientSecret);
        Assert.Equal(string.Empty, settings.Issuer);
        Assert.Equal("SSO", settings.DisplayName);
        Assert.Empty(settings.AllowedDomains);
    }

    [Fact]
    public void Properties_CanBeSetAndRetrieved()
    {
        var settings = new OidcAuthSettings
        {
            Enabled = true,
            ClientId = "0oa1bcdef",
            ClientSecret = "secret-value",
            Issuer = "https://dev-123456.okta.com/oauth2/default",
            DisplayName = "Company SSO",
            AllowedDomains = ["company.com", "partner.com"]
        };

        Assert.True(settings.Enabled);
        Assert.Equal("0oa1bcdef", settings.ClientId);
        Assert.Equal("secret-value", settings.ClientSecret);
        Assert.Equal("https://dev-123456.okta.com/oauth2/default", settings.Issuer);
        Assert.Equal("Company SSO", settings.DisplayName);
        Assert.Equal(2, settings.AllowedDomains.Count);
        Assert.Contains("company.com", settings.AllowedDomains);
        Assert.Contains("partner.com", settings.AllowedDomains);
    }

    [Fact]
    public void BindsFromConfiguration()
    {
        IConfigurationRoot config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Authentication:Oidc:Enabled"] = "true",
                ["Authentication:Oidc:ClientId"] = "0oa1234",
                ["Authentication:Oidc:ClientSecret"] = "kv-secret",
                ["Authentication:Oidc:Issuer"] = "https://dev-123.okta.com/oauth2/default",
                ["Authentication:Oidc:DisplayName"] = "Okta",
                ["Authentication:Oidc:AllowedDomains:0"] = "company.com",
                ["Authentication:Oidc:AllowedDomains:1"] = "partner.com"
            })
            .Build();

        OidcAuthSettings? settings = config.GetSection(OidcAuthSettings.SectionName).Get<OidcAuthSettings>();

        Assert.NotNull(settings);
        Assert.True(settings!.Enabled);
        Assert.Equal("0oa1234", settings.ClientId);
        Assert.Equal("kv-secret", settings.ClientSecret);
        Assert.Equal("https://dev-123.okta.com/oauth2/default", settings.Issuer);
        Assert.Equal("Okta", settings.DisplayName);
        Assert.Equal(2, settings.AllowedDomains.Count);
    }
}
