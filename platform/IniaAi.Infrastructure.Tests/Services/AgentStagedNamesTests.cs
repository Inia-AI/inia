using IniaAi.Common.Interfaces.Services.AgentRuntime;

namespace IniaAi.Infrastructure.Tests.Services;

public class AgentStagedNamesTests
{
    private readonly HashSet<string> _usedNames = new(StringComparer.Ordinal);

    [Fact]
    public void Stage_MapsIllegalCharactersOntoTheAdmittedAlphabet()
    {
        Assert.Equal("Invoice _M_rz_ _2.pdf", AgentStagedNames.Stage("Invoice (März) #2.pdf", _usedNames));
    }

    [Fact]
    public void Stage_AdmissibleNamePassesThroughAndJoinsTheNamespace()
    {
        Assert.Equal("Annual Report 2025.pdf", AgentStagedNames.Stage("Annual Report 2025.pdf", _usedNames));
        Assert.Contains("Annual Report 2025.pdf", _usedNames);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Stage_BlankName_FallsBackToDocument(string? blank)
    {
        Assert.Equal("document", AgentStagedNames.Stage(blank, _usedNames));
    }

    [Theory]
    [InlineData("manifest.json", "doc-manifest.json")]
    [InlineData(".", "doc-.")]
    [InlineData("..", "doc-..")]
    public void Stage_ReservedNames_GetTheReservedPrefix(string reserved, string expected)
    {
        Assert.Equal(expected, AgentStagedNames.Stage(reserved, _usedNames));
    }

    [Fact]
    public void Stage_Collisions_DedupWithExtensionPreservingSuffixes()
    {
        Assert.Equal("notes.md", AgentStagedNames.Stage("notes.md", _usedNames));
        Assert.Equal("notes_2.md", AgentStagedNames.Stage("notes.md", _usedNames));
        Assert.Equal("notes_3.md", AgentStagedNames.Stage("notes.md", _usedNames));
    }

    [Fact]
    public void Stage_LongNames_TruncateToTheCapPreservingTheExtension()
    {
        string staged = AgentStagedNames.Stage(new string('a', 240) + ".pdf", _usedNames);

        Assert.Equal(AgentStagedNames.MAX_LENGTH, staged.Length);
        Assert.EndsWith(".pdf", staged);
    }

    [Fact]
    public void Stage_LongNameCollision_DedupSuffixStaysWithinTheCap()
    {
        _ = AgentStagedNames.Stage(new string('a', 240) + ".pdf", _usedNames);
        string second = AgentStagedNames.Stage(new string('a', 240) + ".pdf", _usedNames);

        Assert.Equal(AgentStagedNames.MAX_LENGTH, second.Length);
        Assert.EndsWith("_2.pdf", second);
    }

    [Fact]
    public void Stage_Dotfiles_StayWholeInsteadOfSplittingAFakeExtension()
    {
        Assert.Equal(".env", AgentStagedNames.Stage(".env", _usedNames));
        Assert.Equal(".env_2", AgentStagedNames.Stage(".env", _usedNames));

        string longDotfile = AgentStagedNames.Stage("." + new string('b', 240), _usedNames);
        Assert.Equal(AgentStagedNames.MAX_LENGTH, longDotfile.Length);
        Assert.StartsWith(".b", longDotfile);
    }

    [Theory]
    [InlineData("../../etc/passwd")]
    [InlineData("/etc/passwd")]
    [InlineData("..\\..\\windows\\system.ini")]
    [InlineData("nul\0byte.pdf")]
    [InlineData("line\nbreak.pdf")]
    [InlineData("trailing-newline.pdf\n")]
    public void Stage_ANameThatWouldChooseAPath_BecomesOneOrdinarySegment(string hostile)
    {
        string staged = AgentStagedNames.Stage(hostile, _usedNames);

        Assert.All(staged, static c => Assert.True(char.IsAsciiLetterOrDigit(c) || c is '.' or '_' or ' ' or '-', $"'{c}' is outside the admitted alphabet"));
        Assert.InRange(staged.Length, 1, AgentStagedNames.MAX_LENGTH);
        Assert.DoesNotContain(staged, new[] { ".", "..", "manifest.json" });
    }

    [Theory]
    [InlineData("Report.pdf", "report.pdf")]
    [InlineData("caf\u00e9.pdf", "cafe\u0301.pdf")]
    public void Stage_NamesThatDifferOnlyByCaseOrUnicodeForm_StayApart(string first, string second)
    {
        string stagedFirst = AgentStagedNames.Stage(first, _usedNames);
        string stagedSecond = AgentStagedNames.Stage(second, _usedNames);

        Assert.NotEqual(stagedFirst, stagedSecond);
    }
}
