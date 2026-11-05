using IniaAi.Domain.Entities.SharedComponents.Enums;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Blocks;
using IniaAi.Domain.Entities.Workflows.Enums;

namespace IniaAi.Domain.Tests.Data.Workflows;

public class SharedComponentLinkFieldsTests
{
    private const string COMPONENT_ID = "cafe0000000000000000000000000001";
    private static readonly string _versionHash = new string('a', 60) + "cafe";

    private static void Link(Block block)
    {
        block.SharedComponentId = COMPONENT_ID;
        block.SharedComponentLinkState = SharedComponentLinkState.Linked;
        block.SharedComponentVersionHash = _versionHash;
        block.SharedComponentBindings = new Dictionary<string, string> { ["slot"] = "instance" };
    }

    private static void Link(Section section)
    {
        section.SharedComponentId = COMPONENT_ID;
        section.SharedComponentLinkState = SharedComponentLinkState.Linked;
        section.SharedComponentVersionHash = _versionHash;
        section.SharedComponentBindings = new Dictionary<string, string> { ["slot"] = "instance" };
    }

    private static void AssertLinked(Block block)
    {
        Assert.Equal(COMPONENT_ID, block.SharedComponentId);
        Assert.Equal(SharedComponentLinkState.Linked, block.SharedComponentLinkState);
        Assert.Equal(_versionHash, block.SharedComponentVersionHash);
        Assert.NotNull(block.SharedComponentBindings);
        Assert.Equal("instance", block.SharedComponentBindings["slot"]);
    }

    private static void AssertLinked(Section section)
    {
        Assert.Equal(COMPONENT_ID, section.SharedComponentId);
        Assert.Equal(SharedComponentLinkState.Linked, section.SharedComponentLinkState);
        Assert.Equal(_versionHash, section.SharedComponentVersionHash);
        Assert.NotNull(section.SharedComponentBindings);
        Assert.Equal("instance", section.SharedComponentBindings["slot"]);
    }

    private static Workflow LinkedWorkflow(out Section section, out Block block)
    {
        var workflow = new Workflow("root");
        section = new Section("s", workflow) { Title = "s" };
        block = new Block(BlockTypes.AiQuery, section, "tag");
        Link(section);
        Link(block);

        return workflow;
    }

    [Fact]
    public void CloneToSection_PreservesLinkFields()
    {
        _ = LinkedWorkflow(out Section section, out Block block);

        Block clone = block.CloneToSection(section);

        AssertLinked(clone);
    }

    [Fact]
    public void CloneToSection_BindingsAreAFreshInstance()
    {
        _ = LinkedWorkflow(out Section section, out Block block);

        Block clone = block.CloneToSection(section);
        clone.SharedComponentBindings!["slot"] = "mutated";

        Assert.Equal("instance", block.SharedComponentBindings!["slot"]);
    }

    [Fact]
    public void CopyToSection_ReMintsStableBlockId_ButPreservesLinkFields()
    {
        _ = LinkedWorkflow(out Section section, out Block block);

        Block copy = block.CopyToSection(section);

        Assert.NotEqual(block.StableBlockId, copy.StableBlockId);
        AssertLinked(copy);
    }

    [Fact]
    public void DuplicateBlockToSection_PreservesLinkFields()
    {
        _ = LinkedWorkflow(out Section section, out Block block);

        Block duplicate = block.DuplicateBlockToSection(section);

        Assert.NotEqual(block.StableBlockId, duplicate.StableBlockId);
        AssertLinked(duplicate);
    }

    [Fact]
    public void SuffixStableBlockId_LeavesLinkFieldsUntouched()
    {
        _ = LinkedWorkflow(out _, out Block block);

        block.SuffixStableBlockId("variant");

        AssertLinked(block);
    }

    [Fact]
    public void SuffixStableSectionId_LeavesLinkFieldsUntouched()
    {
        _ = LinkedWorkflow(out Section section, out _);

        section.SuffixStableSectionId("variant");

        AssertLinked(section);
    }

    [Fact]
    public void SectionCloneTo_PreservesLinkFields()
    {
        var workflow = new Workflow("root");
        var source = new Section("source", workflow);
        var destination = new Section("dest", workflow);
        Link(source);

        source.CloneTo(destination);

        AssertLinked(destination);
    }

    [Fact]
    public void WorkflowCloneTo_PreservesLinkFieldsOnSectionAndBlock()
    {
        Workflow source = LinkedWorkflow(out _, out _);
        var destination = new Workflow("destination");

        source.CloneTo(destination);

        AssertLinked(destination.Sections.Single());
        AssertLinked(destination.Sections.Single().Blocks.Single());
    }

