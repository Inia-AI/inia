using IniaAi.Core.Workflows.Strategies;
using IniaAi.Domain.Entities.Workflows.Enums;
using IniaAi.Samples.WordCount;

namespace IniaAi.Core.Tests.Workflows.Strategies;

/// <summary>
/// A block is unavailable when this build cannot run its type: a closed module's type whose module
/// the build leaves out, an extension's type whose extension is not installed, or a type no
/// registration knows. A platform type is always available.
/// </summary>
public class InstalledBlockTypesTests
{
    private const BlockTypes TYPE_NO_REGISTRATION_KNOWS = (BlockTypes)999;

    [Theory]
    [InlineData(BlockTypes.Verification, false, true)]
    [InlineData(BlockTypes.Verification, true, false)]
    [InlineData(BlockTypes.AiQuery, false, false)]
    [InlineData(BlockTypes.LoopEnd, false, false)]
    [InlineData(TYPE_NO_REGISTRATION_KNOWS, false, true)]
    [InlineData(WordCountProperties.TYPE, false, true)]
    [InlineData(WordCountProperties.TYPE, true, false)]
    public void IsUnavailable_WhenTheBuildCannotRunTheType(BlockTypes type, bool strategyRegistered, bool expected)
    {
        var installed = new InstalledBlockTypes(strategyRegistered ? [type] : []);

        Assert.Equal(expected, installed.IsUnavailable(type));
    }
}
