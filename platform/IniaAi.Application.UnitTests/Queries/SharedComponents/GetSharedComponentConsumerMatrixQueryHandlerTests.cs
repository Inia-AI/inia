using ErrorOr;
using IniaAi.Application.Common.Workflows;
using IniaAi.Application.Queries.SharedComponents.GetSharedComponentConsumerMatrix;
using IniaAi.Application.UnitTests.Common.SharedComponents;
using IniaAi.Domain.Core.Interfaces;
using IniaAi.Domain.Entities.SharedComponents;
using IniaAi.Domain.Entities.SharedComponents.Enums;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Blocks;
using IniaAi.Domain.Entities.Workflows.Enums;
using Microsoft.EntityFrameworkCore;

namespace IniaAi.Application.UnitTests.Queries.SharedComponents;

public class GetSharedComponentConsumerMatrixQueryHandlerTests
{
    private const string ORG = "org-test";
    private const string USER = "user-test";

    [Fact]
    public async Task Matrix_UnknownComponent_ReturnsNotFound()
    {
        using var harness = new SharedComponentsTestHarness();

        ErrorOr<SharedComponentConsumerMatrix> result = await Handler(harness).Handle(
            new GetSharedComponentConsumerMatrixQuery(ORG, USER, "missing"),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsError);
        Assert.Equal(SharedComponentErrorCodes.UNKNOWN_COMPONENT, result.FirstError.Code);
    }

