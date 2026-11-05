using ErrorOr;
using FluentValidation.Results;
using IniaAi.Application.Commands.SharedComponents.PromoteSharedComponent;
using IniaAi.Application.Commands.Workflows.SaveWorkflow;
using IniaAi.Application.Common.Workflows;
using IniaAi.Application.UnitTests.Common.SharedComponents;
using IniaAi.Domain.Entities.SharedComponents;
using IniaAi.Domain.Entities.SharedComponents.Enums;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Blocks;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties;
using IniaAi.Domain.Entities.Workflows.Enums;
using IniaAi.Domain.Serialization;
using Microsoft.Extensions.Logging.Abstractions;

namespace IniaAi.Application.UnitTests.Commands.SharedComponents;

public class PromoteSharedComponentCommandHandlerTests
{
    private const string ORG = "org-test";
    private const string USER = "user-test";
    private const string TYPE = "WT-Report";

    [Fact]
    public async Task Promote_CreatesRegistryRows_StampsInstance_AndAppendsLinkOnlyVersion()
    {
        using var harness = new SharedComponentsTestHarness();
        _ = await harness.AddCategoryAsync("analysis", "Analysis");
        (PromoteSharedComponentCommandHandler promote, SaveWorkflowCommandHandler save) = CreateHandlers(harness);
        (Workflow seeded, string headVersionId) = await SeedTemplateAsync(harness, save);
        string preHash = new WorkflowCanonicalizer().ComputeContentHash(seeded);

        ErrorOr<PromoteSharedComponentResult> result = await promote.Handle(
            new PromoteSharedComponentCommand(
                ORG, USER, TYPE, TargetPath: "Sections[0].Blocks[0]", Name: "Counterparty check",
                Description: "Reusable check", CategoryId: "analysis", BaseVersionId: headVersionId),
            TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        Assert.False(result.Value.WasAttached);
        Assert.False(result.Value.WasDeduped);
        Assert.Equal(preHash, result.Value.ContentHash);
        SharedComponent component = Assert.Single(harness.Context.SharedComponents);
        Assert.Equal("Counterparty check", component.Name);
        Assert.Equal(SharedComponentKind.Block, component.Kind);
        Assert.Equal("analysis", component.CategoryId);
        SharedComponentVersion version = Assert.Single(harness.Context.SharedComponentVersions);
        Assert.Equal(1, version.SequenceNumber);
        Assert.Equal(result.Value.ComponentVersionHash, version.ContentHash);
        Assert.Contains("\"Prompt one\"", version.ContentJson);
        Workflow reloaded = (await harness.UnitOfWork.Workflows.GetByTypeIdAsNoTrackingAsync(TYPE))!;
        Block linked = reloaded.GetAllBlocks().Single(b => b.ReplacementTag == "one");
        Assert.Equal(component.Id, linked.SharedComponentId);
        Assert.Equal(SharedComponentLinkState.Linked, linked.SharedComponentLinkState);
        Assert.Equal(version.ContentHash, linked.SharedComponentVersionHash);
        Assert.Equal(2, harness.Context.WorkflowVersions.Count());
    }

    [Fact]
    public async Task Promote_WithoutNameOverride_TruncatesTheDerivedDefaultNameToTheColumn()
    {
        using var harness = new SharedComponentsTestHarness();
        _ = await harness.AddCategoryAsync("analysis", "Analysis");
        (PromoteSharedComponentCommandHandler promote, SaveWorkflowCommandHandler save) = CreateHandlers(harness);
        string longBlockName = new('x', 400);
        var workflow = new Workflow("Report") { Title = "Report" };
        var section = new Section("Body", workflow) { Title = "Body" };
        _ = new Block(BlockTypes.AiQuery, section, "one")
        {
            Name = longBlockName,
            Properties = new AiQueryProperties { UserPromptTemplate = "Prompt one" },
        };
        string headVersionId = await harness.SaveTemplateAsync(save, ORG, USER, TYPE, workflow);

        ErrorOr<PromoteSharedComponentResult> result = await promote.Handle(
            new PromoteSharedComponentCommand(
                ORG, USER, TYPE, TargetPath: "Sections[0].Blocks[0]", Name: null,
                Description: null, CategoryId: "analysis", BaseVersionId: headVersionId),
            TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        SharedComponent component = Assert.Single(harness.Context.SharedComponents);
        Assert.Equal(SharedComponent.NAME_MAX_LENGTH, component.Name.Length);
        Assert.Equal(longBlockName[..SharedComponent.NAME_MAX_LENGTH], component.Name);
    }

    [Fact]
    public async Task Promote_PreservesEveryExistingStableId_Verbatim()
    {
        using var harness = new SharedComponentsTestHarness();
        (PromoteSharedComponentCommandHandler promote, SaveWorkflowCommandHandler save) = CreateHandlers(harness);
        (_, string headVersionId) = await SeedTemplateAsync(harness, save);
        Workflow before = (await harness.UnitOfWork.Workflows.GetByTypeIdAsNoTrackingAsync(TYPE))!;
        Dictionary<string, string?> blockIds = before.GetAllBlocks()
            .ToDictionary(b => b.ReplacementTag, b => b.StableBlockId);
        Dictionary<string, string?> sectionIds = before.Sections
            .ToDictionary(s => s.Name, s => s.StableSectionId);

        ErrorOr<PromoteSharedComponentResult> result = await promote.Handle(
            new PromoteSharedComponentCommand(
                ORG, USER, TYPE, TargetPath: "Sections[0].Blocks[0]", BaseVersionId: headVersionId),
            TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        Workflow after = (await harness.UnitOfWork.Workflows.GetByTypeIdAsNoTrackingAsync(TYPE))!;
        foreach (Block block in after.GetAllBlocks())
        {
            Assert.Equal(blockIds[block.ReplacementTag], block.StableBlockId);
        }

        foreach (Section section in after.Sections)
        {
            Assert.Equal(sectionIds[section.Name], section.StableSectionId);
        }
    }

    [Fact]
    public async Task Promote_StaleBaseVersion_Conflicts_AndWritesZeroRegistryRows()
    {
        using var harness = new SharedComponentsTestHarness();
        (PromoteSharedComponentCommandHandler promote, SaveWorkflowCommandHandler save) = CreateHandlers(harness);
        _ = await SeedTemplateAsync(harness, save);

        ErrorOr<PromoteSharedComponentResult> result = await promote.Handle(
            new PromoteSharedComponentCommand(
                ORG, USER, TYPE, TargetPath: "Sections[0].Blocks[0]", BaseVersionId: "stale-version-id"),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsError);
        Assert.Equal(SaveWorkflowCommandHandler.VERSION_CONFLICT_CODE, result.FirstError.Code);
        Assert.Empty(harness.Context.SharedComponents);
        Assert.Empty(harness.Context.SharedComponentVersions);
    }

    [Fact]
    public async Task Promote_AlreadyLinkedNode_ReturnsAlreadyShared_WithProvenance()
    {
        using var harness = new SharedComponentsTestHarness();
        (PromoteSharedComponentCommandHandler promote, SaveWorkflowCommandHandler save) = CreateHandlers(harness);
        (_, string headVersionId) = await SeedTemplateAsync(harness, save);
        ErrorOr<PromoteSharedComponentResult> first = await promote.Handle(
            new PromoteSharedComponentCommand(
                ORG, USER, TYPE, TargetPath: "Sections[0].Blocks[0]", BaseVersionId: headVersionId),
            TestContext.Current.CancellationToken);
        Assert.False(first.IsError);

        ErrorOr<PromoteSharedComponentResult> second = await promote.Handle(
            new PromoteSharedComponentCommand(
                ORG, USER, TYPE, TargetPath: "Sections[0].Blocks[0]", BaseVersionId: first.Value.VersionId),
            TestContext.Current.CancellationToken);

        Assert.True(second.IsError);
        Assert.Equal(SharedComponentErrorCodes.ALREADY_SHARED, second.FirstError.Code);
        Assert.Equal(first.Value.SharedComponentId, second.FirstError.Metadata!["existingComponentId"]);
    }

    [Fact]
    public async Task Promote_BrokenOutSectionCopy_SharesItAsANewComponentAndDropsTheOldLink()
    {
        using var harness = new SharedComponentsTestHarness();
        (PromoteSharedComponentCommandHandler promote, SaveWorkflowCommandHandler save) = CreateHandlers(harness);
        Workflow workflow = BuildWorkflow();
        Section brokenOut = workflow.Sections.Single();
        brokenOut.SharedComponentId = "c-retired";
        brokenOut.SharedComponentLinkState = SharedComponentLinkState.BrokenOut;
        brokenOut.SharedComponentVersionHash = new string('a', 64);
        brokenOut.SharedComponentBindings = new Dictionary<string, string> { ["one"] = "one" };
        brokenOut.SharedComponentSlotStates = new Dictionary<string, SharedComponentSlotState>
        {
            ["gone"] = SharedComponentSlotState.Hidden,
        };
        string headVersionId = await harness.SaveTemplateAsync(save, ORG, USER, TYPE, workflow);

        ErrorOr<PromoteSharedComponentResult> result = await promote.Handle(
            new PromoteSharedComponentCommand(
                ORG, USER, TYPE, TargetPath: "Sections[0]", Name: "Body pack", BaseVersionId: headVersionId),
            TestContext.Current.CancellationToken);

        Assert.False(result.IsError, result.IsError ? result.FirstError.Description : null);
        Section reloaded = (await harness.UnitOfWork.Workflows.GetByTypeIdAsNoTrackingAsync(TYPE))!.Sections.Single();
        Assert.Equal(result.Value.SharedComponentId, reloaded.SharedComponentId);
        Assert.NotEqual("c-retired", reloaded.SharedComponentId);
        Assert.Equal(SharedComponentLinkState.Linked, reloaded.SharedComponentLinkState);
        Assert.Equal(result.Value.ComponentVersionHash, reloaded.SharedComponentVersionHash);
        Assert.Null(reloaded.SharedComponentBindings);
        Assert.Null(reloaded.SharedComponentSlotStates);
    }

    [Fact]
    public async Task Promote_ByStableId_ResolvesAndLinks()
    {
        using var harness = new SharedComponentsTestHarness();
        (PromoteSharedComponentCommandHandler promote, SaveWorkflowCommandHandler save) = CreateHandlers(harness);
        (_, string headVersionId) = await SeedTemplateAsync(harness, save);
        Workflow live = (await harness.UnitOfWork.Workflows.GetByTypeIdAsNoTrackingAsync(TYPE))!;
        string stableId = live.GetAllBlocks().Single(b => b.ReplacementTag == "two").StableBlockId!;

        ErrorOr<PromoteSharedComponentResult> result = await promote.Handle(
            new PromoteSharedComponentCommand(
                ORG, USER, TYPE, TargetStableId: stableId, BaseVersionId: headVersionId),
            TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        Assert.Equal("Sections[0].Blocks[1]", result.Value.TargetPath);
    }

    [Fact]
    public async Task Attach_ContentIdenticalComponent_LinksWithoutNewRows()
    {
        using var harness = new SharedComponentsTestHarness();
        (PromoteSharedComponentCommandHandler promote, SaveWorkflowCommandHandler save) = CreateHandlers(harness);
        (_, string headVersionId) = await SeedTemplateAsync(harness, save);
        var canonicalizer = new WorkflowCanonicalizer();
        var normalizer = new SharedComponentContentNormalizer(canonicalizer);
        Block twin = DetachedTwinOfBlockOne();
        twin.ReplacementTag = "renamed_elsewhere";
        twin.Order = 7;
        SharedComponent existing = await harness.AddComponentAsync(SharedComponentKind.Block, "Existing check");
        _ = await harness.AddVersionAsync(
            existing.Id, 1, normalizer.ComputeContentHash(twin), normalizer.ToDefinitionJson(twin));

        ErrorOr<PromoteSharedComponentResult> result = await promote.Handle(
            new PromoteSharedComponentCommand(
                ORG, USER, TYPE, TargetPath: "Sections[0].Blocks[0]",
                AttachToComponentId: existing.Id, BaseVersionId: headVersionId),
            TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        Assert.True(result.Value.WasAttached);
        Assert.Equal(existing.Id, result.Value.SharedComponentId);
        _ = Assert.Single(harness.Context.SharedComponents);
        _ = Assert.Single(harness.Context.SharedComponentVersions);
        Workflow reloaded = (await harness.UnitOfWork.Workflows.GetByTypeIdAsNoTrackingAsync(TYPE))!;
        Assert.Equal(existing.Id, reloaded.GetAllBlocks().Single(b => b.ReplacementTag == "one").SharedComponentId);
    }

    [Fact]
    public async Task Attach_ComponentWhoseHeadWasStoredBeforeAFieldWasDropped_StillMatchesAnIdenticalNode()
    {
        using var harness = new SharedComponentsTestHarness();
        (PromoteSharedComponentCommandHandler promote, SaveWorkflowCommandHandler save) = CreateHandlers(harness);
        (_, string headVersionId) = await SeedTemplateAsync(harness, save);
        (string storedJson, string storedHash) =
            harness.StoredBeforeAFieldWasDropped(harness.Normalizer.ToDefinitionJson(DetachedTwinOfBlockOne()));
        SharedComponent existing = await harness.AddComponentAsync(SharedComponentKind.Block, "Existing check");
        _ = await harness.AddVersionAsync(existing.Id, 1, storedHash, storedJson);

        ErrorOr<PromoteSharedComponentResult> result = await promote.Handle(
            new PromoteSharedComponentCommand(
                ORG, USER, TYPE, TargetPath: "Sections[0].Blocks[0]",
                AttachToComponentId: existing.Id, BaseVersionId: headVersionId),
            TestContext.Current.CancellationToken);

        Assert.False(result.IsError, result.IsError ? result.FirstError.Description : null);
        Workflow reloaded = (await harness.UnitOfWork.Workflows.GetByTypeIdAsNoTrackingAsync(TYPE))!;
        Block attached = reloaded.GetAllBlocks().Single(b => b.ReplacementTag == "one");
        Assert.Equal(existing.Id, attached.SharedComponentId);
        Assert.Equal(storedHash, attached.SharedComponentVersionHash);
    }

    [Fact]
    public async Task Attach_KindMismatch_Retired_AndContentMismatch_AreRejected()
    {
        using var harness = new SharedComponentsTestHarness();
        (PromoteSharedComponentCommandHandler promote, SaveWorkflowCommandHandler save) = CreateHandlers(harness);
        (_, string headVersionId) = await SeedTemplateAsync(harness, save);
        SharedComponent wrongKind = await harness.AddComponentAsync(SharedComponentKind.Section, "A section");
        _ = await harness.AddVersionAsync(wrongKind.Id, 1, new string('1', 64), "{}");
        SharedComponent retired = await harness.AddComponentAsync(
            SharedComponentKind.Block, "Retired", SharedComponentState.Retired);
        _ = await harness.AddVersionAsync(retired.Id, 1, new string('2', 64), "{}");
        SharedComponent different = await harness.AddComponentAsync(SharedComponentKind.Block, "Different");
        _ = await harness.AddVersionAsync(different.Id, 1, new string('3', 64), "{}");

        ErrorOr<PromoteSharedComponentResult> kindResult = await promote.Handle(
            new PromoteSharedComponentCommand(
                ORG, USER, TYPE, TargetPath: "Sections[0].Blocks[0]",
                AttachToComponentId: wrongKind.Id, BaseVersionId: headVersionId),
            TestContext.Current.CancellationToken);
        ErrorOr<PromoteSharedComponentResult> retiredResult = await promote.Handle(
            new PromoteSharedComponentCommand(
                ORG, USER, TYPE, TargetPath: "Sections[0].Blocks[0]",
                AttachToComponentId: retired.Id, BaseVersionId: headVersionId),
            TestContext.Current.CancellationToken);
        ErrorOr<PromoteSharedComponentResult> mismatchResult = await promote.Handle(
            new PromoteSharedComponentCommand(
                ORG, USER, TYPE, TargetPath: "Sections[0].Blocks[0]",
                AttachToComponentId: different.Id, BaseVersionId: headVersionId),
            TestContext.Current.CancellationToken);

        Assert.Equal(SharedComponentErrorCodes.KIND_MISMATCH, kindResult.FirstError.Code);
        Assert.Equal(SharedComponentErrorCodes.RETIRED, retiredResult.FirstError.Code);
        Assert.Equal(SharedComponentErrorCodes.CONTENT_MISMATCH, mismatchResult.FirstError.Code);
        Assert.Equal(new string('3', 64), mismatchResult.FirstError.Metadata!["expectedHash"]);
    }

    [Fact]
    public void Validator_EnforcesLocatorXor_PathShape_AndAttachExclusivity()
    {
        var validator = new PromoteSharedComponentCommandValidator();

        ValidationResult neither = validator.Validate(new PromoteSharedComponentCommand(ORG, USER, TYPE));
        ValidationResult both = validator.Validate(new PromoteSharedComponentCommand(
            ORG, USER, TYPE, TargetPath: "Sections[0]", TargetStableId: "abc"));
        ValidationResult badPath = validator.Validate(new PromoteSharedComponentCommand(
            ORG, USER, TYPE, TargetPath: "Blocks[0]"));
        ValidationResult nameWithAttach = validator.Validate(new PromoteSharedComponentCommand(
            ORG, USER, TYPE, TargetPath: "Sections[0]", Name: "X", AttachToComponentId: "c1"));
        ValidationResult categoryMissingOnCreate = validator.Validate(new PromoteSharedComponentCommand(
            ORG, USER, TYPE, TargetPath: "Sections[0].Blocks[1]", Name: "X"));
        ValidationResult categoryWithAttach = validator.Validate(new PromoteSharedComponentCommand(
            ORG, USER, TYPE, TargetPath: "Sections[0]", AttachToComponentId: "c1", CategoryId: "analysis"));
        ValidationResult okCreate = validator.Validate(new PromoteSharedComponentCommand(
            ORG, USER, TYPE, TargetPath: "Sections[0].Blocks[1]", Name: "X", CategoryId: "analysis"));
        ValidationResult okAttach = validator.Validate(new PromoteSharedComponentCommand(
            ORG, USER, TYPE, TargetPath: "Sections[0]", AttachToComponentId: "c1"));

        Assert.False(neither.IsValid);
        Assert.False(both.IsValid);
        Assert.False(badPath.IsValid);
        Assert.False(nameWithAttach.IsValid);
        Assert.False(categoryMissingOnCreate.IsValid);
        Assert.False(categoryWithAttach.IsValid);
        Assert.True(okCreate.IsValid);
        Assert.True(okAttach.IsValid);
    }

    [Fact]
    public async Task Promote_UnknownCategory_ReturnsNotFound_AndWritesZeroRegistryRows()
    {
        using var harness = new SharedComponentsTestHarness();
        (PromoteSharedComponentCommandHandler promote, SaveWorkflowCommandHandler save) = CreateHandlers(harness);
        (_, string headVersionId) = await SeedTemplateAsync(harness, save);

        ErrorOr<PromoteSharedComponentResult> result = await promote.Handle(
            new PromoteSharedComponentCommand(
                ORG, USER, TYPE, TargetPath: "Sections[0].Blocks[0]",
                CategoryId: "ghost", BaseVersionId: headVersionId),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsError);
        Assert.Equal(ErrorType.NotFound, result.FirstError.Type);
        Assert.Equal(SharedComponentErrorCodes.UNKNOWN_CATEGORY, result.FirstError.Code);
        Assert.Empty(harness.Context.SharedComponents);
        Assert.Empty(harness.Context.SharedComponentVersions);
    }

    [Fact]
    public async Task Promote_BlockInLinkedSection_ConvertsContainerSlotToRef_AndAppendsSectionHead()
    {
        using var harness = new SharedComponentsTestHarness();
        _ = await harness.AddCategoryAsync("analysis", "Analysis");
        (PromoteSharedComponentCommandHandler promote, SaveWorkflowCommandHandler save) = CreateHandlers(harness);
        LinkedSectionSeed seed = await SeedLinkedSectionTemplateAsync(harness, save);

        ErrorOr<PromoteSharedComponentResult> result = await promote.Handle(
            new PromoteSharedComponentCommand(
                ORG, USER, TYPE, TargetPath: "Sections[1].Blocks[0]",
                Name: "KYC block", CategoryId: "analysis",
                BaseVersionId: seed.TemplateHeadVersionId,
                ExpectedContainerHeadVersionId: seed.SectionHeadVersionId),
            TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        SharedComponent blockComponent = harness.Context.SharedComponents
            .Single(c => c.Id == result.Value.SharedComponentId);
        Assert.Equal(SharedComponentKind.Block, blockComponent.Kind);
        SharedComponentVersion containerHead = harness.Context.SharedComponentVersions
            .Where(v => v.SharedComponentId == seed.SectionComponent.Id)
            .OrderByDescending(v => v.SequenceNumber)
            .First();
        Assert.Equal(2, containerHead.SequenceNumber);
        Section containerDefinition = Section.FromJson(containerHead.ContentJson)!;
        Assert.Contains(
            containerDefinition.EffectiveEntries(),
            entry => entry.Kind == SectionEntryKind.ComponentRef
                && entry.SharedComponentId == blockComponent.Id);

        Workflow reloaded = (await harness.UnitOfWork.Workflows.GetByTypeIdAsNoTrackingAsync(TYPE))!;
        Section reloadedRisk = reloaded.Sections.OrderBy(s => s.Order).ElementAt(1);
        Block reloadedRef = reloadedRisk.Blocks.Single(b => b.SharedComponentId == blockComponent.Id);
        Assert.Equal(SharedComponentLinkState.Linked, reloadedRef.SharedComponentLinkState);
    }

    [Fact]
    public async Task Promote_BlockInLinkedSection_MissingContainerToken_Conflicts_AndWritesZeroRegistryRows()
    {
        using var harness = new SharedComponentsTestHarness();
        _ = await harness.AddCategoryAsync("analysis", "Analysis");
        (PromoteSharedComponentCommandHandler promote, SaveWorkflowCommandHandler save) = CreateHandlers(harness);
        LinkedSectionSeed seed = await SeedLinkedSectionTemplateAsync(harness, save);
        int componentsBefore = harness.Context.SharedComponents.Count();
        int versionsBefore = harness.Context.SharedComponentVersions.Count();

        ErrorOr<PromoteSharedComponentResult> result = await promote.Handle(
            new PromoteSharedComponentCommand(
                ORG, USER, TYPE, TargetPath: "Sections[1].Blocks[0]",
                Name: "KYC block", CategoryId: "analysis",
                BaseVersionId: seed.TemplateHeadVersionId),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsError);
        Assert.Equal(SharedComponentErrorCodes.HEAD_MOVED, result.FirstError.Code);
        Assert.Equal(seed.SectionComponent.Id, result.FirstError.Metadata!["containerComponentId"]);
        Assert.Equal(componentsBefore, harness.Context.SharedComponents.Count());
        Assert.Equal(versionsBefore, harness.Context.SharedComponentVersions.Count());
    }

    [Fact]
    public async Task Promote_BlockInBrokenOutSection_PlainPromote_LeavesContainerUntouched()
    {
        using var harness = new SharedComponentsTestHarness();
        _ = await harness.AddCategoryAsync("analysis", "Analysis");
        (PromoteSharedComponentCommandHandler promote, SaveWorkflowCommandHandler save) = CreateHandlers(harness);
        LinkedSectionSeed seed = await SeedLinkedSectionTemplateAsync(
            harness, save, sectionLinkState: SharedComponentLinkState.BrokenOut);

        ErrorOr<PromoteSharedComponentResult> result = await promote.Handle(
            new PromoteSharedComponentCommand(
                ORG, USER, TYPE, TargetPath: "Sections[1].Blocks[0]",
                Name: "KYC block", CategoryId: "analysis",
                BaseVersionId: seed.TemplateHeadVersionId),
            TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        Assert.Equal(
            1,
            harness.Context.SharedComponentVersions.Count(v => v.SharedComponentId == seed.SectionComponent.Id));
    }

    [Fact]
    public async Task Promote_LocalInsertInLinkedSection_PlainPromote_LeavesContainerUntouched()
    {
        using var harness = new SharedComponentsTestHarness();
        _ = await harness.AddCategoryAsync("analysis", "Analysis");
        (PromoteSharedComponentCommandHandler promote, SaveWorkflowCommandHandler save) = CreateHandlers(harness);
        LinkedSectionSeed seed = await SeedLinkedSectionTemplateAsync(harness, save, includeLocalInsert: true);

        ErrorOr<PromoteSharedComponentResult> result = await promote.Handle(
            new PromoteSharedComponentCommand(
                ORG, USER, TYPE, TargetPath: "Sections[1].Blocks[2]",
                Name: "Extra block", CategoryId: "analysis",
                BaseVersionId: seed.TemplateHeadVersionId),
            TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        Assert.Equal(
            1,
            harness.Context.SharedComponentVersions.Count(v => v.SharedComponentId == seed.SectionComponent.Id));
    }

    [Fact]
    public async Task Promote_ConvertToRef_DriftedSlot_Conflicts_AndWritesNothing()
    {
        using var harness = new SharedComponentsTestHarness();
        _ = await harness.AddCategoryAsync("analysis", "Analysis");
        (PromoteSharedComponentCommandHandler promote, SaveWorkflowCommandHandler save) = CreateHandlers(harness);
        LinkedSectionSeed seed = await SeedLinkedSectionTemplateAsync(
            harness, save, anchorKycPrompt: "Cross-check {sanctions} very carefully");

        ErrorOr<PromoteSharedComponentResult> result = await promote.Handle(
            new PromoteSharedComponentCommand(
                ORG, USER, TYPE, TargetPath: "Sections[1].Blocks[1]",
                Name: "KYC block", CategoryId: "analysis",
                BaseVersionId: seed.TemplateHeadVersionId,
                ExpectedContainerHeadVersionId: seed.SectionHeadVersionId),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsError);
        Assert.Equal(SharedComponentErrorCodes.CONTENT_MISMATCH, result.FirstError.Code);
        Assert.DoesNotContain(
            harness.Context.SharedComponents,
            c => c.Kind == SharedComponentKind.Block);
        Assert.Equal(
            1,
            harness.Context.SharedComponentVersions.Count(v => v.SharedComponentId == seed.SectionComponent.Id));
    }

    [Fact]
    public async Task Promote_ConvertToRef_BrokenOutSlot_Rejected()
    {
        using var harness = new SharedComponentsTestHarness();
        _ = await harness.AddCategoryAsync("analysis", "Analysis");
        (PromoteSharedComponentCommandHandler promote, SaveWorkflowCommandHandler save) = CreateHandlers(harness);
        LinkedSectionSeed seed = await SeedLinkedSectionTemplateAsync(
            harness, save,
            slotStates: new Dictionary<string, SharedComponentSlotState>
            {
                ["kyc"] = SharedComponentSlotState.BrokenOut,
            });

        ErrorOr<PromoteSharedComponentResult> result = await promote.Handle(
            new PromoteSharedComponentCommand(
                ORG, USER, TYPE, TargetPath: "Sections[1].Blocks[1]",
                Name: "KYC block", CategoryId: "analysis",
                BaseVersionId: seed.TemplateHeadVersionId,
                ExpectedContainerHeadVersionId: seed.SectionHeadVersionId),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsError);
        Assert.Contains(SharedComponentErrorCodes.INVALID_LINK_SHAPE, result.FirstError.Description);
        Assert.DoesNotContain(
            harness.Context.SharedComponents,
            c => c.Kind == SharedComponentKind.Block);
    }

    [Fact]
    public async Task Promote_ConvertToRef_ContainerAppendFails_ReturnsErrorCleanly_AndWritesNothing()
    {
        using var harness = new SharedComponentsTestHarness();
        _ = await harness.AddCategoryAsync("analysis", "Analysis");
        (PromoteSharedComponentCommandHandler promote, SaveWorkflowCommandHandler save) = CreateHandlers(harness);
        LinkedSectionSeed seed = await SeedLinkedSectionTemplateAsync(harness, save);
        SharedComponent retiredSection = harness.Context.SharedComponents
            .First(c => c.Id == seed.SectionComponent.Id);
        retiredSection.State = SharedComponentState.Retired;
        _ = await harness.Context.SaveChangesAsync(TestContext.Current.CancellationToken);

        ErrorOr<PromoteSharedComponentResult> result = await promote.Handle(
            new PromoteSharedComponentCommand(
                ORG, USER, TYPE, TargetPath: "Sections[1].Blocks[1]",
                Name: "KYC block", CategoryId: "analysis",
                BaseVersionId: seed.TemplateHeadVersionId,
                ExpectedContainerHeadVersionId: seed.SectionHeadVersionId),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsError);
        Assert.Equal(SharedComponentErrorCodes.RETIRED, result.FirstError.Code);
        Assert.DoesNotContain(
            harness.Context.SharedComponents,
            c => c.Kind == SharedComponentKind.Block);
        Assert.Equal(
            1,
            harness.Context.SharedComponentVersions.Count(v => v.SharedComponentId == seed.SectionComponent.Id));
    }

    [Fact]
    public async Task Promote_HiddenSlotNamesakeInsert_PlainPromote_LeavesContainerUntouched()
    {
        using var harness = new SharedComponentsTestHarness();
        _ = await harness.AddCategoryAsync("analysis", "Analysis");
        (PromoteSharedComponentCommandHandler promote, SaveWorkflowCommandHandler save) = CreateHandlers(harness);
        LinkedSectionSeed seed = await SeedLinkedSectionTemplateAsync(
            harness, save,
            slotStates: new Dictionary<string, SharedComponentSlotState>
            {
                ["kyc"] = SharedComponentSlotState.Hidden,
            },
            hideKycOccupantWithNamesakeInsert: true);

        ErrorOr<PromoteSharedComponentResult> result = await promote.Handle(
            new PromoteSharedComponentCommand(
                ORG, USER, TYPE, TargetPath: "Sections[1].Blocks[1]",
                Name: "Namesake local", CategoryId: "analysis",
                BaseVersionId: seed.TemplateHeadVersionId),
            TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        Assert.Equal(
            1,
            harness.Context.SharedComponentVersions.Count(v => v.SharedComponentId == seed.SectionComponent.Id));
        Workflow reloaded = (await harness.UnitOfWork.Workflows.GetByTypeIdAsNoTrackingAsync(TYPE))!;
        Section risk = reloaded.Sections.OrderBy(s => s.Order).ElementAt(1);
        Block insert = risk.Blocks.Single(b => b.ReplacementTag == "kyc");
        Assert.Equal(result.Value.SharedComponentId, insert.SharedComponentId);
        Assert.Equal(SharedComponentLinkState.Linked, insert.SharedComponentLinkState);
    }

    [Fact]
    public async Task Promote_ConvertToRef_SlotWithDocumentClasses_ConvertsAndCarriesTheWiring()
    {
        using var harness = new SharedComponentsTestHarness();
        _ = await harness.AddCategoryAsync("analysis", "Analysis");
        (PromoteSharedComponentCommandHandler promote, SaveWorkflowCommandHandler save) = CreateHandlers(harness);
        LinkedSectionSeed seed = await SeedLinkedSectionTemplateAsync(
            harness, save, defKycDocumentClass: "financials");

        ErrorOr<PromoteSharedComponentResult> result = await promote.Handle(
            new PromoteSharedComponentCommand(
                ORG, USER, TYPE, TargetPath: "Sections[1].Blocks[1]",
                Name: "KYC block", CategoryId: "analysis",
                BaseVersionId: seed.TemplateHeadVersionId,
                ExpectedContainerHeadVersionId: seed.SectionHeadVersionId),
            TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        SharedComponent blockComponent = Assert.Single(
            harness.Context.SharedComponents.Where(c => c.Kind == SharedComponentKind.Block));
        SharedComponentVersion head = harness.Context.SharedComponentVersions
            .Where(v => v.SharedComponentId == blockComponent.Id)
            .OrderByDescending(v => v.SequenceNumber)
            .First();

        Assert.Contains("financials", head.ContentJson, StringComparison.Ordinal);
        Assert.Equal(
            2,
            harness.Context.SharedComponentVersions.Count(v => v.SharedComponentId == seed.SectionComponent.Id));
    }

    [Fact]
    public async Task Promote_ConvertToRef_AnyOnlySlot_Converts()
    {
        using var harness = new SharedComponentsTestHarness();
        _ = await harness.AddCategoryAsync("analysis", "Analysis");
        (PromoteSharedComponentCommandHandler promote, SaveWorkflowCommandHandler save) = CreateHandlers(harness);
        LinkedSectionSeed seed = await SeedLinkedSectionTemplateAsync(
            harness, save, defKycDocumentClass: DocumentClass.KEY_ANY);

        ErrorOr<PromoteSharedComponentResult> result = await promote.Handle(
            new PromoteSharedComponentCommand(
                ORG, USER, TYPE, TargetPath: "Sections[1].Blocks[1]",
                Name: "KYC block", CategoryId: "analysis",
                BaseVersionId: seed.TemplateHeadVersionId,
                ExpectedContainerHeadVersionId: seed.SectionHeadVersionId),
            TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        Assert.Contains(harness.Context.SharedComponents, c => c.Kind == SharedComponentKind.Block);
        Assert.Equal(
            2,
            harness.Context.SharedComponentVersions.Count(v => v.SharedComponentId == seed.SectionComponent.Id));
    }

    [Fact]
    public async Task Promote_ConvertToRef_NoneSlot_Converts()
    {
        using var harness = new SharedComponentsTestHarness();
        _ = await harness.AddCategoryAsync("analysis", "Analysis");
        (PromoteSharedComponentCommandHandler promote, SaveWorkflowCommandHandler save) = CreateHandlers(harness);
        LinkedSectionSeed seed = await SeedLinkedSectionTemplateAsync(
            harness, save, defKycDocumentClass: DocumentClass.KEY_NONE);

        ErrorOr<PromoteSharedComponentResult> result = await promote.Handle(
            new PromoteSharedComponentCommand(
                ORG, USER, TYPE, TargetPath: "Sections[1].Blocks[1]",
                Name: "KYC block", CategoryId: "analysis",
                BaseVersionId: seed.TemplateHeadVersionId,
                ExpectedContainerHeadVersionId: seed.SectionHeadVersionId),
            TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        SharedComponent blockComponent = Assert.Single(
            harness.Context.SharedComponents.Where(c => c.Kind == SharedComponentKind.Block));
        SharedComponentVersion head = harness.Context.SharedComponentVersions
            .Where(v => v.SharedComponentId == blockComponent.Id)
            .OrderByDescending(v => v.SequenceNumber)
            .First();

        Assert.Contains(DocumentClass.KEY_NONE, head.ContentJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Promote_ConvertToRef_AnyPlusNamedClass_CarriesEveryClass()
    {
        using var harness = new SharedComponentsTestHarness();
        _ = await harness.AddCategoryAsync("analysis", "Analysis");
        (PromoteSharedComponentCommandHandler promote, SaveWorkflowCommandHandler save) = CreateHandlers(harness);
        LinkedSectionSeed seed = await SeedLinkedSectionTemplateAsync(
            harness, save, defKycDocumentClass: DocumentClass.KEY_ANY, defKycExtraDocumentClass: "financials");

        ErrorOr<PromoteSharedComponentResult> result = await promote.Handle(
            new PromoteSharedComponentCommand(
                ORG, USER, TYPE, TargetPath: "Sections[1].Blocks[1]",
                Name: "KYC block", CategoryId: "analysis",
                BaseVersionId: seed.TemplateHeadVersionId,
                ExpectedContainerHeadVersionId: seed.SectionHeadVersionId),
            TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        SharedComponent blockComponent = Assert.Single(
            harness.Context.SharedComponents.Where(c => c.Kind == SharedComponentKind.Block));
        SharedComponentVersion head = harness.Context.SharedComponentVersions
            .Where(v => v.SharedComponentId == blockComponent.Id)
            .OrderByDescending(v => v.SequenceNumber)
            .First();

        Assert.Contains(DocumentClass.KEY_ANY, head.ContentJson, StringComparison.Ordinal);
        Assert.Contains("financials", head.ContentJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Promote_LocalInsertReusingRenamedSlotTag_PlainPromote_LeavesContainerUntouched()
    {
        using var harness = new SharedComponentsTestHarness();
        _ = await harness.AddCategoryAsync("analysis", "Analysis");
        (PromoteSharedComponentCommandHandler promote, SaveWorkflowCommandHandler save) = CreateHandlers(harness);
        LinkedSectionSeed seed = await SeedLinkedSectionTemplateAsync(
            harness, save, renameKycWithCollidingInsert: true);

        ErrorOr<PromoteSharedComponentResult> result = await promote.Handle(
            new PromoteSharedComponentCommand(
                ORG, USER, TYPE, TargetPath: "Sections[1].Blocks[2]",
                Name: "Local extra", CategoryId: "analysis",
                BaseVersionId: seed.TemplateHeadVersionId),
            TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        Assert.Equal(
            1,
            harness.Context.SharedComponentVersions.Count(v => v.SharedComponentId == seed.SectionComponent.Id));
        Workflow reloaded = (await harness.UnitOfWork.Workflows.GetByTypeIdAsNoTrackingAsync(TYPE))!;
        Section risk = reloaded.Sections.OrderBy(s => s.Order).ElementAt(1);
        Block insert = risk.Blocks.Single(b => b.ReplacementTag == "kyc");
        Assert.Equal(result.Value.SharedComponentId, insert.SharedComponentId);
        Block occupant = risk.Blocks.Single(b => b.ReplacementTag == "kyc_2");
        Assert.Null(occupant.SharedComponentId);
    }

    [Fact]
    public async Task Promote_ConvertToRef_BuildsComponentFromDefinitionSlot_AndRewritesEveryConsumersTagSpace()
    {
        using var harness = new SharedComponentsTestHarness();
        _ = await harness.AddCategoryAsync("analysis", "Analysis");
        (PromoteSharedComponentCommandHandler promote, SaveWorkflowCommandHandler save) = CreateHandlers(harness);
        LinkedSectionSeed seed = await SeedLinkedSectionTemplateAsync(harness, save, renameAnchorSanctions: true);
        await SeedRenamedSiblingConsumerAsync(harness, save, seed);
        Workflow secondBefore = (await harness.UnitOfWork.Workflows.GetByTypeIdAsNoTrackingAsync(SECOND_TYPE))!;
        string secondKycStableId = secondBefore.Sections.Single().Blocks
            .Single(b => b.ReplacementTag == "kyc").StableBlockId!;

        ErrorOr<PromoteSharedComponentResult> result = await promote.Handle(
            new PromoteSharedComponentCommand(
                ORG, USER, TYPE, TargetPath: "Sections[1].Blocks[1]",
                Name: "KYC block", CategoryId: "analysis",
                BaseVersionId: seed.TemplateHeadVersionId,
                ExpectedContainerHeadVersionId: seed.SectionHeadVersionId),
            TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        SharedComponentVersion blockHead = harness.Context.SharedComponentVersions
            .Single(v => v.SharedComponentId == result.Value.SharedComponentId);
        Assert.Contains("{sanctions}", blockHead.ContentJson);
        Assert.DoesNotContain("{sanctions_3}", blockHead.ContentJson);

        Workflow secondAfter = (await harness.UnitOfWork.Workflows.GetByTypeIdAsNoTrackingAsync(SECOND_TYPE))!;
        Block secondKyc = secondAfter.Sections.Single().Blocks.Single(b => b.ReplacementTag == "kyc");
        Assert.Equal(secondKycStableId, secondKyc.StableBlockId);
        Assert.Equal(result.Value.SharedComponentId, secondKyc.SharedComponentId);
        Assert.Equal(SharedComponentLinkState.Linked, secondKyc.SharedComponentLinkState);
        string secondPrompt = ((AiQueryProperties)secondKyc.Properties).UserPromptTemplate!;
        Assert.Contains("{sanctions_2}", secondPrompt);
        Assert.DoesNotContain("{sanctions}", secondPrompt);
        Assert.DoesNotContain("{sanctions_3}", secondPrompt);

        Workflow anchorAfter = (await harness.UnitOfWork.Workflows.GetByTypeIdAsNoTrackingAsync(TYPE))!;
        Block anchorKyc = anchorAfter.Sections.OrderBy(s => s.Order).ElementAt(1).Blocks
            .Single(b => b.ReplacementTag == "kyc");
        Assert.Equal(result.Value.SharedComponentId, anchorKyc.SharedComponentId);
        string anchorPrompt = ((AiQueryProperties)anchorKyc.Properties).UserPromptTemplate!;
        Assert.Contains("{sanctions_3}", anchorPrompt);
        Assert.NotEqual(seed.TemplateHeadVersionId, result.Value.VersionId);
        Assert.Contains(
            harness.Context.WorkflowVersions,
            v => v.Id == result.Value.VersionId && v.WorkflowTypeId == TYPE);
    }

    private const string SANCTIONS_PROMPT = "List sanctions";
    private const string KYC_PROMPT = "Cross-check {sanctions}";

    private static async Task<LinkedSectionSeed> SeedLinkedSectionTemplateAsync(
        SharedComponentsTestHarness harness,
        SaveWorkflowCommandHandler save,
        SharedComponentLinkState sectionLinkState = SharedComponentLinkState.Linked,
        bool includeLocalInsert = false,
        string? anchorKycPrompt = null,
        Dictionary<string, SharedComponentSlotState>? slotStates = null,
        bool renameKycWithCollidingInsert = false,
        bool renameAnchorSanctions = false,
        string? defKycDocumentClass = null,
        string? defKycExtraDocumentClass = null,
        bool hideKycOccupantWithNamesakeInsert = false)
    {
        var canonicalizer = new WorkflowCanonicalizer();
        var normalizer = new SharedComponentContentNormalizer(canonicalizer);

        var scratch = new Workflow("scratch");
        var scratchSection = new Section("Risk", scratch) { Title = "Risk" };
        _ = new Block(BlockTypes.AiQuery, scratchSection, "sanctions")
        {
            Name = "Sanctions list",
            Properties = new AiQueryProperties { UserPromptTemplate = SANCTIONS_PROMPT },
        };
        var scratchKyc = new Block(BlockTypes.AiQuery, scratchSection, "kyc")
        {
            Name = "KYC check",
            Properties = new AiQueryProperties { UserPromptTemplate = KYC_PROMPT },
        };
        if (defKycDocumentClass is not null)
        {
            scratchKyc.SupportedDocumentClasses.Add(defKycDocumentClass);
        }
        if (defKycExtraDocumentClass is not null)
        {
            scratchKyc.SupportedDocumentClasses.Add(defKycExtraDocumentClass);
        }
        string definitionJson = normalizer.ToDefinitionJson(scratchSection);
        string definitionHash = canonicalizer.ComputeContentHash(definitionJson);
        SharedComponent sectionComponent = await harness.AddComponentAsync(SharedComponentKind.Section, "Risk pack");
        SharedComponentVersion headVersion = await harness.AddVersionAsync(
            sectionComponent.Id, 1, definitionHash, definitionJson);

        var workflow = new Workflow("Report") { Title = "Report" };
        var body = new Section("Body", workflow) { Title = "Body" };
        _ = new Block(BlockTypes.AiQuery, body, "one")
        {
            Name = "Counterparty check",
            Properties = new AiQueryProperties { UserPromptTemplate = "Prompt one" },
        };
        var risk = new Section("Risk", workflow) { Title = "Risk" };
        risk.SharedComponentId = sectionComponent.Id;
        risk.SharedComponentLinkState = sectionLinkState;
        risk.SharedComponentVersionHash = definitionHash;
        risk.SharedComponentSlotStates = slotStates;
        var anchorBindings = new Dictionary<string, string>();
        string sanctionsLiveTag = renameAnchorSanctions ? "sanctions_3" : "sanctions";
        if (renameAnchorSanctions)
        {
            anchorBindings["sanctions"] = "sanctions_3";
        }

        _ = new Block(BlockTypes.AiQuery, risk, sanctionsLiveTag)
        {
            Name = "Sanctions list",
            Properties = new AiQueryProperties { UserPromptTemplate = SANCTIONS_PROMPT },
        };
        if (hideKycOccupantWithNamesakeInsert)
        {
            _ = new Block(BlockTypes.AiQuery, risk, "kyc")
            {
                Name = "Namesake local",
                Properties = new AiQueryProperties { UserPromptTemplate = "Namesake local prompt" },
            };
        }
        else
        {
            string kycLiveTag = renameKycWithCollidingInsert ? "kyc_2" : "kyc";
            string kycDefaultPrompt = renameAnchorSanctions ? "Cross-check {sanctions_3}" : KYC_PROMPT;
            var liveKyc = new Block(BlockTypes.AiQuery, risk, kycLiveTag)
            {
                Name = "KYC check",
                Properties = new AiQueryProperties { UserPromptTemplate = anchorKycPrompt ?? kycDefaultPrompt },
            };
            if (defKycDocumentClass is not null)
            {
                liveKyc.SupportedDocumentClasses.Add(defKycDocumentClass);
            }

            if (defKycExtraDocumentClass is not null)
            {
                liveKyc.SupportedDocumentClasses.Add(defKycExtraDocumentClass);
            }
        }
        if (renameKycWithCollidingInsert)
        {
            anchorBindings["kyc"] = "kyc_2";
            _ = new Block(BlockTypes.AiQuery, risk, "kyc")
            {
                Name = "Local extra",
                Properties = new AiQueryProperties { UserPromptTemplate = "Local extra prompt" },
            };
        }

        risk.SharedComponentBindings = anchorBindings.Count > 0 ? anchorBindings : null;

        if (includeLocalInsert)
        {
            _ = new Block(BlockTypes.AiQuery, risk, "extra")
            {
                Name = "Extra local",
                Properties = new AiQueryProperties { UserPromptTemplate = "Extra prompt" },
            };
        }

        string templateHeadVersionId = await harness.SaveTemplateAsync(save, ORG, USER, TYPE, workflow);

        return new LinkedSectionSeed(sectionComponent, headVersion.Id, definitionHash, templateHeadVersionId);
    }

    private const string SECOND_TYPE = "WT-Second";

    private static async Task SeedRenamedSiblingConsumerAsync(
        SharedComponentsTestHarness harness,
        SaveWorkflowCommandHandler save,
        LinkedSectionSeed seed)
    {
        var workflow = new Workflow("Second Report") { Title = "Second Report" };
        var risk = new Section("Risk", workflow) { Title = "Risk" };
        risk.SharedComponentId = seed.SectionComponent.Id;
        risk.SharedComponentLinkState = SharedComponentLinkState.Linked;
        risk.SharedComponentVersionHash = seed.SectionHeadHash;
        risk.SharedComponentBindings = new Dictionary<string, string> { ["sanctions"] = "sanctions_2" };
        _ = new Block(BlockTypes.AiQuery, risk, "sanctions_2")
        {
            Name = "Sanctions list",
            Properties = new AiQueryProperties { UserPromptTemplate = SANCTIONS_PROMPT },
        };
        _ = new Block(BlockTypes.AiQuery, risk, "kyc")
        {
            Name = "KYC check",
            Properties = new AiQueryProperties { UserPromptTemplate = "Cross-check {sanctions_2}" },
        };

        _ = await harness.SaveTemplateAsync(save, ORG, USER, SECOND_TYPE, workflow);
    }

    private sealed record LinkedSectionSeed(
        SharedComponent SectionComponent,
        string SectionHeadVersionId,
        string SectionHeadHash,
        string TemplateHeadVersionId);

    private static (PromoteSharedComponentCommandHandler Promote, SaveWorkflowCommandHandler Save) CreateHandlers(
        SharedComponentsTestHarness harness)
    {
        var canonicalizer = new WorkflowCanonicalizer();
        (_, SaveWorkflowCommandHandler saveHandler) = harness.CreateRealSaveSender();
        var promoteHandler = new PromoteSharedComponentCommandHandler(
            harness.UnitOfWork,
            harness.Context,
            harness.CreatePipeline(),
            new SharedComponentEditor(canonicalizer, new SharedComponentContentNormalizer(canonicalizer)),
            harness.CreateFanOutService(),
            harness.Normalizer,
            NullLogger<PromoteSharedComponentCommandHandler>.Instance);

        return (promoteHandler, saveHandler);
    }

    private static async Task<(Workflow Workflow, string HeadVersionId)> SeedTemplateAsync(
        SharedComponentsTestHarness harness,
        SaveWorkflowCommandHandler saveHandler)
    {
        Workflow workflow = BuildWorkflow();
        string headVersionId = await harness.SaveTemplateAsync(saveHandler, ORG, USER, TYPE, workflow);

        return (workflow, headVersionId);
    }

    private static Workflow BuildWorkflow()
    {
        var workflow = new Workflow("Report") { Title = "Report" };
        var section = new Section("Body", workflow) { Title = "Body" };
        _ = new Block(BlockTypes.AiQuery, section, "one")
        {
            Name = "Counterparty check",
            Properties = new AiQueryProperties { UserPromptTemplate = "Prompt one" },
        };
        _ = new Block(BlockTypes.AiQuery, section, "two")
        {
            Name = "Second block",
            Properties = new AiQueryProperties { UserPromptTemplate = "Prompt two" },
        };

        return workflow;
    }

    private static Block DetachedTwinOfBlockOne()
    {
        var scratch = new Workflow("scratch");
        var section = new Section("scratch", scratch);

        return new Block(BlockTypes.AiQuery, section, "one")
        {
            Name = "Counterparty check",
            Properties = new AiQueryProperties { UserPromptTemplate = "Prompt one" },
        };
    }
}
