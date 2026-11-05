using IniaAi.Domain.Proposals;
using IniaAi.WebApp.Components.WorkflowEditor.Agent;

namespace IniaAi.WebApp.Tests.Components.WorkflowEditor.Agent;

public sealed class EditorAgentChangeTextTests
{
    [Fact]
    public void AnEditInTheMiddleOfALongPrompt_ShowsOnlyThePartAroundIt()
    {
        string head = string.Join(' ', Enumerable.Repeat("Read the ownership section carefully.", 30));
        string tail = string.Join(' ', Enumerable.Repeat("Answer inside the answer tag.", 30));
        string before = $"{head}\n\n1. Extract the facts.\n\n{tail}";
        string after = $"{head}\n\n1. Extract the facts.\n2. Name the page the answer came from.\n\n{tail}";

        TextExcerpt excerpt = EditorAgentChangeText.Excerpt(before, after);

        Assert.True(excerpt.CutStart);
        Assert.True(excerpt.CutEnd);
        Assert.Contains("Name the page the answer came from.", excerpt.After, StringComparison.Ordinal);
        Assert.DoesNotContain("Name the page", excerpt.Before, StringComparison.Ordinal);
        Assert.True(excerpt.After.Length < after.Length / 2);
        Assert.False(excerpt.After.StartsWith(' '));
    }

    [Fact]
    public void AShortText_IsShownWhole()
    {
        TextExcerpt excerpt = EditorAgentChangeText.Excerpt("Summarize briefly.", "Summarize in three bullets.");

        Assert.False(excerpt.CutStart);
        Assert.False(excerpt.CutEnd);
        Assert.Equal("Summarize briefly.", excerpt.Before);
        Assert.Equal("Summarize in three bullets.", excerpt.After);
    }

    [Theory]
    [InlineData("true", "On")]
    [InlineData("false", "Off")]
    [InlineData("8000", "8000")]
    [InlineData("[\"AnnualReport\",\"Accounts\"]", "AnnualReport, Accounts")]
    [InlineData("{\"Effort\":\"High\",\"ShouldUseThinkingMode\":true}", "Effort: High\nThinking: On")]
    public void Values_ReadAsPlainWords_NotAsCode(string json, string expected)
    {
        Assert.Equal(expected, EditorAgentChangeText.ValueText(json));
    }
}
