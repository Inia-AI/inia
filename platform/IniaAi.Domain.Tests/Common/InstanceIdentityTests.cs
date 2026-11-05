using IniaAi.Domain.Common;
using IniaAi.Domain.Entities.Validation;

namespace IniaAi.Domain.Tests.Common;

public class InstanceIdentityTests
{
    private const string CONFIGURED_NAME = "customer-prod";
    private const string REQUEST_HOST = "api.internal.example.com";

    [Fact]
    public void ConfiguredInstanceNameWinsOverTheAmbientFallback()
    {
        Assert.Equal(CONFIGURED_NAME, InstanceIdentity.Resolve(CONFIGURED_NAME, REQUEST_HOST));
    }

    [Fact]
    public void ConfiguredNameIsTrimmedOfSurroundingWhitespace()
    {
        Assert.Equal(CONFIGURED_NAME, InstanceIdentity.Resolve($"  {CONFIGURED_NAME}\t", REQUEST_HOST));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void UnsetConfigurationFallsBackToTheAmbientFallback(string? configuredName)
    {
        Assert.Equal(REQUEST_HOST, InstanceIdentity.Resolve(configuredName, REQUEST_HOST));
    }

    [Fact]
    public void NothingConfiguredAndNoAmbientFallbackResolvesToTheMachineName()
    {
        Assert.Equal(Environment.MachineName, InstanceIdentity.Resolve(null, null));
    }

    [Fact]
    public void AnOverlongConfiguredNameIsTruncatedToTheColumnLength()
    {
        string overlong = new('x', ValidationCase.MAX_SOURCE_INSTANCE_LENGTH + 50);

        string resolved = InstanceIdentity.Resolve(overlong, REQUEST_HOST);

        Assert.Equal(new string('x', ValidationCase.MAX_SOURCE_INSTANCE_LENGTH), resolved);
    }

    [Fact]
    public void AnOverlongAmbientFallbackIsTruncatedToTheColumnLength()
    {
        string overlong = new('x', ValidationCase.MAX_SOURCE_INSTANCE_LENGTH + 50);

        string resolved = InstanceIdentity.Resolve(null, overlong);

        Assert.Equal(new string('x', ValidationCase.MAX_SOURCE_INSTANCE_LENGTH), resolved);
    }
}
