using ErrorOr;
using IniaAi.Application.Commands.WorkflowTransfer.ImportWorkflowTemplateApply;
using IniaAi.Application.Commands.Workflows.SaveWorkflow;
using IniaAi.Application.Common.Workflows;
using IniaAi.Application.Errors;
using IniaAi.Application.UnitTests.Common.SharedComponents;
using IniaAi.Application.UnitTests.Common.Workflows;
using IniaAi.Configuration.Platform;
using IniaAi.Domain.Entities.SharedComponents;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Blocks;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties;
using IniaAi.Domain.Entities.Workflows.Enums;
using IniaAi.WebApi.Contracts.Dto.WorkflowTransfer;
using Microsoft.EntityFrameworkCore;
using Action = IniaAi.Domain.Entities.SharedComponents.Enums.SharedComponentTransferComponentAction;
using Arm = IniaAi.Domain.Entities.SharedComponents.Enums.SharedComponentTransferArm;
using DomainKind = IniaAi.Domain.Entities.SharedComponents.Enums.SharedComponentKind;
using DomainState = IniaAi.Domain.Entities.SharedComponents.Enums.SharedComponentState;
using LinkState = IniaAi.Domain.Entities.SharedComponents.Enums.SharedComponentLinkState;
using WireKind = IniaAi.WebApi.Contracts.Enums.SharedComponentKind;

namespace IniaAi.Application.UnitTests.Commands.WorkflowTransfer;

public class ImportWorkflowTemplateApplyCommandHandlerTests
{
    private const string ORG = "org-test";
    private const string USER = "user-import";
    private const string TYPE = "WT-Report";
    private const string ORIGIN = "WT-Origin";

    [Fact]
    public async Task Apply_NewBlockComponent_AdoptsBundleIdAndCreatesVersionOne()
    {
        using var harness = new SharedComponentsTestHarness();
        (SaveWorkflowCommandHandler saveHandler, string baseVersionId) = await SeedLocalTemplateAsync(harness);
        (string json, string hash) = BlockDef(harness, "new");
        WorkflowTransferBundle bundle = Bundle(harness, [BlockComp("sc-new", json, hash, hash)]);

        ErrorOr<ImportWorkflowTemplateApplyResult> result = await Apply(harness, bundle, baseVersionId, Arm.KeepMine, []);

        Assert.False(result.IsError);
        ImportComponentOutcome outcome = Assert.Single(result.Value.ComponentOutcomes);
        Assert.Equal(Action.Created, outcome.Action);
        Assert.Equal("sc-new", outcome.LocalComponentId);
        SharedComponentEntityAssert component = await LoadComponentAsync(harness, "sc-new");
        Assert.Equal(DomainState.Active, component.State);
        Assert.Equal(hash, component.HeadHash);
        Assert.Equal(1, component.HeadSequenceNumber);
    }

    [Fact]
    public async Task Apply_TakeTheirsBundleAhead_AppendsVersionOntoLocalChain()
    {
        using var harness = new SharedComponentsTestHarness();
        (SaveWorkflowCommandHandler saveHandler, string baseVersionId) = await SeedLocalTemplateAsync(harness);
        (string json1, string hash1) = BlockDef(harness, "v1");
        (string json2, string hash2) = BlockDef(harness, "v2");
        _ = await harness.AddComponentAsync(DomainKind.Block, "C", id: "sc-1");
        string head1 = (await harness.AddVersionAsync("sc-1", 1, hash1, json1)).Id;
        WorkflowTransferBundle bundle = Bundle(harness, [BlockComp("sc-1", json2, hash2, hash1, hash2)]);

        ErrorOr<ImportWorkflowTemplateApplyResult> result = await Apply(
            harness, bundle, baseVersionId, Arm.KeepMine, [new ImportComponentDecision("sc-1", Arm.TakeTheirs, head1)]);

        Assert.False(result.IsError);
        ImportComponentOutcome outcome = Assert.Single(result.Value.ComponentOutcomes);
        Assert.Equal(Action.Appended, outcome.Action);
        List<SharedComponentVersion> chain = await ChainAsync(harness, "sc-1");
        Assert.Equal(2, chain.Count);
        SharedComponentVersion appended = chain.Single(v => v.SequenceNumber == 2);
        Assert.Equal(hash2, appended.ContentHash);
        Assert.Equal(head1, appended.ParentVersionId);
    }

    [Fact]
    public async Task Apply_TakeTheirsHeadAlreadyMatches_DedupsWithoutAppending()
    {
        using var harness = new SharedComponentsTestHarness();
        (SaveWorkflowCommandHandler saveHandler, string baseVersionId) = await SeedLocalTemplateAsync(harness);
        (string json, string hash) = BlockDef(harness, "same");
        _ = await harness.AddComponentAsync(DomainKind.Block, "C", id: "sc-1");
        string head1 = (await harness.AddVersionAsync("sc-1", 1, hash, json)).Id;
        WorkflowTransferBundle bundle = Bundle(harness, [BlockComp("sc-1", json, hash, hash)]);

        ErrorOr<ImportWorkflowTemplateApplyResult> result = await Apply(
            harness, bundle, baseVersionId, Arm.KeepMine, [new ImportComponentDecision("sc-1", Arm.TakeTheirs, head1)]);

        Assert.False(result.IsError);
        ImportComponentOutcome outcome = Assert.Single(result.Value.ComponentOutcomes);
        Assert.Equal(Action.Deduped, outcome.Action);
        Assert.Single(await ChainAsync(harness, "sc-1"));
    }

