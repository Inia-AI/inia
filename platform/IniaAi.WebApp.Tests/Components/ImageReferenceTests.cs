using IniaAi.Testing;
using System.Text.RegularExpressions;

namespace IniaAi.WebApp.Tests.Components;

public partial class ImageReferenceTests
{
    private const string WEB_APP_DIRECTORY = "IniaAi.WebApp";
    private const string WWWROOT = "wwwroot";

    [GeneratedRegex("""images/[\w.-]+\.(?:png|svg|jpe?g|gif|webp)""")]
    private static partial Regex ImagePath();

    [Fact]
    public void EveryImageAPageShows_IsInWwwroot()
    {
        string webApp = Path.Combine(RepositoryRoot.Find(), WEB_APP_DIRECTORY);

        List<string> references = [.. Directory
            .EnumerateFiles(Path.Combine(webApp, "Components"), "*.razor", SearchOption.AllDirectories)
            .SelectMany(path => ImagePath().Matches(File.ReadAllText(path)).Select(match => match.Value))
            .Distinct()];
        List<string> missing = [.. references.Where(image => !File.Exists(Path.Combine(webApp, WWWROOT, image)))];

        Assert.NotEmpty(references);
        Assert.True(missing.Count == 0, $"Pages show images that wwwroot does not have:\n{string.Join('\n', missing)}");
    }
}
