using IniaAi.Testing.XunitAttributes;

namespace IniaAi.Common.Tests;

public class RequiresChromeTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void OnCi_ATestThatStartsChromeSkips(bool installed)
    {
        Assert.StartsWith("Ignored in CI", Chrome.SkipReason(onCi: true, installed));
    }

    [Fact]
    public void OffCi_WithoutChrome_ATestThatStartsChromeSkips_AndSaysWhatToInstall()
    {
        Assert.Contains("Install Google Chrome", Chrome.SkipReason(onCi: false, installed: false));
    }

    [Fact]
    public void OffCi_WithChrome_ATestThatStartsChromeRuns()
    {
        Assert.Null(Chrome.SkipReason(onCi: false, installed: true));
    }
}
