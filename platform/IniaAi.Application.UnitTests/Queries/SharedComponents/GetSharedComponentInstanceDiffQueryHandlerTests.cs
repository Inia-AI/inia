using ErrorOr;
using IniaAi.Application.Common.Workflows;
using IniaAi.Application.Queries.SharedComponents.GetSharedComponentInstanceDiff;
using IniaAi.Application.UnitTests.Common.SharedComponents;
using IniaAi.Core.Workflows.Diff;
using IniaAi.Domain.Entities.SharedComponents;
using IniaAi.Domain.Entities.SharedComponents.Enums;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Blocks;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties;
using IniaAi.Domain.Entities.Workflows.Enums;
using static IniaAi.Application.UnitTests.Common.Workflows.SharedComponentLinkTestData;

namespace IniaAi.Application.UnitTests.Queries.SharedComponents;

public class GetSharedComponentInstanceDiffQueryHandlerTests : IDisposable
{
    private const string ORG = "org-test";
    private const string USER = "user-test";
    private const string USER_PROMPT_FIELD = "Properties.UserPromptTemplate";

    private readonly SharedComponentsTestHarness _harness = new();
    private readonly GetSharedComponentInstanceDiffQueryHandler _handler;

    public GetSharedComponentInstanceDiffQueryHandlerTests()
    {
        _handler = new GetSharedComponentInstanceDiffQueryHandler(
            _harness.UnitOfWork,
            new SharedComponentEditor(_harness.Canonicalizer, _harness.Normalizer),
            new SharedComponentReconciler(_harness.Normalizer),
            new SharedComponentDefinitionDiffer(new WorkflowDiffService(), new WorkflowNarrativeGenerator()));
    }

    [Fact]
    public async Task Diff_BlockCopyWithALocalPromptEdit_NamesThePromptFromLibraryToCopy()
    {
        (string headJson, string headHash) = _harness.Definition(BuildBlockDefinition("Library prompt"));
        await _harness.AddComponentAsync(SharedComponentKind.Block, "Rates", id: "c-rates");
        _ = await _harness.AddVersionAsync("c-rates", 1, headHash, headJson);
        var workflow = new Workflow("Report") { WorkflowTypeId = "WT-D1" };
        var section = new Section("Main", workflow) { Title = "Main" };
        Block copy = LinkedBlock(section, "rates", "Local prompt", "c-rates", headHash);
        await _harness.AddWorkflowAsync(workflow);

        ErrorOr<WorkflowDiff> result = await _handler.Handle(
            new GetSharedComponentInstanceDiffQuery(ORG, USER, "WT-D1", TargetPath: null, copy.StableBlockId),
            TestContext.Current.CancellationToken);

        Assert.False(result.IsError, result.IsError ? result.FirstError.Description : null);
        BlockDiff block = Assert.Single(result.Value.Blocks);
        FieldDiff prompt = Assert.Single(block.FieldDiffs, field => field.FieldName == USER_PROMPT_FIELD);
        Assert.Equal("Library prompt", prompt.Before);
        Assert.Equal("Local prompt", prompt.After);
    }

    [Fact]
    public async Task Diff_BlockCopyInSync_IsEmpty()
    {
        (string headJson, string headHash) = _harness.Definition(BuildBlockDefinition("Library prompt"));
        await _harness.AddComponentAsync(SharedComponentKind.Block, "Rates", id: "c-rates");
        _ = await _harness.AddVersionAsync("c-rates", 1, headHash, headJson);
        var workflow = new Workflow("Report") { WorkflowTypeId = "WT-D2" };
        var section = new Section("Main", workflow) { Title = "Main" };
        Block copy = LinkedBlock(section, "rates", "Library prompt", "c-rates", headHash);
        await _harness.AddWorkflowAsync(workflow);

        ErrorOr<WorkflowDiff> result = await _handler.Handle(
            new GetSharedComponentInstanceDiffQuery(ORG, USER, "WT-D2", TargetPath: null, copy.StableBlockId),
            TestContext.Current.CancellationToken);

        Assert.False(result.IsError, result.IsError ? result.FirstError.Description : null);
        Assert.True(result.Value.IsEmpty);
    }

