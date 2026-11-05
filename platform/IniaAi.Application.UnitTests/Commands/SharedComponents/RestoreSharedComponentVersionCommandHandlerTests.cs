using ErrorOr;
using IniaAi.Application.Commands.SharedComponents.RestoreSharedComponentVersion;
using IniaAi.Application.Commands.Workflows.SaveWorkflow;
using IniaAi.Application.Common.Workflows;
using IniaAi.Application.UnitTests.Common.SharedComponents;
using IniaAi.Domain.Core.Interfaces;
using IniaAi.Domain.Entities.SharedComponents;
using IniaAi.Domain.Entities.SharedComponents.Enums;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Blocks;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties;
using IniaAi.Domain.Entities.Workflows.Enums;
using IniaAi.Infrastructure.Persistence.Core.Contexts;
using Microsoft.EntityFrameworkCore;

namespace IniaAi.Application.UnitTests.Commands.SharedComponents;

public class RestoreSharedComponentVersionCommandHandlerTests : IDisposable
{
    private const string ORG = "org-test";
    private const string USER = "user-restore";
    private const string COMPONENT_ID = "component-1";

    private readonly SharedComponentsTestHarness _harness = new();
    private readonly CoreDbContext _coreContext;
    private readonly ICoreUnitOfWork _coreUnitOfWork;
    private readonly WorkflowSavePipeline _pipeline;
    private readonly RestoreSharedComponentVersionCommandHandler _handler;
    private readonly SaveWorkflowCommandHandler _saveHandler;

    public RestoreSharedComponentVersionCommandHandlerTests()
    {
        _coreContext = _harness.Context;
        _coreUnitOfWork = _harness.UnitOfWork;
        _pipeline = _harness.CreatePipeline();
        _handler = new RestoreSharedComponentVersionCommandHandler(
            _coreUnitOfWork, _harness.CreateFanOutService(_pipeline));
        (_, _saveHandler) = _harness.CreateRealSaveSender();
    }

