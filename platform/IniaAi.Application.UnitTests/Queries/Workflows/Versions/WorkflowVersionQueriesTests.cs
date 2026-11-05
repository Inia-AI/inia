using ErrorOr;
using IniaAi.Application.Common.Services;
using IniaAi.Application.Queries.Workflows.Versions;
using IniaAi.Core.Workflows.Diff;
using IniaAi.Domain.Core.Interfaces;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Blocks;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties;
using IniaAi.Domain.Entities.Workflows.Enums;
using IniaAi.Domain.Serialization;
using IniaAi.Domain.UserData.Interfaces;
using IniaAi.Infrastructure.Persistence.Core.Contexts;
using IniaAi.Testing.DependencyInjection;
using IniaAi.WebApi.Contracts.Responses.WorkflowVersions;
using Microsoft.Extensions.DependencyInjection;

namespace IniaAi.Application.UnitTests.Queries.Workflows.Versions;

public class WorkflowVersionQueriesTests : IDisposable
{
    private const string ORG = "org-test";
    private const string OTHER_ORG = "org-other";
    private const string TYPE = "WT-Report";
    private const string USER = "user-test";
    private const string OTHER_TYPE = "WT-Other";

    private readonly Microsoft.Extensions.DependencyInjection.ServiceProvider _serviceProvider;
    private readonly CoreDbContext _context;
    private readonly ICoreUnitOfWork _unitOfWork;
    private readonly UserDisplayNameResolver _nameResolver;
    private readonly WorkflowCanonicalizer _canonicalizer = new();

    public WorkflowVersionQueriesTests()
    {
        var services = new ServiceCollection();
        _ = services.AddTestDatabaseServices();

        _serviceProvider = services.BuildServiceProvider();
        _context = _serviceProvider.GetRequiredService<CoreDbContext>();
        _unitOfWork = _serviceProvider.GetRequiredService<ICoreUnitOfWork>();
        _nameResolver = new UserDisplayNameResolver(_serviceProvider.GetRequiredService<IUserDataUnitOfWork>());

        _context.GrantWorkflowTypeAccess(TYPE, ORG);
        _ = _context.SaveChanges();
    }

