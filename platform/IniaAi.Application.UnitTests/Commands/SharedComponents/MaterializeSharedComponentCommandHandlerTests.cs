using ErrorOr;
using FluentValidation.Results;
using IniaAi.Application.Commands.SharedComponents.MaterializeSharedComponent;
using IniaAi.Application.Commands.Workflows.SaveWorkflow;
using IniaAi.Application.Common.Dispatching;
using IniaAi.Application.Common.Workflows;
using IniaAi.Application.UnitTests.Common.SharedComponents;
using IniaAi.Application.UnitTests.Common.Workflows;
using IniaAi.Configuration.Platform;
using IniaAi.Domain.Entities.SharedComponents;
using IniaAi.Domain.Entities.SharedComponents.Enums;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Blocks;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties;
using IniaAi.Domain.Entities.Workflows.Enums;
using IniaAi.Domain.Serialization;
using Microsoft.Extensions.Logging.Abstractions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace IniaAi.Application.UnitTests.Commands.SharedComponents;

public class MaterializeSharedComponentCommandHandlerTests
{
    private const string ORG = "org-test";
    private const string USER = "user-test";
    private const string TYPE = "WT-Report";

    private static readonly WorkflowCanonicalizer _canonicalizer = new();
    private static readonly SharedComponentContentNormalizer _normalizer = new(_canonicalizer);

