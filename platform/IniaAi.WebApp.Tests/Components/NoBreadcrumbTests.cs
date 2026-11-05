using IniaAi.Testing;

namespace IniaAi.WebApp.Tests.Components;

/// <summary>
/// Every page of the editor app names what it shows in its own header, and the menu leads to every
/// page, so no page spends a row on a breadcrumb trail.
/// </summary>
public class NoBreadcrumbTests
{
    private const string WEB_APP_DIRECTORY = "IniaAi.WebApp";

    private static readonly string[] _breadcrumbTags = ["<MudBreadcrumbs", "<Breadcrumb"];

    [Fact]
    public void NoPage_ShowsABreadcrumbTrail()
    {
        string repoRoot = RepositoryRoot.Find();

        List<string> pagesWithATrail = [.. Directory
            .EnumerateFiles(Path.Combine(repoRoot, WEB_APP_DIRECTORY), "*.razor", SearchOption.AllDirectories)
            .Where(path => _breadcrumbTags.Any(tag => File.ReadAllText(path).Contains(tag, StringComparison.Ordinal)))
            .Select(path => Path.GetRelativePath(repoRoot, path).Replace('\\', '/'))];

        Assert.Empty(pagesWithATrail);
    }
}
