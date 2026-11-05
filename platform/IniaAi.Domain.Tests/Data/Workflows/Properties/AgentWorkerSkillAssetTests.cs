using IniaAi.Domain.Entities.Workflows.Blocks.Properties;
using Newtonsoft.Json;

namespace IniaAi.Domain.Tests.Data.Workflows.Properties;

public class AgentWorkerSkillAssetTests
{
    private const string TAG_SHAPED_PYTHON = """
        #!/usr/bin/env python3
        # A tag-shaped string in a script must never become an engine tag: {{PortfolioSummary}}
        print("{{NotATag}}")
        """;

    private static AgentWorkerSkill SkillWithAsset(string assetName = "verify.py")
    {
        return new AgentWorkerSkill
        {
            Name = "extraction",
            Description = "Extracts figures.",
            Content = "Run the staged script.",
            Assets = [new AgentWorkerSkillAsset { Name = assetName, Content = TAG_SHAPED_PYTHON }],
        };
    }

    [Fact]
    public void Clone_DeepCopiesAssets()
    {
        AgentWorkerSkill skill = SkillWithAsset();

        AgentWorkerSkill clone = skill.Clone();
        clone.Assets![0].Content = "mutated";

        Assert.Equal(TAG_SHAPED_PYTHON, skill.Assets![0].Content);
        Assert.Equal("verify.py", clone.Assets[0].Name);
    }

    [Fact]
    public void EmptyAssetsList_NormalizesToNull()
    {
        var skill = new AgentWorkerSkill { Name = "extraction", Assets = [] };

        Assert.Null(skill.Assets);
    }

    [Fact]
    public void NullAssets_StaysNull()
    {
        var skill = new AgentWorkerSkill { Name = "extraction", Assets = null };

        Assert.Null(skill.Assets);
    }

    [Fact]
    public void GetAllPlaceholders_DoesNotWalkAssetContent()
    {
        var props = new AgentProperties();
        props.WorkerSkills.Add(SkillWithAsset());

        List<string> placeholders = props.GetAllPlaceholders();

        Assert.DoesNotContain("PortfolioSummary", placeholders);
        Assert.DoesNotContain("NotATag", placeholders);
    }

    [Fact]
    public void Newtonsoft_SkillWithoutAssets_SerializesWithoutAnAssetsKey()
    {
        var skill = new AgentWorkerSkill
        {
            Name = "extraction",
            Description = "Extracts figures.",
            Content = "Body.",
        };

        string json = JsonConvert.SerializeObject(skill);

        Assert.DoesNotContain("Assets", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Newtonsoft_LegacySkillJson_RoundTripsByteIdentically()
    {
        const string legacyJson = """{"Name":"extraction","Description":"Extracts figures.","Content":"Body."}""";

        AgentWorkerSkill? skill = JsonConvert.DeserializeObject<AgentWorkerSkill>(legacyJson);

        Assert.NotNull(skill);
        Assert.Null(skill.Assets);
        Assert.Equal(legacyJson, JsonConvert.SerializeObject(skill));
    }

    [Fact]
    public void Newtonsoft_AssetsRoundTrip()
    {
        AgentWorkerSkill skill = SkillWithAsset();

        string json = JsonConvert.SerializeObject(skill);
        AgentWorkerSkill? roundTripped = JsonConvert.DeserializeObject<AgentWorkerSkill>(json);

        Assert.NotNull(roundTripped);
        AgentWorkerSkillAsset asset = Assert.Single(roundTripped.Assets!);
        Assert.Equal("verify.py", asset.Name);
        Assert.Equal(TAG_SHAPED_PYTHON, asset.Content);
    }

    [Fact]
    public void Clone_CarriesTheFillTagsFlag()
    {
        var asset = new AgentWorkerSkillAsset { Name = "filled.py", Content = "x = '{{fund}}'", FillTags = true };

        Assert.True(asset.Clone().FillTags);
        Assert.False(new AgentWorkerSkillAsset { Name = "verify.py", Content = "x" }.Clone().FillTags);
    }

    [Fact]
    public void GetAllPlaceholders_WalksTheContentOfAFileFlaggedToFillTags_Only()
    {
        AgentWorkerSkill skill = SkillWithAsset();
        skill.Assets!.Add(new AgentWorkerSkillAsset
        {
            Name = "filled.py",
            Content = "TERMS = '{{fund_terms.answer}}'\nprint(f'{TERMS}')",
            FillTags = true,
        });
        var props = new AgentProperties();
        props.WorkerSkills.Add(skill);

        List<string> placeholders = props.GetAllPlaceholders();

        Assert.Contains("fund_terms.answer", placeholders);
        Assert.DoesNotContain("PortfolioSummary", placeholders);
        Assert.DoesNotContain("NotATag", placeholders);
    }

    [Fact]
    public void VerificationGetAllPlaceholders_WalksAFlaggedFile_AndSkipsAnUnflaggedOne()
    {
        AgentWorkerSkill skill = SkillWithAsset();
        skill.Assets!.Add(new AgentWorkerSkillAsset { Name = "filled.py", Content = "'{{fund_terms}}'", FillTags = true });
        var props = new VerificationProperties();
        props.WorkerSkills.Add(skill);

        List<string> placeholders = props.GetAllPlaceholders();

        Assert.Contains("fund_terms", placeholders);
        Assert.DoesNotContain("NotATag", placeholders);
    }

    [Fact]
    public void TransformAgentTemplateTags_RewritesAFlaggedFile_AndLeavesAnUnflaggedOneByteIdentical()
    {
        AgentWorkerSkill skill = SkillWithAsset();
        skill.Assets!.Add(new AgentWorkerSkillAsset { Name = "filled.py", Content = "'{{facts}}' {facts}", FillTags = true });
        var props = new AgentProperties();
        props.WorkerSkills.Add(skill);

        props.TransformAgentTemplateTags(static text => text.Replace("{facts}", "{facts_0}", StringComparison.Ordinal));

        Assert.Equal("'{{facts_0}}' {facts}", props.WorkerSkills[0].Assets![1].Content);
        Assert.Equal(TAG_SHAPED_PYTHON, props.WorkerSkills[0].Assets![0].Content);
    }

    [Fact]
    public void Newtonsoft_UnflaggedAsset_SerializesWithoutAFillTagsKey()
    {
        AgentWorkerSkill skill = SkillWithAsset();

        string json = JsonConvert.SerializeObject(skill);

        Assert.DoesNotContain("FillTags", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Newtonsoft_AssetJsonSavedBeforeTheFlag_RoundTripsByteIdentically()
    {
        const string legacyJson =
            """{"Name":"extraction","Description":"d","Content":"c","Assets":[{"Name":"verify.py","Content":"print('{{x}}')"}]}""";

        AgentWorkerSkill? skill = JsonConvert.DeserializeObject<AgentWorkerSkill>(legacyJson);

        Assert.NotNull(skill);
        Assert.False(Assert.Single(skill.Assets!).FillTags);
        Assert.Equal(legacyJson, JsonConvert.SerializeObject(skill));
    }

    [Fact]
    public void Newtonsoft_FlaggedAsset_RoundTrips()
    {
        AgentWorkerSkill skill = SkillWithAsset();
        skill.Assets![0].FillTags = true;

        string json = JsonConvert.SerializeObject(skill);
        AgentWorkerSkill? roundTripped = JsonConvert.DeserializeObject<AgentWorkerSkill>(json);

        Assert.Contains("\"FillTags\":true", json);
        Assert.True(Assert.Single(roundTripped!.Assets!).FillTags);
    }
}