    [Fact]
    public void CloneToWorkflow_ReMintsStableIds_ButPreservesLinkFields()
    {
        _ = LinkedWorkflow(out Section section, out Block block);
        var destination = new Workflow("destination");

        Section copied = section.CloneToWorkflow(destination, 0);

        Assert.NotEqual(section.StableSectionId, copied.StableSectionId);
        Assert.NotEqual(block.StableBlockId, copied.Blocks.Single().StableBlockId);
        AssertLinked(copied);
        AssertLinked(copied.Blocks.Single());
    }

    [Fact]
    public void DuplicateSectionAt_PreservesLinkFields()
    {
        Workflow workflow = LinkedWorkflow(out Section section, out _);

        Section duplicated = workflow.DuplicateSectionAt(section.Order);

        Assert.NotEqual(section.StableSectionId, duplicated.StableSectionId);
        AssertLinked(duplicated);
        AssertLinked(duplicated.Blocks.Single());
    }

    [Fact]
    public void JsonRoundTrip_PreservesLinkFields()
    {
        Workflow workflow = LinkedWorkflow(out _, out _);

        Workflow? roundTripped = Workflow.FromJson(workflow.ToJson(pretty: false));

        Assert.NotNull(roundTripped);
        AssertLinked(roundTripped.Sections.Single());
        AssertLinked(roundTripped.Sections.Single().Blocks.Single());
    }

    [Fact]
    public void ToExportJson_NullLinkFields_OmitsThePropertyNames()
    {
        var workflow = new Workflow("root");
        var section = new Section("s", workflow) { Title = "s" };
        _ = new Block(BlockTypes.AiQuery, section, "tag");

        string exported = workflow.ToExportJson();

        Assert.DoesNotContain("\"SharedComponentId\"", exported);
        Assert.DoesNotContain("\"SharedComponentLinkState\"", exported);
        Assert.DoesNotContain("\"SharedComponentVersionHash\"", exported);
        Assert.DoesNotContain("\"SharedComponentBindings\"", exported);
    }

    [Fact]
    public void ToExportJson_LinkedNodes_CarryTheExactPropertyNamesAndStringEnum()
    {
        Workflow workflow = LinkedWorkflow(out _, out _);

        string exported = workflow.ToExportJson();

        Assert.Contains("\"SharedComponentId\"", exported);
        Assert.Contains("\"SharedComponentLinkState\"", exported);
        Assert.Contains("\"SharedComponentVersionHash\"", exported);
        Assert.Contains("\"SharedComponentBindings\"", exported);
        Assert.Contains("\"Linked\"", exported);
    }

    [Fact]
    public void ToExportJson_EmptyBindings_AreOmitted()
    {
        Workflow workflow = LinkedWorkflow(out _, out Block block);
        block.SharedComponentBindings = [];

        string exported = workflow.ToExportJson();

        int bindingsOccurrences = exported.Split("\"SharedComponentBindings\"").Length - 1;
        Assert.Equal(1, bindingsOccurrences);
    }

    [Fact]
    public void FromJson_UnknownLinkStateString_KeepsWorkflowAndNullsTheState()
    {
        Workflow workflow = LinkedWorkflow(out _, out _);
        string json = workflow
            .ToJson(pretty: false)
            .Replace("\"SharedComponentLinkState\":\"Linked\"", "\"SharedComponentLinkState\":\"FutureState\"");

        Workflow? roundTripped = Workflow.FromJson(json);

        Assert.NotNull(roundTripped);
        Section section = roundTripped.Sections.Single();
        Assert.Null(section.SharedComponentLinkState);
        Assert.Null(section.Blocks.Single().SharedComponentLinkState);
        Assert.Equal(COMPONENT_ID, section.SharedComponentId);
    }

    [Fact]
    public void SectionReplaceAll_DoesNotRewriteLinkFields()
    {
        _ = LinkedWorkflow(out Section section, out Block block);
        section.Title = "before cafe after";
        block.SharedComponentBindings!["slot"] = "cafe";

        section.ReplaceAll(new Dictionary<string, string?> { ["cafe"] = "REWRITTEN" });

        Assert.Equal("before REWRITTEN after", section.Title);
        Assert.Equal(COMPONENT_ID, section.SharedComponentId);
        Assert.Equal(_versionHash, section.SharedComponentVersionHash);
        Assert.Equal(COMPONENT_ID, block.SharedComponentId);
        Assert.Equal(_versionHash, block.SharedComponentVersionHash);
        Assert.Equal("cafe", block.SharedComponentBindings["slot"]);
    }
}