    [Fact]
    public async Task Restore_OlderVersion_AppendsItsContentAsNewHeadAndFansOut()
    {
        (string jsonV1, string hashV1) = Definition(DefSection("Market Data", "Extract the FX rate"));
        (string jsonV2, string hashV2) = Definition(DefSection("Market Data", "Extract the spot FX rate"));
        await _harness.AddComponentAsync(SharedComponentKind.Section, "Market Data", id: COMPONENT_ID);
        SharedComponentVersion v1 = await _harness.AddVersionAsync(COMPONENT_ID, 1, hashV1, jsonV1);
        SharedComponentVersion v2 = await _harness.AddVersionAsync(COMPONENT_ID, 2, hashV2, jsonV2, parentVersionId: v1.Id);
        await _harness.SaveTemplateAsync(_saveHandler, ORG, "user-seed", "WT-A", LinkedConsumer(hashV2, "Extract the spot FX rate"));

        ErrorOr<SharedComponentAppendResult> result = await _handler.Handle(
            new RestoreSharedComponentVersionCommand(ORG, USER, COMPONENT_ID, v1.Id, v2.Id),
            TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        Assert.False(result.Value.WasDeduped);
        SharedComponentVersion v3 = await _coreContext.SharedComponentVersions
            .AsNoTracking()
            .SingleAsync(v => v.SequenceNumber == 3, TestContext.Current.CancellationToken);
        Assert.Equal(hashV1, v3.ContentHash);
        Assert.Equal(jsonV1, v3.ContentJson);
        Assert.Equal(v2.Id, v3.ParentVersionId);
        Assert.Equal(v3.Id, result.Value.HeadVersionId);
        Workflow reloaded = (await _coreUnitOfWork.Workflows.GetByTypeIdAsNoTrackingAsync("WT-A"))!;
        Section linked = reloaded.Sections.Single(s => s.SharedComponentId == COMPONENT_ID);
        Assert.Equal(hashV1, linked.SharedComponentVersionHash);
        Assert.Equal(
            "Extract the FX rate",
            ((AiQueryProperties)linked.Blocks.Single().Properties).UserPromptTemplate);
    }

    [Fact]
    public async Task Restore_CurrentHead_DedupsToNoOp()
    {
        (string jsonV1, string hashV1) = Definition(DefSection("Market Data", "Extract the FX rate"));
        await _harness.AddComponentAsync(SharedComponentKind.Section, "Market Data", id: COMPONENT_ID);
        SharedComponentVersion v1 = await _harness.AddVersionAsync(COMPONENT_ID, 1, hashV1, jsonV1);

        ErrorOr<SharedComponentAppendResult> result = await _handler.Handle(
            new RestoreSharedComponentVersionCommand(ORG, USER, COMPONENT_ID, v1.Id, v1.Id),
            TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        Assert.True(result.Value.WasDeduped);
        Assert.Equal(v1.Id, result.Value.HeadVersionId);
        Assert.Equal(1, await _coreContext.SharedComponentVersions
            .CountAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Restore_UnknownVersion_ReturnsNotFound()
    {
        (string jsonV1, string hashV1) = Definition(DefSection("Market Data", "Extract the FX rate"));
        await _harness.AddComponentAsync(SharedComponentKind.Section, "Market Data", id: COMPONENT_ID);
        SharedComponentVersion v1 = await _harness.AddVersionAsync(COMPONENT_ID, 1, hashV1, jsonV1);

        ErrorOr<SharedComponentAppendResult> result = await _handler.Handle(
            new RestoreSharedComponentVersionCommand(ORG, USER, COMPONENT_ID, "missing-version", v1.Id),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsError);
        Assert.Equal(ErrorType.NotFound, result.FirstError.Type);
        Assert.Equal(SharedComponentErrorCodes.UNKNOWN_VERSION, result.FirstError.Code);
    }

    [Fact]
    public async Task Restore_PayloadAbsentSource_ConflictsOnWriteChannel()
    {
        (string jsonV2, string hashV2) = Definition(DefSection("Market Data", "Extract the spot FX rate"));
        await _harness.AddComponentAsync(SharedComponentKind.Section, "Market Data", id: COMPONENT_ID);
        SharedComponentVersion absent = await _harness.AddVersionAsync(
            COMPONENT_ID, 1, new string('a', 64), contentJson: null, payloadAbsentAt: DateTime.UtcNow);
        SharedComponentVersion head = await _harness.AddVersionAsync(COMPONENT_ID, 2, hashV2, jsonV2, parentVersionId: absent.Id);

        ErrorOr<SharedComponentAppendResult> result = await _handler.Handle(
            new RestoreSharedComponentVersionCommand(ORG, USER, COMPONENT_ID, absent.Id, head.Id),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsError);
        Assert.Equal(ErrorType.Conflict, result.FirstError.Type);
        Assert.Equal(SharedComponentErrorCodes.PAYLOAD_ABSENT, result.FirstError.Code);
    }

    [Fact]
    public async Task Restore_UnknownComponent_ReturnsNotFound()
    {
        ErrorOr<SharedComponentAppendResult> result = await _handler.Handle(
            new RestoreSharedComponentVersionCommand(ORG, USER, "missing-component", "any-version", "any-head"),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsError);
        Assert.Equal(ErrorType.NotFound, result.FirstError.Type);
        Assert.Equal(SharedComponentErrorCodes.UNKNOWN_COMPONENT, result.FirstError.Code);
    }

    [Fact]
    public async Task Restore_RetiredComponent_RetiredWinsOverPayloadAbsent()
    {
        (string jsonV2, string hashV2) = Definition(DefSection("Market Data", "Extract the spot FX rate"));
        await _harness.AddComponentAsync(SharedComponentKind.Section, "Market Data", id: COMPONENT_ID);
        SharedComponentVersion absent = await _harness.AddVersionAsync(
            COMPONENT_ID, 1, new string('a', 64), contentJson: null, payloadAbsentAt: DateTime.UtcNow);
        SharedComponentVersion head = await _harness.AddVersionAsync(COMPONENT_ID, 2, hashV2, jsonV2, parentVersionId: absent.Id);
        SharedComponent component = await _coreContext.SharedComponents
            .SingleAsync(c => c.Id == COMPONENT_ID, TestContext.Current.CancellationToken);
        component.State = SharedComponentState.Retired;
        _ = await _coreContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        ErrorOr<SharedComponentAppendResult> result = await _handler.Handle(
            new RestoreSharedComponentVersionCommand(ORG, USER, COMPONENT_ID, absent.Id, head.Id),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsError);
        Assert.Equal(ErrorType.Conflict, result.FirstError.Type);
        Assert.Equal(SharedComponentErrorCodes.RETIRED, result.FirstError.Code);
    }

    private (string DefinitionJson, string ContentHash) Definition(Section section)
    {
        return _harness.Definition(section);
    }

    private static Section DefSection(string title, string ratePrompt)
    {
        var section = new Section(title, new Workflow("Scratch")) { Title = title };
        _ = new Block(BlockTypes.AiQuery, section, "rate")
        {
            Name = "Node",
            Properties = new AiQueryProperties { UserPromptTemplate = ratePrompt },
        };

        return section;
    }

    private static Workflow LinkedConsumer(string versionHash, string ratePrompt)
    {
        var workflow = new Workflow("Consumer") { Title = "Consumer" };
        Section linked = DefSection("Market Data", ratePrompt);
        linked.CloneToWorkflow(workflow, 0);
        Section instance = workflow.Sections.Single();
        instance.SharedComponentId = COMPONENT_ID;
        instance.SharedComponentLinkState = SharedComponentLinkState.Linked;
        instance.SharedComponentVersionHash = versionHash;

        return workflow;
    }

    public void Dispose()
    {
        _harness.Dispose();
    }
}
