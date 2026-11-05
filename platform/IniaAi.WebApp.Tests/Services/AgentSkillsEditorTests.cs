using IniaAi.Domain.Entities.Workflows.Blocks.Properties;
using IniaAi.WebApp.Services;

namespace IniaAi.WebApp.Tests.Services;

public class AgentSkillsEditorTests
{
    private static AgentWorkerSkill Skill(string name, string content, string description = "Does the thing.")
    {
        return new AgentWorkerSkill { Name = name, Description = description, Content = content };
    }

    private static string ContentOf(List<AgentWorkerSkill> skills, string name)
    {
        return skills.Single(skill => skill.Name == name).Content;
    }

    #region ToEntries

    [Fact]
    public void ToEntries_MapsNameDescriptionAndContent()
    {
        List<AgentWorkerSkill> workerSkills =
        [
            Skill("extraction", "Extract the things.", "Extracts things. Use for extraction."),
            Skill("summary", "Summarize the things.", "Summarizes things. Use for summaries."),
        ];

        List<AgentSkillsEditor.Entry> entries = AgentSkillsEditor.ToEntries(workerSkills);

        Assert.Equal(2, entries.Count);
        Assert.Contains(entries, e =>
            e.Name == "extraction"
            && e.Content == "Extract the things."
            && e.Description == "Extracts things. Use for extraction.");
        Assert.Contains(entries, e => e.Name == "summary" && e.Content == "Summarize the things.");
    }

    [Fact]
    public void ToEntries_EmptyList_ReturnsEmptyList()
    {
        Assert.Empty(AgentSkillsEditor.ToEntries([]));
    }

    #endregion

    #region BuildWorkerSkills

    [Fact]
    public void BuildWorkerSkills_TrimsNames()
    {
        List<AgentSkillsEditor.Entry> entries =
        [
            new() { Name = "  extraction  ", Description = "Extracts.", Content = "Extract the things." },
        ];

        List<AgentWorkerSkill> skills = AgentSkillsEditor.BuildWorkerSkills(entries);

        Assert.Equal("Extract the things.", ContentOf(skills, "extraction"));
    }

    [Fact]
    public void BuildWorkerSkills_KeepsTheDescription()
    {
        List<AgentSkillsEditor.Entry> entries =
        [
            new() { Name = "extraction", Description = "  Extracts figures. Use for totals.  ", Content = "Do it." },
        ];

        AgentWorkerSkill skill = Assert.Single(AgentSkillsEditor.BuildWorkerSkills(entries));

        Assert.Equal("Extracts figures. Use for totals.", skill.Description);
    }

    [Theory]
    [InlineData("SKILL.md")]
    [InlineData("Income Statement")]
    [InlineData("extraction_v2")]
    public void BuildWorkerSkills_KeepsTheAuthoredNameVerbatim(string authored)
    {
        List<AgentSkillsEditor.Entry> entries =
        [
            new() { Name = authored, Description = "Does the thing.", Content = "Do it." },
        ];

        AgentWorkerSkill skill = Assert.Single(AgentSkillsEditor.BuildWorkerSkills(entries));

        Assert.Equal(authored, skill.Name);
    }

    [Fact]
    public void BuildWorkerSkills_DropsOnlyBlankNames()
    {
        List<AgentSkillsEditor.Entry> entries =
        [
            new() { Name = "", Description = "d", Content = "Orphaned content." },
            new() { Name = "   ", Description = "d", Content = "Also orphaned." },
            new() { Name = "!!!", Description = "d", Content = "Kept for the guard to refuse." },
            new() { Name = "extraction", Description = "d", Content = "Extract the things." },
        ];

        List<AgentWorkerSkill> skills = AgentSkillsEditor.BuildWorkerSkills(entries);

        Assert.Equal(["!!!", "extraction"], skills.Select(skill => skill.Name));
    }

