using IniaAi.Configuration.Messaging;
using Microsoft.Extensions.Configuration;

namespace IniaAi.Configuration.Tests.Messaging;

public class EmailExpansionSettingsTests
{
    [Fact]
    public void ConfiguredSkippedLinkHosts_AddToTheDefault()
    {
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["EmailExpansion:SkippedLinkHosts:0"] = "skipped.example" })
            .Build();

        EmailExpansionSettings settings = configuration.GetSection("EmailExpansion").Get<EmailExpansionSettings>()!;

        Assert.Equal(["linkedin.com", "skipped.example"], settings.SkippedLinkHosts);
    }
}
