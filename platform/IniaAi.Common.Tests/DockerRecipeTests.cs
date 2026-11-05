using IniaAi.Testing;
using System.Text.RegularExpressions;

namespace IniaAi.Common.Tests;

/// <summary>
/// docker/Dockerfile builds the API and the WebApp of every deployment. Its base takes the security
/// fixes the distribution published before each build and names them in the build log, and its apps
/// start Chrome with the driver the base installed, so no image runs Selenium Manager, which reaches
/// the internet on each Chrome start.
/// </summary>
public partial class DockerRecipeTests
{
    private const string APP_STAGE = " AS app\n";
    private const string NEXT_STAGE = "\nFROM ";
    private const string DRIVER_PATH = "CHROMEDRIVER_PATH=/usr/local/bin/chromedriver";

    [Fact]
    public void TheBase_TakesTheOsFixesPublishedBeforeItsBuild_AndNamesTheTrustStoreAndTlsVersions()
    {
        string recipe = ReadRecipe();

        Assert.Matches(OsUpgrade(), recipe);
        Assert.Matches(TrustStoreVersions(), recipe);
    }

    [Fact]
    public void TheAppStage_NamesTheChromeDriver_SoAnImageOnAnOlderPrebuiltBaseNamesItToo()
    {
        string recipe = ReadRecipe();
        int start = recipe.IndexOf(APP_STAGE, StringComparison.Ordinal);
        Assert.True(start >= 0, $"docker/Dockerfile has no stage named{APP_STAGE}");
        int end = recipe.IndexOf(NEXT_STAGE, start, StringComparison.Ordinal);

        string appStage = end < 0 ? recipe[start..] : recipe[start..end];

        Assert.Contains(DRIVER_PATH, appStage);
    }

    private static string ReadRecipe()
    {
        return File.ReadAllText(Path.Combine(RepositoryRoot.Find(), "docker", "Dockerfile")).ReplaceLineEndings("\n");
    }

    [GeneratedRegex(@"apt-get update \\\n\s+&& (DEBIAN_FRONTEND=noninteractive )?apt-get upgrade -y")]
    private static partial Regex OsUpgrade();

    [GeneratedRegex(@"dpkg-query -W [^\n]*ca-certificates [^\n]*libssl3")]
    private static partial Regex TrustStoreVersions();
}