    public void Dispose()
    {
        _context.Dispose();
        _serviceProvider.Dispose();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task History_ReturnsNewestFirst()
    {
        WorkflowVersion older = Seed(new string('a', 64), DateTime.UtcNow.AddMinutes(-10), "{}");
        WorkflowVersion newer = Seed(new string('b', 64), DateTime.UtcNow.AddMinutes(-1), "{}");
        await _context.SaveChangesAsync(TestContext.Current.CancellationToken);

        var handler = new GetWorkflowHistoryQueryHandler(_unitOfWork, _nameResolver);
        ErrorOr<WorkflowHistoryPageDto> result = await handler.Handle(
            new GetWorkflowHistoryQuery(ORG, TYPE), TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        Assert.Equal(2, result.Value.Versions.Count);
        Assert.Equal(newer.Id, result.Value.Versions[0].Id);
        Assert.Equal(older.Id, result.Value.Versions[1].Id);
        Assert.Equal(newer.ContentHash[..WorkflowVersionCode.LENGTH], result.Value.Versions[0].ShortCode);
        Assert.False(result.Value.HasMore);
        Assert.Equal(2, result.Value.TotalCount);
    }

    [Fact]
    public async Task History_OrgWithoutTypeAccess_StillReturnsVersions()
    {
        WorkflowVersion head = Seed(new string('a', 64), DateTime.UtcNow, "{}");
        await _context.SaveChangesAsync(TestContext.Current.CancellationToken);

        var handler = new GetWorkflowHistoryQueryHandler(_unitOfWork, _nameResolver);
        ErrorOr<WorkflowHistoryPageDto> result = await handler.Handle(
            new GetWorkflowHistoryQuery(OTHER_ORG, TYPE), TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        Assert.Equal(head.Id, Assert.Single(result.Value.Versions).Id);
    }

    [Fact]
    public async Task Version_ReturnsContent_ForAnyOrg_AndNotFoundForMissing()
    {
        WorkflowVersion version = Seed(new string('a', 64), DateTime.UtcNow, "{\"Name\":\"x\"}");
        await _context.SaveChangesAsync(TestContext.Current.CancellationToken);

        var handler = new GetWorkflowVersionQueryHandler(_unitOfWork);

        ErrorOr<WorkflowVersionContentDto> found = await handler.Handle(
            new GetWorkflowVersionQuery(ORG, version.Id), TestContext.Current.CancellationToken);
        Assert.False(found.IsError);
        Assert.Equal("{\"Name\":\"x\"}", found.Value.WorkflowJson);

        ErrorOr<WorkflowVersionContentDto> otherOrg = await handler.Handle(
            new GetWorkflowVersionQuery(OTHER_ORG, version.Id), TestContext.Current.CancellationToken);
        Assert.False(otherOrg.IsError);
        Assert.Equal("{\"Name\":\"x\"}", otherOrg.Value.WorkflowJson);

        ErrorOr<WorkflowVersionContentDto> missing = await handler.Handle(
            new GetWorkflowVersionQuery(ORG, "nope"), TestContext.Current.CancellationToken);
        Assert.True(missing.IsError);
    }

    [Fact]
    public async Task Compare_ReturnsBlockGroupedDiff()
    {
        WorkflowVersion before = Seed(new string('a', 64), DateTime.UtcNow.AddMinutes(-5), Json(OneBlock("b1", "old prompt")));
        WorkflowVersion after = Seed(new string('b', 64), DateTime.UtcNow, Json(OneBlock("b1", "new prompt")));
        await _context.SaveChangesAsync(TestContext.Current.CancellationToken);

        var handler = new CompareWorkflowVersionsQueryHandler(_unitOfWork, new WorkflowDiffService());
        ErrorOr<WorkflowDiff> result = await handler.Handle(
            new CompareWorkflowVersionsQuery(ORG, before.Id, after.Id), TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        BlockDiff blockDiff = Assert.Single(result.Value.Blocks, b => b.ChangeKind == DiffChangeKind.Modified);
        Assert.Equal("b1", blockDiff.ReplacementTag);
    }

    [Fact]
    public async Task Compare_OrgWithoutTypeAccess_StillReturnsDiff()
    {
        WorkflowVersion before = Seed(new string('a', 64), DateTime.UtcNow.AddMinutes(-5), Json(OneBlock("b1", "old prompt")));
        WorkflowVersion after = Seed(new string('b', 64), DateTime.UtcNow, Json(OneBlock("b1", "new prompt")));
        await _context.SaveChangesAsync(TestContext.Current.CancellationToken);

        var handler = new CompareWorkflowVersionsQueryHandler(_unitOfWork, new WorkflowDiffService());
        ErrorOr<WorkflowDiff> result = await handler.Handle(
            new CompareWorkflowVersionsQuery(OTHER_ORG, before.Id, after.Id), TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        _ = Assert.Single(result.Value.Blocks, b => b.ChangeKind == DiffChangeKind.Modified);
    }

    [Fact]
    public async Task Compare_DiffReadsOldToNew_RegardlessOfArgumentOrder()
    {
        WorkflowVersion older = Seed(new string('a', 64), DateTime.UtcNow.AddMinutes(-5), Json(OneBlock("b1", "old prompt")));
        WorkflowVersion newer = Seed(new string('b', 64), DateTime.UtcNow, Json(OneBlock("b1", "new prompt")));
        await _context.SaveChangesAsync(TestContext.Current.CancellationToken);

        var handler = new CompareWorkflowVersionsQueryHandler(_unitOfWork, new WorkflowDiffService());

        ErrorOr<WorkflowDiff> forward = await handler.Handle(
            new CompareWorkflowVersionsQuery(ORG, older.Id, newer.Id), TestContext.Current.CancellationToken);
        ErrorOr<WorkflowDiff> reversed = await handler.Handle(
            new CompareWorkflowVersionsQuery(ORG, newer.Id, older.Id), TestContext.Current.CancellationToken);

        Assert.False(forward.IsError);
        Assert.False(reversed.IsError);

        FieldDiff forwardField = PromptField(forward.Value);
        FieldDiff reversedField = PromptField(reversed.Value);

        Assert.Equal("old prompt", forwardField.Before);
        Assert.Equal("new prompt", forwardField.After);
        Assert.Equal("old prompt", reversedField.Before);
        Assert.Equal("new prompt", reversedField.After);
    }

    private static FieldDiff PromptField(WorkflowDiff diff)
    {
        BlockDiff block = Assert.Single(diff.Blocks, b => b.ChangeKind == DiffChangeKind.Modified);
        return block.FieldDiffs.Single(f =>
            f.Before is "old prompt" or "new prompt" || f.After is "old prompt" or "new prompt");
    }

    [Fact]
    public async Task LastTouched_ReturnsVersionWhereBlockLastChanged()
    {
        WorkflowVersion v1 = Seed(new string('a', 64), DateTime.UtcNow.AddMinutes(-10), Json(OneBlock("b1", "p1")));
        WorkflowVersion v2 = Seed(new string('b', 64), DateTime.UtcNow.AddMinutes(-5), Json(OneBlock("b1", "p2")));
        _ = Seed(new string('c', 64), DateTime.UtcNow, Json(TwoBlocks("b1", "p2", "b2", "added")));
        await _context.SaveChangesAsync(TestContext.Current.CancellationToken);

        var handler = new GetAllBlocksLastTouchedQueryHandler(_unitOfWork, _nameResolver);
        ErrorOr<IReadOnlyDictionary<string, BlockLastTouchedDto>> result = await handler.Handle(
            new GetAllBlocksLastTouchedQuery(ORG, TYPE), TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        Assert.Equal(v2.Id, result.Value["b1"].VersionId);
        Assert.NotEqual(v1.Id, result.Value["b1"].VersionId);
    }

    [Fact]
    public async Task LastTouched_UnchangedSinceCreation_ReturnsCreationVersion()
    {
        WorkflowVersion v1 = Seed(new string('a', 64), DateTime.UtcNow.AddMinutes(-10), Json(OneBlock("b1", "same")));
        _ = Seed(new string('b', 64), DateTime.UtcNow.AddMinutes(-5), Json(TwoBlocks("b1", "same", "b2", "other")));
        await _context.SaveChangesAsync(TestContext.Current.CancellationToken);

        var handler = new GetAllBlocksLastTouchedQueryHandler(_unitOfWork, _nameResolver);
        ErrorOr<IReadOnlyDictionary<string, BlockLastTouchedDto>> result = await handler.Handle(
            new GetAllBlocksLastTouchedQuery(ORG, TYPE), TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        Assert.Equal(v1.Id, result.Value["b1"].VersionId);
    }

    [Fact]
    public async Task AllBlocksLastTouched_OnlyResolvesBlocksInCurrentVersion()
    {
        _ = Seed(new string('a', 64), DateTime.UtcNow, Json(OneBlock("b1", "p1")));
        await _context.SaveChangesAsync(TestContext.Current.CancellationToken);

        var handler = new GetAllBlocksLastTouchedQueryHandler(_unitOfWork, _nameResolver);
        ErrorOr<IReadOnlyDictionary<string, BlockLastTouchedDto>> result = await handler.Handle(
            new GetAllBlocksLastTouchedQuery(ORG, TYPE), TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        Assert.True(result.Value.ContainsKey("b1"));
        Assert.False(result.Value.ContainsKey("ghost"));
    }

    [Fact]
    public async Task LastTouched_OrgWithoutTypeAccess_StillResolvesBlocks()
    {
        _ = Seed(new string('a', 64), DateTime.UtcNow, Json(OneBlock("b1", "p1")));
        await _context.SaveChangesAsync(TestContext.Current.CancellationToken);

        var handler = new GetAllBlocksLastTouchedQueryHandler(_unitOfWork, _nameResolver);
        ErrorOr<IReadOnlyDictionary<string, BlockLastTouchedDto>> result = await handler.Handle(
            new GetAllBlocksLastTouchedQuery(OTHER_ORG, TYPE), TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        Assert.True(result.Value.ContainsKey("b1"));
    }

    [Fact]
    public async Task Compare_OnTrimmedPayload_ReturnsError_DoesNotThrow()
    {
        WorkflowVersion trimmed = SeedTrimmed(new string('a', 64), DateTime.UtcNow.AddMinutes(-5));
        WorkflowVersion current = Seed(new string('b', 64), DateTime.UtcNow, Json(OneBlock("b1", "new prompt")));
        await _context.SaveChangesAsync(TestContext.Current.CancellationToken);

        var handler = new CompareWorkflowVersionsQueryHandler(_unitOfWork, new WorkflowDiffService());
        ErrorOr<WorkflowDiff> result = await handler.Handle(
            new CompareWorkflowVersionsQuery(ORG, trimmed.Id, current.Id), TestContext.Current.CancellationToken);

        Assert.True(result.IsError);
        Assert.Equal(WorkflowVersionErrors.PAYLOAD_TRIMMED_CODE, result.FirstError.Code);
        Assert.Equal(ErrorType.NotFound, result.FirstError.Type);
    }

    [Fact]
    public async Task Version_OnTrimmedPayload_ReturnsError_NotBlankContent()
    {
        WorkflowVersion trimmed = SeedTrimmed(new string('a', 64), DateTime.UtcNow);
        await _context.SaveChangesAsync(TestContext.Current.CancellationToken);

        var handler = new GetWorkflowVersionQueryHandler(_unitOfWork);
        ErrorOr<WorkflowVersionContentDto> result = await handler.Handle(
            new GetWorkflowVersionQuery(ORG, trimmed.Id), TestContext.Current.CancellationToken);

        Assert.True(result.IsError);
        Assert.Equal(WorkflowVersionErrors.PAYLOAD_TRIMMED_CODE, result.FirstError.Code);
        Assert.Equal(ErrorType.NotFound, result.FirstError.Type);
    }

    [Fact]
    public async Task LastTouched_WalkReachesTrimmedHistory_ReturnsOldestUntrimmed_DoesNotThrow()
    {
        _ = SeedTrimmed(new string('a', 64), DateTime.UtcNow.AddMinutes(-10));
        WorkflowVersion untrimmedOlder = Seed(new string('b', 64), DateTime.UtcNow.AddMinutes(-5), Json(OneBlock("b1", "same")));
        _ = Seed(new string('c', 64), DateTime.UtcNow, Json(OneBlock("b1", "same")));
        await _context.SaveChangesAsync(TestContext.Current.CancellationToken);

        var handler = new GetAllBlocksLastTouchedQueryHandler(_unitOfWork, _nameResolver);
        ErrorOr<IReadOnlyDictionary<string, BlockLastTouchedDto>> result = await handler.Handle(
            new GetAllBlocksLastTouchedQuery(ORG, TYPE), TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        Assert.Equal(untrimmedOlder.Id, result.Value["b1"].VersionId);
    }

    [Fact]
    public async Task History_SurfacesPayloadTrimmedAt_ForTrimmedVersions()
    {
        WorkflowVersion trimmed = SeedTrimmed(new string('a', 64), DateTime.UtcNow.AddMinutes(-5));
        WorkflowVersion normal = Seed(new string('b', 64), DateTime.UtcNow, "{}");
        await _context.SaveChangesAsync(TestContext.Current.CancellationToken);

        var handler = new GetWorkflowHistoryQueryHandler(_unitOfWork, _nameResolver);
        ErrorOr<WorkflowHistoryPageDto> result = await handler.Handle(
            new GetWorkflowHistoryQuery(ORG, TYPE), TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        WorkflowVersionSummaryDto trimmedSummary = result.Value.Versions.Single(summary => summary.Id == trimmed.Id);
        WorkflowVersionSummaryDto normalSummary = result.Value.Versions.Single(summary => summary.Id == normal.Id);
        Assert.NotNull(trimmedSummary.PayloadTrimmedAt);
        Assert.Null(normalSummary.PayloadTrimmedAt);
    }

    [Fact]
    public async Task History_FirstPage_ReportsHasMoreAndTotalCount()
    {
        for (int i = 0; i < 5; i++)
        {
            _ = Seed(new string((char)('a' + i), 64), DateTime.UtcNow.AddMinutes(-i), "{}");
        }

        await _context.SaveChangesAsync(TestContext.Current.CancellationToken);

        var handler = new GetWorkflowHistoryQueryHandler(_unitOfWork, _nameResolver);
        ErrorOr<WorkflowHistoryPageDto> result = await handler.Handle(
            new GetWorkflowHistoryQuery(ORG, TYPE, Take: 2), TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        Assert.Equal(2, result.Value.Versions.Count);
        Assert.True(result.Value.HasMore);
        Assert.Equal(5, result.Value.TotalCount);
    }

    [Fact]
    public async Task History_CursorPage_ReturnsOlderRowsWithoutOverlap()
    {
        var seeded = new List<WorkflowVersion>();
        for (int i = 0; i < 5; i++)
        {
            seeded.Add(Seed(new string((char)('a' + i), 64), DateTime.UtcNow.AddMinutes(-i), "{}"));
        }

        await _context.SaveChangesAsync(TestContext.Current.CancellationToken);

        var handler = new GetWorkflowHistoryQueryHandler(_unitOfWork, _nameResolver);
        ErrorOr<WorkflowHistoryPageDto> firstPage = await handler.Handle(
            new GetWorkflowHistoryQuery(ORG, TYPE, Take: 2), TestContext.Current.CancellationToken);

        Assert.False(firstPage.IsError);
        WorkflowVersionSummaryDto cursor = firstPage.Value.Versions[^1];

        ErrorOr<WorkflowHistoryPageDto> secondPage = await handler.Handle(
            new GetWorkflowHistoryQuery(ORG, TYPE, Take: 2, cursor.CreatedAtUtc, cursor.Id),
            TestContext.Current.CancellationToken);

        Assert.False(secondPage.IsError);
        Assert.Equal(2, secondPage.Value.Versions.Count);
        Assert.True(secondPage.Value.HasMore);
        Assert.Empty(firstPage.Value.Versions.Select(v => v.Id)
            .Intersect(secondPage.Value.Versions.Select(v => v.Id)));
        Assert.All(secondPage.Value.Versions, v => Assert.True(v.CreatedAtUtc <= cursor.CreatedAtUtc));
    }

    [Fact]
    public async Task History_IdenticalCreatedAt_CursorTiebreaksOnId_NoDuplicatesOrGaps()
    {
        DateTime sameInstant = DateTime.UtcNow;
        var seeded = new List<WorkflowVersion>();
        for (int i = 0; i < 4; i++)
        {
            seeded.Add(Seed(new string((char)('a' + i), 64), sameInstant, "{}"));
        }

        await _context.SaveChangesAsync(TestContext.Current.CancellationToken);

        var handler = new GetWorkflowHistoryQueryHandler(_unitOfWork, _nameResolver);
        var collected = new List<string>();
        DateTime? cursorCreatedAt = null;
        string? cursorId = null;
        bool hasMore = true;

        while (hasMore)
        {
            ErrorOr<WorkflowHistoryPageDto> page = await handler.Handle(
                new GetWorkflowHistoryQuery(ORG, TYPE, Take: 2, cursorCreatedAt, cursorId),
                TestContext.Current.CancellationToken);

            Assert.False(page.IsError);
            collected.AddRange(page.Value.Versions.Select(v => v.Id));
            hasMore = page.Value.HasMore;
            if (page.Value.Versions.Count > 0)
            {
                WorkflowVersionSummaryDto last = page.Value.Versions[^1];
                cursorCreatedAt = last.CreatedAtUtc;
                cursorId = last.Id;
            }
        }

        Assert.Equal(4, collected.Count);
        Assert.Equal(4, collected.Distinct().Count());
        Assert.Equal([.. seeded.Select(v => v.Id).OrderBy(id => id, StringComparer.Ordinal).Reverse()], collected);
    }

    [Fact]
    public async Task VersionChanges_DiffsAgainstParent_WithFieldLevelBeforeAfter()
    {
        WorkflowVersion parent = Seed(new string('a', 64), DateTime.UtcNow.AddMinutes(-5), Json(OneBlock("b1", "old prompt")));
        WorkflowVersion child = Seed(new string('b', 64), DateTime.UtcNow, Json(OneBlock("b1", "new prompt")), parent.Id);
        await _context.SaveChangesAsync(TestContext.Current.CancellationToken);

        var handler = new GetWorkflowVersionChangesQueryHandler(_unitOfWork, new WorkflowDiffService());
        ErrorOr<WorkflowVersionChangesResult> result = await handler.Handle(
            new GetWorkflowVersionChangesQuery(ORG, child.Id), TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        Assert.Equal(WorkflowVersionChangeStatus.Diffed, result.Value.Status);
        Assert.NotNull(result.Value.Diff);
        FieldDiff field = PromptField(result.Value.Diff);
        Assert.Equal("old prompt", field.Before);
        Assert.Equal("new prompt", field.After);
    }

    [Fact]
    public async Task VersionChanges_NoParent_ReturnsFirstVersionWithNarrative()
    {
        WorkflowVersion first = Seed(new string('a', 64), DateTime.UtcNow, Json(OneBlock("b1", "p1")));
        await _context.SaveChangesAsync(TestContext.Current.CancellationToken);

        var handler = new GetWorkflowVersionChangesQueryHandler(_unitOfWork, new WorkflowDiffService());
        ErrorOr<WorkflowVersionChangesResult> result = await handler.Handle(
            new GetWorkflowVersionChangesQuery(ORG, first.Id), TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        Assert.Equal(WorkflowVersionChangeStatus.FirstVersion, result.Value.Status);
        Assert.Null(result.Value.Diff);
        Assert.Equal("change", result.Value.StoredNarrative);
    }

    [Fact]
    public async Task VersionChanges_TrimmedVersion_ReturnsUnavailableWithNarrative()
    {
        WorkflowVersion parent = Seed(new string('a', 64), DateTime.UtcNow.AddMinutes(-5), Json(OneBlock("b1", "p1")));
        WorkflowVersion trimmed = SeedTrimmed(new string('b', 64), DateTime.UtcNow, parent.Id);
        await _context.SaveChangesAsync(TestContext.Current.CancellationToken);

        var handler = new GetWorkflowVersionChangesQueryHandler(_unitOfWork, new WorkflowDiffService());
        ErrorOr<WorkflowVersionChangesResult> result = await handler.Handle(
            new GetWorkflowVersionChangesQuery(ORG, trimmed.Id), TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        Assert.Equal(WorkflowVersionChangeStatus.Unavailable, result.Value.Status);
        Assert.Null(result.Value.Diff);
        Assert.Equal("change", result.Value.StoredNarrative);
    }

    [Fact]
    public async Task VersionChanges_TrimmedParent_ReturnsUnavailable()
    {
        WorkflowVersion trimmedParent = SeedTrimmed(new string('a', 64), DateTime.UtcNow.AddMinutes(-5));
        WorkflowVersion child = Seed(new string('b', 64), DateTime.UtcNow, Json(OneBlock("b1", "p1")), trimmedParent.Id);
        await _context.SaveChangesAsync(TestContext.Current.CancellationToken);

        var handler = new GetWorkflowVersionChangesQueryHandler(_unitOfWork, new WorkflowDiffService());
        ErrorOr<WorkflowVersionChangesResult> result = await handler.Handle(
            new GetWorkflowVersionChangesQuery(ORG, child.Id), TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        Assert.Equal(WorkflowVersionChangeStatus.Unavailable, result.Value.Status);
        Assert.Null(result.Value.Diff);
    }

    [Fact]
    public async Task VersionChanges_OrgWithoutTypeAccess_StillReturnsChanges()
    {
        WorkflowVersion version = Seed(new string('a', 64), DateTime.UtcNow, Json(OneBlock("b1", "p1")));
        await _context.SaveChangesAsync(TestContext.Current.CancellationToken);

        var handler = new GetWorkflowVersionChangesQueryHandler(_unitOfWork, new WorkflowDiffService());
        ErrorOr<WorkflowVersionChangesResult> result = await handler.Handle(
            new GetWorkflowVersionChangesQuery(OTHER_ORG, version.Id), TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        Assert.Equal(WorkflowVersionChangeStatus.FirstVersion, result.Value.Status);
    }

    [Fact]
    public async Task History_SurfacesParentPointer_NewestFirst()
    {
        WorkflowVersion first = Seed(new string('a', 64), DateTime.UtcNow.AddMinutes(-10), "{}");
        await _context.SaveChangesAsync(TestContext.Current.CancellationToken);
        WorkflowVersion second = Seed(new string('b', 64), DateTime.UtcNow.AddMinutes(-1), "{}", first.Id);
        await _context.SaveChangesAsync(TestContext.Current.CancellationToken);

        var handler = new GetWorkflowHistoryQueryHandler(_unitOfWork, _nameResolver);
        ErrorOr<WorkflowHistoryPageDto> result = await handler.Handle(
            new GetWorkflowHistoryQuery(ORG, TYPE), TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        Assert.Equal([second.Id, first.Id], result.Value.Versions.Select(version => version.Id));
        Assert.Equal(first.Id, result.Value.Versions[0].ParentVersionId);
        Assert.Null(result.Value.Versions[1].ParentVersionId);
    }

    [Fact]
    public async Task History_RequireKnownType_UnknownTypeIsNotFound_KnownTypeWithoutVersionsIsEmpty()
    {
        _ = _context.WorkflowTypes.Add(new WorkflowType(OTHER_TYPE, "Other"));
        await _context.SaveChangesAsync(TestContext.Current.CancellationToken);

        var handler = new GetWorkflowHistoryQueryHandler(_unitOfWork, _nameResolver);

        ErrorOr<WorkflowHistoryPageDto> unknown = await handler.Handle(
            new GetWorkflowHistoryQuery(ORG, "WT-Mistyped", RequireKnownType: true), TestContext.Current.CancellationToken);
        Assert.True(unknown.IsError);
        Assert.Equal(WorkflowVersionErrors.UNKNOWN_TYPE_CODE, unknown.FirstError.Code);
        Assert.Equal(ErrorType.NotFound, unknown.FirstError.Type);

        ErrorOr<WorkflowHistoryPageDto> known = await handler.Handle(
            new GetWorkflowHistoryQuery(ORG, OTHER_TYPE, RequireKnownType: true), TestContext.Current.CancellationToken);
        Assert.False(known.IsError);
        Assert.Empty(known.Value.Versions);
        Assert.Equal(0, known.Value.TotalCount);
    }

    [Fact]
    public async Task Version_ScopedToAnotherType_IsNotFound()
    {
        WorkflowVersion version = Seed(new string('a', 64), DateTime.UtcNow, Json(OneBlock("b1", "prompt")));
        await _context.SaveChangesAsync(TestContext.Current.CancellationToken);

        var handler = new GetWorkflowVersionQueryHandler(_unitOfWork);

        ErrorOr<WorkflowVersionContentDto> ownType = await handler.Handle(
            new GetWorkflowVersionQuery(ORG, version.Id, TYPE), TestContext.Current.CancellationToken);
        Assert.False(ownType.IsError);

        ErrorOr<WorkflowVersionContentDto> otherType = await handler.Handle(
            new GetWorkflowVersionQuery(ORG, version.Id, OTHER_TYPE), TestContext.Current.CancellationToken);
        Assert.True(otherType.IsError);
        Assert.Equal(WorkflowVersionErrors.NOT_FOUND_CODE, otherType.FirstError.Code);
        Assert.Equal(ErrorType.NotFound, otherType.FirstError.Type);
    }

    [Fact]
    public async Task VersionChanges_ScopedToAnotherType_IsNotFound()
    {
        WorkflowVersion parent = Seed(new string('a', 64), DateTime.UtcNow.AddMinutes(-5), Json(OneBlock("b1", "old prompt")));
        await _context.SaveChangesAsync(TestContext.Current.CancellationToken);
        WorkflowVersion child = Seed(new string('b', 64), DateTime.UtcNow, Json(OneBlock("b1", "new prompt")), parent.Id);
        await _context.SaveChangesAsync(TestContext.Current.CancellationToken);

        var handler = new GetWorkflowVersionChangesQueryHandler(_unitOfWork, new WorkflowDiffService());
        ErrorOr<WorkflowVersionChangesResult> result = await handler.Handle(
            new GetWorkflowVersionChangesQuery(ORG, child.Id, OTHER_TYPE), TestContext.Current.CancellationToken);

        Assert.True(result.IsError);
        Assert.Equal(WorkflowVersionErrors.NOT_FOUND_CODE, result.FirstError.Code);
    }

    [Fact]
    public async Task Compare_VersionOfAnotherType_IsRejected()
    {
        WorkflowVersion own = Seed(new string('a', 64), DateTime.UtcNow.AddMinutes(-5), Json(OneBlock("b1", "old prompt")));
        WorkflowVersion foreign = Seed(new string('b', 64), DateTime.UtcNow, Json(OneBlock("b1", "new prompt")));
        foreign.WorkflowTypeId = OTHER_TYPE;
        await _context.SaveChangesAsync(TestContext.Current.CancellationToken);

        var handler = new CompareWorkflowVersionsQueryHandler(_unitOfWork, new WorkflowDiffService());

        foreach ((string from, string to) in new[] { (own.Id, foreign.Id), (foreign.Id, own.Id) })
        {
            ErrorOr<WorkflowDiff> result = await handler.Handle(
                new CompareWorkflowVersionsQuery(ORG, from, to, TYPE), TestContext.Current.CancellationToken);

            Assert.True(result.IsError);
            Assert.Equal(WorkflowVersionErrors.NOT_FOUND_CODE, result.FirstError.Code);
        }
    }

    [Fact]
    public async Task Compare_PreserveArgumentOrder_DiffsFromFirstToSecondArgument()
    {
        WorkflowVersion older = Seed(new string('a', 64), DateTime.UtcNow.AddMinutes(-5), Json(OneBlock("b1", "old prompt")));
        WorkflowVersion newer = Seed(new string('b', 64), DateTime.UtcNow, Json(OneBlock("b1", "new prompt")));
        await _context.SaveChangesAsync(TestContext.Current.CancellationToken);

        var handler = new CompareWorkflowVersionsQueryHandler(_unitOfWork, new WorkflowDiffService());
        ErrorOr<WorkflowDiff> reversed = await handler.Handle(
            new CompareWorkflowVersionsQuery(ORG, newer.Id, older.Id, TYPE, PreserveArgumentOrder: true),
            TestContext.Current.CancellationToken);

        Assert.False(reversed.IsError);
        FieldDiff field = PromptField(reversed.Value);
        Assert.Equal("new prompt", field.Before);
        Assert.Equal("old prompt", field.After);
    }

    [Fact]
    public async Task VersionChanges_CarriesVersionIdsAndBlockIdentity()
    {
        Workflow beforeWorkflow = OneBlock("b1", "old prompt");
        Workflow afterWorkflow = OneBlock("b1", "new prompt");
        string stableBlockId = Guid.NewGuid().ToString("N");
        beforeWorkflow.Sections.First().Blocks.First().StableBlockId = stableBlockId;
        afterWorkflow.Sections.First().Blocks.First().StableBlockId = stableBlockId;

        WorkflowVersion parent = Seed(new string('a', 64), DateTime.UtcNow.AddMinutes(-5), Json(beforeWorkflow));
        await _context.SaveChangesAsync(TestContext.Current.CancellationToken);
        WorkflowVersion child = Seed(new string('b', 64), DateTime.UtcNow, Json(afterWorkflow), parent.Id);
        await _context.SaveChangesAsync(TestContext.Current.CancellationToken);

        var handler = new GetWorkflowVersionChangesQueryHandler(_unitOfWork, new WorkflowDiffService());
        ErrorOr<WorkflowVersionChangesResult> result = await handler.Handle(
            new GetWorkflowVersionChangesQuery(ORG, child.Id, TYPE), TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        Assert.Equal(child.Id, result.Value.VersionId);
        Assert.Equal(parent.Id, result.Value.ParentVersionId);

        BlockDiff block = Assert.Single(result.Value.Diff!.Blocks);
        Assert.Equal("Body", block.SectionName);
        Assert.Equal(stableBlockId, block.StableBlockId);
        Assert.Equal("Block b1", block.DisplayName);
    }

    [Fact]
    public async Task Version_OfAnotherType_AnswersExactlyAsAMissingVersion()
    {
        WorkflowVersion version = Seed(new string('a', 64), DateTime.UtcNow, Json(OneBlock("b1", "prompt")));
        await _context.SaveChangesAsync(TestContext.Current.CancellationToken);

        var handler = new GetWorkflowVersionQueryHandler(_unitOfWork);
        ErrorOr<WorkflowVersionContentDto> foreign = await handler.Handle(
            new GetWorkflowVersionQuery(ORG, version.Id, OTHER_TYPE), TestContext.Current.CancellationToken);
        ErrorOr<WorkflowVersionContentDto> missing = await handler.Handle(
            new GetWorkflowVersionQuery(ORG, "no-such-version", OTHER_TYPE), TestContext.Current.CancellationToken);

        Assert.Equal(missing.FirstError.Code, foreign.FirstError.Code);
        Assert.Equal(missing.FirstError.Type, foreign.FirstError.Type);
        Assert.Equal(
            missing.FirstError.Description.Replace("no-such-version", version.Id, StringComparison.Ordinal),
            foreign.FirstError.Description);
    }

    [Fact]
    public async Task VersionChanges_FailWhenUnavailable_TrimmedVersionOrParent_IsPayloadTrimmedError()
    {
        WorkflowVersion trimmedRoot = SeedTrimmed(new string('a', 64), DateTime.UtcNow.AddMinutes(-10));
        await _context.SaveChangesAsync(TestContext.Current.CancellationToken);
        WorkflowVersion childOfTrimmed = Seed(
            new string('b', 64), DateTime.UtcNow.AddMinutes(-5), Json(OneBlock("b1", "prompt")), trimmedRoot.Id);
        await _context.SaveChangesAsync(TestContext.Current.CancellationToken);
        WorkflowVersion trimmedChild = SeedTrimmed(new string('c', 64), DateTime.UtcNow, childOfTrimmed.Id);
        await _context.SaveChangesAsync(TestContext.Current.CancellationToken);

        var handler = new GetWorkflowVersionChangesQueryHandler(_unitOfWork, new WorkflowDiffService());

        foreach (WorkflowVersion undiffable in new[] { childOfTrimmed, trimmedChild })
        {
            ErrorOr<WorkflowVersionChangesResult> result = await handler.Handle(
                new GetWorkflowVersionChangesQuery(ORG, undiffable.Id, TYPE, FailWhenUnavailable: true),
                TestContext.Current.CancellationToken);

            Assert.True(result.IsError);
            Assert.Equal(WorkflowVersionErrors.PAYLOAD_TRIMMED_CODE, result.FirstError.Code);
            Assert.Equal(ErrorType.NotFound, result.FirstError.Type);
        }

        ErrorOr<WorkflowVersionChangesResult> first = await handler.Handle(
            new GetWorkflowVersionChangesQuery(ORG, trimmedRoot.Id, TYPE, FailWhenUnavailable: true),
            TestContext.Current.CancellationToken);
        Assert.False(first.IsError);
        Assert.Equal(WorkflowVersionChangeStatus.FirstVersion, first.Value.Status);
    }

    [Fact]
    public async Task PayloadAbsentWithoutTrimStamp_IsRefused_NotParsedAsEmpty()
    {
        WorkflowVersion parent = Seed(new string('a', 64), DateTime.UtcNow.AddMinutes(-5), Json(OneBlock("b1", "prompt")));
        await _context.SaveChangesAsync(TestContext.Current.CancellationToken);
        WorkflowVersion damaged = SeedTrimmed(new string('b', 64), DateTime.UtcNow, parent.Id);
        damaged.PayloadTrimmedAt = null;
        await _context.SaveChangesAsync(TestContext.Current.CancellationToken);

        ErrorOr<WorkflowVersionContentDto> content = await new GetWorkflowVersionQueryHandler(_unitOfWork).Handle(
            new GetWorkflowVersionQuery(ORG, damaged.Id, TYPE), TestContext.Current.CancellationToken);
        ErrorOr<WorkflowDiff> compare = await new CompareWorkflowVersionsQueryHandler(_unitOfWork, new WorkflowDiffService()).Handle(
            new CompareWorkflowVersionsQuery(ORG, parent.Id, damaged.Id, TYPE), TestContext.Current.CancellationToken);
        ErrorOr<WorkflowVersionChangesResult> changes = await new GetWorkflowVersionChangesQueryHandler(_unitOfWork, new WorkflowDiffService()).Handle(
            new GetWorkflowVersionChangesQuery(ORG, damaged.Id, TYPE, FailWhenUnavailable: true), TestContext.Current.CancellationToken);

        Assert.Equal(WorkflowVersionErrors.PAYLOAD_MISSING_CODE, content.FirstError.Code);
        Assert.Equal(WorkflowVersionErrors.PAYLOAD_MISSING_CODE, compare.FirstError.Code);
        Assert.Equal(WorkflowVersionErrors.PAYLOAD_MISSING_CODE, changes.FirstError.Code);
    }

    private WorkflowVersion SeedTrimmed(string hash, DateTime createdAt, string? parentVersionId = null)
    {
        var version = new WorkflowVersion
        {
            WorkflowTypeId = TYPE,
            ContentHash = hash,
            WorkflowJsonCompressed = null,
            PayloadTrimmedAt = DateTime.UtcNow,
            AutoNarrativeSummary = "change",
            CreatedByUserId = USER,
            CreatedAt = createdAt,
            ParentVersionId = parentVersionId,
        };
        _ = _context.WorkflowVersions.Add(version);

        return version;
    }

    private WorkflowVersion Seed(string hash, DateTime createdAt, string workflowJson, string? parentVersionId = null)
    {
        var version = new WorkflowVersion
        {
            WorkflowTypeId = TYPE,
            ContentHash = hash,
            WorkflowJson = workflowJson,
            AutoNarrativeSummary = "change",
            CreatedByUserId = USER,
            CreatedAt = createdAt,
            ParentVersionId = parentVersionId,
        };
        _ = _context.WorkflowVersions.Add(version);

        return version;
    }

    private string Json(Workflow workflow)
    {
        return _canonicalizer.ToCanonicalJson(workflow);
    }

    private static Workflow OneBlock(string tag, string prompt)
    {
        var workflow = new Workflow("Report") { Title = "Report", WorkflowTypeId = TYPE };
        var section = new Section("Body", workflow) { Title = "Body" };
        _ = new Block(BlockTypes.AiQuery, section, tag)
        {
            Name = $"Block {tag}",
            Properties = new AiQueryProperties { UserPromptTemplate = prompt },
        };

        return workflow;
    }

    private static Workflow TwoBlocks(string tag1, string prompt1, string tag2, string prompt2)
    {
        var workflow = new Workflow("Report") { Title = "Report", WorkflowTypeId = TYPE };
        var section = new Section("Body", workflow) { Title = "Body" };
        _ = new Block(BlockTypes.AiQuery, section, tag1)
        {
            Name = $"Block {tag1}",
            Properties = new AiQueryProperties { UserPromptTemplate = prompt1 },
        };
        _ = new Block(BlockTypes.AiQuery, section, tag2)
        {
            Name = $"Block {tag2}",
            Properties = new AiQueryProperties { UserPromptTemplate = prompt2 },
        };

        return workflow;
    }
}
