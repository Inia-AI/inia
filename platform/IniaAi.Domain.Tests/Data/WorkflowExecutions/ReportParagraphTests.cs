using IniaAi.Domain.Entities.WorkflowExecutions;

namespace IniaAi.Domain.Tests.Data.WorkflowExecutions;

public class ReportParagraphTests
{
    [Fact]
    public void Content_RemovesNullCharacters()
    {
        var paragraph = new ReportParagraph
        {
            Content = "error output: ab" + '\0' + "cd",
        };

        Assert.Equal("error output: abcd", paragraph.Content);
    }

    [Fact]
    public void Content_NullAndCleanValues_PassThroughUnchanged()
    {
        var paragraph = new ReportParagraph();

        Assert.Null(paragraph.Content);

        paragraph.Content = "clean content";
        Assert.Equal("clean content", paragraph.Content);
    }
}
