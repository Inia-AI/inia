namespace IniaAi.Testing;

/// <summary>
/// The source files of the platform's projects, C# unless a test asks for another file pattern, for a test
/// that guards a rule across the tree. Test code is every project whose name ends in Tests, plus the
/// IniaAi.Testing support projects; product code is the rest.
/// </summary>
public static class SourceFiles
{
    private const string PROJECT_PATTERN = "IniaAi.*";
    private const string SOURCE_PATTERN = "*.cs";
    private const string TEST_PROJECT_SUFFIX = "Tests";
    private const string TEST_SUPPORT_PREFIX = "IniaAi.Testing";

    public static IEnumerable<string> Product(string repoRoot, string filePattern = SOURCE_PATTERN)
    {
        return InProjects(repoRoot, project => !IsTestCode(project), filePattern);
    }

    public static IEnumerable<string> Test(string repoRoot)
    {
        return InProjects(repoRoot, IsTestCode, SOURCE_PATTERN);
    }

    private static IEnumerable<string> InProjects(string repoRoot, Func<string, bool> includesProject, string filePattern)
    {
        return Directory.EnumerateDirectories(repoRoot, PROJECT_PATTERN)
            .Where(project => includesProject(Path.GetFileName(project)))
            .SelectMany(project => Directory.EnumerateFiles(project, filePattern, SearchOption.AllDirectories));
    }

    private static bool IsTestCode(string projectName)
    {
        return projectName.EndsWith(TEST_PROJECT_SUFFIX, StringComparison.Ordinal)
            || projectName.StartsWith(TEST_SUPPORT_PREFIX, StringComparison.Ordinal);
    }
}
