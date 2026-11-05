using System.Text.RegularExpressions;
using IniaAi.Testing;

namespace IniaAi.Common.Tests;

/// <summary>
/// A test that sleeps for a fixed time is slow on every run and flaky on a busy one. Test code waits
/// on a signal instead: a task, a TaskCompletionSource, a fake clock. A delay that only caps a wait
/// (passed to WhenAny or WaitAsync) or hangs until cancelled is fine, and a line that must wait on
/// purpose says why in a "Waits on purpose:" comment on that line or the line above.
/// IniaAi.WebApi.IntegrationTests polls a running API and stays outside CI, so it is not checked.
/// </summary>
public partial class FixedWaitGuardTests
{
    private const string INTEGRATION_TESTS_PROJECT = "IniaAi.WebApi.IntegrationTests";

    [Fact]
    public void TestCode_WaitsOnSignals_NotOnTheClock()
    {
        string repoRoot = RepositoryRoot.Find();

        List<string> offenders = [.. SourceFiles.Test(repoRoot)
            .Where(file => !IsInProject(repoRoot, file, INTEGRATION_TESTS_PROJECT))
            .SelectMany(file => FixedWaits(repoRoot, file))];

        Assert.True(
            offenders.Count == 0,
            "Wait on a signal instead, or say why in a \"// Waits on purpose: ...\" comment:\n  " + string.Join("\n  ", offenders));
    }

    private static IEnumerable<string> FixedWaits(string repoRoot, string file)
    {
        string[] lines = File.ReadAllLines(file);
        for (int index = 0; index < lines.Length; index++)
        {
            string previous = index > 0 ? lines[index - 1] : string.Empty;
            string twoBefore = index > 1 ? lines[index - 2] : string.Empty;
            if (FixedWait().IsMatch(lines[index])
                && !lines[index].Contains("Timeout.Infinite", StringComparison.Ordinal)
                && !CapsAWait().IsMatch(string.Join('\n', twoBefore, previous, lines[index]))
                && !WaitsOnPurpose().IsMatch(previous)
                && !WaitsOnPurpose().IsMatch(lines[index]))
            {
                yield return $"{Path.GetRelativePath(repoRoot, file)}:{index + 1}";
            }
        }
    }

    private static bool IsInProject(string repoRoot, string file, string projectName)
    {
        return Path.GetRelativePath(repoRoot, file).StartsWith(projectName + Path.DirectorySeparatorChar, StringComparison.Ordinal);
    }

    [GeneratedRegex(@"Thread\.Sleep\(|Task\.Delay\(")]
    private static partial Regex FixedWait();

    [GeneratedRegex(@"WhenAny\(|\.WaitAsync\(")]
    private static partial Regex CapsAWait();

    [GeneratedRegex(@"//\s*Waits on purpose:\s*\S")]
    private static partial Regex WaitsOnPurpose();
}
