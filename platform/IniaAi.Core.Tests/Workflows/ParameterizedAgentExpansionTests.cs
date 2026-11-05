using IniaAi.Core.Workflows;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Blocks;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties;
using IniaAi.Domain.Entities.Workflows.Enums;

namespace IniaAi.Core.Tests.Workflows;

public class ParameterizedAgentExpansionTests
{
    private const string SKILL_CONTENT = """
        Facts: {{facts}}
        Answer: {{facts.answer}}
        Region: {{_REGION}}
        Period: {{_PERIOD}}
        Outside: {{upstream}}
        Literal: {"region": "{_REGION}"} and {facts}
        """;

    private static ParameterizedSectionConfig Config()
    {
        return new ParameterizedSectionConfig(
        [
            new SectionVariant("N", "North", new() { ["_REGION"] = "North", ["_PERIOD"] = "{InterimPeriod} month" },
                ClassMap: new() { ["Statement"] = ["Statement North"], ["Workbook"] = ["Workbook North"] }),
            new SectionVariant("S", "South", new() { ["_REGION"] = "South", ["_PERIOD"] = "full year" },
                ClassMap: new() { ["Statement"] = ["Statement South"], ["Workbook"] = ["Workbook South"] }),
        ]);
    }

    private static Section BuildSection()
    {
        var workflow = new Workflow("root");
        var section = new Section("Per region", workflow) { Title = "Per region" };

        _ = new Block(BlockTypes.Merge, section, "facts")
        {
            Properties = new MergeProperties { TextTemplate = "Region {_REGION}" },
        };

        var agent = new Block(BlockTypes.Agent, section, "summary") { StableBlockId = "summary-stable" };
        var props = (AgentProperties)agent.Properties;
        props.WorkerSkills =
        [
            new AgentWorkerSkill
            {
                Name = "region-summary",
                Description = "Summarises {{_REGION}} from {{facts}}.",
                Content = SKILL_CONTENT,
                Assets = [new AgentWorkerSkillAsset { Name = "check.py", Content = "print('{{facts}}')" }],
            },
        ];
        props.PromptTemplate = "Summarise {{facts}} for {{_REGION}}.";
        props.SystemPromptTemplate = "You cover {{_REGION}}.";

        _ = new Block(BlockTypes.MergeJson, section, "merged")
        {
            Properties = new MergeJsonProperties { JsonInputs = ["{summary}", "{facts.answer}"] },
        };

        _ = new Block(BlockTypes.Json2Xlsx, section, "sheet")
        {
            Properties = new Json2XlsxProperties { CreatesDocumentClass = "Workbook", InputDocumentClass = "Statement" },
        };

        return section;
    }

    private static Block Single(Section section, string tag)
    {
        return section.Blocks.Single(b => b.ReplacementTag == tag);
    }

    [Fact]
    public void Expand_EachVariantsAgent_ReadsItsOwnSiblingsAndParameters()
    {
        List<Section> variants = ParameterizedSectionExpander.Expand(BuildSection(), Config());

        var north = (AgentProperties)Single(variants[0], "summary_N").Properties;
        var south = (AgentProperties)Single(variants[1], "summary_S").Properties;

        Assert.Contains("Facts: {{facts_N}}", north.WorkerSkills[0].Content);
        Assert.Contains("Answer: {{facts_N.answer}}", north.WorkerSkills[0].Content);
        Assert.Contains("Region: North", north.WorkerSkills[0].Content);
        Assert.Contains("Period: {{InterimPeriod}} month", north.WorkerSkills[0].Content);
        Assert.Contains("Facts: {{facts_S}}", south.WorkerSkills[0].Content);
        Assert.Contains("Region: South", south.WorkerSkills[0].Content);
        Assert.Contains("Period: full year", south.WorkerSkills[0].Content);
        Assert.Equal("Summarises South from {{facts_S}}.", south.WorkerSkills[0].Description);
        Assert.Equal("Summarise {{facts_N}} for North.", north.PromptTemplate);
        Assert.Equal("You cover South.", south.SystemPromptTemplate);
    }

