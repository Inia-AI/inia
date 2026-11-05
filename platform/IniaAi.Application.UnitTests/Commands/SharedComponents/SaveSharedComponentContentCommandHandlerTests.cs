using ErrorOr;
using IniaAi.Application.Commands.SharedComponents.SaveSharedComponentContent;
using IniaAi.Application.Commands.Workflows.SaveWorkflow;
using IniaAi.Application.Common.Workflows;
using IniaAi.Application.UnitTests.Common.SharedComponents;
using IniaAi.Application.UnitTests.Common.Workflows;
using IniaAi.Domain.Core.Interfaces;
using IniaAi.Domain.Entities.SharedComponents;
using IniaAi.Domain.Entities.SharedComponents.Enums;
using IniaAi.Domain.Entities.Signatures;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Blocks;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties;
using IniaAi.Domain.Entities.Workflows.Enums;
using IniaAi.Domain.Serialization;
using IniaAi.Infrastructure.Persistence.Core.Contexts;
using Microsoft.EntityFrameworkCore;

namespace IniaAi.Application.UnitTests.Commands.SharedComponents;

public class SaveSharedComponentContentCommandHandlerTests : IDisposable
{
    private const string ORG = "org-test";
    private const string USER = "user-editor";
    private const string COMPONENT_ID = "component-1";

    private readonly SharedComponentsTestHarness _harness = new();
    private readonly CoreDbContext _coreContext;
    private readonly ICoreUnitOfWork _coreUnitOfWork;
    private readonly WorkflowCanonicalizer _canonicalizer;
    private readonly SharedComponentContentNormalizer _normalizer;
    private readonly WorkflowSavePipeline _pipeline;
    private readonly SaveSharedComponentContentCommandHandler _handler;
    private readonly SaveWorkflowCommandHandler _saveHandler;

    public SaveSharedComponentContentCommandHandlerTests()
    {
        _coreContext = _harness.Context;
        _coreUnitOfWork = _harness.UnitOfWork;
        _canonicalizer = _harness.Canonicalizer;
        _normalizer = _harness.Normalizer;
        _pipeline = _harness.CreatePipeline();
        _handler = new SaveSharedComponentContentCommandHandler(_harness.CreateFanOutService(_pipeline));
        (_, _saveHandler) = _harness.CreateRealSaveSender();
    }

