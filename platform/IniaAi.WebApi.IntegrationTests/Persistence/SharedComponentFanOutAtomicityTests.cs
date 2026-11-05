using System.Data.Common;
using ErrorOr;
using IniaAi.Application.Commands.SharedComponents.PromoteSharedComponent;
using IniaAi.Application.Commands.SharedComponents.RetireSharedComponent;
using IniaAi.Application.Commands.Workflows.SaveWorkflow;
using IniaAi.Application.Common.Services;
using IniaAi.Application.Common.Signatures;
using IniaAi.Application.Common.Workflows;
using IniaAi.Configuration.Platform;
using IniaAi.Core.Workflows.Diff;
using IniaAi.Domain.Core.Interfaces;
using IniaAi.Domain.Entities.SharedComponents;
using IniaAi.Domain.Entities.SharedComponents.Enums;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Blocks;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties;
using IniaAi.Domain.Entities.Workflows.Enums;
using IniaAi.Domain.Persistence.Core.Repositories;
using IniaAi.Domain.Serialization;
using IniaAi.Domain.UserData.Interfaces;
using IniaAi.Infrastructure.Persistence.Core.Contexts;
using IniaAi.Infrastructure.Persistence.Core.Repositories;
using IniaAi.Infrastructure.Persistence.UserData.Contexts;
using IniaAi.Testing.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using IniaAi.Application.Commands.WorkflowTemplates.DuplicateWorkflowType;
using IniaAi.Application.Commands.WorkflowTransfer.ImportWorkflowTemplateApply;
using IniaAi.WebApi.Contracts.Dto.WorkflowTransfer;
using Arm = IniaAi.Domain.Entities.SharedComponents.Enums.SharedComponentTransferArm;
using WireKind = IniaAi.WebApi.Contracts.Enums.SharedComponentKind;

namespace IniaAi.WebApi.IntegrationTests.Persistence;

/// <summary>
/// Runtime proof, on a real database, that the shared-component write engine is all-or-nothing:
/// a failure anywhere inside the fan-out transaction leaves the component chain, every consumer
/// template, and their version histories exactly as they were, and the same service instance can
/// retry successfully afterwards. The InMemory suite cannot prove any of this — it has no real
/// transactions, no enforced unique indexes, and no aborted-transaction connection state (the
/// PostgreSQL 25P02 hazard the rollback-before-re-read ordering exists for).
/// </summary>
public abstract class SharedComponentFanOutAtomicityTests
{
    private const string ORG = "org-test";
    private const string USER = "user-test";
    private const string PLAIN_PROMPT = "Who is the counterparty?";

    private readonly ICoreDatabaseFixture _fixture;
    private readonly string _skipReason;

