using IniaAi.Common.Interfaces.Services.AgentRuntime;

namespace IniaAi.Infrastructure.Tests.Services;

/// <summary>
/// The re-carried path rules beside <see cref="AgentStagedNamesTests"/>, because the two
/// namespaces answer opposite questions: a document name is flattened onto the staged-name
/// alphabet, while a re-carried <c>out/</c> path must survive as a path — the worker that
/// wrote <c>out/checks/model.py</c> finds it at that same path after the continuation.
/// </summary>
public class AgentRunContinuationCapacityTests
{
    [Theory]
    [InlineData("model.py")]
    [InlineData("checks/model.py")]
    [InlineData("deep/nested/dir/data (1).csv")]
    [InlineData(".hidden")]
    public void IsRecarriedOutPath_NestedPathsSurviveWithoutFlattening(string path)
    {
        Assert.True(AgentRunContinuationCapacity.IsRecarriedOutPath(path));
    }

    [Fact]
    public void NestedPath_WouldHaveBeenFlattenedByTheStagedNameRules()
    {
        // The failure this closes: out/checks/model.py pushed through the document-name
        // repair arrives flattened into a different name in a shared namespace.
        string flattened = AgentStagedNames.Stage("checks/model.py", []);

        Assert.Equal("checks_model.py", flattened);
        Assert.True(AgentRunContinuationCapacity.IsRecarriedOutPath("checks/model.py"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("checks//model.py")]
    [InlineData("./model.py")]
    [InlineData("checks/..")]
    [InlineData("..")]
    [InlineData("checks\\model.py")]
    [InlineData("checks/mo\0del.py")]
    public void IsRecarriedOutPath_RefusesUnsafeSegments(string path)
    {
        Assert.False(AgentRunContinuationCapacity.IsRecarriedOutPath(path));
    }

    [Fact]
    public void IsRecarriedOutPath_RefusesAPathPastTheLengthCap()
    {
        string overlong = new('a', AgentRunContinuationCapacity.MAX_RECARRIED_OUT_PATH_CHARS + 1);

        Assert.False(AgentRunContinuationCapacity.IsRecarriedOutPath(overlong));
        Assert.True(AgentRunContinuationCapacity.IsRecarriedOutPath(
            new string('a', AgentRunContinuationCapacity.MAX_RECARRIED_OUT_PATH_CHARS)));
    }

    [Fact]
    public void DescribeFirstRecarryProblem_CleanSetPasses()
    {
        Assert.Null(AgentRunContinuationCapacity.DescribeFirstRecarryProblem(
            ["report.md", "checks/model.py", "checks/data.csv"]));
    }

    [Fact]
    public void DescribeFirstRecarryProblem_RefusesDuplicates()
    {
        string? problem = AgentRunContinuationCapacity.DescribeFirstRecarryProblem(
            ["checks/model.py", "checks/model.py"]);

        Assert.NotNull(problem);
        Assert.Contains("more than once", problem, StringComparison.Ordinal);
    }

    [Fact]
    public void DescribeFirstRecarryProblem_RefusesAFileThatIsAlsoADirectoryPrefix()
    {
        string? problem = AgentRunContinuationCapacity.DescribeFirstRecarryProblem(
            ["checks", "checks/model.py"]);

        Assert.NotNull(problem);
        Assert.Contains("directory prefix", problem, StringComparison.Ordinal);
    }

    [Fact]
    public void DescribeFirstRecarryProblem_RefusesASetOverTheEntryCap()
    {
        List<string> paths = [.. Enumerable
            .Range(0, AgentRunContinuationCapacity.MAX_RECARRIED_OUT_FILES + 1)
            .Select(static index => $"file-{index}.txt")];

        string? problem = AgentRunContinuationCapacity.DescribeFirstRecarryProblem(paths);

        Assert.NotNull(problem);
        Assert.Contains("continuation ceiling", problem, StringComparison.Ordinal);
    }
}