    [Fact]
    public void BuildWorkerSkills_DuplicateNames_LastWins()
    {
        List<AgentSkillsEditor.Entry> entries =
        [
            new() { Name = "extraction", Description = "d", Content = "First version." },
            new() { Name = "extraction", Description = "d", Content = "Second version." },
        ];

        List<AgentWorkerSkill> skills = AgentSkillsEditor.BuildWorkerSkills(entries);

        Assert.Equal("Second version.", Assert.Single(skills).Content);
    }

    [Fact]
    public void BuildWorkerSkills_NamesCollidingOnOneIdentity_AreBothKept()
    {
        List<AgentSkillsEditor.Entry> entries =
        [
            new() { Name = "SKILL.md", Description = "d", Content = "First version." },
            new() { Name = "skill", Description = "d", Content = "Second version." },
        ];

        List<AgentWorkerSkill> skills = AgentSkillsEditor.BuildWorkerSkills(entries);

        Assert.Equal(["SKILL.md", "skill"], skills.Select(skill => skill.Name));
        Assert.Equal("First version.", skills[0].Content);
    }

    [Fact]
    public void BuildWorkerSkills_KeepsAuthoredOrder_EvenWhenALaterRowWins()
    {
        List<AgentSkillsEditor.Entry> entries =
        [
            new() { Name = "alpha", Description = "d", Content = "First." },
            new() { Name = "beta", Description = "d", Content = "Second." },
            new() { Name = "alpha", Description = "d", Content = "Alpha again." },
        ];

        List<AgentWorkerSkill> skills = AgentSkillsEditor.BuildWorkerSkills(entries);

        Assert.Equal(["alpha", "beta"], skills.Select(skill => skill.Name));
        Assert.Equal("Alpha again.", skills[0].Content);
    }

    [Fact]
    public void BuildWorkerSkills_NoEntries_ReturnsEmptyListNotNull()
    {
        List<AgentWorkerSkill> skills = AgentSkillsEditor.BuildWorkerSkills([]);

        Assert.NotNull(skills);
        Assert.Empty(skills);
    }

    [Fact]
    public void BuildWorkerSkills_NullContentAndDescription_TreatedAsEmptyStrings()
    {
        List<AgentSkillsEditor.Entry> entries =
        [
            new() { Name = "extraction", Description = null!, Content = null! },
        ];

        AgentWorkerSkill skill = Assert.Single(AgentSkillsEditor.BuildWorkerSkills(entries));

        Assert.Equal(string.Empty, skill.Content);
        Assert.Equal(string.Empty, skill.Description);
    }

    [Fact]
    public void ToEntries_MapsAssets()
    {
        List<AgentWorkerSkill> workerSkills =
        [
            new AgentWorkerSkill
            {
                Name = "extraction",
                Description = "Extracts.",
                Content = "Run the script.",
                Assets = [new AgentWorkerSkillAsset { Name = "verify.py", Content = "print(1)" }],
            },
        ];

        AgentSkillsEditor.Entry entry = Assert.Single(AgentSkillsEditor.ToEntries(workerSkills));

        AgentSkillsEditor.AssetEntry asset = Assert.Single(entry.Assets);
        Assert.Equal("verify.py", asset.Name);
        Assert.Equal("print(1)", asset.Content);
    }

    [Fact]
    public void ToEntries_SkillWithoutAssets_HasAnEmptyAssetList()
    {
        List<AgentSkillsEditor.Entry> entries = AgentSkillsEditor.ToEntries(
            [Skill("extraction", "Do it.")]);

        Assert.Empty(Assert.Single(entries).Assets);
    }

    [Fact]
    public void BuildWorkerSkills_KeepsAssetsAndTrimsTheirNames()
    {
        List<AgentSkillsEditor.Entry> entries =
        [
            new()
            {
                Name = "extraction",
                Description = "d",
                Content = "Run the script.",
                Assets = [new() { Name = "  verify.py  ", Content = "print(1)" }],
            },
        ];

        AgentWorkerSkill skill = Assert.Single(AgentSkillsEditor.BuildWorkerSkills(entries));

        AgentWorkerSkillAsset asset = Assert.Single(skill.Assets!);
        Assert.Equal("verify.py", asset.Name);
        Assert.Equal("print(1)", asset.Content);
    }

