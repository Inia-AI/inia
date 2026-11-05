using IniaAi.Testing;
using System.Text.RegularExpressions;

namespace IniaAi.Common.Tests;

/// <summary>
/// Product code logs through ILogger, which carries levels, structure and the deployment's sinks. A
/// console write bypasses all three and can leak report content into a container's stdout, and a
/// console.log in a page shows the same content in every user's browser tools.
/// </summary>
public partial class ConsoleWriteGuardTests
{
    private const string RAZOR_PATTERN = "*.razor";
    private const string MIGRATIONS_PROJECT_PREFIX = "IniaAi.Migrations.";

    [Fact]
    public void ProductCode_NeverWritesToTheConsole()
    {
        string repoRoot = RepositoryRoot.Find();

        List<string> offenders = [.. SourceFiles.Product(repoRoot)
            .Where(file => !IsGeneratedMigration(repoRoot, file))
            .Concat(SourceFiles.Product(repoRoot, RAZOR_PATTERN))
            .SelectMany(file => Matches(repoRoot, file, ConsoleWrite()))];

        Assert.True(offenders.Count == 0, "Log through ILogger instead:\n  " + string.Join("\n  ", offenders));
    }

    [Fact]
    public void RazorPages_NeverLogToTheBrowserConsole()
    {
        string repoRoot = RepositoryRoot.Find();

        List<string> offenders = [.. SourceFiles.Product(repoRoot, RAZOR_PATTERN)
            .SelectMany(file => Matches(repoRoot, file, BrowserConsoleLog()))];

        Assert.True(offenders.Count == 0, "Remove the browser console logging:\n  " + string.Join("\n  ", offenders));
    }

    private static bool IsGeneratedMigration(string repoRoot, string file)
    {
        return Path.GetRelativePath(repoRoot, file).StartsWith(MIGRATIONS_PROJECT_PREFIX, StringComparison.Ordinal);
    }

    private static IEnumerable<string> Matches(string repoRoot, string file, Regex pattern)
    {
        return File.ReadLines(file)
            .Select((line, index) => (Line: line, Number: index + 1))
            .Where(entry => pattern.IsMatch(entry.Line))
            .Select(entry => $"{Path.GetRelativePath(repoRoot, file)}:{entry.Number}: {entry.Line.Trim()}");
    }

    [GeneratedRegex(@"\bConsole\.(?:Write|WriteLine|Error|Out)\b")]
    private static partial Regex ConsoleWrite();

    [GeneratedRegex(@"\bconsole\.(?:log|debug|info)\(")]
    private static partial Regex BrowserConsoleLog();
}
