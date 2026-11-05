using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Blocks;
using IniaAi.Domain.Entities.Workflows.Enums;
using IniaAi.WebApp.Components.WorkflowEditor.Common;

namespace IniaAi.WebApp.Tests.Components.WorkflowEditor.Common;

public class DuplicateTagDisclosureTests
{
    private static Block AddBlock(Section section, string tag, string name)
    {
        return new Block(BlockTypes.AiQuery, section, tag) { Name = name };
    }

    [Fact]
    public void Describe_WhenTagIsUnique_ReturnsNull()
    {
        var workflow = new Workflow("w");
        var first = new Section("a", workflow) { Title = "Extraction" };
        var second = new Section("b", workflow) { Title = "Synthesis" };
        Block subject = AddBlock(first, "final_summary", "Combine Findings");
        _ = AddBlock(second, "other_tag", "Something Else");

        Assert.Null(DuplicateTagDisclosure.Describe(subject, workflow));
    }

    [Fact]
    public void Describe_WhenAnotherSectionSharesTheTag_NamesTheBlockAndItsSection()
    {
        var workflow = new Workflow("w");
        var first = new Section("a", workflow) { Title = "Extraction", Order = 0 };
        var second = new Section("b", workflow) { Title = "Synthesis", Order = 1 };
        Block subject = AddBlock(first, "final_summary", "Combine Findings");
        _ = AddBlock(second, "final_summary", "Summarise Everything");

        string? disclosure = DuplicateTagDisclosure.Describe(subject, workflow);

        Assert.NotNull(disclosure);
        Assert.Contains("Summarise Everything", disclosure);
        Assert.Contains("'1. Synthesis'", disclosure);
        Assert.Contains("runs first", disclosure);
    }

    [Fact]
    public void Describe_WhenSeveralSectionsShareTheTag_CountsTheRest()
    {
        var workflow = new Workflow("w");
        var first = new Section("a", workflow) { Title = "Extraction" };
        var second = new Section("b", workflow) { Title = "Synthesis" };
        var third = new Section("c", workflow) { Title = "Review" };
        Block subject = AddBlock(first, "final_summary", "Combine Findings");
        _ = AddBlock(second, "final_summary", "Summarise Everything");
        _ = AddBlock(third, "final_summary", "Recap");

        string? disclosure = DuplicateTagDisclosure.Describe(subject, workflow);

        Assert.NotNull(disclosure);
        Assert.Contains("1 other block", disclosure);
    }

    [Fact]
    public void Describe_WhenThreeOtherSectionsShareTheTag_PluralisesTheRemainder()
    {
        var workflow = new Workflow("w");
        var first = new Section("a", workflow) { Title = "Extraction" };
        var second = new Section("b", workflow) { Title = "Synthesis" };
        var third = new Section("c", workflow) { Title = "Review" };
        var fourth = new Section("d", workflow) { Title = "Output" };
        Block subject = AddBlock(first, "final_summary", "Combine Findings");
        _ = AddBlock(second, "final_summary", "Summarise Everything");
        _ = AddBlock(third, "final_summary", "Recap");
        _ = AddBlock(fourth, "final_summary", "Restate");

        string? disclosure = DuplicateTagDisclosure.Describe(subject, workflow);

        Assert.NotNull(disclosure);
        Assert.Contains("and 2 other blocks", disclosure);
    }

    [Fact]
    public void Describe_WhenTheSharingBlockHasNoName_FallsBackToItsTag()
    {
        var workflow = new Workflow("w");
        var first = new Section("a", workflow) { Title = "Extraction" };
        var second = new Section("b", workflow) { Title = "Synthesis" };
        Block subject = AddBlock(first, "final_summary", "Combine Findings");
        _ = AddBlock(second, "final_summary", string.Empty);

        string? disclosure = DuplicateTagDisclosure.Describe(subject, workflow);

        Assert.NotNull(disclosure);
        Assert.Contains("'final_summary'", disclosure);
    }

    [Fact]
    public void Describe_WhenTheSharingBlockIsInTheSameSection_ReturnsNull()
    {
        var workflow = new Workflow("w");
        var section = new Section("a", workflow) { Title = "Extraction" };
        Block subject = AddBlock(section, "final_summary", "Combine Findings");
        Block sibling = AddBlock(section, "unique_tag", "Sibling");
        sibling.ReplacementTag = "final_summary";

        Assert.Null(DuplicateTagDisclosure.Describe(subject, workflow));
    }

    [Fact]
    public void Describe_WithAnEmptyTag_ReturnsNull()
    {
        var workflow = new Workflow("w");
        var section = new Section("a", workflow) { Title = "Extraction" };
        Block subject = AddBlock(section, "placeholder", "Fresh");
        subject.ReplacementTag = string.Empty;

        Assert.Null(DuplicateTagDisclosure.Describe(subject, workflow));
    }

    [Fact]
    public void Describe_WithoutARootWorkflow_ReturnsNull()
    {
        var workflow = new Workflow("w");
        var section = new Section("a", workflow) { Title = "Extraction" };
        Block subject = AddBlock(section, "final_summary", "Combine Findings");

        Assert.Null(DuplicateTagDisclosure.Describe(subject, null));
    }

    [Fact]
    public void Describe_WhenTheSharingSectionHasNoTitle_FallsBackToItsName()
    {
        var workflow = new Workflow("w");
        var first = new Section("a", workflow) { Title = "Extraction" };
        var second = new Section("Fallback Name", workflow);
        Block subject = AddBlock(first, "final_summary", "Combine Findings");
        _ = AddBlock(second, "final_summary", "Summarise Everything");

        string? disclosure = DuplicateTagDisclosure.Describe(subject, workflow);

        Assert.NotNull(disclosure);
        Assert.Contains("Fallback Name", disclosure);
    }
}
