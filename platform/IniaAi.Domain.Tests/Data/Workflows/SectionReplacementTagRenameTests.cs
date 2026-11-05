using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Blocks;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties;
using IniaAi.Domain.Entities.Workflows.Enums;

namespace IniaAi.Domain.Tests.Data.Workflows;

public class SectionReplacementTagRenameTests
{
    private static Section TwoBlockSection(out Block alpha, out Block beta)
    {
        var workflow = new Workflow("root");
        var section = new Section("s", workflow) { Title = "s" };
        alpha = new Block(BlockTypes.AiQuery, section, "alpha")
        {
            Properties = new AiQueryProperties { UserPromptTemplate = "Use {beta} and {beta.answer}" },
        };
        beta = new Block(BlockTypes.AiQuery, section, "beta")
        {
            Properties = new AiQueryProperties { UserPromptTemplate = "Standalone" },
        };

        return section;
    }

    [Fact]
    public void ApplyReplacementTagRenames_RenamesBlockTag_AndRewritesBareAndDottedReferences()
    {
        Section section = TwoBlockSection(out Block alpha, out Block beta);

        section.ApplyReplacementTagRenames(new Dictionary<string, string> { ["beta"] = "beta_2" });

        Assert.Equal("beta_2", beta.ReplacementTag);
        string prompt = ((AiQueryProperties)alpha.Properties!).UserPromptTemplate!;
        Assert.Contains("{beta_2}", prompt);
        Assert.Contains("{beta_2.answer}", prompt);
        Assert.DoesNotContain("{beta}", prompt);
        Assert.DoesNotContain("{beta.", prompt);
    }

    [Fact]
    public void ApplyReplacementTagRenames_MultipleRenames_AppliedInOnePass()
    {
        var workflow = new Workflow("root");
        var section = new Section("s", workflow) { Title = "s" };
        var first = new Block(BlockTypes.AiQuery, section, "first")
        {
            Properties = new AiQueryProperties { UserPromptTemplate = "{second.value}" },
        };
        var second = new Block(BlockTypes.AiQuery, section, "second")
        {
            Properties = new AiQueryProperties { UserPromptTemplate = "{first}" },
        };

        section.ApplyReplacementTagRenames(new Dictionary<string, string>
        {
            ["first"] = "first_2",
            ["second"] = "second_2",
        });

        Assert.Equal("first_2", first.ReplacementTag);
        Assert.Equal("second_2", second.ReplacementTag);
        Assert.Equal("{second_2.value}", ((AiQueryProperties)first.Properties!).UserPromptTemplate);
        Assert.Equal("{first_2}", ((AiQueryProperties)second.Properties!).UserPromptTemplate);
    }

    [Fact]
    public void ApplyReplacementTagRenames_LinkFieldsStayUntouched()
    {
        Section section = TwoBlockSection(out Block alpha, out Block _);
        alpha.SharedComponentId = "component-1";
        alpha.SharedComponentBindings = new Dictionary<string, string> { ["slot"] = "{beta}" };
        section.SharedComponentBindings = new Dictionary<string, string> { ["slot"] = "{beta}" };

        section.ApplyReplacementTagRenames(new Dictionary<string, string> { ["beta"] = "beta_2" });

        Assert.Equal("component-1", alpha.SharedComponentId);
        Assert.Equal("{beta}", alpha.SharedComponentBindings["slot"]);
        Assert.Equal("{beta}", section.SharedComponentBindings["slot"]);
        Assert.Contains("{beta_2}", ((AiQueryProperties)alpha.Properties!).UserPromptTemplate);
    }

    [Fact]
    public void ApplyReplacementTagRenames_EmptyMap_ChangesNothing()
    {
        Section section = TwoBlockSection(out Block alpha, out Block beta);

        section.ApplyReplacementTagRenames(new Dictionary<string, string>());

        Assert.Equal("beta", beta.ReplacementTag);
        Assert.Equal("Use {beta} and {beta.answer}", ((AiQueryProperties)alpha.Properties!).UserPromptTemplate);
    }

    [Fact]
    public void ApplyReplacementTagRenames_RenameTargetEqualsAnotherSource_NoCascade()
    {
        var workflow = new Workflow("root");
        var section = new Section("s", workflow) { Title = "s" };
        var blockA = new Block(BlockTypes.AiQuery, section, "a")
        {
            Properties = new AiQueryProperties { UserPromptTemplate = "Standalone" },
        };
        var blockA2 = new Block(BlockTypes.AiQuery, section, "a_2")
        {
            Properties = new AiQueryProperties { UserPromptTemplate = "Standalone" },
        };
        var probe = new Block(BlockTypes.AiQuery, section, "probe")
        {
            Properties = new AiQueryProperties { UserPromptTemplate = "Use {a} then {a_2} and {a.x} {a_2.x}" },
        };

        section.ApplyReplacementTagRenames(new Dictionary<string, string>
        {
            ["a"] = "a_2",
            ["a_2"] = "a_3",
        });

        Assert.Equal("a_2", blockA.ReplacementTag);
        Assert.Equal("a_3", blockA2.ReplacementTag);
        Assert.Equal(
            "Use {a_2} then {a_3} and {a_2.x} {a_3.x}",
            ((AiQueryProperties)probe.Properties!).UserPromptTemplate);
    }

    [Fact]
    public void RenameTagReferences_RewritesReferences_LeavesBlockTagsUntouched()
    {
        Section section = TwoBlockSection(out Block alpha, out Block beta);
        section.TextInputTemplate = "Fill {beta} here";

        section.RenameTagReferences(new Dictionary<string, string> { ["beta"] = "beta_2" });

        Assert.Equal("beta", beta.ReplacementTag);
        Assert.Equal("Fill {beta_2} here", section.TextInputTemplate);
        string prompt = ((AiQueryProperties)alpha.Properties!).UserPromptTemplate!;
        Assert.Contains("{beta_2}", prompt);
        Assert.Contains("{beta_2.answer}", prompt);
    }

    [Fact]
    public void RenameTagReferences_ChainedMap_AppliesSimultaneously()
    {
        Section section = TwoBlockSection(out Block alpha, out Block _);
        ((AiQueryProperties)alpha.Properties!).UserPromptTemplate = "See {a} and {a_2}";

        section.RenameTagReferences(new Dictionary<string, string>
        {
            ["a"] = "a_2",
            ["a_2"] = "a_3",
        });

        Assert.Equal("See {a_2} and {a_3}", ((AiQueryProperties)alpha.Properties!).UserPromptTemplate);
    }
}