    protected SharedComponentFanOutAtomicityTests(ICoreDatabaseFixture fixture, string skipReason)
    {
        _fixture = fixture;
        _skipReason = skipReason;
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task AppendAndFanOut_RivalAppendWinsTheRace_RollsBackEverythingAndRetriesCleanly()
    {
        SkipUnlessAvailable();
        const string componentId = "component-race";
        const string typeId = "WT-Atomicity-Race";

        using var seedHarness = new AtomicityHarness(_fixture.Options);
        Seeded seeded = await SeedLinkedComponentAsync(seedHarness, componentId, typeId);

        (string rivalJson, string rivalHash) = Definition(seedHarness, $"A rival definition prompt for {componentId}.");
        string rivalVersionId = string.Empty;
        var tripwire = new CommandTripwire(
            text => ContainsInsertInto(text, "SharedComponentVersions"),
            () => rivalVersionId = InsertCommittedRivalVersion(componentId, seeded.HeadVersionId, rivalJson, rivalHash));

        using var sutHarness = new AtomicityHarness(_fixture.BuildOptions(tripwire));
        SharedComponentFanOutService service = sutHarness.CreateFanOutService();
        (string v2Json, string v2Hash) = Definition(sutHarness, PromptV2(componentId));

        ErrorOr<SharedComponentAppendResult> result = await service.AppendAndFanOutAsync(
            ORG, USER, componentId, v2Json, seeded.HeadVersionId, Ct);

        Assert.True(result.IsError);
        Assert.Equal(SharedComponentErrorCodes.CONCURRENT_APPEND, result.FirstError.Code);
        Assert.NotNull(result.FirstError.Metadata);
        await AssertComponentVersionCountAsync(componentId, 2);
        await AssertConsumerAsync(typeId, componentId, PromptV1(componentId), seeded.HeadContentHash);
        await AssertWorkflowVersionCountAsync(typeId, 1);

        ErrorOr<SharedComponentAppendResult> retry = await service.AppendAndFanOutAsync(
            ORG, USER, componentId, v2Json, rivalVersionId, Ct);

        Assert.False(retry.IsError);
        await AssertComponentVersionCountAsync(componentId, 3);
        await AssertConsumerAsync(typeId, componentId, PromptV2(componentId), v2Hash);
        await AssertWorkflowVersionCountAsync(typeId, 2);
    }

    [Fact]
    public async Task AppendAndFanOut_UnderRetryingExecutionStrategy_CommitsAndFansOut()
    {
        SkipUnlessAvailable();
        const string componentId = "component-retrying";
        const string typeId = "WT-Atomicity-Retrying";

        using var seedHarness = new AtomicityHarness(_fixture.Options);
        Seeded seeded = await SeedLinkedComponentAsync(seedHarness, componentId, typeId);

        using var sutHarness = new AtomicityHarness(_fixture.BuildRetryingOptions());
        SharedComponentFanOutService service = sutHarness.CreateFanOutService();
        (string v2Json, string v2Hash) = Definition(sutHarness, PromptV2(componentId));

        ErrorOr<SharedComponentAppendResult> result = await service.AppendAndFanOutAsync(
            ORG, USER, componentId, v2Json, seeded.HeadVersionId, Ct);

        Assert.False(result.IsError);
        await AssertComponentVersionCountAsync(componentId, 2);
        await AssertConsumerAsync(typeId, componentId, PromptV2(componentId), v2Hash);
        await AssertWorkflowVersionCountAsync(typeId, 2);
    }

    [Fact]
    public async Task Promote_UnderRetryingExecutionStrategy_CommitsComponentAndTemplateTogether()
    {
        SkipUnlessAvailable();
        const string typeId = "WT-Atomicity-Promote-Retrying";
        const string componentName = "Promoted retrying block";

        using var seedHarness = new AtomicityHarness(_fixture.Options);
        (_, SaveWorkflowCommandHandler save) = seedHarness.CreatePromoteHandler();
        string v1 = await seedHarness.SaveTemplateAsync(save, ORG, USER, typeId, BuildPlainWorkflow("Report"));

        using var sutHarness = new AtomicityHarness(_fixture.BuildRetryingOptions());
        (PromoteSharedComponentCommandHandler promote, _) = sutHarness.CreatePromoteHandler();

        ErrorOr<PromoteSharedComponentResult> result = await promote.Handle(
            new PromoteSharedComponentCommand(
                ORG, USER, typeId, "Sections[0].Blocks[0]", null, componentName, null, null, v1, null),
            Ct);

        Assert.False(result.IsError);
        (int components, int versions) = await CountComponentRowsByNameAsync(componentName);
        Assert.Equal(1, components);
        Assert.Equal(1, versions);
        await AssertWorkflowVersionCountAsync(typeId, 2);
    }

    [Fact]
    public async Task AppendAndFanOut_SecondConsumerSaveThrows_RollsBackTheFirstConsumerRewrite()
    {
        SkipUnlessAvailable();
        const string componentId = "component-midtxn";
        const string typeA = "WT-Atomicity-MidTxn-A";
        const string typeB = "WT-Atomicity-MidTxn-B";

        using var seedHarness = new AtomicityHarness(_fixture.Options);
        Seeded seeded = await SeedLinkedComponentAsync(seedHarness, componentId, typeA, typeB);

        int workflowVersionInserts = 0;
        var tripwire = new CommandTripwire(
            text => ContainsInsertInto(text, "WorkflowVersions") && ++workflowVersionInserts == 2,
            () => throw new InvalidOperationException("Injected mid-transaction failure after the first consumer save."));

        using var sutHarness = new AtomicityHarness(_fixture.BuildOptions(tripwire));
        SharedComponentFanOutService service = sutHarness.CreateFanOutService();
        (string v2Json, string v2Hash) = Definition(sutHarness, PromptV2(componentId));

        ErrorOr<SharedComponentAppendResult>? outcome = null;
        Exception? thrown = null;
        try
        {
            outcome = await service.AppendAndFanOutAsync(ORG, USER, componentId, v2Json, seeded.HeadVersionId, Ct);
        }
        catch (Exception ex)
        {
            thrown = ex;
        }

        Assert.True(
            thrown is not null || outcome!.Value.IsError,
            "the injected failure must surface as an exception or an error");
        await AssertComponentVersionCountAsync(componentId, 1);
        await AssertConsumerAsync(typeA, componentId, PromptV1(componentId), seeded.HeadContentHash);
        await AssertConsumerAsync(typeB, componentId, PromptV1(componentId), seeded.HeadContentHash);
        await AssertWorkflowVersionCountAsync(typeA, 1);
        await AssertWorkflowVersionCountAsync(typeB, 1);

        ErrorOr<SharedComponentAppendResult> retry = await service.AppendAndFanOutAsync(
            ORG, USER, componentId, v2Json, seeded.HeadVersionId, Ct);

        Assert.False(retry.IsError);
        Assert.Equal(2, retry.Value.ConsumerSaves.Count);
        await AssertComponentVersionCountAsync(componentId, 2);
        await AssertConsumerAsync(typeA, componentId, PromptV2(componentId), v2Hash);
        await AssertConsumerAsync(typeB, componentId, PromptV2(componentId), v2Hash);
        await AssertWorkflowVersionCountAsync(typeA, 2);
        await AssertWorkflowVersionCountAsync(typeB, 2);
    }

    [Fact]
    public async Task Promote_StaleTemplateBaseVersion_LeavesNoComponentOrVersionBehind()
    {
        SkipUnlessAvailable();
        const string typeId = "WT-Atomicity-Promote";
        const string componentName = "Promoted atomicity block";

        using var harness = new AtomicityHarness(_fixture.Options);
        (PromoteSharedComponentCommandHandler promote, SaveWorkflowCommandHandler save) = harness.CreatePromoteHandler();

        string v1 = await harness.SaveTemplateAsync(save, ORG, USER, typeId, BuildPlainWorkflow("Report"));
        ErrorOr<SaveWorkflowResult> rivalSave = await save.Handle(
            new SaveWorkflowCommand(ORG, USER, typeId, BuildPlainWorkflow("Report (rival edit)")), Ct);
        Assert.False(rivalSave.IsError);

        var staleCommand = new PromoteSharedComponentCommand(
            ORG, USER, typeId, "Sections[0].Blocks[0]", null, componentName, null, null, v1, null);
        ErrorOr<PromoteSharedComponentResult> result = await promote.Handle(staleCommand, Ct);

        Assert.True(result.IsError);
        (int components, int versions) = await CountComponentRowsByNameAsync(componentName);
        Assert.Equal(0, components);
        Assert.Equal(0, versions);
        await AssertWorkflowVersionCountAsync(typeId, 2);

        ErrorOr<PromoteSharedComponentResult> retry = await promote.Handle(
            staleCommand with { BaseVersionId = rivalSave.Value.VersionId }, Ct);

        Assert.False(retry.IsError);
        (components, versions) = await CountComponentRowsByNameAsync(componentName);
        Assert.Equal(1, components);
        Assert.Equal(1, versions);
        await AssertWorkflowVersionCountAsync(typeId, 3);
    }

    [Fact]
    public async Task Retire_SecondConsumerSaveThrows_RollsBackInlinedVersionRestampAndFlips()
    {
        SkipUnlessAvailable();
        const string componentId = "component-retire";
        const string containerId = "container-retire";
        const string directTypeId = "WT-Atomicity-Retire-A";
        const string containerTypeId = "WT-Atomicity-Retire-B";

        using var seedHarness = new AtomicityHarness(_fixture.Options);
        Seeded seeded = await SeedLinkedComponentAsync(seedHarness, componentId, directTypeId);
        (_, string containerHash) = await SeedContainerWithRefAsync(
            seedHarness, containerId, componentId, seeded.HeadContentHash, containerTypeId);

        int workflowVersionInserts = 0;
        var tripwire = new CommandTripwire(
            text => ContainsInsertInto(text, "WorkflowVersions") && ++workflowVersionInserts == 2,
            () => throw new InvalidOperationException("Injected mid-transaction failure after the first retire save."));

        using var sutHarness = new AtomicityHarness(_fixture.BuildOptions(tripwire));
        SharedComponentFanOutService service = sutHarness.CreateFanOutService();

        ErrorOr<SharedComponentRetireResult>? outcome = null;
        Exception? thrown = null;
        try
        {
            outcome = await service.RetireAndBreakOutAsync(ORG, USER, componentId, Ct);
        }
        catch (Exception ex)
        {
            thrown = ex;
        }

        Assert.True(
            thrown is not null || outcome!.Value.IsError,
            "the injected failure must surface as an exception or an error");
        await AssertComponentStateAsync(componentId, SharedComponentState.Active);
        await AssertComponentVersionCountAsync(containerId, 1);
        await AssertConsumerAsync(directTypeId, componentId, PromptV1(componentId), seeded.HeadContentHash);
        await AssertDirectCopyStateAsync(directTypeId, componentId, SharedComponentLinkState.Linked);
        await AssertContainerConsumerAsync(
            containerTypeId, containerId, containerHash, componentId, SharedComponentLinkState.Linked);
        await AssertWorkflowVersionCountAsync(directTypeId, 1);
        await AssertWorkflowVersionCountAsync(containerTypeId, 1);

        using var retryHarness = new AtomicityHarness(_fixture.Options);
        ErrorOr<SharedComponentRetireResult> retry = await retryHarness.CreateFanOutService()
            .RetireAndBreakOutAsync(ORG, USER, componentId, Ct);

        Assert.False(retry.IsError);
        Assert.Equal([containerId], retry.Value.InlinedContainerIds);
        await AssertComponentStateAsync(componentId, SharedComponentState.Retired);
        await AssertComponentVersionCountAsync(containerId, 2);
        string inlinedHash = await ReadHeadContentHashAsync(containerId);
        await AssertConsumerAsync(directTypeId, componentId, PromptV1(componentId), seeded.HeadContentHash);
        await AssertDirectCopyStateAsync(directTypeId, componentId, SharedComponentLinkState.BrokenOut);
        await AssertContainerConsumerAsync(
            containerTypeId, containerId, inlinedHash, componentId, SharedComponentLinkState.BrokenOut);
        await AssertWorkflowVersionCountAsync(directTypeId, 2);
        await AssertWorkflowVersionCountAsync(containerTypeId, 2);
    }

    [Fact]
    public async Task AppendAndFanOut_TransitiveBlockEditIntoSectionConsumer_RollsBackAsOneTransaction()
    {
        SkipUnlessAvailable();
        const string componentId = "component-transitive";
        const string containerId = "container-transitive";
        const string typeId = "WT-Atomicity-Transitive";

        using var seedHarness = new AtomicityHarness(_fixture.Options);
        Seeded seeded = await SeedLinkedComponentAsync(seedHarness, componentId);
        (_, string containerHash) = await SeedContainerWithRefAsync(
            seedHarness, containerId, componentId, seeded.HeadContentHash, typeId);

        var tripwire = new CommandTripwire(
            text => ContainsInsertInto(text, "WorkflowVersions"),
            () => throw new InvalidOperationException("Injected mid-transaction failure during the transitive rewrite."));

        using var sutHarness = new AtomicityHarness(_fixture.BuildOptions(tripwire));
        SharedComponentFanOutService service = sutHarness.CreateFanOutService();
        (string v2Json, string v2Hash) = Definition(sutHarness, PromptV2(componentId));

        ErrorOr<SharedComponentAppendResult>? outcome = null;
        Exception? thrown = null;
        try
        {
            outcome = await service.AppendAndFanOutAsync(ORG, USER, componentId, v2Json, seeded.HeadVersionId, Ct);
        }
        catch (Exception ex)
        {
            thrown = ex;
        }

        Assert.True(
            thrown is not null || outcome!.Value.IsError,
            "the injected failure must surface as an exception or an error");
        await AssertComponentVersionCountAsync(componentId, 1);
        await AssertConsumerAsync(typeId, componentId, PromptV1(componentId), seeded.HeadContentHash);
        await AssertContainerConsumerAsync(
            typeId, containerId, containerHash, componentId, SharedComponentLinkState.Linked);
        await AssertWorkflowVersionCountAsync(typeId, 1);

        using var retryHarness = new AtomicityHarness(_fixture.Options);
        ErrorOr<SharedComponentAppendResult> retry = await retryHarness.CreateFanOutService()
            .AppendAndFanOutAsync(ORG, USER, componentId, v2Json, seeded.HeadVersionId, Ct);

        Assert.False(retry.IsError);
        await AssertComponentVersionCountAsync(componentId, 2);
        await AssertConsumerAsync(typeId, componentId, PromptV2(componentId), v2Hash);
        await AssertContainerConsumerAsync(
            typeId, containerId, containerHash, componentId, SharedComponentLinkState.Linked);
        await AssertWorkflowVersionCountAsync(typeId, 2);
    }

    [Fact]
    public async Task Import_RivalAppendWinsTheRace_RollsBackEverythingAndRetriesCleanly()
    {
        SkipUnlessAvailable();
        if (_fixture is not PostgresCoreDatabaseFixture)
        {
            Assert.Skip(
                "The mid-transaction, cross-connection rival append deadlocks under SQL Server's pessimistic "
                + "locking; SQL Server import concurrency is covered by AppendAndFanOut_RivalAppendWinsTheRace "
                + "and Import_MidTransactionFailure.");
        }

        const string componentId = "component-import-race";
        const string typeId = "WT-Import-Race";

        using var seedHarness = new AtomicityHarness(_fixture.Options);
        Seeded seeded = await SeedLinkedComponentAsync(seedHarness, componentId, typeId);
        string baseVersionId = await ReadTemplateHeadVersionIdAsync(typeId);
        (string v2Json, string v2Hash) = Definition(seedHarness, PromptV2(componentId));
        (string rivalJson, string rivalHash) = Definition(seedHarness, $"A rival import-race prompt for {componentId}.");

        string rivalVersionId = string.Empty;
        var tripwire = new CommandTripwire(
            text => ContainsInsertInto(text, "SharedComponentVersions"),
            () => rivalVersionId = InsertCommittedRivalVersion(componentId, seeded.HeadVersionId, rivalJson, rivalHash));

        using var sutHarness = new AtomicityHarness(_fixture.BuildOptions(tripwire));
        SharedComponentFanOutService service = sutHarness.CreateFanOutService();

        ErrorOr<ImportWorkflowTemplateApplyResult> result = await service.ImportApplyAsync(
            ImportCommand(
                typeId,
                baseVersionId,
                ImportBundle(sutHarness, [BlockComp(componentId, v2Json, v2Hash, null, seeded.HeadContentHash, v2Hash)]),
                [new ImportComponentDecision(componentId, Arm.TakeTheirs, seeded.HeadVersionId)]),
            Ct);

        Assert.True(result.IsError);
        Assert.Equal(SharedComponentErrorCodes.CONCURRENT_APPEND, result.FirstError.Code);
        await AssertComponentVersionCountAsync(componentId, 2);
        await AssertConsumerAsync(typeId, componentId, PromptV1(componentId), seeded.HeadContentHash);
        await AssertWorkflowVersionCountAsync(typeId, 1);

        using var retryHarness = new AtomicityHarness(_fixture.Options);
        ErrorOr<ImportWorkflowTemplateApplyResult> retry = await retryHarness.CreateFanOutService().ImportApplyAsync(
            ImportCommand(
                typeId,
                baseVersionId,
                ImportBundle(retryHarness, [BlockComp(componentId, v2Json, v2Hash, null, seeded.HeadContentHash, v2Hash)]),
                [new ImportComponentDecision(componentId, Arm.TakeTheirs, rivalVersionId)]),
            Ct);

        Assert.False(retry.IsError);
        await AssertComponentVersionCountAsync(componentId, 3);
        await AssertConsumerAsync(typeId, componentId, PromptV2(componentId), v2Hash);
        await AssertWorkflowVersionCountAsync(typeId, 2);
    }

    [Fact]
    public async Task Import_MidTransactionFailure_RollsBackCategoryComponentAndTemplateTogether()
    {
        SkipUnlessAvailable();
        const string existingId = "component-import-existing";
        const string newId = "component-import-new";
        const string categoryId = "cat-import-new";
        const string typeId = "WT-Import-MidTxn";

        using var seedHarness = new AtomicityHarness(_fixture.Options);
        Seeded seeded = await SeedLinkedComponentAsync(seedHarness, existingId, typeId);
        string baseVersionId = await ReadTemplateHeadVersionIdAsync(typeId);
        (string v2Json, string v2Hash) = Definition(seedHarness, PromptV2(existingId));
        (string newJson, string newHash) = Definition(seedHarness, PromptV1(newId));

        var tripwire = new CommandTripwire(
            text => ContainsInsertInto(text, "WorkflowVersions"),
            () => throw new InvalidOperationException("Injected mid-import failure during the template save."));

        using var sutHarness = new AtomicityHarness(_fixture.BuildOptions(tripwire));
        SharedComponentFanOutService service = sutHarness.CreateFanOutService();
        WorkflowTransferBundle bundle = ImportBundle(
            sutHarness,
            [
                BlockComp(existingId, v2Json, v2Hash, null, seeded.HeadContentHash, v2Hash),
                BlockComp(newId, newJson, newHash, categoryId, newHash),
            ],
            [new TransferCategoryDto(categoryId, "Imported category", null)]);

        ErrorOr<ImportWorkflowTemplateApplyResult>? outcome = null;
        Exception? thrown = null;
        try
        {
            outcome = await service.ImportApplyAsync(
                ImportCommand(typeId, baseVersionId, bundle,
                    [new ImportComponentDecision(existingId, Arm.TakeTheirs, seeded.HeadVersionId)]),
                Ct);
        }
        catch (Exception ex)
        {
            thrown = ex;
        }

        Assert.True(
            thrown is not null || outcome!.Value.IsError,
            "the injected failure must surface as an exception or an error");
        await AssertCategoryExistsAsync(categoryId, false);
        await AssertComponentVersionCountAsync(newId, 0);
        await AssertComponentVersionCountAsync(existingId, 1);
        await AssertConsumerAsync(typeId, existingId, PromptV1(existingId), seeded.HeadContentHash);
        await AssertWorkflowVersionCountAsync(typeId, 1);

        using var retryHarness = new AtomicityHarness(_fixture.Options);
        WorkflowTransferBundle retryBundle = ImportBundle(
            retryHarness,
            [
                BlockComp(existingId, v2Json, v2Hash, null, seeded.HeadContentHash, v2Hash),
                BlockComp(newId, newJson, newHash, categoryId, newHash),
            ],
            [new TransferCategoryDto(categoryId, "Imported category", null)]);
        ErrorOr<ImportWorkflowTemplateApplyResult> retry = await retryHarness.CreateFanOutService().ImportApplyAsync(
            ImportCommand(typeId, baseVersionId, retryBundle,
                [new ImportComponentDecision(existingId, Arm.TakeTheirs, seeded.HeadVersionId)]),
            Ct);

        Assert.False(retry.IsError);
        await AssertCategoryExistsAsync(categoryId, true);
        await AssertComponentVersionCountAsync(newId, 1);
        await AssertComponentVersionCountAsync(existingId, 2);
        await AssertConsumerAsync(typeId, existingId, PromptV2(existingId), v2Hash);
        await AssertWorkflowVersionCountAsync(typeId, 2);
    }

    private static ImportWorkflowTemplateApplyCommand ImportCommand(
        string workflowTypeId,
        string baseVersionId,
        WorkflowTransferBundle bundle,
        IReadOnlyList<ImportComponentDecision> decisions)
    {
        return new ImportWorkflowTemplateApplyCommand(
            ORG, USER, workflowTypeId, bundle, baseVersionId, Arm.KeepMine, false, false, decisions);
    }

    private static WorkflowTransferBundle ImportBundle(
        AtomicityHarness harness,
        IReadOnlyList<TransferComponentDto> components,
        IReadOnlyList<TransferCategoryDto>? categories = null)
    {
        return new WorkflowTransferBundle(
            1,
            "WT-Origin",
            "Imported",
            null,
            DateTime.UtcNow,
            USER,
            harness.Canonicalizer.ToCanonicalJson(BuildPlainWorkflow("Imported")),
            ["tpl-bundle"],
            [.. components],
            categories is null ? [] : [.. categories]);
    }

    private static TransferComponentDto BlockComp(
        string id,
        string headJson,
        string headHash,
        string? categoryId,
        params string[] trail)
    {
        return new TransferComponentDto(id, WireKind.Block, id, null, categoryId, headHash, headJson, [.. trail]);
    }

    private async Task<string> ReadTemplateHeadVersionIdAsync(string workflowTypeId)
    {
        await using CoreDbContext context = _fixture.CreateContext();

        return (await context.WorkflowVersions
            .AsNoTracking()
            .Where(v => v.WorkflowTypeId == workflowTypeId)
            .OrderByDescending(v => v.CreatedAt)
            .ThenByDescending(v => v.Id)
            .FirstAsync(Ct)).Id;
    }

    private async Task AssertCategoryExistsAsync(string categoryId, bool expected)
    {
        await using CoreDbContext context = _fixture.CreateContext();
        bool exists = await context.SharedComponentCategories.AnyAsync(c => c.Id == categoryId, Ct);

        Assert.Equal(expected, exists);
    }

    [Fact]
    public async Task DuplicateWorkflowType_UserTransactionRidesTheRetryingStrategy()
    {
        SkipUnlessAvailable();
        const string typeId = "WT-Duplicate-Strategy";
        const string copyId = "WT-Duplicate-Strategy-Copy";

        using var harness = new AtomicityHarness(_fixture.Options);
        var workflow = new Workflow("Duplicate source") { Title = "Duplicate source" };
        var section = new Section("Body", workflow) { Title = "Body" };
        _ = new Block(BlockTypes.AiQuery, section, "ask")
        {
            Name = "Ask",
            Properties = new AiQueryProperties { UserPromptTemplate = PLAIN_PROMPT },
        };
        _ = await harness.SaveTemplateAsync(harness.CreateSaveHandler(), ORG, USER, typeId, workflow);

        ErrorOr<DuplicateWorkflowTypeResult> result = await harness.CreateDuplicateHandler().Handle(
            new DuplicateWorkflowTypeCommand(ORG, USER, typeId, copyId, "Duplicate copy"), Ct);

        Assert.False(result.IsError, result.IsError ? result.FirstError.Description : string.Empty);
        Assert.Equal(copyId, result.Value.NewWorkflowTypeId);

        await using CoreDbContext context = _fixture.CreateContext();
        Workflow copy = await context.Workflows
            .AsNoTracking()
            .Include(w => w.Sections)
            .SingleAsync(w => w.WorkflowTypeId == copyId, Ct);
        Assert.Equal("Body", Assert.Single(copy.Sections).Title);
    }

    private void SkipUnlessAvailable()
    {
        if (!_fixture.DatabaseAvailable)
        {
            Assert.Skip(_skipReason);
        }
    }

    private static bool ContainsInsertInto(string commandText, string tableName)
    {
        return commandText.Contains("INSERT", StringComparison.OrdinalIgnoreCase)
            && commandText.Contains(tableName, StringComparison.Ordinal);
    }

    private string InsertCommittedRivalVersion(
        string componentId,
        string parentVersionId,
        string contentJson,
        string contentHash)
    {
        using CoreDbContext rival = _fixture.CreateContext();
        var version = new SharedComponentVersion
        {
            SharedComponentId = componentId,
            SequenceNumber = 2,
            ContentHash = contentHash,
            ParentVersionId = parentVersionId,
            CreatedByUserId = "user-rival",
            ContentJson = contentJson,
        };
        _ = rival.Add(version);
        _ = rival.SaveChanges();

        return version.Id;
    }

    private async Task<Seeded> SeedLinkedComponentAsync(
        AtomicityHarness harness,
        string componentId,
        params string[] workflowTypeIds)
    {
        (string definitionJson, string contentHash) = Definition(harness, PromptV1(componentId));
        _ = harness.Context.Add(new SharedComponent
        {
            Id = componentId,
            Kind = SharedComponentKind.Block,
            Name = $"Atomicity {componentId}",
            State = SharedComponentState.Active,
        });
        var head = new SharedComponentVersion
        {
            SharedComponentId = componentId,
            SequenceNumber = 1,
            ContentHash = contentHash,
            CreatedByUserId = USER,
            ContentJson = definitionJson,
        };
        _ = harness.Context.Add(head);
        _ = await harness.Context.SaveChangesAsync(Ct);

        SaveWorkflowCommandHandler saveHandler = harness.CreateSaveHandler();
        foreach (string workflowTypeId in workflowTypeIds)
        {
            _ = await harness.SaveTemplateAsync(
                saveHandler, ORG, USER, workflowTypeId, BuildLinkedConsumerWorkflow(componentId, contentHash));
        }

        return new Seeded(componentId, head.Id, contentHash);
    }

    private async Task<(string HeadVersionId, string ContentHash)> SeedContainerWithRefAsync(
        AtomicityHarness harness,
        string containerId,
        string blockComponentId,
        string blockHeadHash,
        string workflowTypeId)
    {
        var containerDef = new Section("Risk", new Workflow("Scratch")) { Title = "Risk" };
        _ = new Block(BlockTypes.AiQuery, containerDef, "intro")
        {
            Name = "Node",
            Properties = new AiQueryProperties { UserPromptTemplate = "Intro prompt" },
        };
        containerDef.Entries =
        [
            new SectionEntry { SlotTag = "intro", Kind = SectionEntryKind.Inline },
            new SectionEntry { SlotTag = "kyc", Kind = SectionEntryKind.ComponentRef, SharedComponentId = blockComponentId },
        ];
        string containerJson = harness.Normalizer.ToDefinitionJson(containerDef);
        string containerHash = harness.Normalizer.ComputeContentHashOfDefinitionJson(containerJson);
        _ = harness.Context.Add(new SharedComponent
        {
            Id = containerId,
            Kind = SharedComponentKind.Section,
            Name = $"Atomicity {containerId}",
            State = SharedComponentState.Active,
        });
        var head = new SharedComponentVersion
        {
            SharedComponentId = containerId,
            SequenceNumber = 1,
            ContentHash = containerHash,
            CreatedByUserId = USER,
            ContentJson = containerJson,
        };
        _ = harness.Context.Add(head);
        _ = await harness.Context.SaveChangesAsync(Ct);

        var workflow = new Workflow("Report") { Title = "Report" };
        var instance = new Section("Risk", workflow) { Title = "Risk" };
        _ = new Block(BlockTypes.AiQuery, instance, "intro")
        {
            Name = "Node",
            Properties = new AiQueryProperties { UserPromptTemplate = "Intro prompt" },
        };
        var inner = new Block(BlockTypes.AiQuery, instance, "kyc")
        {
            Name = "Node",
            Properties = new AiQueryProperties { UserPromptTemplate = PromptV1(blockComponentId) },
        };
        inner.SharedComponentId = blockComponentId;
        inner.SharedComponentLinkState = SharedComponentLinkState.Linked;
        inner.SharedComponentVersionHash = blockHeadHash;
        instance.SharedComponentId = containerId;
        instance.SharedComponentLinkState = SharedComponentLinkState.Linked;
        instance.SharedComponentVersionHash = containerHash;
        instance.SharedComponentBindings = new Dictionary<string, string> { ["intro"] = "intro", ["kyc"] = "kyc" };
        _ = await harness.SaveTemplateAsync(harness.CreateSaveHandler(), ORG, USER, workflowTypeId, workflow);

        return (head.Id, containerHash);
    }

    // The class's tests share one database, and an import matches components by their content
    // history, so each component's prompt names the component.
    private static string PromptV1(string componentId)
    {
        return $"Who is the counterparty of {componentId}?";
    }

    private static string PromptV2(string componentId)
    {
        return $"Who is the counterparty of {componentId}, and in which jurisdiction?";
    }

    private static (string Json, string Hash) Definition(AtomicityHarness harness, string prompt)
    {
        string json = harness.Normalizer.ToDefinitionJson(BuildBlockDefinition(prompt));

        return (json, harness.Normalizer.ComputeContentHashOfDefinitionJson(json));
    }

    private static Block BuildBlockDefinition(string prompt)
    {
        var scratch = new Workflow("Scratch");
        var section = new Section("Scratch", scratch) { Title = "Scratch" };

        return new Block(BlockTypes.AiQuery, section, "definition")
        {
            Name = "Node",
            Properties = new AiQueryProperties { UserPromptTemplate = prompt },
        };
    }

    private static Workflow BuildLinkedConsumerWorkflow(string componentId, string versionHash)
    {
        var workflow = new Workflow("Report") { Title = "Report" };
        var section = new Section("Body", workflow) { Title = "Body" };
        var block = new Block(BlockTypes.AiQuery, section, "linked_copy")
        {
            Name = "Node",
            Properties = new AiQueryProperties { UserPromptTemplate = PromptV1(componentId) },
        };
        block.SharedComponentId = componentId;
        block.SharedComponentLinkState = SharedComponentLinkState.Linked;
        block.SharedComponentVersionHash = versionHash;

        return workflow;
    }

    private static Workflow BuildPlainWorkflow(string title)
    {
        var workflow = new Workflow("Report") { Title = title };
        var section = new Section("Body", workflow) { Title = "Body" };
        _ = new Block(BlockTypes.AiQuery, section, "plain_block")
        {
            Name = "Node",
            Properties = new AiQueryProperties { UserPromptTemplate = PLAIN_PROMPT },
        };

        return workflow;
    }

    private async Task AssertComponentVersionCountAsync(string componentId, int expected)
    {
        await using CoreDbContext context = _fixture.CreateContext();
        int count = await context.SharedComponentVersions.CountAsync(v => v.SharedComponentId == componentId, Ct);

        Assert.Equal(expected, count);
    }

    private async Task AssertWorkflowVersionCountAsync(string workflowTypeId, int expected)
    {
        await using CoreDbContext context = _fixture.CreateContext();
        int count = await context.WorkflowVersions.CountAsync(v => v.WorkflowTypeId == workflowTypeId, Ct);

        Assert.Equal(expected, count);
    }

    private async Task AssertComponentStateAsync(string componentId, SharedComponentState expected)
    {
        await using CoreDbContext context = _fixture.CreateContext();
        SharedComponent component = await context.SharedComponents
            .AsNoTracking()
            .SingleAsync(c => c.Id == componentId, Ct);

        Assert.Equal(expected, component.State);
    }

    private async Task<string> ReadHeadContentHashAsync(string componentId)
    {
        await using CoreDbContext context = _fixture.CreateContext();

        return (await context.SharedComponentVersions
            .AsNoTracking()
            .Where(v => v.SharedComponentId == componentId)
            .OrderByDescending(v => v.SequenceNumber)
            .FirstAsync(Ct)).ContentHash;
    }

    private async Task AssertDirectCopyStateAsync(
        string workflowTypeId,
        string componentId,
        SharedComponentLinkState expected)
    {
        using var verify = new AtomicityHarness(_fixture.Options);
        Workflow? workflow = await verify.UnitOfWork.Workflows.GetByTypeIdAsNoTrackingAsync(workflowTypeId);

        Assert.NotNull(workflow);
        Block copy = Assert.Single(workflow!.GetAllBlocks(), b => b.SharedComponentId == componentId);
        Assert.Equal(expected, copy.SharedComponentLinkState);
    }

    private async Task AssertContainerConsumerAsync(
        string workflowTypeId,
        string containerId,
        string expectedSectionHash,
        string innerComponentId,
        SharedComponentLinkState expectedInnerState)
    {
        using var verify = new AtomicityHarness(_fixture.Options);
        Workflow? workflow = await verify.UnitOfWork.Workflows.GetByTypeIdAsNoTrackingAsync(workflowTypeId);

        Assert.NotNull(workflow);
        Section instance = Assert.Single(workflow!.Sections, s => s.SharedComponentId == containerId);
        Assert.Equal(SharedComponentLinkState.Linked, instance.SharedComponentLinkState);
        Assert.Equal(expectedSectionHash, instance.SharedComponentVersionHash);
        Block inner = Assert.Single(instance.Blocks, b => b.SharedComponentId == innerComponentId);
        Assert.Equal(expectedInnerState, inner.SharedComponentLinkState);
    }

    private async Task<(int Components, int Versions)> CountComponentRowsByNameAsync(string name)
    {
        await using CoreDbContext context = _fixture.CreateContext();
        List<string> componentIds = await context.SharedComponents
            .Where(c => c.Name == name)
            .Select(c => c.Id)
            .ToListAsync(Ct);
        int versions = await context.SharedComponentVersions
            .CountAsync(v => componentIds.Contains(v.SharedComponentId), Ct);

        return (componentIds.Count, versions);
    }

    private async Task AssertConsumerAsync(
        string workflowTypeId,
        string componentId,
        string expectedPrompt,
        string expectedVersionHash)
    {
        using var verify = new AtomicityHarness(_fixture.Options);
        Workflow? workflow = await verify.UnitOfWork.Workflows.GetByTypeIdAsNoTrackingAsync(workflowTypeId);

        Assert.NotNull(workflow);
        Block linked = Assert.Single(workflow!.GetAllBlocks(), b => b.SharedComponentId == componentId);
        AiQueryProperties properties = Assert.IsType<AiQueryProperties>(linked.Properties);
        Assert.Equal(expectedPrompt, properties.UserPromptTemplate);
        Assert.Equal(expectedVersionHash, linked.SharedComponentVersionHash);
    }

    private sealed record Seeded(string ComponentId, string HeadVersionId, string HeadContentHash);

    private sealed class AtomicityHarness : IDisposable
    {
        public CoreDbContext Context { get; }
        public ICoreUnitOfWork UnitOfWork { get; }
        public WorkflowCanonicalizer Canonicalizer { get; } = new();
        public SharedComponentContentNormalizer Normalizer { get; }

        private readonly Microsoft.Extensions.DependencyInjection.ServiceProvider _provider;

        public AtomicityHarness(DbContextOptions<CoreDbContext> coreOptions)
        {
            Context = new CoreDbContext(coreOptions);

            var services = new ServiceCollection();
            _ = services.AddDbContext<UserDataDbContext>(o => o.UseInMemoryDatabase($"fanout-atomicity-{Guid.NewGuid()}"));
            _ = services.AddScoped(_ => Context);
            _ = services.AddScoped<ICoreWorkflowRepository, CoreWorkflowRepository>();
            _ = services.AddScoped<ICoreWorkflowTypeRepository, CoreWorkflowTypeRepository>();
            _ = services.AddTestDatabaseServicesWithoutDbContext();
            _provider = services.BuildServiceProvider();

            UnitOfWork = _provider.GetRequiredService<ICoreUnitOfWork>();
            Normalizer = new SharedComponentContentNormalizer(Canonicalizer);
        }

        public SaveWorkflowCommandHandler CreateSaveHandler()
        {
            return new SaveWorkflowCommandHandler(CreatePipeline(), Context, NullLogger<SaveWorkflowCommandHandler>.Instance);
        }

        public DuplicateWorkflowTypeCommandHandler CreateDuplicateHandler()
        {
            return new DuplicateWorkflowTypeCommandHandler(
                UnitOfWork,
                Context,
                CreatePipeline(),
                NullLogger<DuplicateWorkflowTypeCommandHandler>.Instance, SuperAdminLookups.OverEmptyUserData());
        }

        public SharedComponentFanOutService CreateFanOutService()
        {
            return new SharedComponentFanOutService(
                UnitOfWork,
                _provider.GetRequiredService<IUserDataUnitOfWork>(),
                Context,
                CreatePipeline(),
                Normalizer,
                new SharedComponentReconciler(Normalizer),
                Canonicalizer,
                new WorkflowTemplateStructuralValidator(),
                new SharedComponentEditor(Canonicalizer, Normalizer),
                new NoBaselineReader(),
                Options.Create(new AgentRuntimeSettings()),
                NullLogger<SharedComponentFanOutService>.Instance);
        }

        public (PromoteSharedComponentCommandHandler Promote, SaveWorkflowCommandHandler Save) CreatePromoteHandler()
        {
            SaveWorkflowCommandHandler saveHandler = CreateSaveHandler();
            var promoteHandler = new PromoteSharedComponentCommandHandler(
                UnitOfWork,
                Context,
                CreatePipeline(),
                new SharedComponentEditor(Canonicalizer, Normalizer),
                CreateFanOutService(),
                Normalizer,
                NullLogger<PromoteSharedComponentCommandHandler>.Instance);

            return (promoteHandler, saveHandler);
        }

        public async Task<string> SaveTemplateAsync(
            SaveWorkflowCommandHandler saveHandler,
            string organizationId,
            string userId,
            string workflowTypeId,
            Workflow workflow)
        {
            _ = Context.Add(new WorkflowType(workflowTypeId, "Report"));
            _ = await Context.SaveChangesAsync(Ct);

            ErrorOr<SaveWorkflowResult> saveResult = await saveHandler.Handle(
                new SaveWorkflowCommand(organizationId, userId, workflowTypeId, workflow), Ct);
            Assert.False(saveResult.IsError);

            return saveResult.Value.VersionId;
        }

        private WorkflowSavePipeline CreatePipeline()
        {
            return new WorkflowSavePipeline(
                UnitOfWork,
                Context,
                Canonicalizer,
                new WorkflowDiffService(),
                new WorkflowNarrativeGenerator(),
                Options.Create(new AgentRuntimeSettings()),
                NullLogger<WorkflowSavePipeline>.Instance, SuperAdminLookups.OverEmptyUserData());
        }

        public void Dispose()
        {
            Context.Dispose();
            _provider.Dispose();
        }
    }

    private sealed class CommandTripwire : DbCommandInterceptor
    {
        private readonly Func<string, bool> _matches;
        private readonly Action _fire;
        private int _fired;

        public CommandTripwire(Func<string, bool> matches, Action fire)
        {
            _matches = matches;
            _fire = fire;
        }

        public override InterceptionResult<DbDataReader> ReaderExecuting(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result)
        {
            MaybeFire(command);

            return base.ReaderExecuting(command, eventData, result);
        }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            MaybeFire(command);

            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }

        public override InterceptionResult<int> NonQueryExecuting(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<int> result)
        {
            MaybeFire(command);

            return base.NonQueryExecuting(command, eventData, result);
        }

        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            MaybeFire(command);

            return base.NonQueryExecutingAsync(command, eventData, result, cancellationToken);
        }