    [Fact]
    public async Task Apply_KeepMineComponent_LeavesLocalChainUntouched()
    {
        using var harness = new SharedComponentsTestHarness();
        (SaveWorkflowCommandHandler saveHandler, string baseVersionId) = await SeedLocalTemplateAsync(harness);
        (string json1, string hash1) = BlockDef(harness, "v1");
        (string json2, string hash2) = BlockDef(harness, "v2");
        _ = await harness.AddComponentAsync(DomainKind.Block, "C", id: "sc-1");
        string head1 = (await harness.AddVersionAsync("sc-1", 1, hash1, json1)).Id;
        WorkflowTransferBundle bundle = Bundle(harness, [BlockComp("sc-1", json2, hash2, hash1, hash2)]);

        ErrorOr<ImportWorkflowTemplateApplyResult> result = await Apply(
            harness, bundle, baseVersionId, Arm.KeepMine, [new ImportComponentDecision("sc-1", Arm.KeepMine, head1)]);

        Assert.False(result.IsError);
        ImportComponentOutcome outcome = Assert.Single(result.Value.ComponentOutcomes);
        Assert.Equal(Action.Kept, outcome.Action);
        Assert.Single(await ChainAsync(harness, "sc-1"));
    }

    [Fact]
    public async Task Apply_TakeTheirsWithStaleExpectedHead_ReturnsHeadMoved()
    {
        using var harness = new SharedComponentsTestHarness();
        (SaveWorkflowCommandHandler saveHandler, string baseVersionId) = await SeedLocalTemplateAsync(harness);
        (string json1, string hash1) = BlockDef(harness, "v1");
        (string json2, string hash2) = BlockDef(harness, "v2");
        _ = await harness.AddComponentAsync(DomainKind.Block, "C", id: "sc-1");
        _ = await harness.AddVersionAsync("sc-1", 1, hash1, json1);
        WorkflowTransferBundle bundle = Bundle(harness, [BlockComp("sc-1", json2, hash2, hash1, hash2)]);

        ErrorOr<ImportWorkflowTemplateApplyResult> result = await Apply(
            harness, bundle, baseVersionId, Arm.KeepMine, [new ImportComponentDecision("sc-1", Arm.TakeTheirs, "stale-version-id")]);

        Assert.True(result.IsError);
        Assert.Equal(SharedComponentErrorCodes.HEAD_MOVED, result.FirstError.Code);
        Assert.Single(await ChainAsync(harness, "sc-1"));
    }

    [Fact]
    public async Task Apply_TakeTheirsZeroOverlapWithoutAck_ReturnsDivergedLineage()
    {
        using var harness = new SharedComponentsTestHarness();
        (SaveWorkflowCommandHandler saveHandler, string baseVersionId) = await SeedLocalTemplateAsync(harness);
        (string jsonA, string hashA) = BlockDef(harness, "A");
        (string jsonB, string hashB) = BlockDef(harness, "B");
        _ = await harness.AddComponentAsync(DomainKind.Block, "C", id: "sc-1");
        string head1 = (await harness.AddVersionAsync("sc-1", 1, hashA, jsonA)).Id;
        WorkflowTransferBundle bundle = Bundle(harness, [BlockComp("sc-1", jsonB, hashB, hashB)]);

        ErrorOr<ImportWorkflowTemplateApplyResult> result = await Apply(
            harness, bundle, baseVersionId, Arm.KeepMine, [new ImportComponentDecision("sc-1", Arm.TakeTheirs, head1)]);

        Assert.True(result.IsError);
        Assert.Equal(SharedComponentErrorCodes.DIVERGED_LINEAGE, result.FirstError.Code);
        Assert.Single(await ChainAsync(harness, "sc-1"));
    }

    [Fact]
    public async Task Apply_TakeTheirsZeroOverlapWithAck_AppendsUnrelatedContent()
    {
        using var harness = new SharedComponentsTestHarness();
        (SaveWorkflowCommandHandler saveHandler, string baseVersionId) = await SeedLocalTemplateAsync(harness);
        (string jsonA, string hashA) = BlockDef(harness, "A");
        (string jsonB, string hashB) = BlockDef(harness, "B");
        _ = await harness.AddComponentAsync(DomainKind.Block, "C", id: "sc-1");
        string head1 = (await harness.AddVersionAsync("sc-1", 1, hashA, jsonA)).Id;
        WorkflowTransferBundle bundle = Bundle(harness, [BlockComp("sc-1", jsonB, hashB, hashB)]);

        ErrorOr<ImportWorkflowTemplateApplyResult> result = await Apply(
            harness, bundle, baseVersionId, Arm.KeepMine, [new ImportComponentDecision("sc-1", Arm.TakeTheirs, head1)],
            acknowledgeUnrelatedHistory: true);

        Assert.False(result.IsError);
        Assert.Equal(Action.Appended, Assert.Single(result.Value.ComponentOutcomes).Action);
        Assert.Equal(hashB, (await ChainAsync(harness, "sc-1")).Single(v => v.SequenceNumber == 2).ContentHash);
    }

