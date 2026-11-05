using ErrorOr;
using IniaAi.Application.Commands.Workflows.SaveWorkflow;
using IniaAi.Application.Common.Signatures;
using IniaAi.Configuration.Platform;
using IniaAi.Core.Workflows.Diff;
using IniaAi.Domain.Core.Interfaces;
using IniaAi.Domain.Entities.SharedComponents.Enums;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Blocks;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties;
using IniaAi.Domain.Entities.Workflows.Enums;
using IniaAi.Domain.Persistence.Core.Repositories;
using IniaAi.Domain.Serialization;
using IniaAi.Infrastructure.Persistence.Core;
using IniaAi.Infrastructure.Persistence.Core.Contexts;
using IniaAi.Infrastructure.Persistence.Core.Repositories;
using IniaAi.Infrastructure.Persistence.Core.UnitOfWork;
using IniaAi.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using IniaAi.Testing.DependencyInjection;

namespace IniaAi.Application.UnitTests.Commands.Workflows.SaveWorkflow;

public class SaveWorkflowCommandHandlerTests
{
    private const string ORG = "org-test";
    private const string OTHER_ORG = "org-other";
    private const string USER = "user-test";
    private const string TYPE = "WT-Report";

    [Fact]
    public async Task FirstSave_CreatesVersion_NullParent()
    {
        using var harness = new CoreHarness();
        SaveWorkflowCommandHandler handler = CreateHandler(harness);

        ErrorOr<SaveWorkflowResult> result = await handler.Handle(
            new SaveWorkflowCommand(ORG, USER, TYPE, BuildWorkflow()),
            TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        Assert.False(result.Value.WasDeduped);

        List<WorkflowVersion> versions = await harness.Context.WorkflowVersions
            .ToListAsync(TestContext.Current.CancellationToken);
        WorkflowVersion only = Assert.Single(versions);
        Assert.Null(only.ParentVersionId);
        Assert.Equal(result.Value.VersionId, only.Id);
        Assert.Equal(USER, only.CreatedByUserId);
        Assert.Equal(TYPE, only.WorkflowTypeId);
    }

    [Fact]
    public async Task SecondChangedSave_NewVersion_ParentIsPriorHead()
    {
        using var harness = new CoreHarness();
        SaveWorkflowCommandHandler handler = CreateHandler(harness);

        ErrorOr<SaveWorkflowResult> first = await handler.Handle(
            new SaveWorkflowCommand(ORG, USER, TYPE, BuildWorkflow(prompt: "first")),
            TestContext.Current.CancellationToken);
        ErrorOr<SaveWorkflowResult> second = await handler.Handle(
            new SaveWorkflowCommand(ORG, USER, TYPE, BuildWorkflow(prompt: "second changed")),
            TestContext.Current.CancellationToken);

        Assert.False(second.Value.WasDeduped);

        List<WorkflowVersion> versions = await harness.Context.WorkflowVersions
            .ToListAsync(TestContext.Current.CancellationToken);
        Assert.Equal(2, versions.Count);

        WorkflowVersion head = versions.Single(v => v.Id == second.Value.VersionId);
        Assert.Equal(first.Value.VersionId, head.ParentVersionId);
    }

    [Fact]
    public async Task UnchangedSave_Deduped_NoNewRow()
    {
        using var harness = new CoreHarness();
        SaveWorkflowCommandHandler handler = CreateHandler(harness);

        ErrorOr<SaveWorkflowResult> first = await handler.Handle(
            new SaveWorkflowCommand(ORG, USER, TYPE, BuildWorkflow()),
            TestContext.Current.CancellationToken);
        ErrorOr<SaveWorkflowResult> second = await handler.Handle(
            new SaveWorkflowCommand(ORG, USER, TYPE, await LoadHeadRoundTripAsync(harness)),
            TestContext.Current.CancellationToken);

        Assert.True(second.Value.WasDeduped);
        Assert.Equal(first.Value.VersionId, second.Value.VersionId);

        List<WorkflowVersion> versions = await harness.Context.WorkflowVersions
            .ToListAsync(TestContext.Current.CancellationToken);
        _ = Assert.Single(versions);
    }

    [Fact]
    public async Task Save_IdenticalContentIndependentlyRebuilt_AdoptsIdsAndDedupes()
    {
        using var harness = new CoreHarness();
        SaveWorkflowCommandHandler handler = CreateHandler(harness);

        ErrorOr<SaveWorkflowResult> first = await handler.Handle(
            new SaveWorkflowCommand(ORG, USER, TYPE, BuildWorkflow()),
            TestContext.Current.CancellationToken);
        ErrorOr<SaveWorkflowResult> second = await handler.Handle(
            new SaveWorkflowCommand(ORG, USER, TYPE, BuildWorkflow()),
            TestContext.Current.CancellationToken);

        Assert.True(second.Value.WasDeduped);
        Assert.Equal(first.Value.VersionId, second.Value.VersionId);
        Assert.Equal(first.Value.ContentHash, second.Value.ContentHash);

        List<WorkflowVersion> versions = await harness.Context.WorkflowVersions
            .ToListAsync(TestContext.Current.CancellationToken);
        _ = Assert.Single(versions);
    }

    [Fact]
    public async Task ChangedSave_AfterDedupe_InsertsAgain_ParentIsHead()
    {
        using var harness = new CoreHarness();
        SaveWorkflowCommandHandler handler = CreateHandler(harness);

        ErrorOr<SaveWorkflowResult> first = await handler.Handle(
            new SaveWorkflowCommand(ORG, USER, TYPE, BuildWorkflow()),
            TestContext.Current.CancellationToken);
        ErrorOr<SaveWorkflowResult> deduped = await handler.Handle(
            new SaveWorkflowCommand(ORG, USER, TYPE, await LoadHeadRoundTripAsync(harness)),
            TestContext.Current.CancellationToken);
        ErrorOr<SaveWorkflowResult> changed = await handler.Handle(
            new SaveWorkflowCommand(ORG, USER, TYPE, BuildWorkflow(prompt: "changed")),
            TestContext.Current.CancellationToken);

        Assert.True(deduped.Value.WasDeduped);
        Assert.False(changed.Value.WasDeduped);

        List<WorkflowVersion> versions = await harness.Context.WorkflowVersions
            .ToListAsync(TestContext.Current.CancellationToken);
        Assert.Equal(2, versions.Count);

        WorkflowVersion head = versions.Single(v => v.Id == changed.Value.VersionId);
        Assert.Equal(first.Value.VersionId, head.ParentVersionId);
    }

    [Fact]
    public async Task RestoreThenSaveUnchanged_EqualsHead_Deduped()
    {
        using var harness = new CoreHarness();
        SaveWorkflowCommandHandler handler = CreateHandler(harness);

        ErrorOr<SaveWorkflowResult> first = await handler.Handle(
            new SaveWorkflowCommand(ORG, USER, TYPE, BuildWorkflow()),
            TestContext.Current.CancellationToken);

        WorkflowVersion head = await harness.Context.WorkflowVersions
            .SingleAsync(TestContext.Current.CancellationToken);
        Workflow restored = Workflow.FromJson(head.WorkflowJson)!;

        ErrorOr<SaveWorkflowResult> resaved = await handler.Handle(
            new SaveWorkflowCommand(ORG, USER, TYPE, restored),
            TestContext.Current.CancellationToken);

        Assert.True(resaved.Value.WasDeduped);
        Assert.Equal(first.Value.VersionId, resaved.Value.VersionId);

        List<WorkflowVersion> versions = await harness.Context.WorkflowVersions
            .ToListAsync(TestContext.Current.CancellationToken);
        _ = Assert.Single(versions);
    }

    [Fact]
    public async Task LinkOnlyChange_AppendsVersion_WithUnchangedContentHash()
    {
        using var harness = new CoreHarness();
        SaveWorkflowCommandHandler handler = CreateHandler(harness);

        ErrorOr<SaveWorkflowResult> first = await handler.Handle(
            new SaveWorkflowCommand(ORG, USER, TYPE, BuildWorkflow()),
            TestContext.Current.CancellationToken);

        Workflow linked = BuildWorkflow();
        Block block = linked.Sections.Single().Blocks.Single();
        block.SharedComponentId = "component";
        block.SharedComponentLinkState = SharedComponentLinkState.Linked;
        block.SharedComponentVersionHash = new string('a', 64);
        block.SharedComponentBindings = new Dictionary<string, string> { ["slot"] = "block1" };

        ErrorOr<SaveWorkflowResult> second = await handler.Handle(
            new SaveWorkflowCommand(ORG, USER, TYPE, linked),
            TestContext.Current.CancellationToken);

        Assert.False(second.Value.WasDeduped);

        List<WorkflowVersion> versions = await harness.Context.WorkflowVersions
            .ToListAsync(TestContext.Current.CancellationToken);
        Assert.Equal(2, versions.Count);

        WorkflowVersion head = versions.Single(v => v.Id == second.Value.VersionId);
        WorkflowVersion parent = versions.Single(v => v.Id == first.Value.VersionId);
        Assert.Equal(parent.ContentHash, head.ContentHash);
        Assert.Equal(parent.Id, head.ParentVersionId);
        Assert.Contains("\"SharedComponentId\"", head.WorkflowJson);
        Assert.Contains("\"SharedComponentBindings\"", head.WorkflowJson);
    }

    [Fact]
    public async Task TrimmedPriorHead_IdenticalContent_AppendsInsteadOfDeduping()
    {
        using var harness = new CoreHarness();
        SaveWorkflowCommandHandler handler = CreateHandler(harness);

        _ = await handler.Handle(
            new SaveWorkflowCommand(ORG, USER, TYPE, BuildWorkflow()),
            TestContext.Current.CancellationToken);

        WorkflowVersion trimmedHead = await harness.Context.WorkflowVersions
            .SingleAsync(TestContext.Current.CancellationToken);
        trimmedHead.WorkflowJsonCompressed = null;
        trimmedHead.PayloadTrimmedAt = DateTime.UtcNow;
        _ = await harness.Context.SaveChangesAsync(TestContext.Current.CancellationToken);

        ErrorOr<SaveWorkflowResult> second = await handler.Handle(
            new SaveWorkflowCommand(ORG, USER, TYPE, BuildWorkflow()),
            TestContext.Current.CancellationToken);

        Assert.False(second.Value.WasDeduped);

        List<WorkflowVersion> versions = await harness.Context.WorkflowVersions
            .ToListAsync(TestContext.Current.CancellationToken);
        Assert.Equal(2, versions.Count);

        WorkflowVersion appended = versions.Single(v => v.Id == second.Value.VersionId);
        Assert.Equal(trimmedHead.ContentHash, appended.ContentHash);
        Assert.Equal(trimmedHead.Id, appended.ParentVersionId);
    }

    [Fact]
    public void Validator_RejectsEmptyOrgUserType()
    {
        var validator = new SaveWorkflowCommandValidator(Options.Create(new PlatformSettings()));

        Assert.False(validator.Validate(new SaveWorkflowCommand("", USER, TYPE, BuildWorkflow())).IsValid);
        Assert.False(validator.Validate(new SaveWorkflowCommand(ORG, "", TYPE, BuildWorkflow())).IsValid);
        Assert.False(validator.Validate(new SaveWorkflowCommand(ORG, USER, "", BuildWorkflow())).IsValid);
        Assert.True(validator.Validate(new SaveWorkflowCommand(ORG, USER, TYPE, BuildWorkflow())).IsValid);
    }

    [Fact]
    public async Task FirstSave_PersistsFirstVersionNarrative()
    {
        using var harness = new CoreHarness();
        SaveWorkflowCommandHandler handler = CreateHandler(harness);

        _ = await handler.Handle(
            new SaveWorkflowCommand(ORG, USER, TYPE, BuildWorkflow()),
            TestContext.Current.CancellationToken);

        WorkflowVersion version = await harness.Context.WorkflowVersions
            .SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal(WorkflowNarrativeGenerator.FirstVersionSummary, version.AutoNarrativeSummary);
    }

    [Fact]
    public async Task ChangedSave_PersistsNarrativeDescribingChange()
    {
        using var harness = new CoreHarness();
        SaveWorkflowCommandHandler handler = CreateHandler(harness);

        _ = await handler.Handle(
            new SaveWorkflowCommand(ORG, USER, TYPE, BuildWorkflow(prompt: "first")),
            TestContext.Current.CancellationToken);
        ErrorOr<SaveWorkflowResult> second = await handler.Handle(
            new SaveWorkflowCommand(ORG, USER, TYPE, BuildWorkflow(prompt: "a second, changed prompt")),
            TestContext.Current.CancellationToken);

        WorkflowVersion head = await harness.Context.WorkflowVersions
            .SingleAsync(v => v.Id == second.Value.VersionId, TestContext.Current.CancellationToken);
        Assert.False(string.IsNullOrWhiteSpace(head.AutoNarrativeSummary));
        Assert.Contains("prompt", head.AutoNarrativeSummary!);
    }

    [Fact]
    public async Task EnforcedSave_StaleBaseVersion_Conflict_NothingPersisted()
    {
        using var harness = new CoreHarness();
        SaveWorkflowCommandHandler handler = CreateHandler(harness);

        ErrorOr<SaveWorkflowResult> first = await handler.Handle(
            new SaveWorkflowCommand(ORG, USER, TYPE, BuildWorkflow()),
            TestContext.Current.CancellationToken);

        ErrorOr<SaveWorkflowResult> stale = await handler.Handle(
            new SaveWorkflowCommand(ORG, USER, TYPE, BuildWorkflow(prompt: "changed"),
                BaseVersionId: "not-the-head", EnforceOptimisticConcurrency: true),
            TestContext.Current.CancellationToken);

        Assert.True(stale.IsError);
        Assert.Equal(ErrorType.Conflict, stale.FirstError.Type);
        Assert.NotNull(stale.FirstError.Metadata);
        Assert.Equal(first.Value.VersionId, stale.FirstError.Metadata!["currentHeadVersionId"]);
        // The recovery fields must be present; the editor's user id is deliberately omitted.
        Assert.True(stale.FirstError.Metadata.ContainsKey("currentContentHash"));
        Assert.True(stale.FirstError.Metadata.ContainsKey("headCreatedAtUtc"));
        Assert.False(stale.FirstError.Metadata.ContainsKey("headCreatedByUserId"));

        List<WorkflowVersion> versions = await harness.Context.WorkflowVersions
            .ToListAsync(TestContext.Current.CancellationToken);
        _ = Assert.Single(versions);
    }

    [Fact]
    public async Task EnforcedSave_MatchingBaseVersion_Saves()
    {
        using var harness = new CoreHarness();
        SaveWorkflowCommandHandler handler = CreateHandler(harness);

        ErrorOr<SaveWorkflowResult> first = await handler.Handle(
            new SaveWorkflowCommand(ORG, USER, TYPE, BuildWorkflow()),
            TestContext.Current.CancellationToken);

        ErrorOr<SaveWorkflowResult> second = await handler.Handle(
            new SaveWorkflowCommand(ORG, USER, TYPE, BuildWorkflow(prompt: "changed"),
                BaseVersionId: first.Value.VersionId, EnforceOptimisticConcurrency: true),
            TestContext.Current.CancellationToken);

        Assert.False(second.IsError);
        Assert.False(second.Value.WasDeduped);

        List<WorkflowVersion> versions = await harness.Context.WorkflowVersions
            .ToListAsync(TestContext.Current.CancellationToken);
        Assert.Equal(2, versions.Count);
    }

    [Fact]
    public async Task EnforcedSave_NullBase_WithExistingHead_Conflict()
    {
        using var harness = new CoreHarness();
        SaveWorkflowCommandHandler handler = CreateHandler(harness);

        _ = await handler.Handle(
            new SaveWorkflowCommand(ORG, USER, TYPE, BuildWorkflow()),
            TestContext.Current.CancellationToken);

        ErrorOr<SaveWorkflowResult> result = await handler.Handle(
            new SaveWorkflowCommand(ORG, USER, TYPE, BuildWorkflow(prompt: "changed"),
                BaseVersionId: null, EnforceOptimisticConcurrency: true),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsError);
        Assert.Equal(ErrorType.Conflict, result.FirstError.Type);
    }

    [Fact]
    public async Task EnforcedSave_NullBase_NoHead_Saves()
    {
        using var harness = new CoreHarness();
        SaveWorkflowCommandHandler handler = CreateHandler(harness);

        ErrorOr<SaveWorkflowResult> result = await handler.Handle(
            new SaveWorkflowCommand(ORG, USER, TYPE, BuildWorkflow(),
                BaseVersionId: null, EnforceOptimisticConcurrency: true),
            TestContext.Current.CancellationToken);

        Assert.False(result.IsError);

        List<WorkflowVersion> versions = await harness.Context.WorkflowVersions
            .ToListAsync(TestContext.Current.CancellationToken);
        _ = Assert.Single(versions);
    }

    [Fact]
    public async Task EnforcedSave_NonNullBase_NoHead_Conflict()
    {
        using var harness = new CoreHarness();
        SaveWorkflowCommandHandler handler = CreateHandler(harness);

        ErrorOr<SaveWorkflowResult> result = await handler.Handle(
            new SaveWorkflowCommand(ORG, USER, TYPE, BuildWorkflow(),
                BaseVersionId: "phantom-version", EnforceOptimisticConcurrency: true),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsError);
        Assert.Equal(ErrorType.Conflict, result.FirstError.Type);

        List<WorkflowVersion> versions = await harness.Context.WorkflowVersions
            .ToListAsync(TestContext.Current.CancellationToken);
        Assert.Empty(versions);
    }

    [Fact]
    public async Task EnforcedSave_NullBase_HeadFromAnotherOrg_Conflict()
    {
        using var harness = new CoreHarness();
        SaveWorkflowCommandHandler handler = CreateHandler(harness);

        _ = await handler.Handle(
            new SaveWorkflowCommand(ORG, USER, TYPE, BuildWorkflow()),
            TestContext.Current.CancellationToken);

        ErrorOr<SaveWorkflowResult> otherOrgSave = await handler.Handle(
            new SaveWorkflowCommand(OTHER_ORG, USER, TYPE, BuildWorkflow(prompt: "changed by another org"),
                BaseVersionId: null, EnforceOptimisticConcurrency: true),
            TestContext.Current.CancellationToken);

        Assert.True(otherOrgSave.IsError);
        Assert.Equal(ErrorType.Conflict, otherOrgSave.FirstError.Type);

        List<WorkflowVersion> versions = await harness.Context.WorkflowVersions
            .ToListAsync(TestContext.Current.CancellationToken);
        _ = Assert.Single(versions);
    }

    [Fact]
    public async Task Save_ContentIdenticalToOtherOrgsHead_Deduped_NoNewRow()
    {
        using var harness = new CoreHarness();
        SaveWorkflowCommandHandler handler = CreateHandler(harness);

        ErrorOr<SaveWorkflowResult> first = await handler.Handle(
            new SaveWorkflowCommand(ORG, USER, TYPE, BuildWorkflow()),
            TestContext.Current.CancellationToken);

        ErrorOr<SaveWorkflowResult> second = await handler.Handle(
            new SaveWorkflowCommand(OTHER_ORG, USER, TYPE, await LoadHeadRoundTripAsync(harness)),
            TestContext.Current.CancellationToken);

        Assert.True(second.Value.WasDeduped);
        Assert.Equal(first.Value.VersionId, second.Value.VersionId);

        List<WorkflowVersion> versions = await harness.Context.WorkflowVersions
            .ToListAsync(TestContext.Current.CancellationToken);
        _ = Assert.Single(versions);
    }

    [Fact]
    public async Task CrossWorkflowSectionCopy_SavedThroughHandler_AppendsVersionAttributedToCopier()
    {
        using var harness = new CoreHarness();
        SaveWorkflowCommandHandler handler = CreateHandler(harness);
        const string copier = "user-copier";

        ErrorOr<SaveWorkflowResult> first = await handler.Handle(
            new SaveWorkflowCommand(ORG, USER, TYPE, BuildWorkflow()),
            TestContext.Current.CancellationToken);

        var sourceWorkflow = new Workflow("Source") { Title = "Source" };
        var sourceSection = new Section("Risk Factors", sourceWorkflow);
        _ = new Block(BlockTypes.AiQuery, sourceSection, "riskBlock")
        {
            Name = "Risk summary",
            Properties = new AiQueryProperties { UserPromptTemplate = "List the risk factors." },
        };

        Workflow? target = await harness.UnitOfWork.Workflows.GetByTypeIdAsNoTrackingAsync(TYPE);
        Assert.NotNull(target);
        _ = sourceSection.CloneToWorkflow(target, 1);

        ErrorOr<SaveWorkflowResult> copySave = await handler.Handle(
            new SaveWorkflowCommand(ORG, copier, TYPE, target),
            TestContext.Current.CancellationToken);

        Assert.False(copySave.IsError);
        Assert.False(copySave.Value.WasDeduped);

        List<WorkflowVersion> versions = await harness.Context.WorkflowVersions
            .ToListAsync(TestContext.Current.CancellationToken);
        Assert.Equal(2, versions.Count);

        WorkflowVersion head = versions.Single(v => v.Id == copySave.Value.VersionId);
        Assert.Equal(first.Value.VersionId, head.ParentVersionId);
        Assert.Equal(copier, head.CreatedByUserId);
        Assert.Contains("Risk Factors", head.AutoNarrativeSummary);

        Workflow? persisted = await harness.UnitOfWork.Workflows.GetByTypeIdAsNoTrackingAsync(TYPE);
        Assert.NotNull(persisted);
        Assert.Equal(2, persisted.Sections.Count);
    }

    [Fact]
    public async Task CrossWorkflowBlockCopy_SavedThroughHandler_AppendsVersionAttributedToCopier()
    {
        using var harness = new CoreHarness();
        SaveWorkflowCommandHandler handler = CreateHandler(harness);
        const string copier = "user-copier";

        ErrorOr<SaveWorkflowResult> first = await handler.Handle(
            new SaveWorkflowCommand(ORG, USER, TYPE, BuildWorkflow()),
            TestContext.Current.CancellationToken);

        var sourceWorkflow = new Workflow("Source") { Title = "Source" };
        var sourceSection = new Section("Source Body", sourceWorkflow);
        var sourceBlock = new Block(BlockTypes.AiQuery, sourceSection, "litigationBlock")
        {
            Name = "Litigation check",
            Properties = new AiQueryProperties { UserPromptTemplate = "Any ongoing litigation?" },
        };

        Workflow? target = await harness.UnitOfWork.Workflows.GetByTypeIdAsNoTrackingAsync(TYPE);
        Assert.NotNull(target);
        Section targetSection = Assert.Single(target.Sections);
        sourceBlock.CloneToSection(targetSection, 1);
        targetSection.NormalizeBlockOrdering();

        ErrorOr<SaveWorkflowResult> copySave = await handler.Handle(
            new SaveWorkflowCommand(ORG, copier, TYPE, target),
            TestContext.Current.CancellationToken);

        Assert.False(copySave.IsError);
        Assert.False(copySave.Value.WasDeduped);

        List<WorkflowVersion> versions = await harness.Context.WorkflowVersions
            .ToListAsync(TestContext.Current.CancellationToken);
        Assert.Equal(2, versions.Count);

        WorkflowVersion head = versions.Single(v => v.Id == copySave.Value.VersionId);
        Assert.Equal(first.Value.VersionId, head.ParentVersionId);
        Assert.Equal(copier, head.CreatedByUserId);
        Assert.Contains("Litigation check", head.AutoNarrativeSummary);

        Workflow? persisted = await harness.UnitOfWork.Workflows.GetByTypeIdAsNoTrackingAsync(TYPE);
        Assert.NotNull(persisted);
        Assert.Equal(2, Assert.Single(persisted.Sections).Blocks.Count);
    }

    [Fact]
    public async Task Save_BackfillsMissingStableSectionIds()
    {
        using var harness = new CoreHarness();
        SaveWorkflowCommandHandler handler = CreateHandler(harness);

        Workflow workflow = BuildWorkflow();
        Section section = Assert.Single(workflow.Sections);
        section.StableSectionId = null;

        ErrorOr<SaveWorkflowResult> result = await handler.Handle(
            new SaveWorkflowCommand(ORG, USER, TYPE, workflow),
            TestContext.Current.CancellationToken);

        Assert.False(result.IsError);

        Workflow? persisted = await harness.UnitOfWork.Workflows.GetByTypeIdAsNoTrackingAsync(TYPE);
        Assert.NotNull(persisted);
        Assert.False(string.IsNullOrEmpty(Assert.Single(persisted.Sections).StableSectionId));
    }

    [Fact]
    public async Task Save_ReMintsDuplicateStableSectionIds_FirstKeepsId()
    {
        using var harness = new CoreHarness();
        SaveWorkflowCommandHandler handler = CreateHandler(harness);

        Workflow workflow = BuildWorkflow();
        var secondSection = new Section("Body 2", workflow);
        _ = new Block(BlockTypes.AiQuery, secondSection, "block2")
        {
            Name = "Second",
            Properties = new AiQueryProperties { UserPromptTemplate = "Second prompt" },
        };
        const string DUPLICATED_ID = "duplicated-section-id";
        foreach (Section section in workflow.Sections)
        {
            section.StableSectionId = DUPLICATED_ID;
        }

        ErrorOr<SaveWorkflowResult> result = await handler.Handle(
            new SaveWorkflowCommand(ORG, USER, TYPE, workflow),
            TestContext.Current.CancellationToken);

        Assert.False(result.IsError);

        Workflow? persisted = await harness.UnitOfWork.Workflows.GetByTypeIdAsNoTrackingAsync(TYPE);
        Assert.NotNull(persisted);
        List<string?> ids = [.. persisted.Sections.OrderBy(s => s.Order).Select(s => s.StableSectionId)];
        Assert.Equal(2, ids.Count);
        Assert.Equal(DUPLICATED_ID, ids[0]);
        Assert.False(string.IsNullOrEmpty(ids[1]));
        Assert.NotEqual(DUPLICATED_ID, ids[1]);
    }

    [Fact]
    public void IsVersionConflict_VersionConflictCode_True()
    {
        Error error = Error.Conflict(SaveWorkflowCommandHandler.VERSION_CONFLICT_CODE, "stale base version");

        Assert.True(SaveWorkflowCommandHandler.IsVersionConflict(error));
    }

    [Fact]
    public void IsVersionConflict_ConcurrentSaveCode_True()
    {
        Error error = Error.Conflict(SaveWorkflowCommandHandler.CONCURRENT_SAVE_CODE, "concurrent save");

        Assert.True(SaveWorkflowCommandHandler.IsVersionConflict(error));
    }

    [Fact]
    public void IsVersionConflict_OtherCode_False()
    {
        Error error = Error.Failure("General.Failure", "unrelated failure");

        Assert.False(SaveWorkflowCommandHandler.IsVersionConflict(error));
    }

    [Fact]
    public void GetCurrentHeadVersionId_HeadMetadataPresent_ReturnsHeadId()
    {
        Error error = Error.Conflict(
            SaveWorkflowCommandHandler.VERSION_CONFLICT_CODE,
            "stale base version",
            new Dictionary<string, object>
            {
                [SaveWorkflowCommandHandler.CURRENT_HEAD_VERSION_ID_METADATA_KEY] = "head-123",
            });

        Assert.Equal("head-123", SaveWorkflowCommandHandler.GetCurrentHeadVersionId(error));
    }

    [Fact]
    public void GetCurrentHeadVersionId_NoMetadata_ReturnsNull()
    {
        Error error = Error.Conflict(SaveWorkflowCommandHandler.VERSION_CONFLICT_CODE, "stale base version");

        Assert.Null(SaveWorkflowCommandHandler.GetCurrentHeadVersionId(error));
    }

    [Fact]
    public void GetCurrentHeadVersionId_KeyMissing_ReturnsNull()
    {
        Error error = Error.Conflict(
            SaveWorkflowCommandHandler.VERSION_CONFLICT_CODE,
            "stale base version",
            new Dictionary<string, object> { ["currentContentHash"] = "abc" });

        Assert.Null(SaveWorkflowCommandHandler.GetCurrentHeadVersionId(error));
    }

    private static async Task<Workflow> LoadHeadRoundTripAsync(CoreHarness harness)
    {
        WorkflowVersion head = await harness.Context.WorkflowVersions
            .SingleAsync(TestContext.Current.CancellationToken);

        return Workflow.FromJson(head.WorkflowJson)!;
    }

    private static SaveWorkflowCommandHandler CreateHandler(CoreHarness harness)
    {
        return new SaveWorkflowCommandHandler(
            new WorkflowSavePipeline(
                harness.UnitOfWork,
                harness.Context,
                new WorkflowCanonicalizer(),
                new WorkflowDiffService(),
                new WorkflowNarrativeGenerator(),
                Options.Create(new AgentRuntimeSettings()),
                NullLogger<WorkflowSavePipeline>.Instance, SuperAdminLookups.OverEmptyUserData()),
            harness.Context,
            NullLogger<SaveWorkflowCommandHandler>.Instance);
    }

    [Fact]
    public async Task Save_PayloadWithoutStableIds_AdoptsPriorStableIds()
    {
        using var harness = new CoreHarness();
        SaveWorkflowCommandHandler handler = CreateHandler(harness);

        Workflow first = BuildWorkflow();
        _ = await handler.Handle(
            new SaveWorkflowCommand(ORG, USER, TYPE, first),
            TestContext.Current.CancellationToken);
        string originalBlockId = first.Sections.Single().Blocks.Single().StableBlockId!;
        string originalSectionId = first.Sections.Single().StableSectionId!;

        // A client that round-tripped the content API before stable ids were part of the payload
        // sends the workflow back without them (and without entity Ids).
        Workflow second = BuildWorkflow(prompt: "changed prompt");
        foreach (Block block in second.GetAllBlocks())
        {
            block.StableBlockId = null;
        }

        foreach (Section section in second.Sections)
        {
            section.StableSectionId = null;
        }

        _ = await handler.Handle(
            new SaveWorkflowCommand(ORG, USER, TYPE, second),
            TestContext.Current.CancellationToken);

        Workflow saved = await harness.Context.Workflows
            .Include(w => w.Sections)
            .ThenInclude(s => s.Blocks)
            .SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal(originalBlockId, saved.Sections.Single().Blocks.Single().StableBlockId);
        Assert.Equal(originalSectionId, saved.Sections.Single().StableSectionId);
    }

    [Fact]
    public async Task Save_FreshlyMintedStableIds_SameStructure_AdoptsPriorStableIds()
    {
        using var harness = new CoreHarness();
        SaveWorkflowCommandHandler handler = CreateHandler(harness);

        Workflow first = BuildWorkflow();
        _ = await handler.Handle(
            new SaveWorkflowCommand(ORG, USER, TYPE, first),
            TestContext.Current.CancellationToken);
        string originalBlockId = first.Sections.Single().Blocks.Single().StableBlockId!;

        // Rebuilding the same structure mints fresh ids (block constructors always mint); the save
        // must keep the row's established identity, or baselines break on every such save.
        Workflow second = BuildWorkflow(prompt: "changed prompt");

        _ = await handler.Handle(
            new SaveWorkflowCommand(ORG, USER, TYPE, second),
            TestContext.Current.CancellationToken);

        Workflow saved = await harness.Context.Workflows
            .Include(w => w.Sections)
            .ThenInclude(s => s.Blocks)
            .SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal(originalBlockId, saved.Sections.Single().Blocks.Single().StableBlockId);
    }

    private static Workflow BuildWorkflow(string prompt = "Who is the counterparty?")
    {
        var workflow = new Workflow("Report") { Title = "Report" };
        var section = new Section("Body", workflow);
        _ = new Block(BlockTypes.AiQuery, section, "block1")
        {
            Name = "Counterparty",
            Properties = new AiQueryProperties { UserPromptTemplate = prompt },
        };

        return workflow;
    }

    private sealed class CoreHarness : IDisposable
    {
        public CoreDbContext Context { get; }
        public ICoreUnitOfWork UnitOfWork { get; }

        private readonly Microsoft.Extensions.DependencyInjection.ServiceProvider _provider;

        public CoreHarness()
        {
            DbContextOptions<CoreDbContext> options = new DbContextOptionsBuilder<CoreDbContext>()
                .UseInMemoryDatabase($"savewf-{Guid.NewGuid()}")
                .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
                .Options;
            Context = new CoreDbContext(options);

            var services = new ServiceCollection();
            _ = services.AddScoped(typeof(ICoreRepository<>), typeof(CoreRepositoryWrapper<>));
            _ = services.AddScoped<CoreDbContext>(_ => Context);
            _ = services.AddScoped<ICoreWorkflowRepository, CoreWorkflowRepository>();
            _ = services.AddScoped<IToolsSettingsRepository, ToolsSettingsRepository>();
            _provider = services.BuildServiceProvider();

            UnitOfWork = new CoreUnitOfWork(Context, _provider);
        }

        public void Dispose()
        {
            Context.Dispose();
            _provider.Dispose();
        }
    }
}
