using Azure.Security.KeyVault.Secrets;
using IniaAi.Configuration.Extensions;

namespace IniaAi.Configuration.Tests.Extensions;

public class PrefixKeyVaultSecretManagerTests
{
    [Theory]
    [InlineData("shared--PlatformSettings--Anthropic--ApiKey", true)]
    [InlineData("Shared--PlatformSettings--Cohere--ApiKey", true)]
    [InlineData("api--ConnectionStrings--CoreDbConnection", false)]
    [InlineData("sharedSecrets--SomeKey", false)]
    [InlineData("shared--", false)]
    public void Load_MatchesScopePrefixOnly_IgnoringCase(string secretName, bool expected)
    {
        var manager = new PrefixKeyVaultSecretManager("shared");

        Assert.Equal(expected, manager.Load(new SecretProperties(secretName)));
    }

    [Theory]
    [InlineData("shared", "shared--PlatformSettings--Anthropic--ApiKey", "PlatformSettings:Anthropic:ApiKey")]
    [InlineData("api", "api--ConnectionStrings--CoreDbConnection", "ConnectionStrings:CoreDbConnection")]
    [InlineData("api", "api--SomeKey", "SomeKey")]
    public void GetKey_StripsScopePrefixAndMapsSeparatorsToColons(string scope, string secretName, string expected)
    {
        var manager = new PrefixKeyVaultSecretManager(scope);

        Assert.Equal(expected, manager.GetKey(new KeyVaultSecret(secretName, "value")));
    }

    [Fact]
    public void SharedAndServiceScopes_ProduceTheSameConfigKey_SoTheLaterSourceCanOverride()
    {
        string sharedKey = new PrefixKeyVaultSecretManager("shared")
            .GetKey(new KeyVaultSecret("shared--PlatformSettings--Anthropic--ApiKey", "value"));
        string apiKey = new PrefixKeyVaultSecretManager("api")
            .GetKey(new KeyVaultSecret("api--PlatformSettings--Anthropic--ApiKey", "value"));

        Assert.Equal(sharedKey, apiKey);
    }
}
