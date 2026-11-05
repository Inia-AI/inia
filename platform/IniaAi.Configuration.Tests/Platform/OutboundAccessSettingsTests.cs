using IniaAi.Common.Http;
using IniaAi.Configuration.Platform;
using Microsoft.Extensions.Configuration;

namespace IniaAi.Configuration.Tests.Platform;

public class OutboundAccessSettingsTests
{
    private static IConfiguration ConfigurationWith(Dictionary<string, string?> values)
    {
        return new ConfigurationBuilder().AddInMemoryCollection(values).Build();
    }

    [Fact]
    public void TheDefault_AllowsNoInternalHost()
    {
        Assert.Empty(new OutboundAccessSettings().AllowedInternalHosts);
        OutboundAccessSettings.ThrowIfAllowListInvalid(ConfigurationWith([]));
    }

    [Fact]
    public void TheSettingTheRefusalNames_BindsToTheAllowList()
    {
        IConfiguration configuration = ConfigurationWith(new()
        {
            [$"{OutboundAddressRefusedException.ALLOW_LIST_SETTING}:0"] = "agent-runtime",
            [$"{OutboundAddressRefusedException.ALLOW_LIST_SETTING}:1"] = "10.0.0.0/8",
        });

        PlatformSettings settings = configuration.GetSection("PlatformSettings").Get<PlatformSettings>()!;

        Assert.Equal(["agent-runtime", "10.0.0.0/8"], settings.OutboundAccess.AllowedInternalHosts);
        OutboundAccessSettings.ThrowIfAllowListInvalid(configuration);
    }

    [Fact]
    public void AnEntryThatIsNotAHostAnAddressOrARange_StopsStartupAndIsNamed()
    {
        IConfiguration configuration = ConfigurationWith(new()
        {
            [$"{OutboundAddressRefusedException.ALLOW_LIST_SETTING}:0"] = "http://reports.internal:8080",
        });

        ArgumentException error = Assert.Throws<ArgumentException>(() => OutboundAccessSettings.ThrowIfAllowListInvalid(configuration));

        Assert.Contains("http://reports.internal:8080", error.Message);
        Assert.Contains(OutboundAddressRefusedException.ALLOW_LIST_SETTING, error.Message);
    }

    [Fact]
    public void AListSetAsOneValue_StopsStartup()
    {
        IConfiguration configuration = ConfigurationWith(new()
        {
            [OutboundAddressRefusedException.ALLOW_LIST_SETTING] = "agent-runtime",
        });

        _ = Assert.Throws<InvalidOperationException>(() => OutboundAccessSettings.ThrowIfAllowListInvalid(configuration));
    }
}
