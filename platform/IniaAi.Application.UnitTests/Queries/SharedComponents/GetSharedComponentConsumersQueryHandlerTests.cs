using ErrorOr;
using IniaAi.Application.Common.Workflows;
using IniaAi.Application.Queries.SharedComponents.GetSharedComponentConsumers;
using IniaAi.Application.UnitTests.Common.SharedComponents;
using IniaAi.Domain.Entities.SharedComponents;
using IniaAi.Domain.Entities.SharedComponents.Enums;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Blocks;
using IniaAi.Domain.Entities.Workflows.Enums;
using IniaAi.Infrastructure.Persistence.Core.Contexts;

namespace IniaAi.Application.UnitTests.Queries.SharedComponents;

public class GetSharedComponentConsumersQueryHandlerTests : IDisposable
{
    private const string ORG = "org-test";
    private const string USER = "user-test";

    private readonly SharedComponentsTestHarness _harness = new();
    private readonly CoreDbContext _coreContext;
    private readonly GetSharedComponentConsumersQueryHandler _handler;

    public GetSharedComponentConsumersQueryHandlerTests()
    {
        _coreContext = _harness.Context;
        _handler = new GetSharedComponentConsumersQueryHandler(_harness.UnitOfWork, _harness.CreateFanOutService());
    }

