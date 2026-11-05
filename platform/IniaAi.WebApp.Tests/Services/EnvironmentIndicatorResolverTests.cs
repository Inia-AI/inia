using IniaAi.WebApp.Theme;
using IniaAi.Configuration.Platform;
using IniaAi.WebApp.Services;

namespace IniaAi.WebApp.Tests.Services;

public class EnvironmentIndicatorResolverTests
{
    [Fact]
    public void Production_WithDefaultBanner_IsHiddenWithProductionColour()
    {
        EnvironmentIndicator indicator = EnvironmentIndicatorResolver.Resolve("Production", new EnvironmentBannerSettings());

        Assert.False(indicator.Show);
        Assert.Equal("PRODUCTION", indicator.Label);
        Assert.Equal(EnvironmentColours.PRODUCTION, indicator.Color);
    }

    [Fact]
    public void Staging_WithDefaultBanner_IsShownWithStagingColour()
    {
        EnvironmentIndicator indicator = EnvironmentIndicatorResolver.Resolve("Staging", new EnvironmentBannerSettings());

        Assert.True(indicator.Show);
        Assert.Equal("STAGING", indicator.Label);
        Assert.Equal(EnvironmentColours.STAGING, indicator.Color);
    }

    [Fact]
    public void LocalDevelopment_WithDefaultBanner_IsShownWithFriendlyLabel()
    {
        EnvironmentIndicator indicator = EnvironmentIndicatorResolver.Resolve("LocalDevelopment", new EnvironmentBannerSettings());

        Assert.True(indicator.Show);
        Assert.Equal(EnvironmentIndicatorResolver.LABEL_LOCAL, indicator.Label);
        Assert.Equal(EnvironmentColours.LOCAL_DEVELOPMENT, indicator.Color);
    }

    [Fact]
    public void UnknownEnvironment_WithDefaultBanner_IsShownWithFallbackColour()
    {
        EnvironmentIndicator indicator = EnvironmentIndicatorResolver.Resolve("QA", new EnvironmentBannerSettings());

        Assert.True(indicator.Show);
        Assert.Equal("QA", indicator.Label);
        Assert.Equal(EnvironmentColours.NON_PRODUCTION_DEFAULT, indicator.Color);
    }

    [Fact]
    public void Production_WithEnabledOverride_IsShown()
    {
        EnvironmentBannerSettings banner = new EnvironmentBannerSettings { Enabled = true };

        EnvironmentIndicator indicator = EnvironmentIndicatorResolver.Resolve("Production", banner);

        Assert.True(indicator.Show);
        Assert.Equal(EnvironmentColours.PRODUCTION, indicator.Color);
    }

    [Fact]
    public void Staging_WithDisabledOverride_IsHidden()
    {
        EnvironmentBannerSettings banner = new EnvironmentBannerSettings { Enabled = false };

        EnvironmentIndicator indicator = EnvironmentIndicatorResolver.Resolve("Staging", banner);

        Assert.False(indicator.Show);
    }

    [Theory]
    [InlineData("production")]
    [InlineData("PRODUCTION")]
    public void Production_IsMatchedCaseInsensitively(string environmentName)
    {
        EnvironmentIndicator indicator = EnvironmentIndicatorResolver.Resolve(environmentName, new EnvironmentBannerSettings());

        Assert.False(indicator.Show);
    }

    [Fact]
    public void CustomLabelOverride_IsUsedVerbatim()
    {
        EnvironmentBannerSettings banner = new EnvironmentBannerSettings { Label = "Acme Sandbox" };

        EnvironmentIndicator indicator = EnvironmentIndicatorResolver.Resolve("Staging", banner);

        Assert.Equal("Acme Sandbox", indicator.Label);
    }

    [Fact]
    public void CustomColourOverride_IsUsed()
    {
        EnvironmentBannerSettings banner = new EnvironmentBannerSettings { Color = "#123456" };

        EnvironmentIndicator indicator = EnvironmentIndicatorResolver.Resolve("Staging", banner);

        Assert.Equal("#123456", indicator.Color);
    }

    [Fact]
    public void BlankOverrides_FallBackToDefaults()
    {
        EnvironmentBannerSettings banner = new EnvironmentBannerSettings { Label = "   ", Color = "" };

        EnvironmentIndicator indicator = EnvironmentIndicatorResolver.Resolve("Staging", banner);

        Assert.Equal("STAGING", indicator.Label);
        Assert.Equal(EnvironmentColours.STAGING, indicator.Color);
    }

    [Fact]
    public void DarkBackground_UsesLightText()
    {
        EnvironmentIndicator indicator = EnvironmentIndicatorResolver.Resolve("Staging", new EnvironmentBannerSettings());

        Assert.Equal(EnvironmentColours.TEXT_LIGHT, indicator.TextColor);
    }

    [Theory]
    [InlineData("#FFEB3B")]
    [InlineData("#FFFFFF")]
    [InlineData("#ff0")]
    public void LightBackground_UsesDarkText(string color)
    {
        EnvironmentBannerSettings banner = new EnvironmentBannerSettings { Color = color };

        EnvironmentIndicator indicator = EnvironmentIndicatorResolver.Resolve("Staging", banner);

        Assert.Equal(color, indicator.Color);
        Assert.Equal(EnvironmentColours.TEXT_DARK, indicator.TextColor);
    }

    [Theory]
    [InlineData("red;position:fixed;top:40%")]
    [InlineData("rebeccapurple")]
    [InlineData("#12345")]
    [InlineData("#GGGGGG")]
    public void InvalidColourOverride_FallsBackToDefault(string color)
    {
        EnvironmentBannerSettings banner = new EnvironmentBannerSettings { Color = color };

        EnvironmentIndicator indicator = EnvironmentIndicatorResolver.Resolve("Staging", banner);

        Assert.Equal(EnvironmentColours.STAGING, indicator.Color);
    }

    [Fact]
    public void Production_WithCustomLabel_IsShown()
    {
        EnvironmentBannerSettings banner = new EnvironmentBannerSettings { Label = "PROD-EU" };

        EnvironmentIndicator indicator = EnvironmentIndicatorResolver.Resolve("Production", banner);

        Assert.True(indicator.Show);
        Assert.Equal("PROD-EU", indicator.Label);
    }

    [Fact]
    public void Production_WithCustomColour_IsShown()
    {
        EnvironmentBannerSettings banner = new EnvironmentBannerSettings { Color = "#0B5CAD" };

        EnvironmentIndicator indicator = EnvironmentIndicatorResolver.Resolve("Production", banner);

        Assert.True(indicator.Show);
    }

    [Fact]
    public void Production_WithDisabledOverride_StaysHiddenDespiteCustomLabel()
    {
        EnvironmentBannerSettings banner = new EnvironmentBannerSettings { Enabled = false, Label = "PROD-EU" };

        EnvironmentIndicator indicator = EnvironmentIndicatorResolver.Resolve("Production", banner);

        Assert.False(indicator.Show);
    }
}