    [Fact]
    public void BuildWorkerSkills_DropsAssetRowsWithBlankNames()
    {
        List<AgentSkillsEditor.Entry> entries =
        [
            new()
            {
                Name = "extraction",
                Description = "d",
                Content = "Run the script.",
                Assets =
                [
                    new() { Name = "   ", Content = "orphaned" },
                    new() { Name = "verify.py", Content = "print(1)" },
                ],
            },
        ];

        AgentWorkerSkill skill = Assert.Single(AgentSkillsEditor.BuildWorkerSkills(entries));

        Assert.Equal("verify.py", Assert.Single(skill.Assets!).Name);
    }

    [Fact]
    public void TheFillTagsFlag_SurvivesTheEditorRoundTrip_AndDefaultsOff()
    {
        List<AgentWorkerSkill> workerSkills =
        [
            new AgentWorkerSkill
            {
                Name = "extraction",
                Description = "Extracts.",
                Content = "Run the scripts.",
                Assets =
                [
                    new AgentWorkerSkillAsset { Name = "filled.py", Content = "x = '{{fund}}'", FillTags = true },
                    new AgentWorkerSkillAsset { Name = "verify.py", Content = "print(1)" },
                ],
            },
        ];

        List<AgentSkillsEditor.Entry> entries = AgentSkillsEditor.ToEntries(workerSkills);
        AgentWorkerSkill rebuilt = Assert.Single(AgentSkillsEditor.BuildWorkerSkills(entries));

        Assert.True(rebuilt.Assets![0].FillTags);
        Assert.False(rebuilt.Assets[1].FillTags);
        Assert.False(new AgentSkillsEditor.AssetEntry().FillTags);
    }

    [Fact]
    public void BuildWorkerSkills_NoAssetRows_LeavesAssetsNull()
    {
        List<AgentSkillsEditor.Entry> entries =
        [
            new() { Name = "extraction", Description = "d", Content = "Do it." },
        ];

        AgentWorkerSkill skill = Assert.Single(AgentSkillsEditor.BuildWorkerSkills(entries));

        Assert.Null(skill.Assets);
    }

    [Fact]
    public void AssetsRoundTripThroughEntries()
    {
        List<AgentWorkerSkill> original =
        [
            new AgentWorkerSkill
            {
                Name = "extraction",
                Description = "Extracts.",
                Content = "Run the script.",
                Assets = [new AgentWorkerSkillAsset { Name = "verify.py", Content = "print(1)" }],
            },
        ];

        List<AgentWorkerSkill> rebuilt = AgentSkillsEditor.BuildWorkerSkills(
            AgentSkillsEditor.ToEntries(original));

        AgentWorkerSkillAsset asset = Assert.Single(Assert.Single(rebuilt).Assets!);
        Assert.Equal("verify.py", asset.Name);
        Assert.Equal("print(1)", asset.Content);
    }

    [Fact]
    public void BuildWorkerSkills_RoundTripsThroughToEntries()
    {
        List<AgentWorkerSkill> original =
        [
            Skill("extraction", "Extract the things.", "Extracts things."),
            Skill("summary", "Summarize the things.", "Summarizes things."),
        ];

        List<AgentSkillsEditor.Entry> entries = AgentSkillsEditor.ToEntries(original);
        List<AgentWorkerSkill> rebuilt = AgentSkillsEditor.BuildWorkerSkills(entries);

        Assert.Equal(original.Count, rebuilt.Count);
        Assert.Equal("Extract the things.", ContentOf(rebuilt, "extraction"));
        Assert.Equal("Summarize the things.", ContentOf(rebuilt, "summary"));
        Assert.Equal("Extracts things.", rebuilt.Single(skill => skill.Name == "extraction").Description);
    }

    #endregion
}