    [Fact]
    public void Expand_LeavesSingleBraceTextOutsideTagsAndAssetsAsWritten()
    {
        Section template = BuildSection();
        List<Section> variants = ParameterizedSectionExpander.Expand(template, Config());

        AgentWorkerSkill skill = ((AgentProperties)Single(variants[0], "summary_N").Properties).WorkerSkills[0];

        Assert.Contains("Outside: {{upstream}}", skill.Content);
        Assert.Contains("""Literal: {"region": "{_REGION}"} and {facts}""", skill.Content);
        Assert.Equal("print('{{facts}}')", skill.Assets![0].Content);
        Assert.Equal(SKILL_CONTENT, ((AgentProperties)Single(template, "summary").Properties).WorkerSkills[0].Content);
    }

    [Fact]
    public void Expand_AFileFlaggedToFillTags_IsRewrittenPerVariantLikeSkillContent()
    {
        Section template = BuildSection();
        const string flaggedScript = "REGION = '{{_REGION}}'\nFACTS = '{{facts.answer}}'\nprint(f'{REGION}')";
        ((AgentProperties)Single(template, "summary").Properties).WorkerSkills[0].Assets!.Add(
            new AgentWorkerSkillAsset { Name = "filled.py", Content = flaggedScript, FillTags = true });

        List<Section> variants = ParameterizedSectionExpander.Expand(template, Config());

        AgentWorkerSkill north = ((AgentProperties)Single(variants[0], "summary_N").Properties).WorkerSkills[0];
        AgentWorkerSkill south = ((AgentProperties)Single(variants[1], "summary_S").Properties).WorkerSkills[0];

        Assert.Equal("REGION = 'North'\nFACTS = '{{facts_N.answer}}'\nprint(f'{REGION}')", north.Assets![1].Content);
        Assert.Equal("REGION = 'South'\nFACTS = '{{facts_S.answer}}'\nprint(f'{REGION}')", south.Assets![1].Content);
        Assert.True(south.Assets[1].FillTags);
        Assert.Equal("print('{{facts}}')", south.Assets[0].Content);
        Assert.Equal(flaggedScript, ((AgentProperties)Single(template, "summary").Properties).WorkerSkills[0].Assets![1].Content);
    }

    [Fact]
    public void Expand_EachAgentCopy_HasItsOwnIdentityAndDependencies()
    {
        List<Section> variants = ParameterizedSectionExpander.Expand(BuildSection(), Config());

        Block north = Single(variants[0], "summary_N");
        Block south = Single(variants[1], "summary_S");

        Assert.NotEqual(north.Id, south.Id);
        Assert.Equal("summary-stable_N", north.StableBlockId);
        Assert.Equal("summary-stable_S", south.StableBlockId);
        Assert.Contains("facts_S", south.GetDependencyReplacementTags());
        Assert.DoesNotContain("facts", south.GetDependencyReplacementTags());
        Assert.DoesNotContain("facts_N", south.GetDependencyReplacementTags());
    }

    [Fact]
    public void Expand_MergeJsonInputs_PointAtTheVariantsTags()
    {
        List<Section> variants = ParameterizedSectionExpander.Expand(BuildSection(), Config());

        var south = (MergeJsonProperties)Single(variants[1], "merged_S").Properties;

        Assert.Equal(["{summary_S}", "{facts_S.answer}"], south.JsonInputs);
    }

    [Fact]
    public void Expand_Json2XlsxClasses_MapThroughTheVariantsClassMap()
    {
        List<Section> variants = ParameterizedSectionExpander.Expand(BuildSection(), Config());

        var north = (Json2XlsxProperties)Single(variants[0], "sheet_N").Properties;
        var south = (Json2XlsxProperties)Single(variants[1], "sheet_S").Properties;

        Assert.Equal("Workbook North", north.CreatesDocumentClass);
        Assert.Equal("Statement North", north.InputDocumentClass);
        Assert.Equal("Workbook South", south.CreatesDocumentClass);
        Assert.Equal("Statement South", south.InputDocumentClass);
    }

    [Fact]
    public void Expand_Json2XlsxClassesWithoutAClassMap_StayAsAuthored()
    {
        var config = new ParameterizedSectionConfig([new SectionVariant("A", "A"), new SectionVariant("B", "B")]);

        List<Section> variants = ParameterizedSectionExpander.Expand(BuildSection(), config);

        var b = (Json2XlsxProperties)Single(variants[1], "sheet_B").Properties;
        Assert.Equal("Workbook", b.CreatesDocumentClass);
        Assert.Equal("Statement", b.InputDocumentClass);
    }
}