    [Fact]
    public async Task Matrix_BlockComponent_IsRejectedRatherThanReturningAnEmptyGrid()
    {
        using var harness = new SharedComponentsTestHarness();
        SharedComponent block = await harness.AddComponentAsync(SharedComponentKind.Block, "Signer");
        _ = await harness.AddVersionAsync(block.Id, 1, new string('a', 64), """{"Name":"Signer"}""");

        ErrorOr<SharedComponentConsumerMatrix> result = await Handler(harness).Handle(
            new GetSharedComponentConsumerMatrixQuery(ORG, USER, block.Id),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsError);
        Assert.Equal(ErrorType.Validation, result.FirstError.Type);
        Assert.Contains(SharedComponentErrorCodes.KIND_MISMATCH, result.FirstError.Description, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Matrix_ColumnsComeFromTheHeadDefinitionEntries_InOrder()
    {
        using var harness = new SharedComponentsTestHarness();
        (SharedComponent component, _) = await SeedSectionComponentAsync(harness);

        ErrorOr<SharedComponentConsumerMatrix> result = await Handler(harness).Handle(
            new GetSharedComponentConsumerMatrixQuery(ORG, USER, component.Id),
            TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        Assert.Equal(["intro", "body"], result.Value.Entries.Select(entry => entry.SlotTag));
        Assert.All(result.Value.Entries, entry => Assert.Equal(SectionEntryKind.Inline, entry.Kind));
    }

    [Fact]
    public async Task Matrix_UntouchedConsumer_IsLiveInEverySlot()
    {
        using var harness = new SharedComponentsTestHarness();
        (SharedComponent component, string headHash) = await SeedSectionComponentAsync(harness);
        await SeedConsumerAsync(harness, component.Id, headHash, "WT-A", "Alpha", slotStates: null);

        ErrorOr<SharedComponentConsumerMatrix> result = await Handler(harness).Handle(
            new GetSharedComponentConsumerMatrixQuery(ORG, USER, component.Id),
            TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        SharedComponentConsumerMatrixRow row = Assert.Single(result.Value.Rows);
        Assert.Equal("WT-A", row.WorkflowTypeId);
        Assert.Equal(
            [SharedComponentEntryCellState.Live, SharedComponentEntryCellState.Live],
            row.Cells.Select(cell => cell.State));
    }

    [Fact]
    public async Task Matrix_HiddenSlot_ReadsHidden()
    {
        using var harness = new SharedComponentsTestHarness();
        (SharedComponent component, string headHash) = await SeedSectionComponentAsync(harness);
        await SeedConsumerAsync(
            harness,
            component.Id,
            headHash,
            "WT-A",
            "Alpha",
            slotStates: new Dictionary<string, SharedComponentSlotState> { ["body"] = SharedComponentSlotState.Hidden },
            presentSlotTags: ["intro"]);

        ErrorOr<SharedComponentConsumerMatrix> result = await Handler(harness).Handle(
            new GetSharedComponentConsumerMatrixQuery(ORG, USER, component.Id),
            TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        SharedComponentConsumerMatrixRow row = Assert.Single(result.Value.Rows);
        Assert.Equal(SharedComponentEntryCellState.Live, CellFor(row, "intro").State);
        Assert.Equal(SharedComponentEntryCellState.Hidden, CellFor(row, "body").State);
    }

    [Fact]
    public async Task Matrix_BrokenOutSlot_ReadsCustom()
    {
        using var harness = new SharedComponentsTestHarness();
        (SharedComponent component, string headHash) = await SeedSectionComponentAsync(harness);
        await SeedConsumerAsync(
            harness,
            component.Id,
            headHash,
            "WT-A",
            "Alpha",
            slotStates: new Dictionary<string, SharedComponentSlotState> { ["body"] = SharedComponentSlotState.BrokenOut });

        ErrorOr<SharedComponentConsumerMatrix> result = await Handler(harness).Handle(
            new GetSharedComponentConsumerMatrixQuery(ORG, USER, component.Id),
            TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        SharedComponentConsumerMatrixRow row = Assert.Single(result.Value.Rows);
        Assert.Equal(SharedComponentEntryCellState.Custom, CellFor(row, "body").State);
    }

    [Fact]
    public async Task Matrix_SlotWithNoBlock_ReadsMissing()
    {
        using var harness = new SharedComponentsTestHarness();
        (SharedComponent component, string headHash) = await SeedSectionComponentAsync(harness);
        await SeedConsumerAsync(harness, component.Id, headHash, "WT-A", "Alpha", presentSlotTags: ["intro"]);

        ErrorOr<SharedComponentConsumerMatrix> result = await Handler(harness).Handle(
            new GetSharedComponentConsumerMatrixQuery(ORG, USER, component.Id),
            TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        SharedComponentConsumerMatrixRow row = Assert.Single(result.Value.Rows);
        Assert.Equal(SharedComponentEntryCellState.Missing, CellFor(row, "body").State);
    }

    [Fact]
    public async Task Matrix_LocallyInsertedBlock_ReadsAdded_InItsOwnColumn()
    {
        using var harness = new SharedComponentsTestHarness();
        (SharedComponent component, string headHash) = await SeedSectionComponentAsync(harness);
        await SeedConsumerAsync(
            harness,
            component.Id,
            headHash,
            "WT-A",
            "Alpha",
            presentSlotTags: ["intro", "body", "local_note"]);

        ErrorOr<SharedComponentConsumerMatrix> result = await Handler(harness).Handle(
            new GetSharedComponentConsumerMatrixQuery(ORG, USER, component.Id),
            TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        Assert.Equal(["intro", "body", "local_note"], result.Value.Entries.Select(entry => entry.SlotTag));
        Assert.True(result.Value.Entries.Single(entry => entry.SlotTag == "local_note").IsLocalOnly);

        SharedComponentConsumerMatrixRow row = Assert.Single(result.Value.Rows);
        Assert.Equal(SharedComponentEntryCellState.Added, CellFor(row, "local_note").State);
    }

    [Fact]
    public async Task Matrix_ConsumerWithoutTheLocalColumn_ReadsNotApplicable()
    {
        using var harness = new SharedComponentsTestHarness();
        (SharedComponent component, string headHash) = await SeedSectionComponentAsync(harness);
        await SeedConsumerAsync(
            harness, component.Id, headHash, "WT-A", "Alpha", presentSlotTags: ["intro", "body", "local_note"]);
        await SeedConsumerAsync(
            harness, component.Id, headHash, "WT-B", "Beta", presentSlotTags: ["intro", "body"]);

        ErrorOr<SharedComponentConsumerMatrix> result = await Handler(harness).Handle(
            new GetSharedComponentConsumerMatrixQuery(ORG, USER, component.Id),
            TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        SharedComponentConsumerMatrixRow beta = result.Value.Rows.Single(r => r.WorkflowTypeId == "WT-B");
        Assert.Equal(SharedComponentEntryCellState.NotApplicable, CellFor(beta, "local_note").State);
    }

    [Fact]
    public async Task Matrix_RenamedSlotViaBindings_ReadsLive_NoPhantomColumns()
    {
        using var harness = new SharedComponentsTestHarness();
        (SharedComponent component, string headHash) = await SeedSectionComponentAsync(harness);
        await SeedConsumerAsync(
            harness,
            component.Id,
            headHash,
            "WT-A",
            "Alpha",
            slotStates: null,
            presentSlotTags: ["intro_2", "body"],
            bindings: new Dictionary<string, string> { ["intro"] = "intro_2" });

        ErrorOr<SharedComponentConsumerMatrix> result = await Handler(harness).Handle(
            new GetSharedComponentConsumerMatrixQuery(ORG, USER, component.Id),
            TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        Assert.Equal(["intro", "body"], result.Value.Entries.Select(entry => entry.SlotTag));
        SharedComponentConsumerMatrixRow row = Assert.Single(result.Value.Rows);
        Assert.Equal(SharedComponentEntryCellState.Live, CellFor(row, "intro").State);
        Assert.Equal(SharedComponentEntryCellState.Live, CellFor(row, "body").State);
    }

    [Fact]
    public async Task Matrix_BoundTag_NotMistakenForAnotherConsumersLocalColumn()
    {
        using var harness = new SharedComponentsTestHarness();
        (SharedComponent component, string headHash) = await SeedSectionComponentAsync(harness);
        await SeedConsumerAsync(
            harness,
            component.Id,
            headHash,
            "WT-A",
            "Alpha",
            slotStates: null,
            presentSlotTags: ["intro_2", "body"],
            bindings: new Dictionary<string, string> { ["intro"] = "intro_2" });
        await SeedConsumerAsync(
            harness,
            component.Id,
            headHash,
            "WT-B",
            "Beta",
            slotStates: null,
            presentSlotTags: ["intro", "body", "intro_2"]);

        ErrorOr<SharedComponentConsumerMatrix> result = await Handler(harness).Handle(
            new GetSharedComponentConsumerMatrixQuery(ORG, USER, component.Id),
            TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        SharedComponentMatrixEntry localColumn = Assert.Single(result.Value.Entries, entry => entry.IsLocalOnly);
        Assert.Equal("intro_2", localColumn.SlotTag);
        SharedComponentConsumerMatrixRow alpha = Assert.Single(result.Value.Rows, row => row.WorkflowTypeId == "WT-A");
        SharedComponentConsumerMatrixRow beta = Assert.Single(result.Value.Rows, row => row.WorkflowTypeId == "WT-B");
        Assert.Equal(SharedComponentEntryCellState.NotApplicable, CellFor(alpha, "intro_2").State);
        Assert.Equal(SharedComponentEntryCellState.Added, CellFor(beta, "intro_2").State);
        Assert.Equal(SharedComponentEntryCellState.Live, CellFor(alpha, "intro").State);
        Assert.Equal(SharedComponentEntryCellState.Live, CellFor(beta, "intro").State);
    }

    [Fact]
    public async Task Matrix_BrokenOutCopies_AreNotRows()
    {
        using var harness = new SharedComponentsTestHarness();
        (SharedComponent component, string headHash) = await SeedSectionComponentAsync(harness);
        await SeedConsumerAsync(
            harness,
            component.Id,
            headHash,
            "WT-FORKED",
            "Forked",
            linkState: SharedComponentLinkState.BrokenOut);

        ErrorOr<SharedComponentConsumerMatrix> result = await Handler(harness).Handle(
            new GetSharedComponentConsumerMatrixQuery(ORG, USER, component.Id),
            TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        Assert.Empty(result.Value.Rows);
    }

    [Fact]
    public async Task Matrix_TwoInstancesInOneType_AreSeparateRows()
    {
        using var harness = new SharedComponentsTestHarness();
        (SharedComponent component, string headHash) = await SeedSectionComponentAsync(harness);
        await SeedConsumerAsync(harness, component.Id, headHash, "WT-A", "Alpha");
        await SeedConsumerAsync(
            harness,
            component.Id,
            headHash,
            "WT-A",
            "Alpha",
            slotStates: new Dictionary<string, SharedComponentSlotState> { ["body"] = SharedComponentSlotState.Hidden });

        ErrorOr<SharedComponentConsumerMatrix> result = await Handler(harness).Handle(
            new GetSharedComponentConsumerMatrixQuery(ORG, USER, component.Id),
            TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        Assert.Equal(2, result.Value.Rows.Count);
        Assert.All(result.Value.Rows, row => Assert.Equal("WT-A", row.WorkflowTypeId));
        Assert.Contains(result.Value.Rows, row => CellFor(row, "body").State == SharedComponentEntryCellState.Hidden);
        Assert.Contains(result.Value.Rows, row => CellFor(row, "body").State == SharedComponentEntryCellState.Live);
    }

    [Fact]
    public async Task Matrix_HeadWithoutStoredPayload_ReturnsPayloadAbsent()
    {
        using var harness = new SharedComponentsTestHarness();
        SharedComponent component = await harness.AddComponentAsync(SharedComponentKind.Section, "Risk Pack");
        _ = await harness.AddVersionAsync(
            component.Id, 1, new string('a', 64), contentJson: null, payloadAbsentAt: DateTime.UtcNow);

        ErrorOr<SharedComponentConsumerMatrix> result = await Handler(harness).Handle(
            new GetSharedComponentConsumerMatrixQuery(ORG, USER, component.Id),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsError);
        Assert.Equal(SharedComponentErrorCodes.PAYLOAD_ABSENT, result.FirstError.Code);
    }

    [Fact]
    public void Matrix_DependsOnlyOnTheUnitOfWork_SoNoConsumerGraphOrHashingCanCreepIn()
    {
        Type[] dependencies =
        [
            .. typeof(GetSharedComponentConsumerMatrixQueryHandler)
                .GetConstructors()
                .Single()
                .GetParameters()
                .Select(parameter => parameter.ParameterType),
        ];

        Assert.Equal([typeof(ICoreUnitOfWork)], dependencies);
    }

    [Fact]
    public async Task Matrix_TracksNoConsumerEntities_SoRowsStayProjections()
    {
        using var harness = new SharedComponentsTestHarness();
        (SharedComponent component, string headHash) = await SeedSectionComponentAsync(harness);
        await SeedConsumerAsync(harness, component.Id, headHash, "WT-A", "Alpha");
        await SeedConsumerAsync(harness, component.Id, headHash, "WT-B", "Beta");
        harness.Context.ChangeTracker.Clear();

        _ = await Handler(harness).Handle(
            new GetSharedComponentConsumerMatrixQuery(ORG, USER, component.Id),
            TestContext.Current.CancellationToken);

        Assert.Empty(harness.Context.ChangeTracker.Entries<Workflow>());
        Assert.Empty(harness.Context.ChangeTracker.Entries<Section>());
        Assert.Empty(harness.Context.ChangeTracker.Entries<Block>());
    }

    private static SharedComponentConsumerMatrixCell CellFor(SharedComponentConsumerMatrixRow row, string slotTag)
    {
        return row.Cells.Single(cell => cell.SlotTag == slotTag);
    }

    private static GetSharedComponentConsumerMatrixQueryHandler Handler(SharedComponentsTestHarness harness)
    {
        return new GetSharedComponentConsumerMatrixQueryHandler(harness.UnitOfWork);
    }

    private static async Task<(SharedComponent Component, string HeadHash)> SeedSectionComponentAsync(
        SharedComponentsTestHarness harness)
    {
        SharedComponent component = await harness.AddComponentAsync(SharedComponentKind.Section, "Risk Pack");

        var definitionWorkflow = new Workflow("definition") { WorkflowTypeId = "WT-DEF" };
        var definition = new Section("Risk", definitionWorkflow) { Title = "Risk" };
        _ = new Block(BlockTypes.AiQuery, definition, "intro") { Name = "Intro" };
        _ = new Block(BlockTypes.AiQuery, definition, "body") { Name = "Body" };
        definition.Entries =
        [
            new SectionEntry { SlotTag = "intro", Kind = SectionEntryKind.Inline },
            new SectionEntry { SlotTag = "body", Kind = SectionEntryKind.Inline },
        ];

        (string definitionJson, string contentHash) = harness.Definition(definition);
        _ = await harness.AddVersionAsync(component.Id, 1, contentHash, definitionJson);

        return (component, contentHash);
    }

    private static async Task SeedConsumerAsync(
        SharedComponentsTestHarness harness,
        string componentId,
        string headHash,
        string workflowTypeId,
        string workflowTypeName,
        Dictionary<string, SharedComponentSlotState>? slotStates = null,
        IReadOnlyList<string>? presentSlotTags = null,
        SharedComponentLinkState linkState = SharedComponentLinkState.Linked,
        Dictionary<string, string>? bindings = null)
    {
        if (!await harness.Context.WorkflowTypes.AnyAsync(
            type => type.Id == workflowTypeId, TestContext.Current.CancellationToken))
        {
            _ = harness.Context.WorkflowTypes.Add(new WorkflowType(workflowTypeId, workflowTypeName));
        }

        var workflow = new Workflow($"{workflowTypeName} report") { WorkflowTypeId = workflowTypeId };
        var section = new Section("Risk", workflow)
        {
            Title = "Risk",
            SharedComponentId = componentId,
            SharedComponentLinkState = linkState,
            SharedComponentVersionHash = headHash,
            SharedComponentSlotStates = slotStates,
            SharedComponentBindings = bindings,
        };

        foreach (string slotTag in presentSlotTags ?? ["intro", "body"])
        {
            _ = new Block(BlockTypes.AiQuery, section, slotTag) { Name = slotTag };
        }

        await harness.UnitOfWork.Workflows.AddAsync(workflow);
        _ = await harness.Context.SaveChangesAsync(TestContext.Current.CancellationToken);
    }
}
