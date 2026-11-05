using IniaAi.Common.Interfaces.Services.AgentRuntime;

namespace IniaAi.Infrastructure.Tests.Services;

/// <summary>
/// Pins what counts as a skill invocation in prompt text and what stays ordinary prose. The
/// resolution rule mirrors the runtime: a skill registers under its normalized identity inside
/// the runtime's "skills" plugin, so <c>/skills:&lt;identity&gt;</c> is the only form that
/// resolves, and it resolves only against the block's own skill set, spelled exactly.
/// </summary>
public class AgentSkillInvocationsTests
{
    private static readonly HashSet<string> IDENTITIES =
        new(["extraction", "income-statement"], StringComparer.Ordinal);

    private static List<AgentSkillInvocations.InvocationProblem> Find(string? text)
    {
        return AgentSkillInvocations.FindProblems(text, IDENTITIES);
    }

    [Theory]
    [InlineData("/skills:extraction")]
    [InlineData("Use /skills:extraction on the filing.")]
    [InlineData("Run /skills:income-statement, then summarise.")]
    [InlineData("First (/skills:extraction) then check.")]
    public void PrefixedInvocationOfABlockSkill_IsNoProblem(string text)
    {
        Assert.Empty(Find(text));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Plain prose without any slash at all.")]
    [InlineData("Read /docs/report.pdf and /workspaces/out/result.json first.")]
    [InlineData("word1 /word2 word3")]
    [InlineData("See https://example.com/skills:extraction for details.")]
    [InlineData("a/skills:extraction is not an invocation")]
    [InlineData("Mention skills:extraction without a slash is prose.")]
    [InlineData("/unrelated-name is not a skill of this block")]
    public void ProseAndPaths_AreNoProblem(string? text)
    {
        Assert.Empty(Find(text));
    }

    [Theory]
    [InlineData("/skills:{{skillName}}")]
    [InlineData("Use /skills:{{chosen-skill}} on the filing.")]
    [InlineData("Run /skills:pre{{tag}}post now.")]
    public void SubstitutionTagInTheName_IsNotResolvableAtSaveTime_AndIsSkipped(string text)
    {
        Assert.Empty(Find(text));
    }

    [Theory]
    [InlineData("Invoke skills with /skills:<name> as needed.")]
    [InlineData("Skill invocations use the form /skills:[skill-name].")]
    [InlineData("Write /skills:{identity} to invoke one.")]
    public void PlaceholderShapedName_IsDocumentationProse_AndIsSkipped(string text)
    {
        Assert.Empty(Find(text));
    }

    [Theory]
    [InlineData("**/skills:extraction** then check.")]
    [InlineData("Use /skills:extraction_ here.")]
    [InlineData("{/skills:extraction} in braces.")]
    [InlineData("See </skills:extraction> quoted.")]
    public void MarkdownEmphasisAndBracketProse_AroundABlockSkill_IsNoProblem(string text)
    {
        Assert.Empty(Find(text));
    }

    [Fact]
    public void UnknownPrefixedName_IsAnUnknownNameProblem()
    {
        AgentSkillInvocations.InvocationProblem problem = Assert.Single(Find("Use /skills:income-statment now."));

        Assert.Equal(AgentSkillInvocations.ProblemKind.UnknownName, problem.Kind);
        Assert.Equal("/skills:income-statment", problem.Token);
        Assert.Null(problem.ResolvableIdentity);
    }

    [Theory]
    [InlineData("/skills:Extraction", "/skills:Extraction")]
    [InlineData("/skills:extraction.md", "/skills:extraction.md")]
    [InlineData("/Skills:extraction", "/Skills:extraction")]
    public void MisspelledFormOfABlockSkill_IsUnknown_ButNamesTheResolvableIdentity(string text, string token)
    {
        AgentSkillInvocations.InvocationProblem problem = Assert.Single(Find(text));

        Assert.Equal(AgentSkillInvocations.ProblemKind.UnknownName, problem.Kind);
        Assert.Equal(token, problem.Token);
        Assert.Equal("extraction", problem.ResolvableIdentity);
    }

    [Theory]
    [InlineData("/skills:")]
    [InlineData("Invoke /skills: extraction")]
    public void PrefixWithoutAName_IsAMissingNameProblem(string text)
    {
        AgentSkillInvocations.InvocationProblem problem = Assert.Single(Find(text));

        Assert.Equal(AgentSkillInvocations.ProblemKind.MissingName, problem.Kind);
        Assert.Equal("/skills:", problem.Token);
    }

    [Theory]
    [InlineData("/extraction", "/extraction")]
    [InlineData("Start with /extraction.", "/extraction")]
    [InlineData("Use /extraction.md here.", "/extraction.md")]
    [InlineData("Then /Income-Statement please.", "/Income-Statement")]
    public void BareFormOfABlockSkill_IsABareFormProblem(string text, string token)
    {
        AgentSkillInvocations.InvocationProblem problem = Assert.Single(Find(text));

        Assert.Equal(AgentSkillInvocations.ProblemKind.BareForm, problem.Kind);
        Assert.Equal(token, problem.Token);
        Assert.NotNull(problem.ResolvableIdentity);
    }

    [Fact]
    public void BareFormResolvableIdentity_IsTheNormalizedBlockSkill()
    {
        AgentSkillInvocations.InvocationProblem problem = Assert.Single(Find("Then /Income-Statement please."));

        Assert.Equal("income-statement", problem.ResolvableIdentity);
    }

    [Fact]
    public void TheSameProblemRepeated_IsReportedOnce()
    {
        Assert.Single(Find("/skills:nope first, /skills:nope again"));
    }

    [Fact]
    public void DistinctProblems_AreAllReported()
    {
        List<AgentSkillInvocations.InvocationProblem> problems =
            Find("/skills:nope then /extraction then /skills:");

        Assert.Equal(3, problems.Count);
    }
}
