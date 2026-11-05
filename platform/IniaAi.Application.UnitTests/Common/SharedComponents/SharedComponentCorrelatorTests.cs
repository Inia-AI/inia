using IniaAi.Application.Common.Workflows;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Blocks;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties;
using IniaAi.Domain.Entities.Workflows.Enums;
using IniaAi.WebApi.Contracts.Dto.WorkflowTransfer;
using DomainKind = IniaAi.Domain.Entities.SharedComponents.Enums.SharedComponentKind;
using LinkState = IniaAi.Domain.Entities.SharedComponents.Enums.SharedComponentLinkState;
using TransferMethod = IniaAi.Domain.Entities.SharedComponents.Enums.SharedComponentTransferCorrelationMethod;
using WireKind = IniaAi.WebApi.Contracts.Enums.SharedComponentKind;

namespace IniaAi.Application.UnitTests.Common.SharedComponents;

public class SharedComponentCorrelatorTests
{
    [Fact]
    public async Task Correlate_GuidEquality_MatchesLocalRowWithSameIdAndKind()
    {
        using var harness = new SharedComponentsTestHarness();
        await SeedComponentAsync(harness, DomainKind.Block, "shared-1", "h1");

        var map = await SharedComponentCorrelator.CorrelateAsync(
            harness.UnitOfWork,
            EmptyTemplate(),
            EmptyTemplate(),
            [BundleComp("shared-1", WireKind.Block, "hZ")],
            TestContext.Current.CancellationToken);

        Assert.Equal(TransferMethod.GuidEquality, map["shared-1"].Method);
        Assert.Equal("shared-1", map["shared-1"].LocalComponentId);
    }

    [Fact]
    public async Task Correlate_NoMatch_FallsToCreate()
    {
        using var harness = new SharedComponentsTestHarness();

        var map = await SharedComponentCorrelator.CorrelateAsync(
            harness.UnitOfWork,
            EmptyTemplate(),
            EmptyTemplate(),
            [BundleComp("foreign-new", WireKind.Block, "hX")],
            TestContext.Current.CancellationToken);

        Assert.Equal(TransferMethod.Create, map["foreign-new"].Method);
        Assert.Null(map["foreign-new"].LocalComponentId);
    }

    [Fact]
    public async Task Correlate_HashTrailOverlap_MatchesLocalChainOfSameKind()
    {
        using var harness = new SharedComponentsTestHarness();
        await SeedComponentAsync(harness, DomainKind.Block, "local-y", "h1", "h2");

        var map = await SharedComponentCorrelator.CorrelateAsync(
            harness.UnitOfWork,
            EmptyTemplate(),
            EmptyTemplate(),
            [BundleComp("foreign-x", WireKind.Block, "h0", "h2")],
            TestContext.Current.CancellationToken);

        Assert.Equal(TransferMethod.HashTrail, map["foreign-x"].Method);
        Assert.Equal("local-y", map["foreign-x"].LocalComponentId);
    }

    [Fact]
    public async Task Correlate_HashTrailOverlap_IgnoresKindMismatch()
    {
        using var harness = new SharedComponentsTestHarness();
        await SeedComponentAsync(harness, DomainKind.Section, "local-section", "hShared");

        var map = await SharedComponentCorrelator.CorrelateAsync(
            harness.UnitOfWork,
            EmptyTemplate(),
            EmptyTemplate(),
            [BundleComp("foreign-block", WireKind.Block, "hShared")],
            TestContext.Current.CancellationToken);

        Assert.Equal(TransferMethod.Create, map["foreign-block"].Method);
        Assert.Null(map["foreign-block"].LocalComponentId);
    }

    [Fact]
    public async Task Correlate_InstanceAnchor_MatchesLocalComponentAtSameStableId()
    {
        using var harness = new SharedComponentsTestHarness();
        await SeedComponentAsync(harness, DomainKind.Block, "local-a", "hL");

        var map = await SharedComponentCorrelator.CorrelateAsync(
            harness.UnitOfWork,
            TemplateWithLinkedBlock("S1", "local-a"),
            TemplateWithLinkedBlock("S1", "foreign-a"),
            [BundleComp("foreign-a", WireKind.Block, "hNo")],
            TestContext.Current.CancellationToken);

        Assert.Equal(TransferMethod.Anchor, map["foreign-a"].Method);
        Assert.Equal("local-a", map["foreign-a"].LocalComponentId);
    }

    [Fact]
    public async Task Correlate_Anchor_WinsOverHashTrail()
    {
        using var harness = new SharedComponentsTestHarness();
        await SeedComponentAsync(harness, DomainKind.Block, "local-a", "hL");
        await SeedComponentAsync(harness, DomainKind.Block, "local-hash", "hShared");

        var map = await SharedComponentCorrelator.CorrelateAsync(
            harness.UnitOfWork,
            TemplateWithLinkedBlock("S1", "local-a"),
            TemplateWithLinkedBlock("S1", "foreign-a"),
            [BundleComp("foreign-a", WireKind.Block, "hShared")],
            TestContext.Current.CancellationToken);

        Assert.Equal(TransferMethod.Anchor, map["foreign-a"].Method);
        Assert.Equal("local-a", map["foreign-a"].LocalComponentId);
    }

    private static TransferComponentDto BundleComp(string id, WireKind kind, params string[] trail)
    {
        return new TransferComponentDto(id, kind, "Name", null, null, trail[^1], "{}", [.. trail]);
    }

    private static Workflow EmptyTemplate()
    {
        return new Workflow("T") { Title = "T" };
    }

    private static Workflow TemplateWithLinkedBlock(string stableId, string componentId)
    {
        var workflow = new Workflow("T") { Title = "T" };
        var section = new Section("Body", workflow) { Title = "Body" };
        var block = new Block(BlockTypes.AiQuery, section, "b")
        {
            Name = "Node",
            Properties = new AiQueryProperties { UserPromptTemplate = "?" },
        };
        block.StableBlockId = stableId;
        block.SharedComponentId = componentId;
        block.SharedComponentLinkState = LinkState.Linked;
        block.SharedComponentVersionHash = new string('a', 64);
        block.SharedComponentBindings = new Dictionary<string, string>();

        return workflow;
    }

    private static async Task SeedComponentAsync(
        SharedComponentsTestHarness harness,
        DomainKind kind,
        string id,
        params string[] hashes)
    {
        _ = await harness.AddComponentAsync(kind, id, id: id);
        int sequenceNumber = 1;
        foreach (string hash in hashes)
        {
            _ = await harness.AddVersionAsync(id, sequenceNumber++, hash, """{"Title":"x"}""");
        }
    }
}
