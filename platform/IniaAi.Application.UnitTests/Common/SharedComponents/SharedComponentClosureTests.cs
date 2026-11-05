using IniaAi.Application.Common.Workflows;
using IniaAi.Application.UnitTests.Common.Workflows;
using IniaAi.Domain.Entities.SharedComponents.Enums;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Blocks;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties;
using IniaAi.Domain.Entities.Workflows.Enums;

namespace IniaAi.Application.UnitTests.Common.SharedComponents;

public class SharedComponentClosureTests
{
    [Fact]
    public async Task CollectAsync_DirectBlockLink_ReturnsThatComponent()
    {
        using var harness = new SharedComponentsTestHarness();
        await SeedBlockComponentAsync(harness, "sc-block-direct");

        var template = new Workflow("Report") { Title = "Report" };
        var section = new Section("Body", template) { Title = "Body" };
        LinkBlock(
            new Block(BlockTypes.AiQuery, section, "b1")
            {
                Name = "Node",
                Properties = new AiQueryProperties { UserPromptTemplate = "?" },
            },
            "sc-block-direct");

        IReadOnlyList<string> closure = await SharedComponentClosure.CollectAsync(
            harness.UnitOfWork, template, TestContext.Current.CancellationToken);

        Assert.Equal(["sc-block-direct"], closure);
    }

    [Fact]
    public async Task CollectAsync_LinkedSection_IncludesRefTargetsEvenWhenNotMaterialized()
    {
        using var harness = new SharedComponentsTestHarness();
        await SeedBlockComponentAsync(harness, "sc-block-a");
        await SeedBlockComponentAsync(harness, "sc-block-b");
        await SeedBlockComponentAsync(harness, "sc-block-direct");

        var defSection = new Section("Market", new Workflow("Scratch")) { Title = "Market" };
        LinkBlock(
            new Block(BlockTypes.AiQuery, defSection, "ra")
            {
                Name = "A",
                Properties = new AiQueryProperties { UserPromptTemplate = "a" },
            },
            "sc-block-a");
        LinkBlock(
            new Block(BlockTypes.AiQuery, defSection, "rb")
            {
                Name = "B",
                Properties = new AiQueryProperties { UserPromptTemplate = "b" },
            },
            "sc-block-b");
        (string secJson, string secHash) = harness.Definition(defSection);
        await harness.AddComponentAsync(SharedComponentKind.Section, "Market", id: "sc-section");
        await harness.AddVersionAsync("sc-section", 1, secHash, secJson);

        var template = new Workflow("Report") { Title = "Report" };
        var linkedSection = new Section("Body", template) { Title = "Body" };
        linkedSection.SharedComponentId = "sc-section";
        linkedSection.SharedComponentLinkState = SharedComponentLinkState.Linked;
        linkedSection.SharedComponentVersionHash = secHash;
        linkedSection.SharedComponentBindings = new Dictionary<string, string>();

        var extra = new Section("Extra", template) { Title = "Extra" };
        LinkBlock(
            new Block(BlockTypes.AiQuery, extra, "d")
            {
                Name = "Direct",
                Properties = new AiQueryProperties { UserPromptTemplate = "d" },
            },
            "sc-block-direct");

        IReadOnlyList<string> closure = await SharedComponentClosure.CollectAsync(
            harness.UnitOfWork, template, TestContext.Current.CancellationToken);

        Assert.Equal(["sc-block-a", "sc-block-b", "sc-block-direct", "sc-section"], closure);
    }

    [Fact]
    public async Task CollectAsync_NoLinks_ReturnsEmpty()
    {
        using var harness = new SharedComponentsTestHarness();

        var template = new Workflow("Report") { Title = "Report" };
        var section = new Section("Body", template) { Title = "Body" };
        _ = new Block(BlockTypes.AiQuery, section, "b1")
        {
            Name = "Node",
            Properties = new AiQueryProperties { UserPromptTemplate = "?" },
        };

        IReadOnlyList<string> closure = await SharedComponentClosure.CollectAsync(
            harness.UnitOfWork, template, TestContext.Current.CancellationToken);

        Assert.Empty(closure);
    }

    private static void LinkBlock(Block block, string componentId)
    {
        block.SharedComponentId = componentId;
        block.SharedComponentLinkState = SharedComponentLinkState.Linked;
        block.SharedComponentVersionHash = new string('a', 64);
        block.SharedComponentBindings = new Dictionary<string, string>();
    }

    private static async Task SeedBlockComponentAsync(SharedComponentsTestHarness harness, string id)
    {
        Block definition = SharedComponentLinkTestData.BuildBlockDefinition($"prompt-{id}");
        (string json, string hash) = harness.Definition(definition);
        _ = await harness.AddComponentAsync(SharedComponentKind.Block, id, id: id);
        _ = await harness.AddVersionAsync(id, 1, hash, json);
    }
}