    [Fact]
    public async Task Diff_BlockCopyThatOnlyRenamesTheBlock_IsEmptyBecauseLibrarySyncIgnoresLabels()
    {
        (string headJson, string headHash) = _harness.Definition(BuildBlockDefinition("Library prompt"));
        await _harness.AddComponentAsync(SharedComponentKind.Block, "Rates", id: "c-rates");
        _ = await _harness.AddVersionAsync("c-rates", 1, headHash, headJson);
        var workflow = new Workflow("Report") { WorkflowTypeId = "WT-D5" };
        var section = new Section("Main", workflow) { Title = "Main" };
        Block copy = LinkedBlock(section, "rates", "Library prompt", "c-rates", headHash);
        copy.Name = "FX rates for this report";
        await _harness.AddWorkflowAsync(workflow);

        ErrorOr<WorkflowDiff> result = await _handler.Handle(
            new GetSharedComponentInstanceDiffQuery(ORG, USER, "WT-D5", TargetPath: null, copy.StableBlockId),
            TestContext.Current.CancellationToken);

        Assert.False(result.IsError, result.IsError ? result.FirstError.Description : null);
        Assert.True(result.Value.IsEmpty);
    }

    [Fact]
    public async Task Diff_LibraryVersionStoredWithAFieldTheModelDropped_IsEmpty()
    {
        (string headJson, string headHash) = _harness.StoredBeforeAFieldWasDropped(
            _harness.Definition(BuildBlockDefinition("Library prompt")).DefinitionJson);
        await _harness.AddComponentAsync(SharedComponentKind.Block, "Rates", id: "c-rates");
        _ = await _harness.AddVersionAsync("c-rates", 1, headHash, headJson);
        var workflow = new Workflow("Report") { WorkflowTypeId = "WT-D7" };
        var section = new Section("Main", workflow) { Title = "Main" };
        Block copy = LinkedBlock(section, "rates", "Library prompt", "c-rates", headHash);
        await _harness.AddWorkflowAsync(workflow);

        ErrorOr<WorkflowDiff> result = await _handler.Handle(
            new GetSharedComponentInstanceDiffQuery(ORG, USER, "WT-D7", TargetPath: null, copy.StableBlockId),
            TestContext.Current.CancellationToken);

        Assert.False(result.IsError, result.IsError ? result.FirstError.Description : null);
        Assert.True(result.Value.IsEmpty);
    }

    [Fact]
    public async Task Diff_SectionCopyWithAnEditedBlock_NamesThatBlock()
    {
        Section definition = DefSection("Currency", ("fx", "Library FX prompt"), ("period", "Period prompt"));
        (string headJson, string headHash) = _harness.Definition(definition);
        await _harness.AddComponentAsync(SharedComponentKind.Section, "Currency and period dates", id: "c-cur");
        _ = await _harness.AddVersionAsync("c-cur", 1, headHash, headJson);

        var workflow = new Workflow("Report") { WorkflowTypeId = "WT-D3" };
        var section = new Section("Currency", workflow) { Title = "Currency" };
        _ = new Block(BlockTypes.AiQuery, section, "fx")
        {
            Name = definition.Blocks.Single(b => b.ReplacementTag == "fx").Name,
            Properties = new AiQueryProperties { UserPromptTemplate = "Local FX prompt" },
        };
        _ = new Block(BlockTypes.AiQuery, section, "period")
        {
            Name = definition.Blocks.Single(b => b.ReplacementTag == "period").Name,
            Properties = new AiQueryProperties { UserPromptTemplate = "Period prompt" },
        };
        section.SharedComponentId = "c-cur";
        section.SharedComponentLinkState = SharedComponentLinkState.Linked;
        section.SharedComponentVersionHash = headHash;
        await _harness.AddWorkflowAsync(workflow);

        ErrorOr<WorkflowDiff> result = await _handler.Handle(
            new GetSharedComponentInstanceDiffQuery(ORG, USER, "WT-D3", "Sections[0]", TargetStableId: null),
            TestContext.Current.CancellationToken);

        Assert.False(result.IsError, result.IsError ? result.FirstError.Description : null);
        BlockDiff block = Assert.Single(result.Value.Blocks);
        Assert.Equal("fx", block.ReplacementTag);
        FieldDiff prompt = Assert.Single(block.FieldDiffs, field => field.FieldName == USER_PROMPT_FIELD);
        Assert.Equal("Library FX prompt", prompt.Before);
        Assert.Equal("Local FX prompt", prompt.After);
    }

