using IniaAi.Common.Text;
using IniaAi.Configuration.Extensions;
using IniaAi.Configuration.Platform;
using Microsoft.Extensions.Configuration;

namespace IniaAi.Configuration.Tests.Extensions;

public class PlatformSettingsDescriptionTests
{
    private const string TOKEN = "runtime-token-0123456789abcdef";

    private static string Describe(Dictionary<string, string?> values)
    {
        IConfigurationRoot configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        PlatformSettings platformSettings = configuration.GetSection("PlatformSettings").Get<PlatformSettings>()
            ?? new PlatformSettings();

        return ConfigurationBuilderExtensions.DescribePlatformSettings(
            configuration, platformSettings, sendGridSettings: null, fileStorageSettings: null, smtpSettings: null);
    }

    [Fact]
    public void DescribesTheAgentRuntimeSettingsWithTheTokenMasked()
    {
        string description = Describe(new Dictionary<string, string?>
        {
            ["PlatformSettings:AgentRuntime:Enabled"] = "true",
            ["PlatformSettings:AgentRuntime:BaseUrl"] = "http://agent-runtime.internal:8130",
            ["PlatformSettings:AgentRuntime:AuthToken"] = TOKEN,
            ["PlatformSettings:AgentRuntime:AllowedModels:0"] = "us.anthropic.claude-sonnet-5-5",
            ["PlatformSettings:AgentRuntime:AllowedModels:1"] = "us.anthropic.claude-opus-5-5",
        });

        Assert.Contains($"\nAgent Runtime Settings:{Environment.NewLine}", description);
        Assert.Contains("  Enabled: True [secrets-manager]", description);
        Assert.Contains("  BaseUrl: http://agent-runtime.internal:8130 [secrets-manager]", description);
        Assert.Contains(
            "  AllowedModels: us.anthropic.claude-sonnet-5-5, us.anthropic.claude-opus-5-5 [secrets-manager]",
            description);
        Assert.Contains($"  AuthToken: {SecretMasker.Mask(TOKEN)} [secrets-manager]", description);
        Assert.DoesNotContain(TOKEN, description);
    }

    [Fact]
    public void TheStartupDescription_ShowsTheFileStorageSectionTheApplicationReads()
    {
        IConfigurationRoot configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["PlatformSettings:UserDataDbProvider"] = "PostgreSQL",
            ["PlatformSettings:CoreDbProvider"] = "PostgreSQL",
            ["FileStorage:DefaultProvider"] = "S3",
            ["FileStorage:S3:Region"] = "eu-west-2",
        }).Build();

        string description = ConfigurationBuilderExtensions.DescribeStartupSettings(configuration);

        Assert.Contains("  DefaultProvider: S3 [secrets-manager]", description);
        Assert.Contains("    Region: eu-west-2 [secrets-manager]", description);
    }

    [Fact]
    public void DescribesAnUnsetAgentRuntimeByItsDefaults()
    {
        string description = Describe([]);

        Assert.Contains("  Enabled: False [default]", description);
        Assert.Contains("  AllowedModels: (none) [default]", description);
        Assert.Contains("  AuthToken: **** [default]", description);
    }
}
