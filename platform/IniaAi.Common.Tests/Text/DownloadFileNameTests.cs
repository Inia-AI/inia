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

    [Theory]
    [InlineData("../../a")]
    [InlineData("/etc/x")]
    [InlineData("..")]
    [InlineData(".")]
    [InlineData("..\\..\\b")]
    [InlineData("CON")]
    [InlineData("a\u0007b\u0000c\r\nd")]
    public void Build_OfAStrangersName_NeverNamesAFolderOrAControlCharacter(string name)
    {
        string built = DownloadFileName.Build(name, ID, "file", ".pdf");

        Assert.DoesNotContain('/', built);
        Assert.DoesNotContain('\\', built);
        Assert.DoesNotContain(built, char.IsControl);
        Assert.NotEqual(".", Path.GetFileNameWithoutExtension(built));
        Assert.NotEqual("..", Path.GetFileNameWithoutExtension(built));
        Assert.EndsWith(".pdf", built, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_OfANameOf300Characters_KeepsAtMost120OfThem()
    {
        Assert.Equal(120 + ".pdf".Length, DownloadFileName.Build(new string('a', 300), ID, "file", ".pdf").Length);
    }
}