    [Fact]
    public async Task Apply_TakeTheirsOntoRetiredLocal_ReturnsRetired()
    {
        using var harness = new SharedComponentsTestHarness();
        (SaveWorkflowCommandHandler saveHandler, string baseVersionId) = await SeedLocalTemplateAsync(harness);
        (string json1, string hash1) = BlockDef(harness, "v1");
        (string json2, string hash2) = BlockDef(harness, "v2");
        _ = await harness.AddComponentAsync(DomainKind.Block, "C", state: DomainState.Retired, id: "sc-1");
        string head1 = (await harness.AddVersionAsync("sc-1", 1, hash1, json1)).Id;
        WorkflowTransferBundle bundle = Bundle(harness, [BlockComp("sc-1", json2, hash2, hash1, hash2)]);

        ErrorOr<ImportWorkflowTemplateApplyResult> result = await Apply(
            harness, bundle, baseVersionId, Arm.KeepMine, [new ImportComponentDecision("sc-1", Arm.TakeTheirs, head1)]);

        Assert.True(result.IsError);
        Assert.Equal(SharedComponentErrorCodes.RETIRED, result.FirstError.Code);
    }

    [Fact]
    public async Task Apply_NewerBundleFormat_IsRejected()
    {
        using var harness = new SharedComponentsTestHarness();
        (SaveWorkflowCommandHandler saveHandler, string baseVersionId) = await SeedLocalTemplateAsync(harness);
        WorkflowTransferBundle bundle = Bundle(harness, [], formatVersion: 2);

        ErrorOr<ImportWorkflowTemplateApplyResult> result = await Apply(harness, bundle, baseVersionId, Arm.KeepMine, []);

        Assert.True(result.IsError);
        Assert.Equal(SharedComponentErrorCodes.BUNDLE_FORMAT_UNSUPPORTED, result.FirstError.Code);
    }

    [Fact]
    public async Task Apply_TargetTypeMissing_CreatesTypeSeedsAccessAndLandsTemplate()
    {
        using var harness = new SharedComponentsTestHarness();
        (string json, string hash) = BlockDef(harness, "linked");
        string bundleTemplate = LinkingBundleTemplate(harness, "sc-new", hash);
        WorkflowTransferBundle bundle = Bundle(harness, [BlockComp("sc-new", json, hash, hash)], templateJson: bundleTemplate)
            with
        { WorkflowTypeDescription = "ported from origin" };

        ErrorOr<ImportWorkflowTemplateApplyResult> result = await Apply(harness, bundle, null, Arm.TakeTheirs, []);

        Assert.False(result.IsError);
        WorkflowType createdType = await harness.Context.Set<WorkflowType>()
            .SingleAsync(t => t.Id == TYPE, TestContext.Current.CancellationToken);
        Assert.Equal("Imported", createdType.Name);
        Assert.Equal("ported from origin", createdType.Description);
        bool accessSeeded = await harness.Context.Set<WorkflowTypeOrganizationAccess>()
            .AnyAsync(a => a.WorkflowTypeId == TYPE && a.OrganizationId == ORG, TestContext.Current.CancellationToken);
        Assert.True(accessSeeded);
        Assert.Equal(Action.Created, Assert.Single(result.Value.ComponentOutcomes).Action);
        Assert.NotNull(result.Value.Template.VersionId);
        Assert.False(result.Value.Template.WasDeduped);
        Workflow landed = await LoadTemplateHeadAsync(harness, result.Value.Template.VersionId!);
        Block linkedBlock = Assert.Single(landed.GetAllBlocks(), b => b.SharedComponentLinkState == LinkState.Linked);
        Assert.Equal("sc-new", linkedBlock.SharedComponentId);
    }

    [Fact]
    public async Task Apply_TargetTypeHeadless_LandsFirstVersionKeepingExistingTypeRow()
    {
        using var harness = new SharedComponentsTestHarness();
        _ = harness.Context.Add(new WorkflowType(TYPE, "Existing name"));
        _ = harness.Context.SaveChanges();
        WorkflowTransferBundle bundle = Bundle(harness, []);

        ErrorOr<ImportWorkflowTemplateApplyResult> result = await Apply(harness, bundle, null, Arm.TakeTheirs, []);

        Assert.False(result.IsError);
        Assert.NotNull(result.Value.Template.VersionId);
        WorkflowType existing = await harness.Context.Set<WorkflowType>()
            .SingleAsync(t => t.Id == TYPE, TestContext.Current.CancellationToken);
        Assert.Equal("Existing name", existing.Name);
        bool accessSeeded = await harness.Context.Set<WorkflowTypeOrganizationAccess>()
            .AnyAsync(a => a.WorkflowTypeId == TYPE, TestContext.Current.CancellationToken);
        Assert.False(accessSeeded);
    }

