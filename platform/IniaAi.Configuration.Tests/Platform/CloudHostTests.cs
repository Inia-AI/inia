using IniaAi.Configuration.Platform;

namespace IniaAi.Configuration.Tests.Platform;

public class CloudHostTests
{
    private int _ec2Lookups;

    [Fact]
    public void ANamedAzureHost_IsNotAws_WithoutAskingEc2()
    {
        CloudHost host = Host(forceAws: null, forceAzure: "true", onEc2: true);

        Assert.True(host.IsAzure);
        Assert.False(host.IsAws);
        Assert.Equal(0, _ec2Lookups);
    }

    [Theory]
    [InlineData("true", true)]
    [InlineData("false", false)]
    public void ANamedAwsMode_DecidesWithoutAskingEc2(string forceAws, bool expected)
    {
        CloudHost host = Host(forceAws, forceAzure: null, onEc2: !expected);

        Assert.Equal(expected, host.IsAws);
        Assert.Equal(0, _ec2Lookups);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AnUnnamedHost_AsksEc2Once_HoweverOftenItIsAsked(bool onEc2)
    {
        CloudHost host = Host(forceAws: null, forceAzure: null, onEc2);

        Assert.Equal(onEc2, host.IsAws);
        Assert.Equal(onEc2, host.IsAws);
        Assert.Equal(1, _ec2Lookups);
    }

    [Theory]
    [InlineData(null, "true", true)]
    [InlineData("true", null, true)]
    [InlineData(null, null, true)]
    [InlineData(null, null, false)]
    public void GatewayMode_IsTheGateway_WithoutAskingWhichCloud(string? forceAws, string? forceAzure, bool onEc2)
    {
        CloudHost host = Host(forceAws, forceAzure, onEc2);

        Assert.Equal(DeploymentMode.Gateway, host.ModeFor(usesModelGateway: true));
        Assert.Equal(0, _ec2Lookups);
    }

    [Theory]
    [InlineData(null, "true", false, DeploymentMode.Azure)]
    [InlineData("true", "true", false, DeploymentMode.Azure)]
    [InlineData("true", null, false, DeploymentMode.Aws)]
    [InlineData(null, null, true, DeploymentMode.Aws)]
    [InlineData(null, null, false, DeploymentMode.DirectApi)]
    [InlineData("false", null, true, DeploymentMode.DirectApi)]
    public void WithoutAGateway_AzureWinsThenAwsThenTheDirectApi(
        string? forceAws, string? forceAzure, bool onEc2, DeploymentMode expected)
    {
        Assert.Equal(expected, Host(forceAws, forceAzure, onEc2).ModeFor(usesModelGateway: false));
    }

    private CloudHost Host(string? forceAws, string? forceAzure, bool onEc2)
    {
        var variables = new Dictionary<string, string?>
        {
            [CloudHost.FORCE_AWS_MODE] = forceAws,
            [CloudHost.FORCE_AZURE_MODE] = forceAzure,
        };

        return new CloudHost(variables.GetValueOrDefault, () =>
        {
            _ec2Lookups++;
            return onEc2;
        });
    }
}