    [Fact]
    public async Task MaterializeBlock_LandsLinkedCopy_AtIndex_AndAppendsVersion()
    {
        using var harness = new SharedComponentsTestHarness();
        (MaterializeSharedComponentCommandHandler materialize, SaveWorkflowCommandHandler save) = CreateHandlers(harness);
        (Workflow seeded, string headVersionId) = await SeedTemplateAsync(harness, save);
        string preHash = _canonicalizer.ComputeContentHash(seeded);
        SharedComponent component = await SeedBlockComponentAsync(harness, "FX Rate", "Fetch the FX rate");

        ErrorOr<MaterializeSharedComponentResult> result = await materialize.Handle(
            new MaterializeSharedComponentCommand(
                ORG, USER, TYPE, component.Id,
                TargetSectionPath: "Sections[0]", Index: 1, ReplacementTag: "landed_fx",
                BaseVersionId: headVersionId),
            TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        Assert.Equal("Sections[0].Blocks[1]", result.Value.LandedPath);
        Assert.Equal("landed_fx", result.Value.LandedReplacementTag);
        Assert.Null(result.Value.Bindings);
        Assert.False(result.Value.WasDeduped);
        Assert.NotEqual(preHash, result.Value.ContentHash);
        Workflow reloaded = (await harness.UnitOfWork.Workflows.GetByTypeIdAsNoTrackingAsync(TYPE))!;
        Block landed = reloaded.GetAllBlocks().Single(b => b.ReplacementTag == "landed_fx");
        Assert.Equal(component.Id, landed.SharedComponentId);
        Assert.Equal(SharedComponentLinkState.Linked, landed.SharedComponentLinkState);
        Assert.Equal(result.Value.ComponentVersionHash, landed.SharedComponentVersionHash);
        Assert.False(string.IsNullOrEmpty(landed.StableBlockId));
        Assert.Equal(2, harness.Context.WorkflowVersions.Count());
    }

    [Fact]
    public async Task MaterializeBlock_IntoLinkedSection_LandsAsLocalInsert()
    {
        using var harness = new SharedComponentsTestHarness();
        (MaterializeSharedComponentCommandHandler materialize, SaveWorkflowCommandHandler save) = CreateHandlers(harness);
        (_, string headVersionId) = await SeedTemplateAsync(harness, save);
        SharedComponent sectionComponent = await SeedSectionComponentAsync(harness, "Risk", innerTag: "risk_intro");

        ErrorOr<MaterializeSharedComponentResult> sectionLanded = await materialize.Handle(
            new MaterializeSharedComponentCommand(
                ORG, USER, TYPE, sectionComponent.Id, BaseVersionId: headVersionId),
            TestContext.Current.CancellationToken);
        Assert.False(sectionLanded.IsError);

        SharedComponent blockComponent = await SeedBlockComponentAsync(harness, "FX Rate", "Fetch the FX rate");

        ErrorOr<MaterializeSharedComponentResult> result = await materialize.Handle(
            new MaterializeSharedComponentCommand(
                ORG, USER, TYPE, blockComponent.Id,
                TargetSectionPath: "Sections[1]", ReplacementTag: "landed_fx",
                BaseVersionId: sectionLanded.Value.VersionId),
            TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        Workflow reloaded = (await harness.UnitOfWork.Workflows.GetByTypeIdAsNoTrackingAsync(TYPE))!;
        Section linkedSection = reloaded.Sections.OrderBy(s => s.Order).ElementAt(1);
        Assert.Equal(sectionComponent.Id, linkedSection.SharedComponentId);
        Assert.Equal(SharedComponentLinkState.Linked, linkedSection.SharedComponentLinkState);
        Block landed = linkedSection.Blocks.Single(b => b.ReplacementTag == "landed_fx");
        Assert.Equal(blockComponent.Id, landed.SharedComponentId);
        Assert.Equal(SharedComponentLinkState.Linked, landed.SharedComponentLinkState);
        Assert.Equal(2, linkedSection.Blocks.Count);
    }

    [Fact]
    public async Task MaterializeSection_RenamesCollidingInnerTags_AndRecordsBindings()
    {
        using var harness = new SharedComponentsTestHarness();
        (MaterializeSharedComponentCommandHandler materialize, SaveWorkflowCommandHandler save) = CreateHandlers(harness);
        (_, string headVersionId) = await SeedTemplateAsync(harness, save);
        SharedComponent component = await SeedSectionComponentAsync(harness, "Risk", innerTag: "one");

        ErrorOr<MaterializeSharedComponentResult> result = await materialize.Handle(
            new MaterializeSharedComponentCommand(
                ORG, USER, TYPE, component.Id, BaseVersionId: headVersionId),
            TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        Assert.Equal("Sections[1]", result.Value.LandedPath);
        Assert.NotNull(result.Value.Bindings);
        string renamed = result.Value.Bindings!["one"];
        Assert.NotEqual("one", renamed);
        Workflow reloaded = (await harness.UnitOfWork.Workflows.GetByTypeIdAsNoTrackingAsync(TYPE))!;
        Section landed = reloaded.Sections.OrderBy(s => s.Order).ElementAt(1);
        Assert.Equal(component.Id, landed.SharedComponentId);
        Assert.Equal(renamed, Assert.Single(landed.Blocks).ReplacementTag);
        Assert.Equal(result.Value.Bindings, landed.SharedComponentBindings);
    }

    [Fact]
    public async Task Materialize_UnknownRetiredAndPayloadAbsentComponents_AreRejected()
    {
        using var harness = new SharedComponentsTestHarness();
        (MaterializeSharedComponentCommandHandler materialize, SaveWorkflowCommandHandler save) = CreateHandlers(harness);
        (_, string headVersionId) = await SeedTemplateAsync(harness, save);
        SharedComponent retired = await harness.AddComponentAsync(
            SharedComponentKind.Block, "Retired", SharedComponentState.Retired);
        _ = await harness.AddVersionAsync(retired.Id, 1, new string('1', 64), "{}");
        SharedComponent absent = await harness.AddComponentAsync(SharedComponentKind.Block, "Absent");
        _ = await harness.AddVersionAsync(
            absent.Id, 1, new string('2', 64), contentJson: null, payloadAbsentAt: DateTime.UtcNow);

        ErrorOr<MaterializeSharedComponentResult> unknown = await materialize.Handle(
            new MaterializeSharedComponentCommand(
                ORG, USER, TYPE, "missing", TargetSectionPath: "Sections[0]", BaseVersionId: headVersionId),
            TestContext.Current.CancellationToken);
        ErrorOr<MaterializeSharedComponentResult> retiredResult = await materialize.Handle(
            new MaterializeSharedComponentCommand(
                ORG, USER, TYPE, retired.Id, TargetSectionPath: "Sections[0]", BaseVersionId: headVersionId),
            TestContext.Current.CancellationToken);
        ErrorOr<MaterializeSharedComponentResult> absentResult = await materialize.Handle(
            new MaterializeSharedComponentCommand(
                ORG, USER, TYPE, absent.Id, TargetSectionPath: "Sections[0]", BaseVersionId: headVersionId),
            TestContext.Current.CancellationToken);

        Assert.Equal(SharedComponentErrorCodes.UNKNOWN_COMPONENT, unknown.FirstError.Code);
        Assert.Equal(SharedComponentErrorCodes.RETIRED, retiredResult.FirstError.Code);
        Assert.Equal(SharedComponentErrorCodes.PAYLOAD_ABSENT, absentResult.FirstError.Code);
    }

    [Fact]
    public async Task MaterializeBlock_FromAVersionStoredWithAFailureSetting_LandsWithTheSettingUnset()
    {
        using var harness = new SharedComponentsTestHarness();
        (MaterializeSharedComponentCommandHandler materialize, SaveWorkflowCommandHandler save) = CreateHandlers(harness);
        (_, string headVersionId) = await SeedTemplateAsync(harness, save);
        SharedComponent component = await SeedBlockComponentAsync(
            harness, "FX Rate", "Fetch the FX rate", storedAsCritical: true);

        ErrorOr<MaterializeSharedComponentResult> result = await materialize.Handle(
            new MaterializeSharedComponentCommand(
                ORG, USER, TYPE, component.Id,
                TargetSectionPath: "Sections[0]", ReplacementTag: "landed_fx",
                BaseVersionId: headVersionId),
            TestContext.Current.CancellationToken);

        Assert.False(result.IsError, result.IsError ? result.FirstError.Description : null);
        Workflow reloaded = (await harness.UnitOfWork.Workflows.GetByTypeIdAsNoTrackingAsync(TYPE))!;
        Block landed = reloaded.GetAllBlocks().Single(b => b.ReplacementTag == "landed_fx");
        Assert.Equal(component.Id, landed.SharedComponentId);
        Assert.Null(landed.Properties.IsCritical);
    }

    [Fact]
    public async Task MaterializeSection_FromVersionsStoredWithAFailureSetting_LandsInnerAndReferencedBlocksUnset()
    {
        using var harness = new SharedComponentsTestHarness();
        (MaterializeSharedComponentCommandHandler materialize, SaveWorkflowCommandHandler save) = CreateHandlers(harness);
        (_, string headVersionId) = await SeedTemplateAsync(harness, save);
        SharedComponent referenced = await SeedBlockComponentAsync(
            harness, "KYC", "Check the KYC file", storedAsCritical: true);
        SharedComponent container = await SeedSectionComponentAsync(
            harness, "Risk", "risk_intro", referencedComponentId: referenced.Id, storedAsCritical: true);

        ErrorOr<MaterializeSharedComponentResult> result = await materialize.Handle(
            new MaterializeSharedComponentCommand(
                ORG, USER, TYPE, container.Id, BaseVersionId: headVersionId),
            TestContext.Current.CancellationToken);

        Assert.False(result.IsError, result.IsError ? result.FirstError.Description : null);
        Workflow reloaded = (await harness.UnitOfWork.Workflows.GetByTypeIdAsNoTrackingAsync(TYPE))!;
        Section landed = reloaded.Sections.Single(s => s.SharedComponentId == container.Id);
        Assert.Equal(2, landed.Blocks.Count);
        Assert.Contains(landed.Blocks, b => b.SharedComponentId == referenced.Id);
        Assert.All(landed.Blocks, b => Assert.Null(b.Properties.IsCritical));
    }

    [Fact]
    public async Task Materialize_KindShapeMismatches_AreRejected()
    {
        using var harness = new SharedComponentsTestHarness();
        (MaterializeSharedComponentCommandHandler materialize, SaveWorkflowCommandHandler save) = CreateHandlers(harness);
        (_, string headVersionId) = await SeedTemplateAsync(harness, save);
        SharedComponent blockComponent = await SeedBlockComponentAsync(harness, "FX Rate", "Prompt");
        SharedComponent sectionComponent = await SeedSectionComponentAsync(harness, "Risk", "risk_intro");

        ErrorOr<MaterializeSharedComponentResult> blockWithoutLocator = await materialize.Handle(
            new MaterializeSharedComponentCommand(ORG, USER, TYPE, blockComponent.Id, BaseVersionId: headVersionId),
            TestContext.Current.CancellationToken);
        ErrorOr<MaterializeSharedComponentResult> sectionWithLocator = await materialize.Handle(
            new MaterializeSharedComponentCommand(
                ORG, USER, TYPE, sectionComponent.Id, TargetSectionPath: "Sections[0]", BaseVersionId: headVersionId),
            TestContext.Current.CancellationToken);

        Assert.True(blockWithoutLocator.IsError);
        Assert.Equal(ErrorType.Validation, blockWithoutLocator.FirstError.Type);
        Assert.True(sectionWithLocator.IsError);
        Assert.Equal(ErrorType.Validation, sectionWithLocator.FirstError.Type);
    }

    [Fact]
    public async Task Materialize_StaleBaseVersion_Conflicts()
    {
        using var harness = new SharedComponentsTestHarness();
        (MaterializeSharedComponentCommandHandler materialize, SaveWorkflowCommandHandler save) = CreateHandlers(harness);
        _ = await SeedTemplateAsync(harness, save);
        SharedComponent component = await SeedBlockComponentAsync(harness, "FX Rate", "Prompt");

        ErrorOr<MaterializeSharedComponentResult> result = await materialize.Handle(
            new MaterializeSharedComponentCommand(
                ORG, USER, TYPE, component.Id, TargetSectionPath: "Sections[0]", BaseVersionId: "stale-id"),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsError);
        Assert.Equal(SaveWorkflowCommandHandler.VERSION_CONFLICT_CODE, result.FirstError.Code);
    }

    [Fact]
    public async Task Materialize_DanglingReferenceInDefinition_SurfacesAsWarning()
    {
        using var harness = new SharedComponentsTestHarness();
        (MaterializeSharedComponentCommandHandler materialize, SaveWorkflowCommandHandler save) = CreateHandlers(harness);
        (_, string headVersionId) = await SeedTemplateAsync(harness, save);
        SharedComponent component = await SeedBlockComponentAsync(
            harness, "Reader", "Reads {tag_that_exists_nowhere.answer}");

        ErrorOr<MaterializeSharedComponentResult> result = await materialize.Handle(
            new MaterializeSharedComponentCommand(
                ORG, USER, TYPE, component.Id, TargetSectionPath: "Sections[0]", BaseVersionId: headVersionId),
            TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        Assert.Contains(
            result.Value.Warnings,
            w => w.Message.Contains("tag_that_exists_nowhere", StringComparison.Ordinal));
    }

    [Fact]
    public void Validator_RejectsDualLocators_BadPathShape_AndNegativeIndex()
    {
        var validator = new MaterializeSharedComponentCommandValidator();

        ValidationResult dual = validator.Validate(new MaterializeSharedComponentCommand(
            ORG, USER, TYPE, "c1", TargetSectionPath: "Sections[0]", TargetSectionStableId: "sid"));
        ValidationResult badPath = validator.Validate(new MaterializeSharedComponentCommand(
            ORG, USER, TYPE, "c1", TargetSectionPath: "Sections[0].Blocks[1]"));
        ValidationResult negativeIndex = validator.Validate(new MaterializeSharedComponentCommand(
            ORG, USER, TYPE, "c1", Index: -1));
        ValidationResult ok = validator.Validate(new MaterializeSharedComponentCommand(
            ORG, USER, TYPE, "c1", TargetSectionPath: "Sections[0]", Index: 0));

        Assert.False(dual.IsValid);
        Assert.False(badPath.IsValid);
        Assert.False(negativeIndex.IsValid);
        Assert.True(ok.IsValid);
    }

    [Fact]
    public async Task MaterializeSection_WhenARefTargetHasNoResolvableHead_FailsInsteadOfDroppingTheBlock()
    {
        using var harness = new SharedComponentsTestHarness();
        (MaterializeSharedComponentCommandHandler materialize, SaveWorkflowCommandHandler save) = CreateHandlers(harness);
        (_, string headVersionId) = await SeedTemplateAsync(harness, save);
        SharedComponent vanished = await harness.AddComponentAsync(SharedComponentKind.Block, "Vanished", id: "c-vanished");
        _ = await harness.AddVersionAsync(vanished.Id, 1, "hash-vanished", contentJson: null, payloadAbsentAt: DateTime.UtcNow);
        SharedComponent container = await SeedSectionComponentAsync(
            harness, "Risk", "risk_intro", referencedComponentId: "c-vanished");

        ErrorOr<MaterializeSharedComponentResult> result = await materialize.Handle(
            new MaterializeSharedComponentCommand(
                ORG, USER, TYPE, container.Id, BaseVersionId: headVersionId),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsError);
        Assert.Equal(ErrorType.Conflict, result.FirstError.Type);
        Assert.Equal(SharedComponentErrorCodes.PAYLOAD_ABSENT, result.FirstError.Code);
        Assert.Contains("c-vanished", result.FirstError.Description);
    }

    [Fact]
    public async Task MaterializeAgentBlock_SkillReferencingAbsentTag_LandsWithAWarning()
    {
        using var harness = new SharedComponentsTestHarness();
        (MaterializeSharedComponentCommandHandler materialize, SaveWorkflowCommandHandler save) =
            CreateHandlers(harness, AgentTestData.EnabledRuntime());
        (_, string headVersionId) = await SeedTemplateAsync(harness, save);
        SharedComponent component = await SeedAgentComponentAsync(
            harness, skillContent: "Read {{ghost_tag.answer}} before answering.");

        ErrorOr<MaterializeSharedComponentResult> result = await materialize.Handle(
            new MaterializeSharedComponentCommand(
                ORG, USER, TYPE, component.Id,
                TargetSectionPath: "Sections[0]", Index: null, ReplacementTag: "landed_agent",
                BaseVersionId: headVersionId),
            TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        Assert.Contains(result.Value.Warnings, warning => warning.Message.Contains("ghost_tag", StringComparison.Ordinal));
    }

    [Fact]
    public async Task MaterializeAgentBlock_SkillReferencingPresentTag_Lands()
    {
        using var harness = new SharedComponentsTestHarness();
        (MaterializeSharedComponentCommandHandler materialize, SaveWorkflowCommandHandler save) =
            CreateHandlers(harness, AgentTestData.EnabledRuntime());
        (_, string headVersionId) = await SeedTemplateAsync(harness, save);
        SharedComponent component = await SeedAgentComponentAsync(
            harness, skillContent: "Read {{one.answer}} before answering.");

        ErrorOr<MaterializeSharedComponentResult> result = await materialize.Handle(
            new MaterializeSharedComponentCommand(
                ORG, USER, TYPE, component.Id,
                TargetSectionPath: "Sections[0]", Index: null, ReplacementTag: "landed_agent",
                BaseVersionId: headVersionId),
            TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        Workflow reloaded = (await harness.UnitOfWork.Workflows.GetByTypeIdAsNoTrackingAsync(TYPE))!;
        Block landed = reloaded.GetAllBlocks().Single(b => b.ReplacementTag == "landed_agent");
        Assert.Equal(BlockTypes.Agent, landed.Type);
        Assert.Equal(component.Id, landed.SharedComponentId);
    }

    private static async Task<SharedComponent> SeedAgentComponentAsync(
        SharedComponentsTestHarness harness,
        string skillContent)
    {
        Block agent = AgentTestData.AgentBlockDefinition(skillContent);
        SharedComponent component = await harness.AddComponentAsync(SharedComponentKind.Block, "Extractor");
        _ = await harness.AddVersionAsync(
            component.Id, 1, _normalizer.ComputeContentHash(agent), _normalizer.ToDefinitionJson(agent));

        return component;
    }

    private static (MaterializeSharedComponentCommandHandler Materialize, SaveWorkflowCommandHandler Save) CreateHandlers(
        SharedComponentsTestHarness harness,
        AgentRuntimeSettings? agentRuntime = null)
    {
        (ISender sender, SaveWorkflowCommandHandler saveHandler) = harness.CreateRealSaveSender(agentRuntime);
        var materializeHandler = new MaterializeSharedComponentCommandHandler(
            harness.UnitOfWork,
            sender,
            new SharedComponentEditor(_canonicalizer, _normalizer),
            new WorkflowTemplateStructuralValidator(),
            NullLogger<MaterializeSharedComponentCommandHandler>.Instance);

        return (materializeHandler, saveHandler);
    }

    private static async Task<(Workflow Workflow, string HeadVersionId)> SeedTemplateAsync(
        SharedComponentsTestHarness harness,
        SaveWorkflowCommandHandler saveHandler)
    {
        var workflow = new Workflow("Report") { Title = "Report" };
        var section = new Section("Body", workflow) { Title = "Body" };
        _ = new Block(BlockTypes.AiQuery, section, "one")
        {
            Name = "Counterparty check",
            Properties = new AiQueryProperties { UserPromptTemplate = "Prompt one" },
        };

        string headVersionId = await harness.SaveTemplateAsync(saveHandler, ORG, USER, TYPE, workflow);

        return (workflow, headVersionId);
    }

    private static async Task<SharedComponent> SeedBlockComponentAsync(
        SharedComponentsTestHarness harness,
        string name,
        string prompt,
        bool storedAsCritical = false)
    {
        var scratch = new Workflow("scratch");
        var section = new Section("scratch", scratch);
        var block = new Block(BlockTypes.AiQuery, section, "source_tag")
        {
            Name = name,
            Properties = new AiQueryProperties { UserPromptTemplate = prompt },
        };
        SharedComponent component = await harness.AddComponentAsync(SharedComponentKind.Block, name);
        (string json, string hash) = StoredDefinition(_normalizer.ToDefinitionJson(block), storedAsCritical);
        _ = await harness.AddVersionAsync(component.Id, 1, hash, json);

        return component;
    }

    private static async Task<SharedComponent> SeedSectionComponentAsync(
        SharedComponentsTestHarness harness,
        string title,
        string innerTag,
        string? referencedComponentId = null,
        bool storedAsCritical = false)
    {
        var scratch = new Workflow("scratch");
        var section = new Section(title, scratch) { Title = title };
        _ = new Block(BlockTypes.AiQuery, section, innerTag)
        {
            Name = $"Block {innerTag}",
            Properties = new AiQueryProperties { UserPromptTemplate = $"Prompt {innerTag}" },
        };
        if (referencedComponentId is not null)
        {
            section.Entries =
            [
                new SectionEntry { SlotTag = innerTag, Kind = SectionEntryKind.Inline },
                new SectionEntry
                {
                    SlotTag = "ref_slot",
                    Kind = SectionEntryKind.ComponentRef,
                    SharedComponentId = referencedComponentId,
                },
            ];
        }

        SharedComponent component = await harness.AddComponentAsync(SharedComponentKind.Section, title);
        (string json, string hash) = StoredDefinition(_normalizer.ToDefinitionJson(section), storedAsCritical);
        _ = await harness.AddVersionAsync(component.Id, 1, hash, json);

        return component;
    }

    /// <summary>
    /// A definition as a version stored before the failure setting left definitions holds it: every
    /// block set to stop the run.
    /// </summary>
    private static (string Json, string Hash) StoredDefinition(string definitionJson, bool storedAsCritical)
    {
        if (!storedAsCritical)
        {
            return (definitionJson, _normalizer.ComputeContentHashOfDefinitionJson(definitionJson));
        }

        var stored = JObject.Parse(definitionJson);
        IEnumerable<JObject> blocks = stored[nameof(Section.Blocks)] is JArray sectionBlocks
            ? sectionBlocks.OfType<JObject>()
            : [stored];
        foreach (JObject block in blocks)
        {
            block[nameof(Block.Properties)]![nameof(AiQueryProperties.IsCritical)] = true;
        }

        string json = stored.ToString(Formatting.None);

        return (json, _normalizer.ComputeContentHashOfDefinitionJson(json));
    }
}