    [Fact]
    public async Task Apply_TargetTypeRetired_ReportsItDeleted_AndLandsNothing()
    {
        using var harness = new SharedComponentsTestHarness();
        _ = harness.Context.Add(new WorkflowType(TYPE, "Deleted") { RetiredAt = DateTime.UtcNow });
        _ = harness.Context.SaveChanges();
        WorkflowTransferBundle bundle = Bundle(harness, []);

        ErrorOr<ImportWorkflowTemplateApplyResult> result = await Apply(harness, bundle, null, Arm.TakeTheirs, []);

        Assert.True(result.IsError);
        Assert.Equal(WorkflowTypeErrorFactory.NOT_FOUND_CODE, result.FirstError.Code);
        Assert.False(await harness.Context.Set<Workflow>().IgnoreQueryFilters()
            .AnyAsync(workflow => workflow.WorkflowTypeId == TYPE, TestContext.Current.CancellationToken));
        Assert.False(await harness.Context.Set<WorkflowVersion>().IgnoreQueryFilters()
            .AnyAsync(version => version.WorkflowTypeId == TYPE, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Apply_TargetTypeMissing_WithStaleBaseVersion_Conflicts()
    {
        using var harness = new SharedComponentsTestHarness();
        (string json, string hash) = BlockDef(harness, "x");
        WorkflowTransferBundle bundle = Bundle(harness, [BlockComp("sc-x", json, hash, hash)]);

        ErrorOr<ImportWorkflowTemplateApplyResult> result = await Apply(harness, bundle, "any-base", Arm.TakeTheirs, []);

        Assert.True(result.IsError);
        Assert.Equal(ErrorType.Conflict, result.FirstError.Type);
    }

    [Fact]
    public async Task Apply_TargetTypeMissing_FailedApplyThenRetry_ProvisionsTypeOnce()
    {
        using var harness = new SharedComponentsTestHarness();
        (string json1, string hash1) = BlockDef(harness, "v1");
        (string json2, string hash2) = BlockDef(harness, "v2");
        _ = await harness.AddComponentAsync(DomainKind.Block, "C", id: "sc-1");
        string head1 = (await harness.AddVersionAsync("sc-1", 1, hash1, json1)).Id;
        WorkflowTransferBundle bundle = Bundle(harness, [BlockComp("sc-1", json2, hash2, hash1, hash2)]);

        ErrorOr<ImportWorkflowTemplateApplyResult> failed = await Apply(
            harness, bundle, null, Arm.TakeTheirs, [new ImportComponentDecision("sc-1", Arm.TakeTheirs, "stale-version-id")]);
        Assert.Equal(SharedComponentErrorCodes.HEAD_MOVED, failed.FirstError.Code);

        ErrorOr<ImportWorkflowTemplateApplyResult> retried = await Apply(
            harness, bundle, null, Arm.TakeTheirs, [new ImportComponentDecision("sc-1", Arm.TakeTheirs, head1)]);

        Assert.False(retried.IsError);
        WorkflowType createdType = await harness.Context.Set<WorkflowType>()
            .SingleAsync(t => t.Id == TYPE, TestContext.Current.CancellationToken);
        Assert.Equal("Imported", createdType.Name);
    }

    [Fact]
    public async Task Apply_TargetTypeMissing_NamelessBundle_ReturnsValidationError()
    {
        using var harness = new SharedComponentsTestHarness();
        WorkflowTransferBundle bundle = Bundle(harness, []) with { WorkflowTypeName = "" };

        ErrorOr<ImportWorkflowTemplateApplyResult> result = await Apply(harness, bundle, null, Arm.TakeTheirs, []);

        Assert.True(result.IsError);
        Assert.Equal(SharedComponentErrorCodes.BUNDLE_TYPE_NAME_MISSING, result.FirstError.Code);
    }

    [Fact]
    public async Task Apply_TakeTheirsTemplate_LandsBundleTemplateLinkingCreatedComponent()
    {
        using var harness = new SharedComponentsTestHarness();
        (SaveWorkflowCommandHandler saveHandler, string baseVersionId) = await SeedLocalTemplateAsync(harness);
        (string json, string hash) = BlockDef(harness, "linked");
        string bundleTemplate = LinkingBundleTemplate(harness, "sc-new", hash);
        WorkflowTransferBundle bundle = Bundle(harness, [BlockComp("sc-new", json, hash, hash)], templateJson: bundleTemplate);

        ErrorOr<ImportWorkflowTemplateApplyResult> result = await Apply(harness, bundle, baseVersionId, Arm.TakeTheirs, []);

        Assert.False(result.IsError);
        Assert.Equal(Action.Created, Assert.Single(result.Value.ComponentOutcomes).Action);
        Assert.NotEqual(baseVersionId, result.Value.Template.VersionId);
        Workflow landed = await LoadTemplateHeadAsync(harness, result.Value.Template.VersionId!);
        Block linkedBlock = Assert.Single(landed.GetAllBlocks(), b => b.SharedComponentLinkState == LinkState.Linked);
        Assert.Equal("sc-new", linkedBlock.SharedComponentId);
    }

    [Fact]
    public async Task Apply_NewComponentWithCategory_UpsertsCategoryAndStampsIt()
    {
        using var harness = new SharedComponentsTestHarness();
        (SaveWorkflowCommandHandler saveHandler, string baseVersionId) = await SeedLocalTemplateAsync(harness);
        (string json, string hash) = BlockDef(harness, "cat");
        var component = new TransferComponentDto("sc-new", WireKind.Block, "Named", null, "cat-fin", hash, json, [hash]);
        WorkflowTransferBundle bundle = Bundle(
            harness, [component], categories: [new TransferCategoryDto("cat-fin", "Finance", null)]);

        ErrorOr<ImportWorkflowTemplateApplyResult> result = await Apply(harness, bundle, baseVersionId, Arm.KeepMine, []);

        Assert.False(result.IsError);
        bool categoryExists = await harness.Context.SharedComponentCategories
            .AnyAsync(c => c.Id == "cat-fin", TestContext.Current.CancellationToken);
        Assert.True(categoryExists);
        SharedComponent created = await harness.Context.SharedComponents
            .SingleAsync(c => c.Id == "sc-new", TestContext.Current.CancellationToken);
        Assert.Equal("cat-fin", created.CategoryId);
    }

    [Fact]
    public async Task Apply_NewComponentCategorySoftDeletedLocally_RevivesCategory()
    {
        using var harness = new SharedComponentsTestHarness();
        (SaveWorkflowCommandHandler saveHandler, string baseVersionId) = await SeedLocalTemplateAsync(harness);
        _ = harness.Context.SharedComponentCategories.Add(new SharedComponentCategory
        {
            Id = "cat-x",
            Name = "Old local name",
            IsDeleted = true,
            DeletedAt = DateTime.UtcNow,
            DeletedByUserId = "someone",
        });
        _ = await harness.Context.SaveChangesAsync(TestContext.Current.CancellationToken);
        (string json, string hash) = BlockDef(harness, "cat");
        var component = new TransferComponentDto("sc-new", WireKind.Block, "Named", null, "cat-x", hash, json, [hash]);
        WorkflowTransferBundle bundle = Bundle(
            harness, [component], categories: [new TransferCategoryDto("cat-x", "Imported name", null)]);

        ErrorOr<ImportWorkflowTemplateApplyResult> result = await Apply(harness, bundle, baseVersionId, Arm.KeepMine, []);

        Assert.False(result.IsError);
        SharedComponentCategory revived = await harness.Context.SharedComponentCategories
            .SingleAsync(c => c.Id == "cat-x", TestContext.Current.CancellationToken);
        Assert.False(revived.IsDeleted);
        SharedComponent created = await harness.Context.SharedComponents
            .SingleAsync(c => c.Id == "sc-new", TestContext.Current.CancellationToken);
        Assert.Equal("cat-x", created.CategoryId);
    }

    [Fact]
    public async Task Apply_IdempotentReimport_DedupsComponentAndTemplate()
    {
        using var harness = new SharedComponentsTestHarness();
        var localWorkflow = new Workflow("Report") { Title = "Report" };
        _ = new Section("Body", localWorkflow) { Title = "Body" };
        (SaveWorkflowCommandHandler saveHandler, string baseVersionId) = await SeedLocalTemplateAsync(harness, localWorkflow);
        (string json, string hash) = BlockDef(harness, "same");
        _ = await harness.AddComponentAsync(DomainKind.Block, "C", id: "sc-1");
        string head1 = (await harness.AddVersionAsync("sc-1", 1, hash, json)).Id;
        string sameTemplate = harness.Canonicalizer.ToCanonicalJson(localWorkflow);
        WorkflowTransferBundle bundle = Bundle(harness, [BlockComp("sc-1", json, hash, hash)], templateJson: sameTemplate);

        ErrorOr<ImportWorkflowTemplateApplyResult> result = await Apply(
            harness, bundle, baseVersionId, Arm.TakeTheirs, [new ImportComponentDecision("sc-1", Arm.TakeTheirs, head1)]);

        Assert.False(result.IsError);
        Assert.Equal(Action.Deduped, Assert.Single(result.Value.ComponentOutcomes).Action);
        Assert.True(result.Value.Template.WasDeduped);
        Assert.Single(await ChainAsync(harness, "sc-1"));
    }

    [Fact]
    public async Task Apply_DuplicateComponentIds_ReturnsCorruptDefinition()
    {
        using var harness = new SharedComponentsTestHarness();
        (SaveWorkflowCommandHandler saveHandler, string baseVersionId) = await SeedLocalTemplateAsync(harness);
        (string json, string hash) = BlockDef(harness, "dup");
        WorkflowTransferBundle bundle = Bundle(
            harness,
            [
                BlockComp("sc-dup", json, hash, hash),
                BlockComp("sc-dup", json, hash, hash),
            ]);

        ErrorOr<ImportWorkflowTemplateApplyResult> result = await Apply(harness, bundle, baseVersionId, Arm.KeepMine, []);

        Assert.True(result.IsError);
        Assert.Equal(SharedComponentErrorCodes.CORRUPT_DEFINITION, result.FirstError.Code);
    }

    [Fact]
    public async Task Apply_TargetSaveFails_LeavesNoCreatedComponentTracked()
    {
        using var harness = new SharedComponentsTestHarness();
        _ = await SeedLocalTemplateAsync(harness);
        (string json, string hash) = BlockDef(harness, "new");
        WorkflowTransferBundle bundle = Bundle(harness, [BlockComp("sc-new", json, hash, hash)]);

        ErrorOr<ImportWorkflowTemplateApplyResult> result = await Apply(
            harness, bundle, "stale-base-version", Arm.KeepMine, []);

        Assert.True(result.IsError);
        bool lingering = harness.Context.ChangeTracker.Entries<SharedComponent>()
            .Any(entry => entry.State == EntityState.Added && entry.Entity.Id == "sc-new");
        Assert.False(lingering);
    }

    [Fact]
    public async Task Apply_CreatedSectionRefTargetCarriedAsSection_ReturnsKindMismatch()
    {
        using var harness = new SharedComponentsTestHarness();
        (_, string baseVersionId) = await SeedLocalTemplateAsync(harness);
        (string tamperedJson, string tamperedHash) = SectionDef(harness, "target");
        (string refJson, string refHash) = RefSectionDef(harness, "sc-tampered");
        WorkflowTransferBundle bundle = Bundle(
            harness,
            [
                SectionComp("sc-sec", refJson, refHash, refHash),
                SectionComp("sc-tampered", tamperedJson, tamperedHash, tamperedHash),
            ]);

        ErrorOr<ImportWorkflowTemplateApplyResult> result = await Apply(harness, bundle, baseVersionId, Arm.KeepMine, []);

        Assert.True(result.IsError);
        Assert.Equal(SharedComponentErrorCodes.KIND_MISMATCH, result.FirstError.Code);
        Assert.False(await harness.Context.SharedComponents
            .AnyAsync(c => c.Id == "sc-sec", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Apply_TakeTheirsSectionRefToRetiredKeepMineComponent_ReturnsRetired()
    {
        using var harness = new SharedComponentsTestHarness();
        (_, string baseVersionId) = await SeedLocalTemplateAsync(harness);
        (string depJson, string depHash) = BlockDef(harness, "dep");
        _ = await harness.AddComponentAsync(DomainKind.Block, "Dep", state: DomainState.Retired, id: "sc-dep");
        string depHead = (await harness.AddVersionAsync("sc-dep", 1, depHash, depJson)).Id;
        (string secJson1, string secHash1) = SectionDef(harness, "v1");
        _ = await harness.AddComponentAsync(DomainKind.Section, "Sec", id: "sc-sec");
        string secHead = (await harness.AddVersionAsync("sc-sec", 1, secHash1, secJson1)).Id;
        (string refJson, string refHash) = RefSectionDef(harness, "sc-dep");
        WorkflowTransferBundle bundle = Bundle(
            harness,
            [
                SectionComp("sc-sec", refJson, refHash, secHash1, refHash),
                BlockComp("sc-dep", depJson, depHash, depHash),
            ]);

        ErrorOr<ImportWorkflowTemplateApplyResult> result = await Apply(
            harness, bundle, baseVersionId, Arm.KeepMine,
            [
                new ImportComponentDecision("sc-sec", Arm.TakeTheirs, secHead),
                new ImportComponentDecision("sc-dep", Arm.KeepMine, depHead),
            ]);

        Assert.True(result.IsError);
        Assert.Equal(SharedComponentErrorCodes.RETIRED, result.FirstError.Code);
        Assert.Single(await ChainAsync(harness, "sc-sec"));
    }

    [Fact]
    public async Task Apply_TakeTheirsSectionRefToPayloadAbsentKeepMineHead_ReturnsPayloadAbsent()
    {
        using var harness = new SharedComponentsTestHarness();
        (_, string baseVersionId) = await SeedLocalTemplateAsync(harness);
        (string depJson, string depHash) = BlockDef(harness, "dep");
        _ = await harness.AddComponentAsync(DomainKind.Block, "Dep", id: "sc-dep");
        string depHead = (await harness.AddVersionAsync(
            "sc-dep", 1, depHash, contentJson: null, payloadAbsentAt: DateTime.UtcNow)).Id;
        (string secJson1, string secHash1) = SectionDef(harness, "v1");
        _ = await harness.AddComponentAsync(DomainKind.Section, "Sec", id: "sc-sec");
        string secHead = (await harness.AddVersionAsync("sc-sec", 1, secHash1, secJson1)).Id;
        (string refJson, string refHash) = RefSectionDef(harness, "sc-dep");
        WorkflowTransferBundle bundle = Bundle(
            harness,
            [
                SectionComp("sc-sec", refJson, refHash, secHash1, refHash),
                BlockComp("sc-dep", depJson, depHash, depHash),
            ]);

        ErrorOr<ImportWorkflowTemplateApplyResult> result = await Apply(
            harness, bundle, baseVersionId, Arm.KeepMine,
            [
                new ImportComponentDecision("sc-sec", Arm.TakeTheirs, secHead),
                new ImportComponentDecision("sc-dep", Arm.KeepMine, depHead),
            ]);

        Assert.True(result.IsError);
        Assert.Equal(SharedComponentErrorCodes.PAYLOAD_ABSENT, result.FirstError.Code);
        Assert.Single(await ChainAsync(harness, "sc-sec"));
    }

    [Fact]
    public async Task Apply_CreatedSectionRefResolvingToCreatedBlock_LandsBoth()
    {
        using var harness = new SharedComponentsTestHarness();
        (_, string baseVersionId) = await SeedLocalTemplateAsync(harness);
        (string blkJson, string blkHash) = BlockDef(harness, "blk");
        (string refJson, string refHash) = RefSectionDef(harness, "sc-blk");
        WorkflowTransferBundle bundle = Bundle(
            harness,
            [
                SectionComp("sc-sec", refJson, refHash, refHash),
                BlockComp("sc-blk", blkJson, blkHash, blkHash),
            ]);

        ErrorOr<ImportWorkflowTemplateApplyResult> result = await Apply(harness, bundle, baseVersionId, Arm.KeepMine, []);

        Assert.False(result.IsError);
        Assert.Equal(2, result.Value.ComponentOutcomes.Count);
        Assert.All(result.Value.ComponentOutcomes, outcome => Assert.Equal(Action.Created, outcome.Action));
        SharedComponentVersion landedSection = Assert.Single(await ChainAsync(harness, "sc-sec"));
        Section? landed = Section.FromJson(landedSection.ContentJson);
        SectionEntry refEntry = Assert.Single(landed!.Entries!, e => e.Kind == SectionEntryKind.ComponentRef);
        Assert.Equal("sc-blk", refEntry.SharedComponentId);
    }

    [Fact]
    public async Task Apply_TakeTheirsSectionRefToActiveKeepMineBlock_Appends()
    {
        using var harness = new SharedComponentsTestHarness();
        (_, string baseVersionId) = await SeedLocalTemplateAsync(harness);
        (string depJson, string depHash) = BlockDef(harness, "dep");
        _ = await harness.AddComponentAsync(DomainKind.Block, "Dep", id: "sc-dep");
        _ = await harness.AddVersionAsync("sc-dep", 1, depHash, depJson);
        (string secJson1, string secHash1) = SectionDef(harness, "v1");
        _ = await harness.AddComponentAsync(DomainKind.Section, "Sec", id: "sc-sec");
        string secHead = (await harness.AddVersionAsync("sc-sec", 1, secHash1, secJson1)).Id;
        (string refJson, string refHash) = RefSectionDef(harness, "sc-dep");
        WorkflowTransferBundle bundle = Bundle(
            harness,
            [
                SectionComp("sc-sec", refJson, refHash, secHash1, refHash),
                BlockComp("sc-dep", depJson, depHash, depHash),
            ]);

        ErrorOr<ImportWorkflowTemplateApplyResult> result = await Apply(
            harness, bundle, baseVersionId, Arm.KeepMine,
            [new ImportComponentDecision("sc-sec", Arm.TakeTheirs, secHead)]);

        Assert.False(result.IsError);
        Assert.Equal(2, (await ChainAsync(harness, "sc-sec")).Count);
    }

    [Fact]
    public async Task Apply_AgentBearingBundleTemplate_DisabledRuntime_Refuses()
    {
        using var harness = new SharedComponentsTestHarness();
        WorkflowTransferBundle bundle = Bundle(
            harness, [], templateJson: AgentBundleTemplate(harness));

        ErrorOr<ImportWorkflowTemplateApplyResult> result =
            await Apply(harness, bundle, null, Arm.TakeTheirs, []);

        Assert.True(result.IsError);
        Assert.Contains(result.Errors, e =>
            e.Description.Contains("agent runtime", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Apply_AgentBearingBundleTemplate_EnabledRuntime_Lands()
    {
        using var harness = new SharedComponentsTestHarness();
        WorkflowTransferBundle bundle = Bundle(
            harness, [], templateJson: AgentBundleTemplate(harness));

        ErrorOr<ImportWorkflowTemplateApplyResult> result = await Apply(
            harness,
            bundle,
            null,
            Arm.TakeTheirs,
            [],
            agentRuntime: AgentTestData.EnabledRuntime());

        Assert.False(result.IsError);
        Workflow landed = await LoadTemplateHeadAsync(harness, result.Value.Template.VersionId!);
        _ = Assert.Single(landed.GetAllBlocks(), b => b.Type == BlockTypes.Agent);
    }

    [Fact]
    public async Task Apply_AgentBearingComponent_KeepMineTemplate_DisabledRuntime_Refuses()
    {
        using var harness = new SharedComponentsTestHarness();
        (_, string baseVersionId) = await SeedLocalTemplateAsync(harness);
        (string json, string hash) = harness.Definition(AgentTestData.AgentBlockDefinition());
        WorkflowTransferBundle bundle = Bundle(harness, [BlockComp("sc-agent", json, hash, hash)]);

        ErrorOr<ImportWorkflowTemplateApplyResult> result =
            await Apply(harness, bundle, baseVersionId, Arm.KeepMine, []);

        Assert.True(result.IsError);
        Assert.Equal(SharedComponentErrorCodes.AGENT_RUNTIME_DISABLED, result.FirstError.Code);
        Assert.Empty(harness.Context.SharedComponents);
    }

    [Fact]
    public async Task Apply_AgentBearingComponent_KeepMineTemplate_EnabledRuntime_Lands()
    {
        using var harness = new SharedComponentsTestHarness();
        (_, string baseVersionId) = await SeedLocalTemplateAsync(harness);
        (string json, string hash) = harness.Definition(AgentTestData.AgentBlockDefinition());
        WorkflowTransferBundle bundle = Bundle(harness, [BlockComp("sc-agent", json, hash, hash)]);

        ErrorOr<ImportWorkflowTemplateApplyResult> result = await Apply(
            harness,
            bundle,
            baseVersionId,
            Arm.KeepMine,
            [],
            agentRuntime: AgentTestData.EnabledRuntime());

        Assert.False(result.IsError);
        Assert.Equal(Action.Created, Assert.Single(result.Value.ComponentOutcomes).Action);
        _ = Assert.Single(harness.Context.SharedComponents);
    }

    private static Task<ErrorOr<ImportWorkflowTemplateApplyResult>> Apply(
        SharedComponentsTestHarness harness,
        WorkflowTransferBundle bundle,
        string? baseVersionId,
        Arm templateArm,
        IReadOnlyList<ImportComponentDecision> decisions,
        bool acknowledgeUnrelatedHistory = false,
        bool acknowledgeSignatureOrphans = false,
        AgentRuntimeSettings? agentRuntime = null)
    {
        var handler = new ImportWorkflowTemplateApplyCommandHandler(
            harness.CreateFanOutService(agentRuntime));

        return handler.Handle(
            new ImportWorkflowTemplateApplyCommand(
                ORG,
                USER,
                TYPE,
                bundle,
                baseVersionId,
                templateArm,
                acknowledgeUnrelatedHistory,
                acknowledgeSignatureOrphans,
                decisions),
            TestContext.Current.CancellationToken);
    }

    private static async Task<(SaveWorkflowCommandHandler SaveHandler, string BaseVersionId)> SeedLocalTemplateAsync(
        SharedComponentsTestHarness harness,
        Workflow? workflow = null)
    {
        (_, SaveWorkflowCommandHandler saveHandler) = harness.CreateRealSaveSender();
        Workflow template = workflow ?? BuildPlainWorkflow();
        string baseVersionId = await harness.SaveTemplateAsync(saveHandler, ORG, "user-seed", TYPE, template);

        return (saveHandler, baseVersionId);
    }

    private static (string Json, string Hash) BlockDef(SharedComponentsTestHarness harness, string prompt)
    {
        return harness.Definition(SharedComponentLinkTestData.BuildBlockDefinition(prompt));
    }

    private static TransferComponentDto BlockComp(string id, string headJson, string headHash, params string[] trail)
    {
        return new TransferComponentDto(id, WireKind.Block, id, null, null, headHash, headJson, [.. trail]);
    }

    private static TransferComponentDto SectionComp(string id, string headJson, string headHash, params string[] trail)
    {
        return new TransferComponentDto(id, WireKind.Section, id, null, null, headHash, headJson, [.. trail]);
    }

    private static (string Json, string Hash) SectionDef(SharedComponentsTestHarness harness, string prompt)
    {
        return harness.Definition(SharedComponentLinkTestData.DefSection("Market", ("p", prompt)));
    }

    private static (string Json, string Hash) RefSectionDef(SharedComponentsTestHarness harness, string refTargetId)
    {
        return harness.Definition(SharedComponentLinkTestData.RefSection("Market", "r", refTargetId));
    }

    private static WorkflowTransferBundle Bundle(
        SharedComponentsTestHarness harness,
        IReadOnlyList<TransferComponentDto> components,
        int formatVersion = 1,
        string? templateJson = null,
        List<string>? templateTrail = null,
        List<TransferCategoryDto>? categories = null)
    {
        return new WorkflowTransferBundle(
            formatVersion,
            ORIGIN,
            "Imported",
            null,
            DateTime.UtcNow,
            USER,
            templateJson ?? PlainBundleTemplate(harness),
            templateTrail ?? ["tpl-bundle"],
            [.. components],
            categories ?? []);
    }

    private static Workflow BuildPlainWorkflow()
    {
        var workflow = new Workflow("Report") { Title = "Report" };
        _ = new Section("Body", workflow) { Title = "Body" };

        return workflow;
    }

    private static string AgentBundleTemplate(SharedComponentsTestHarness harness)
    {
        var workflow = new Workflow("Imported") { Title = "Imported" };
        var section = new Section("Body", workflow) { Title = "Body" };
        _ = AgentTestData.AddAgent(section, "extraction_agent");

        return harness.Canonicalizer.ToCanonicalJson(workflow);
    }

    private static string PlainBundleTemplate(SharedComponentsTestHarness harness)
    {
        var workflow = new Workflow("Imported") { Title = "Imported" };
        var section = new Section("Body", workflow) { Title = "Body" };
        _ = new Block(BlockTypes.AiQuery, section, "b1")
        {
            Name = "Node",
            Properties = new AiQueryProperties { UserPromptTemplate = "?" },
        };

        return harness.Canonicalizer.ToCanonicalJson(workflow);
    }

    private static string LinkingBundleTemplate(SharedComponentsTestHarness harness, string componentId, string versionHash)
    {
        var workflow = new Workflow("Imported") { Title = "Imported" };
        var section = new Section("Body", workflow) { Title = "Body" };
        var block = new Block(BlockTypes.AiQuery, section, "b1")
        {
            Name = "Node",
            Properties = new AiQueryProperties { UserPromptTemplate = "?" },
        };
        block.SharedComponentId = componentId;
        block.SharedComponentLinkState = LinkState.Linked;
        block.SharedComponentVersionHash = versionHash;
        block.SharedComponentBindings = new Dictionary<string, string>();

        return harness.Canonicalizer.ToCanonicalJson(workflow);
    }

    private static async Task<List<SharedComponentVersion>> ChainAsync(SharedComponentsTestHarness harness, string componentId)
    {
        return await harness.Context.SharedComponentVersions
            .Where(v => v.SharedComponentId == componentId)
            .ToListAsync(TestContext.Current.CancellationToken);
    }

    private static async Task<SharedComponentEntityAssert> LoadComponentAsync(SharedComponentsTestHarness harness, string componentId)
    {
        SharedComponent component = await harness.Context.SharedComponents
            .SingleAsync(c => c.Id == componentId, TestContext.Current.CancellationToken);
        SharedComponentVersion head = (await ChainAsync(harness, componentId))
            .OrderByDescending(v => v.SequenceNumber)
            .First();

        return new SharedComponentEntityAssert(component.State, head.ContentHash, head.SequenceNumber);
    }

    private static async Task<Workflow> LoadTemplateHeadAsync(SharedComponentsTestHarness harness, string versionId)
    {
        WorkflowVersion version = await harness.Context.WorkflowVersions
            .SingleAsync(v => v.Id == versionId, TestContext.Current.CancellationToken);

        return Workflow.FromJson(version.WorkflowJson)!;
    }

    private sealed record SharedComponentEntityAssert(DomainState State, string HeadHash, int HeadSequenceNumber);
}
