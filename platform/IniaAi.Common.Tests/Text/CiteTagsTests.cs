using IniaAi.Common.Text;

namespace IniaAi.Common.Tests.Text;

public class CiteTagsTests
{
    [Fact]
    public void Strip_RemovesTheMarks_AndKeepsTheCitedText()
    {
        Assert.Equal(
            "{ \"cash\": \"96410\" } and total assets of 4,215,730",
            CiteTags.Strip("{ \"cash\": \"<cite index=\\\"1-3\\\">96410</cite>\" } and total assets of <CITE index=\"2\">4,215,730</CITE>"));
    }

    [Fact]
    public void Strip_LeavesTagsWhoseNamesOnlyStartWithCite()
    {
        const string answer = "<cited_text>Revenue 4,215,730</cited_text><cite_list>[1]</cite_list><cites/>";

        Assert.Equal(answer, CiteTags.Strip(answer));
    }

    [Fact]
    public void Strip_LeavesTextWithoutMarks_AndNull_AsTheyAre()
    {
        Assert.Equal("Total assets of 4,215,730", CiteTags.Strip("Total assets of 4,215,730"));
        Assert.Null(CiteTags.Strip(null));
    }
}