    [Fact]
    public async Task Diff_CopyBehindANewerLibraryVersion_NamesWhatTheLibraryChanged()
    {
        (string v1Json, string v1Hash) = _harness.Definition(BuildBlockDefinition("Old prompt"));
        (string v2Json, string v2Hash) = _harness.Definition(BuildBlockDefinition("New prompt"));
        await _harness.AddComponentAsync(SharedComponentKind.Block, "Rates", id: "c-rates");
        SharedComponentVersion v1 = await _harness.AddVersionAsync("c-rates", 1, v1Hash, v1Json);
        _ = await _harness.AddVersionAsync("c-rates", 2, v2Hash, v2Json, parentVersionId: v1.Id);
        var workflow = new Workflow("Report") { WorkflowTypeId = "WT-D8" };
        var section = new Section("Main", workflow) { Title = "Main" };
        Block copy = LinkedBlock(section, "rates", "Old prompt", "c-rates", v1Hash);
        await _harness.AddWorkflowAsync(workflow);

        ErrorOr<WorkflowDiff> result = await _handler.Handle(
            new GetSharedComponentInstanceDiffQuery(ORG, USER, "WT-D8", TargetPath: null, copy.StableBlockId),
            TestContext.Current.CancellationToken);

        Assert.False(result.IsError, result.IsError ? result.FirstError.Description : null);
        FieldDiff prompt = Assert.Single(Assert.Single(result.Value.Blocks).FieldDiffs, field => field.FieldName == USER_PROMPT_FIELD);
        Assert.Equal("New prompt", prompt.Before);
        Assert.Equal("Old prompt", prompt.After);
    }

    [Fact]
    public async Task Diff_SectionCopyThatHidesAnEntry_IsEmptyBecauseHidingIsNotAContentChange()
    {
        Section definition = DefSection("Currency", ("fx", "FX prompt"), ("period", "Period prompt"));
        (string headJson, string headHash) = _harness.Definition(definition);
        await _harness.AddComponentAsync(SharedComponentKind.Section, "Currency and period dates", id: "c-cur");
        _ = await _harness.AddVersionAsync("c-cur", 1, headHash, headJson);
        var workflow = new Workflow("Report") { WorkflowTypeId = "WT-D9" };
        var section = new Section("Currency", workflow) { Title = "Currency" };
        _ = new Block(BlockTypes.AiQuery, section, "fx")
        {
            Name = definition.Blocks.Single(b => b.ReplacementTag == "fx").Name,
            Properties = new AiQueryProperties { UserPromptTemplate = "FX prompt" },
        };
        section.SharedComponentId = "c-cur";
        section.SharedComponentLinkState = SharedComponentLinkState.Linked;
        section.SharedComponentVersionHash = headHash;
        section.SharedComponentSlotStates = new Dictionary<string, SharedComponentSlotState>
        {
            ["period"] = SharedComponentSlotState.Hidden,
        };
        await _harness.AddWorkflowAsync(workflow);

        ErrorOr<WorkflowDiff> result = await _handler.Handle(
            new GetSharedComponentInstanceDiffQuery(ORG, USER, "WT-D9", "Sections[0]", TargetStableId: null),
            TestContext.Current.CancellationToken);

        Assert.False(result.IsError, result.IsError ? result.FirstError.Description : null);
        Assert.True(result.Value.IsEmpty);
    }