    [Fact]
    public async Task SaveContent_AppendsNewHeadAndFansOut()
    {
        (string jsonV1, string hashV1) = Definition(DefSection("Extract the FX rate"));
        SharedComponentVersion v1 = await SeedComponentAsync(jsonV1, hashV1);
        await _harness.SaveTemplateAsync(_saveHandler, ORG, "user-seed", "WT-A", LinkedConsumer(hashV1, "Extract the FX rate"));
        (string jsonV2, string hashV2) = Definition(DefSection("Extract the spot FX rate"));

        ErrorOr<SharedComponentAppendResult> result = await _handler.Handle(
            new SaveSharedComponentContentCommand(
                ORG, USER, COMPONENT_ID, DefSection("Extract the spot FX rate").ToJson(pretty: false), v1.Id),
            TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        Assert.False(result.Value.WasDeduped);
        Assert.Equal(hashV2, result.Value.HeadContentHash);
        SharedComponentVersion v2 = await _coreContext.SharedComponentVersions
            .AsNoTracking()
            .SingleAsync(v => v.SequenceNumber == 2, TestContext.Current.CancellationToken);
        Assert.Equal(jsonV2, v2.ContentJson);
        Assert.Equal(USER, v2.CreatedByUserId);
        Workflow reloaded = (await _coreUnitOfWork.Workflows.GetByTypeIdAsNoTrackingAsync("WT-A"))!;
        Section linked = reloaded.Sections.Single(s => s.SharedComponentId == COMPONENT_ID);
        Assert.Equal(hashV2, linked.SharedComponentVersionHash);
        Assert.Equal(
            "Extract the spot FX rate",
            ((AiQueryProperties)linked.Blocks.Single().Properties).UserPromptTemplate);
    }

    [Fact]
    public async Task SaveContent_PassesOrphanAcknowledgementThrough()
    {
        var scratch = new Workflow("Scratch");
        var twoSlots = new Section("Market Data", scratch) { Title = "Market Data" };
        _ = new Block(BlockTypes.AiQuery, twoSlots, "rate")
        {
            Name = "Node",
            Properties = new AiQueryProperties { UserPromptTemplate = "Extract the FX rate" },
        };
        _ = new Block(BlockTypes.AiQuery, twoSlots, "source")
        {
            Name = "Node",
            Properties = new AiQueryProperties
            {
                UserPromptTemplate = "Cite the source",
                SignatureFields = new Dictionary<string, SignatureFieldDefinition>
                {
                    ["director_name"] = new("Director", "Who signs", "N/A"),
                },
            },
        };
        string jsonV1 = _normalizer.ToDefinitionJson(twoSlots);
        string hashV1 = _canonicalizer.ComputeContentHash(jsonV1);
        SharedComponentVersion v1 = await SeedComponentAsync(jsonV1, hashV1);
        var consumer = new Workflow("Consumer") { Title = "Consumer" };
        twoSlots.CloneToWorkflow(consumer, 0);
        Section instance = consumer.Sections.Single();
        instance.SharedComponentId = COMPONENT_ID;
        instance.SharedComponentLinkState = SharedComponentLinkState.Linked;
        instance.SharedComponentVersionHash = hashV1;
        await _harness.SaveTemplateAsync(_saveHandler, ORG, "user-seed", "WT-A", consumer);
        Workflow saved = (await _coreUnitOfWork.Workflows.GetByTypeIdAsNoTrackingAsync("WT-A"))!;
        string sourceStableId = saved.Sections.Single()
            .Blocks.Single(b => b.ReplacementTag == "source").StableBlockId!;
        _ = _coreContext.Add(new SignatureFieldOverride
        {
            OrganizationId = "org-client",
            WorkflowTypeId = "WT-A",
            PlaceholderName = "director_name",
            Value = "Jane Director",
        });
        _ = await _coreContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        string slimJson = DefSection("Extract the FX rate").ToJson(pretty: false);

        ErrorOr<SharedComponentAppendResult> refused = await _handler.Handle(
            new SaveSharedComponentContentCommand(ORG, USER, COMPONENT_ID, slimJson, v1.Id),
            TestContext.Current.CancellationToken);

        Assert.True(refused.IsError);
        Assert.Equal(SharedComponentErrorCodes.ORPHANS_NEED_ACKNOWLEDGEMENT, refused.FirstError.Code);

        ErrorOr<SharedComponentAppendResult> accepted = await _handler.Handle(
            new SaveSharedComponentContentCommand(
                ORG, USER, COMPONENT_ID, slimJson, v1.Id, AcknowledgeSignatureOrphans: true),
            TestContext.Current.CancellationToken);

        Assert.False(accepted.IsError);
        Assert.Equal(0, await _coreContext.SignatureFieldOverrides
            .CountAsync(o => o.WorkflowTypeId == "WT-A", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task SaveContent_AgentBlockInDefinition_DisabledRuntime_Rejected()
    {
        (string jsonV1, string hashV1) = Definition(DefSection("Extract the FX rate"));
        SharedComponentVersion v1 = await SeedComponentAsync(jsonV1, hashV1);

        ErrorOr<SharedComponentAppendResult> result = await _handler.Handle(
            new SaveSharedComponentContentCommand(
                ORG, USER, COMPONENT_ID, AgentTestData.AgentSection(parameterized: false).ToJson(pretty: false), v1.Id),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsError);
        Assert.Equal(SharedComponentErrorCodes.AGENT_RUNTIME_DISABLED, result.FirstError.Code);
        _ = Assert.Single(await _coreContext.SharedComponentVersions
            .Where(v => v.SharedComponentId == COMPONENT_ID)
            .ToListAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task SaveContent_ParameterizedSectionWithAgent_EnabledRuntime_Appends()
    {
        (string jsonV1, string hashV1) = Definition(DefSection("Extract the FX rate"));
        SharedComponentVersion v1 = await SeedComponentAsync(jsonV1, hashV1);
        var enabledHandler = new SaveSharedComponentContentCommandHandler(
            _harness.CreateFanOutService(AgentTestData.EnabledRuntime()));

        ErrorOr<SharedComponentAppendResult> result = await enabledHandler.Handle(
            new SaveSharedComponentContentCommand(
                ORG, USER, COMPONENT_ID, AgentTestData.AgentSection(parameterized: true).ToJson(pretty: false), v1.Id),
            TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        Assert.Equal(2, (await _coreContext.SharedComponentVersions
            .Where(v => v.SharedComponentId == COMPONENT_ID)
            .ToListAsync(TestContext.Current.CancellationToken)).Count);
    }

    private async Task<SharedComponentVersion> SeedComponentAsync(string definitionJson, string contentHash)
    {
        _ = await _harness.AddComponentAsync(SharedComponentKind.Section, "Market Data", id: COMPONENT_ID);

        return await _harness.AddVersionAsync(COMPONENT_ID, 1, contentHash, definitionJson);
    }

    private (string DefinitionJson, string ContentHash) Definition(Section section)
    {
        return _harness.Definition(section);
    }

    private static Section DefSection(string ratePrompt)
    {
        var section = new Section("Market Data", new Workflow("Scratch")) { Title = "Market Data" };
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
        DefSection(ratePrompt).CloneToWorkflow(workflow, 0);
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