    [Fact]
    public async Task Consumers_ReturnsLinkedWorkflowTypes()
    {
        var component = new SharedComponent { Kind = SharedComponentKind.Block, Name = "Alpha" };
        _ = _coreContext.SharedComponents.Add(component);
        _ = _coreContext.WorkflowTypes.Add(new WorkflowType("WT-A", "Alpha Type"));
        await SeedLinkedBlockConsumerAsync(component.Id, "WT-A", "Alpha Report - 3_6_2026");

        ErrorOr<List<SharedComponentConsumer>> result = await _handler.Handle(
            new GetSharedComponentConsumersQuery(ORG, USER, component.Id),
            TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        SharedComponentConsumer consumer = Assert.Single(result.Value);
        Assert.Equal("WT-A", consumer.WorkflowTypeId);
        Assert.Equal("Alpha Type", consumer.WorkflowTypeName);
        Assert.Equal(1, consumer.LinkedInstanceCount);
        Assert.Null(consumer.CategoryName);
    }

    [Fact]
    public async Task Consumers_CarryTheConsumingTypesDomainCategory()
    {
        var component = new SharedComponent { Kind = SharedComponentKind.Block, Name = "Alpha" };
        _ = _coreContext.SharedComponents.Add(component);
        _ = _coreContext.WorkflowTypeCategories.Add(
            new WorkflowTypeCategory { Id = "corporate", Name = "Corporate" });
        _ = _coreContext.WorkflowTypes.Add(
            new WorkflowType("WT-A", "Alpha Report") { CategoryId = "corporate" });
        await SeedLinkedBlockConsumerAsync(component.Id, "WT-A", "Alpha Report");

        ErrorOr<List<SharedComponentConsumer>> result = await _handler.Handle(
            new GetSharedComponentConsumersQuery(ORG, USER, component.Id),
            TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        SharedComponentConsumer consumer = Assert.Single(result.Value);
        Assert.Equal("Corporate", consumer.CategoryName);
    }

    [Fact]
    public async Task Consumers_DirectBlockInPlainSection_IsDirectChannel()
    {
        var component = new SharedComponent { Kind = SharedComponentKind.Block, Name = "Alpha" };
        _ = _coreContext.SharedComponents.Add(component);
        _ = _coreContext.WorkflowTypes.Add(new WorkflowType("WT-A", "Alpha Type"));
        await SeedLinkedBlockConsumerAsync(component.Id, "WT-A", "Alpha Report");

        ErrorOr<List<SharedComponentConsumer>> result = await _handler.Handle(
            new GetSharedComponentConsumersQuery(ORG, USER, component.Id),
            TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        SharedComponentConsumer consumer = Assert.Single(result.Value);
        Assert.Equal(SharedComponentConsumerChannel.Direct, consumer.Channel);
        Assert.Null(consumer.ViaSharedComponentId);
        Assert.Null(consumer.ViaSharedComponentName);
    }

    [Fact]
    public async Task Consumers_BlockInsideLinkedSection_IsViaSectionChannel()
    {
        var blockComponent = new SharedComponent { Kind = SharedComponentKind.Block, Name = "Signer" };
        var sectionComponent = new SharedComponent { Kind = SharedComponentKind.Section, Name = "Risk Pack" };
        _ = _coreContext.SharedComponents.Add(blockComponent);
        _ = _coreContext.SharedComponents.Add(sectionComponent);
        _ = _coreContext.WorkflowTypes.Add(new WorkflowType("WT-A", "Alpha Type"));
        await SeedBlockInsideLinkedSectionAsync(blockComponent.Id, sectionComponent.Id, "WT-A", "Alpha Report");

        ErrorOr<List<SharedComponentConsumer>> result = await _handler.Handle(
            new GetSharedComponentConsumersQuery(ORG, USER, blockComponent.Id),
            TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        SharedComponentConsumer consumer = Assert.Single(result.Value);
        Assert.Equal("WT-A", consumer.WorkflowTypeId);
        Assert.Equal(1, consumer.LinkedInstanceCount);
        Assert.Equal(SharedComponentConsumerChannel.ViaSection, consumer.Channel);
        Assert.Equal(sectionComponent.Id, consumer.ViaSharedComponentId);
        Assert.Equal("Risk Pack", consumer.ViaSharedComponentName);
    }

    [Fact]
    public async Task Consumers_MixedChannelsInOneType_DirectWins()
    {
        var blockComponent = new SharedComponent { Kind = SharedComponentKind.Block, Name = "Signer" };
        var sectionComponent = new SharedComponent { Kind = SharedComponentKind.Section, Name = "Risk Pack" };
        _ = _coreContext.SharedComponents.Add(blockComponent);
        _ = _coreContext.SharedComponents.Add(sectionComponent);
        _ = _coreContext.WorkflowTypes.Add(new WorkflowType("WT-A", "Alpha Type"));
        await SeedMixedChannelConsumerAsync(blockComponent.Id, sectionComponent.Id, "WT-A", "Alpha Report");

        ErrorOr<List<SharedComponentConsumer>> result = await _handler.Handle(
            new GetSharedComponentConsumersQuery(ORG, USER, blockComponent.Id),
            TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        SharedComponentConsumer consumer = Assert.Single(result.Value);
        Assert.Equal(2, consumer.LinkedInstanceCount);
        Assert.Equal(SharedComponentConsumerChannel.Direct, consumer.Channel);
        Assert.Null(consumer.ViaSharedComponentId);
    }

    [Fact]
    public async Task Consumers_BrokenOutParentSection_CountsBlockAsDirect()
    {
        var blockComponent = new SharedComponent { Kind = SharedComponentKind.Block, Name = "Signer" };
        var sectionComponent = new SharedComponent { Kind = SharedComponentKind.Section, Name = "Risk Pack" };
        _ = _coreContext.SharedComponents.Add(blockComponent);
        _ = _coreContext.SharedComponents.Add(sectionComponent);
        _ = _coreContext.WorkflowTypes.Add(new WorkflowType("WT-A", "Alpha Type"));
        await SeedBlockInsideLinkedSectionAsync(
            blockComponent.Id, sectionComponent.Id, "WT-A", "Alpha Report",
            parentSectionLinkState: SharedComponentLinkState.BrokenOut);

        ErrorOr<List<SharedComponentConsumer>> result = await _handler.Handle(
            new GetSharedComponentConsumersQuery(ORG, USER, blockComponent.Id),
            TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        SharedComponentConsumer consumer = Assert.Single(result.Value);
        Assert.Equal(SharedComponentConsumerChannel.Direct, consumer.Channel);
        Assert.Null(consumer.ViaSharedComponentId);
    }

    [Fact]
    public async Task Consumers_UnknownComponent_ReturnsNotFound()
    {
        ErrorOr<List<SharedComponentConsumer>> result = await _handler.Handle(
            new GetSharedComponentConsumersQuery(ORG, USER, "missing"),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsError);
        Assert.Equal(ErrorType.NotFound, result.FirstError.Type);
        Assert.Equal(
            IniaAi.Application.Common.Workflows.SharedComponentErrorCodes.UNKNOWN_COMPONENT,
            result.FirstError.Code);
    }

    private async Task SeedLinkedBlockConsumerAsync(string componentId, string workflowTypeId, string workflowName)
    {
        var workflow = new Workflow(workflowName) { WorkflowTypeId = workflowTypeId };
        var section = new Section("S1", workflow) { Title = "S1" };
        var block = new Block(BlockTypes.AiQuery, section, "a1") { Name = "Block a1" };
        block.SharedComponentId = componentId;
        block.SharedComponentLinkState = SharedComponentLinkState.Linked;
        block.SharedComponentVersionHash = new string('a', 64);
        _ = _coreContext.Add(workflow);
        _ = await _coreContext.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    private async Task SeedBlockInsideLinkedSectionAsync(
        string blockComponentId,
        string sectionComponentId,
        string workflowTypeId,
        string workflowName,
        SharedComponentLinkState parentSectionLinkState = SharedComponentLinkState.Linked)
    {
        var workflow = new Workflow(workflowName) { WorkflowTypeId = workflowTypeId };
        var section = new Section("Risk", workflow) { Title = "Risk" };
        section.SharedComponentId = sectionComponentId;
        section.SharedComponentLinkState = parentSectionLinkState;
        section.SharedComponentVersionHash = new string('b', 64);
        var inner = new Block(BlockTypes.AiQuery, section, "signer") { Name = "Signer" };
        inner.SharedComponentId = blockComponentId;
        inner.SharedComponentLinkState = SharedComponentLinkState.Linked;
        inner.SharedComponentVersionHash = new string('a', 64);
        _ = _coreContext.Add(workflow);
        _ = await _coreContext.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    private async Task SeedMixedChannelConsumerAsync(
        string blockComponentId,
        string sectionComponentId,
        string workflowTypeId,
        string workflowName)
    {
        var workflow = new Workflow(workflowName) { WorkflowTypeId = workflowTypeId };
        var plain = new Section("Standalone", workflow) { Title = "Standalone" };
        var directBlock = new Block(BlockTypes.AiQuery, plain, "signer_direct") { Name = "Signer" };
        directBlock.SharedComponentId = blockComponentId;
        directBlock.SharedComponentLinkState = SharedComponentLinkState.Linked;
        directBlock.SharedComponentVersionHash = new string('a', 64);
        var linkedSection = new Section("Risk", workflow) { Title = "Risk" };
        linkedSection.SharedComponentId = sectionComponentId;
        linkedSection.SharedComponentLinkState = SharedComponentLinkState.Linked;
        linkedSection.SharedComponentVersionHash = new string('b', 64);
        var innerBlock = new Block(BlockTypes.AiQuery, linkedSection, "signer_via") { Name = "Signer" };
        innerBlock.SharedComponentId = blockComponentId;
        innerBlock.SharedComponentLinkState = SharedComponentLinkState.Linked;
        innerBlock.SharedComponentVersionHash = new string('a', 64);
        _ = _coreContext.Add(workflow);
        _ = await _coreContext.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    public void Dispose()
    {
        _harness.Dispose();
    }
}
