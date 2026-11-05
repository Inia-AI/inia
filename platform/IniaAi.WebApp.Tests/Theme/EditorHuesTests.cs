using IniaAi.Domain.Entities.Workflows.Enums;
using IniaAi.Samples.WordCount;
using IniaAi.WebApp.Tests.Components.WorkflowEditor.Common;
using IniaAi.WebApp.Theme;

namespace IniaAi.WebApp.Tests.Theme;

public class EditorHuesTests
{
    [Theory]
    [MemberData(nameof(BlockTypeVisualsTests.SelectableTypes), MemberType = typeof(BlockTypeVisualsTests))]
    public void Block_ForSelectableType_DoesNotFallBackToDefaultHue(BlockTypes type)
    {
        string hue = EditorHues.Block(type);

        Assert.Matches("^#[0-9A-Fa-f]{6}$", hue);
        Assert.NotEqual(EditorHues.FALLBACK, hue);
    }

    [Fact]
    public void Block_ForAnExtensionType_IsTheHueItRegistered()
    {
        Assert.Equal(WordCountExtension.Definition.Hue, EditorHues.Block(WordCountProperties.TYPE));
    }

    [Fact]
    public void Block_LoopStart_UsesTheSharedLoopHue()
    {
        Assert.Equal(EditorHues.LOOP, EditorHues.Block(BlockTypes.LoopStart));
    }
}
