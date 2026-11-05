using IniaAi.Common.Text;

namespace IniaAi.Common.Tests.Text;

public class DownloadFileNameTests
{
    private const string ID = "3f1c9a4e-5d2b-4f7a-9c1e-8b6d2a0f4e11";

    [Fact]
    public void Build_NameWithCharactersWindowsRefuses_ReplacesThemAndCollapsesSpaces()
    {
        string fileName = DownloadFileName.Build("Q3/Q4: Review?  - A<B> \"C\" | D.", ID, "report", ".docx");

        Assert.Equal("Q3 Q4 Review - A B C D.docx", fileName);
    }

    [Fact]
    public void Build_NothingOfTheNameSurvives_FallsBackToThePrefixAndTheIdHead()
    {
        string fileName = DownloadFileName.Build(" ?* ", ID, "workflow-execution", ".docx");

        Assert.Equal("workflow-execution-3f1c9a4e.docx", fileName);
    }

    [Fact]
    public void Build_LongNameCutInsideASurrogatePair_DropsTheHalfCharacter()
    {
        string name = new string('a', 119) + "\U0001F600";

        string fileName = DownloadFileName.Build(name, ID, "report", ".txt");

        Assert.Equal(new string('a', 119) + ".txt", fileName);
    }
}
