using IniaAi.Common.Interfaces.Services.AgentRuntime;

namespace IniaAi.Infrastructure.Tests.Services;

public class AgentSkillNamesTests
{
    [Theory]
    [InlineData("extraction", "extraction")]
    [InlineData("income-statement", "income-statement")]
    [InlineData("SKILL.md", "skill")]
    [InlineData("SKILL.markdown", "skill")]
    [InlineData("Income Statement.md", "income-statement")]
    [InlineData("extraction_v2", "extraction-v2")]
    [InlineData("Balance  Sheet", "balance-sheet")]
    [InlineData("--leading--and--trailing--", "leading-and-trailing")]
    [InlineData("a.b.c", "a-b-c")]
    public void Normalize_MatchesTheRuntimeIdentity(string raw, string expected)
    {
        Assert.Equal(expected, AgentSkillNames.Normalize(raw));
    }

    [Theory]
    [InlineData("SKILL.md ", "skill-md")]
    [InlineData("SKILL.md\n", "skill-md")]
    [InlineData(" SKILL.md", "skill")]
    public void Normalize_DoesNotTrim_BecauseTheRuntimeDoesNot(string raw, string expected)
    {
        // agent-runtime/src/skill-plugin.ts never trims and anchors its markdown strip with `$`, so
        // only trailing whitespace keeps the extension. Trimming here would hand the runtime a name
        // whose identity the pre-flight had computed differently.
        Assert.Equal(expected, AgentSkillNames.Normalize(raw));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("!!!")]
    [InlineData("---")]
    [InlineData(".md")]
    public void Normalize_NamesThatKeepNoIdentity_AreNull(string? raw)
    {
        Assert.Null(AgentSkillNames.Normalize(raw));
    }

    [Fact]
    public void Normalize_CutsToMaxLength_AndNeverEndsOnAHyphen()
    {
        string raw = new string('a', AgentSkillNames.MAX_LENGTH) + " tail";

        string? normalized = AgentSkillNames.Normalize(raw);

        Assert.NotNull(normalized);
        Assert.Equal(AgentSkillNames.MAX_LENGTH, normalized.Length);
        Assert.DoesNotContain('-', normalized);
    }

    [Fact]
    public void Normalize_IsIdempotent()
    {
        string? once = AgentSkillNames.Normalize("Income Statement.md");

        Assert.Equal(once, AgentSkillNames.Normalize(once));
    }
}
