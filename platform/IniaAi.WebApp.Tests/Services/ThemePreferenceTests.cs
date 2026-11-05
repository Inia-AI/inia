using IniaAi.WebApp.Services;

namespace IniaAi.WebApp.Tests.Services;

public class ThemePreferenceTests
{
    [Fact]
    public void IsDarkMode_WithNoStoredValue_DefaultsToDark()
    {
        Assert.True(ThemePreference.IsDarkMode(null));
    }

    [Fact]
    public void IsDarkMode_WithStoredLight_IsLight()
    {
        Assert.False(ThemePreference.IsDarkMode(ThemePreference.LIGHT));
    }

    [Fact]
    public void IsDarkMode_WithStoredDark_IsDark()
    {
        Assert.True(ThemePreference.IsDarkMode(ThemePreference.DARK));
    }

    [Fact]
    public void IsDarkMode_WithUnrecognizedValue_DefaultsToDark()
    {
        Assert.True(ThemePreference.IsDarkMode("solarized"));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void FromIsDarkMode_RoundTripsThroughStorage(bool isDarkMode)
    {
        Assert.Equal(isDarkMode, ThemePreference.IsDarkMode(ThemePreference.FromIsDarkMode(isDarkMode)));
    }
}
