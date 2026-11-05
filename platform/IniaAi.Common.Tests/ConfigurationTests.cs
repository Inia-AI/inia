using IniaAi.Testing.XunitAttributes;
using Microsoft.Extensions.Configuration;

namespace IniaAi.Common.Tests;

public class ConfigurationTests
{
    private const string ANTHROPIC_KEY = "PlatformSettings:Anthropic:ApiKey";
    private const string PROBE_KEY = "PlatformSettings:ConfigurationTestProbe:ApiKey";
    private const string PROBE_VARIABLE = "PLATFORMSETTINGS__CONFIGURATIONTESTPROBE__APIKEY";
    private const string PROBE_VALUE = "exported-in-the-shell";
    private const string EXCHANGE_RATE_KEY = "PlatformSettings:ExchangeRateApi:ApiKey";
    private const string READS_NO_PARAMETER_STORE =
        $"Reads the shared test secrets. Set {Testing.Configuration.SSM_ENVIRONMENT_VARIABLE} to the Parameter Store environment, such as unittests, with AWS credentials, to run it.";

    [Fact]
    [Obsolete("This method is only for testing purposes.")]
    public void GetConfigurationRoot_WithTheParameterStoreVariable_ReadsTheStoredKeys()
    {
        Assert.SkipUnless(Testing.Configuration.ReadsParameterStore(), READS_NO_PARAMETER_STORE);

        IConfigurationRoot result = Testing.Configuration.GetConfigurationRoot([]);

        Assert.NotNull(result[ANTHROPIC_KEY]);
    }

    [Fact]
    [Obsolete("This method is only for testing purposes.")]
    public void GetConfigurationRoot_WithoutThePaidApiOptIn_WithholdsEveryKeyButTheFreeOnes()
    {
        Assert.SkipUnless(Testing.Configuration.ReadsParameterStore(), READS_NO_PARAMETER_STORE);
        Assert.SkipWhen(PaidApi.IsAllowed(), $"{PaidApi.OPT_IN_VARIABLE} is set, so the loader hands out the real keys.");

        IConfigurationRoot result = Testing.Configuration.GetConfigurationRoot([]);

        Assert.True(result[ANTHROPIC_KEY] == PaidApi.WITHHELD_KEY, "The Anthropic key reached the tests.");
        Assert.True(result["PlatformSettings:Exa:ApiKey"] == PaidApi.WITHHELD_KEY, "The Exa key reached the tests.");
        Assert.True(IsHandedOut(result[EXCHANGE_RATE_KEY]), "The free exchange-rate key was withheld or is missing.");
    }

    [Fact]
    [Obsolete("This method is only for testing purposes.")]
    public void GetConfigurationRoot_WithoutTheParameterStoreVariable_ReadsTheEnvironment_AndWithholdsItsKeysWithoutTheOptIn()
    {
        Assert.SkipWhen(Testing.Configuration.ReadsParameterStore(), $"{Testing.Configuration.SSM_ENVIRONMENT_VARIABLE} is set, so the loader reads the Parameter Store.");
        Environment.SetEnvironmentVariable(PROBE_VARIABLE, PROBE_VALUE);

        try
        {
            IConfigurationRoot result = Testing.Configuration.GetConfigurationRoot([]);

            Assert.Equal(PaidApi.IsAllowed() ? PROBE_VALUE : PaidApi.WITHHELD_KEY, result[PROBE_KEY]);
        }
        finally
        {
            Environment.SetEnvironmentVariable(PROBE_VARIABLE, null);
        }
    }

    [Fact]
    public void WithheldKeys_AreEveryStoredSettingAndEveryConfiguredCredential_ButTheFreeOnes()
    {
        string[] stored = [ANTHROPIC_KEY, EXCHANGE_RATE_KEY, "PlatformSettings:Google:BroadSearchEngineId"];
        string[] configured = [ANTHROPIC_KEY, "PlatformSettings:Exa:ApiKey", "PlatformSettings:GitHub:Pat", "PlatformSettings:Google", "PATH"];

        IEnumerable<string> withheld = Testing.Configuration.WithheldKeys(stored, configured);

        Assert.Equal(
            [ANTHROPIC_KEY, "PlatformSettings:Exa:ApiKey", "PlatformSettings:GitHub:Pat", "PlatformSettings:Google:BroadSearchEngineId"],
            withheld.Order(StringComparer.Ordinal));
    }

    private static bool IsHandedOut(string? value)
    {
        return !string.IsNullOrEmpty(value) && value != PaidApi.WITHHELD_KEY;
    }
}