    [Fact]
    public async Task Diff_SectionCopyThatOnlyReordersItsBlocks_NamesTheBlockOrder()
    {
        var scratch = new Workflow("Scratch");
        var definition = new Section("Currency", scratch) { Title = "Currency" };
        _ = new Block(BlockTypes.AiQuery, definition, "fx")
        {
            Name = "FX rate",
            Properties = new AiQueryProperties { UserPromptTemplate = "FX prompt" },
        };
        _ = new Block(BlockTypes.AiQuery, definition, "period")
        {
            Name = "Period end",
            Properties = new AiQueryProperties { UserPromptTemplate = "Period prompt" },
        };
        (string headJson, string headHash) = _harness.Definition(definition);
        await _harness.AddComponentAsync(SharedComponentKind.Section, "Currency and period dates", id: "c-cur");
        _ = await _harness.AddVersionAsync("c-cur", 1, headHash, headJson);
        var workflow = new Workflow("Report") { WorkflowTypeId = "WT-D10" };
        var section = new Section("Currency", workflow) { Title = "Currency" };
        _ = new Block(BlockTypes.AiQuery, section, "period")
        {
            Name = "Period end",
            Properties = new AiQueryProperties { UserPromptTemplate = "Period prompt" },
        };
        _ = new Block(BlockTypes.AiQuery, section, "fx")
        {
            Name = "FX rate",
            Properties = new AiQueryProperties { UserPromptTemplate = "FX prompt" },
        };
        section.SharedComponentId = "c-cur";
        section.SharedComponentLinkState = SharedComponentLinkState.Linked;
        section.SharedComponentVersionHash = headHash;
        await _harness.AddWorkflowAsync(workflow);

        ErrorOr<WorkflowDiff> result = await _handler.Handle(
            new GetSharedComponentInstanceDiffQuery(ORG, USER, "WT-D10", "Sections[0]", TargetStableId: null),
            TestContext.Current.CancellationToken);

        Assert.False(result.IsError, result.IsError ? result.FirstError.Description : null);
        Assert.Empty(result.Value.Blocks);
        FieldDiff order = Assert.Single(Assert.Single(result.Value.Sections).FieldDiffs);
        Assert.Equal(SharedComponentDefinitionDiffer.BLOCK_ORDER_FIELD, order.FieldName);
        Assert.Equal("FX rate\nPeriod end", order.Before);
        Assert.Equal("Period end\nFX rate", order.After);
    }

    [Fact]
    public async Task Diff_CopyThatIsNotLinked_IsRefused()
    {
        var workflow = new Workflow("Report") { WorkflowTypeId = "WT-D4" };
        var section = new Section("Main", workflow) { Title = "Main" };
        var plain = new Block(BlockTypes.AiQuery, section, "plain")
        {
            Name = "Plain",
            Properties = new AiQueryProperties { UserPromptTemplate = "Prompt" },
        };
        await _harness.AddWorkflowAsync(workflow);

        ErrorOr<WorkflowDiff> result = await _handler.Handle(
            new GetSharedComponentInstanceDiffQuery(ORG, USER, "WT-D4", TargetPath: null, plain.StableBlockId),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsError);
        Assert.Equal(SharedComponentErrorCodes.TARGET_NOT_FOUND, result.FirstError.Code);
    }

    [Fact]
    public async Task Diff_UnknownWorkflow_IsNotFound()
    {
        ErrorOr<WorkflowDiff> result = await _handler.Handle(
            new GetSharedComponentInstanceDiffQuery(ORG, USER, "WT-NONE", "Sections[0]", TargetStableId: null),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsError);
        Assert.Equal(ErrorType.NotFound, result.FirstError.Type);
    }

    [Theory]
    [InlineData("Sections[0].Blocks[0]", "stable-1")]
    [InlineData(null, null)]
    [InlineData("Blocks[0]", null)]
    public void Validator_RefusesAnythingButOneWellFormedLocator(string? targetPath, string? targetStableId)
    {
        var validator = new GetSharedComponentInstanceDiffQueryValidator();

        Assert.False(validator.Validate(
            new GetSharedComponentInstanceDiffQuery(ORG, USER, "WT-D1", targetPath, targetStableId)).IsValid);
    }

    public void Dispose()
    {
        _harness.Dispose();
    }
}