        public override InterceptionResult<object> ScalarExecuting(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<object> result)
        {
            MaybeFire(command);

            return base.ScalarExecuting(command, eventData, result);
        }

        public override ValueTask<InterceptionResult<object>> ScalarExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<object> result,
            CancellationToken cancellationToken = default)
        {
            MaybeFire(command);

            return base.ScalarExecutingAsync(command, eventData, result, cancellationToken);
        }

        private void MaybeFire(DbCommand command)
        {
            if (_matches(command.CommandText) && Interlocked.CompareExchange(ref _fired, 1, 0) == 0)
            {
                _fire();
            }
        }
    }
}

public sealed class SharedComponentFanOutAtomicityPostgresTests
    : SharedComponentFanOutAtomicityTests, IClassFixture<PostgresCoreDatabaseFixture>
{
    public SharedComponentFanOutAtomicityPostgresTests(PostgresCoreDatabaseFixture fixture)
        : base(fixture, "No local PostgreSQL reachable (set INIA_TEST_PG to enable).")
    {
    }
}

public sealed class SharedComponentFanOutAtomicitySqlServerTests
    : SharedComponentFanOutAtomicityTests, IClassFixture<SqlServerCoreDatabaseFixture>
{
    public SharedComponentFanOutAtomicitySqlServerTests(SqlServerCoreDatabaseFixture fixture)
        : base(fixture, "No SQL Server reachable (set INIA_TEST_MSSQL to enable).")
    {
    }
}
