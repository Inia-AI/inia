using ErrorOr;
using IniaAi.Application.Queries.Signatures.GetSignatureHistory;
using IniaAi.Domain.Core.Interfaces;
using IniaAi.Domain.Entities.Signatures;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Infrastructure.Persistence.Core.Contexts;
using IniaAi.Testing.DependencyInjection;
using IniaAi.WebApi.Contracts.Responses.Signatures;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using System.Text.Json;

namespace IniaAi.Application.UnitTests.Queries.Signatures.GetSignatureHistory;

public class GetSignatureHistoryQueryHandlerTests : IDisposable
{
    private const string ORG_ID = "org-test";
    private const string OTHER_ORG_ID = "org-other";
    private const string USER_ID = "user-test";
    private const string WORKFLOW_TYPE_ID = "WT-Test";
    private const string OTHER_WORKFLOW_TYPE_ID = "WT-Other";
    private const string BLOCK_ID = "block-1";
    private const string FIELD_A = "field_a";
    private const string FIELD_B = "field_b";

    private readonly Microsoft.Extensions.DependencyInjection.ServiceProvider _serviceProvider;
    private readonly CoreDbContext _context;
    private readonly GetSignatureHistoryQueryHandler _handler;

    public GetSignatureHistoryQueryHandlerTests()
    {
        var services = new ServiceCollection();
        _ = services.AddTestDatabaseServices();

        _serviceProvider = services.BuildServiceProvider();
        _context = _serviceProvider.GetRequiredService<CoreDbContext>();
        ICoreUnitOfWork unitOfWork = _serviceProvider.GetRequiredService<ICoreUnitOfWork>();
        _handler = new GetSignatureHistoryQueryHandler(
            unitOfWork,
            NullLogger<GetSignatureHistoryQueryHandler>.Instance);
    }

    public void Dispose()
    {
        _context.Dispose();
        _serviceProvider.Dispose();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task Handle_NoAccess_ReturnsNotFound()
    {
        // No WorkflowTypeOrganizationAccess seeded — access check should fail.
        var query = new GetSignatureHistoryQuery(ORG_ID, USER_ID, WORKFLOW_TYPE_ID);

        ErrorOr<SignatureHistoryPageDto> result = await _handler.Handle(
            query, TestContext.Current.CancellationToken);

        Assert.True(result.IsError);
        Assert.Equal(ErrorType.NotFound, result.FirstError.Type);
    }

    [Fact]
    public async Task Handle_ReturnsRowsNewestFirst_WithApproversDeserialized()
    {
        await SeedAccessAsync(ORG_ID, WORKFLOW_TYPE_ID);

        DateTime t0 = DateTime.UtcNow.AddMinutes(-30);
        DateTime t1 = DateTime.UtcNow.AddMinutes(-20);
        DateTime t2 = DateTime.UtcNow.AddMinutes(-10);

        SeedAuditRow(WORKFLOW_TYPE_ID, FIELD_A, SignatureAuditAction.CREATED, previous: null, @new: "first", actorId: "alice", actorEmail: "alice@example.com", approvers: [new("alice", "alice@example.com")], occurredAt: t0);
        SeedAuditRow(WORKFLOW_TYPE_ID, FIELD_A, SignatureAuditAction.UPDATED, previous: "first", @new: "second", actorId: "bob", actorEmail: "bob@example.com", approvers: [new("bob", "bob@example.com"), new("alice", "alice@example.com")], occurredAt: t1);
        SeedAuditRow(WORKFLOW_TYPE_ID, FIELD_A, SignatureAuditAction.REMOVED, previous: "second", @new: null, actorId: "carol", actorEmail: "carol@example.com", approvers: [new("carol", "carol@example.com")], occurredAt: t2);
        await _context.SaveChangesAsync(TestContext.Current.CancellationToken);

        var query = new GetSignatureHistoryQuery(ORG_ID, USER_ID, WORKFLOW_TYPE_ID);
        ErrorOr<SignatureHistoryPageDto> result = await _handler.Handle(
            query, TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        Assert.Equal(3, result.Value.TotalCount);
        Assert.Equal(3, result.Value.Items.Count);
        Assert.Equal(SignatureAuditAction.REMOVED, result.Value.Items[0].Action);
        Assert.Equal(SignatureAuditAction.UPDATED, result.Value.Items[1].Action);
        Assert.Equal(SignatureAuditAction.CREATED, result.Value.Items[2].Action);

        // Approvers on the second row should round-trip.
        Assert.Equal(2, result.Value.Items[1].Approvers.Count);
        Assert.Contains(result.Value.Items[1].Approvers, a => a.UserId == "bob");
        Assert.Contains(result.Value.Items[1].Approvers, a => a.UserId == "alice");
    }

    [Fact]
    public async Task Handle_FilterByPlaceholder_NarrowsResults()
    {
        await SeedAccessAsync(ORG_ID, WORKFLOW_TYPE_ID);
        SeedAuditRow(WORKFLOW_TYPE_ID, FIELD_A, SignatureAuditAction.CREATED, previous: null, @new: "v-a", actorId: "u", actorEmail: "u@e.com", approvers: [], occurredAt: DateTime.UtcNow);
        SeedAuditRow(WORKFLOW_TYPE_ID, FIELD_B, SignatureAuditAction.CREATED, previous: null, @new: "v-b", actorId: "u", actorEmail: "u@e.com", approvers: [], occurredAt: DateTime.UtcNow);
        await _context.SaveChangesAsync(TestContext.Current.CancellationToken);

        var query = new GetSignatureHistoryQuery(ORG_ID, USER_ID, WORKFLOW_TYPE_ID, PlaceholderName: FIELD_B);
        ErrorOr<SignatureHistoryPageDto> result = await _handler.Handle(
            query, TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        Assert.Equal(1, result.Value.TotalCount);
        Assert.Equal(FIELD_B, result.Value.Items.Single().PlaceholderName);
    }

    [Fact]
    public async Task Handle_FilterByActor_NarrowsResults()
    {
        await SeedAccessAsync(ORG_ID, WORKFLOW_TYPE_ID);
        SeedAuditRow(WORKFLOW_TYPE_ID, FIELD_A, SignatureAuditAction.CREATED, previous: null, @new: "v-alice", actorId: "alice", actorEmail: "a@e.com", approvers: [], occurredAt: DateTime.UtcNow);
        SeedAuditRow(WORKFLOW_TYPE_ID, FIELD_A, SignatureAuditAction.UPDATED, previous: "v-alice", @new: "v-bob", actorId: "bob", actorEmail: "b@e.com", approvers: [], occurredAt: DateTime.UtcNow);
        await _context.SaveChangesAsync(TestContext.Current.CancellationToken);

        var query = new GetSignatureHistoryQuery(ORG_ID, USER_ID, WORKFLOW_TYPE_ID, ActorUserId: "alice");
        ErrorOr<SignatureHistoryPageDto> result = await _handler.Handle(
            query, TestContext.Current.CancellationToken);

        Assert.Equal(1, result.Value.TotalCount);
        Assert.Equal("alice", result.Value.Items.Single().ActorUserId);
    }

    [Fact]
    public async Task Handle_FilterByDateRange_NarrowsResults()
    {
        await SeedAccessAsync(ORG_ID, WORKFLOW_TYPE_ID);
        DateTime t0 = DateTime.UtcNow.AddDays(-5);
        DateTime t1 = DateTime.UtcNow.AddDays(-3);
        DateTime t2 = DateTime.UtcNow.AddDays(-1);

        SeedAuditRow(WORKFLOW_TYPE_ID, FIELD_A, SignatureAuditAction.CREATED, previous: null, @new: "old", actorId: "u", actorEmail: "u@e.com", approvers: [], occurredAt: t0);
        SeedAuditRow(WORKFLOW_TYPE_ID, FIELD_A, SignatureAuditAction.UPDATED, previous: "old", @new: "mid", actorId: "u", actorEmail: "u@e.com", approvers: [], occurredAt: t1);
        SeedAuditRow(WORKFLOW_TYPE_ID, FIELD_A, SignatureAuditAction.UPDATED, previous: "mid", @new: "new", actorId: "u", actorEmail: "u@e.com", approvers: [], occurredAt: t2);
        await _context.SaveChangesAsync(TestContext.Current.CancellationToken);

        var query = new GetSignatureHistoryQuery(
            ORG_ID,
            USER_ID,
            WORKFLOW_TYPE_ID,
            FromUtc: DateTime.UtcNow.AddDays(-4),
            ToUtc: DateTime.UtcNow.AddDays(-2));

        ErrorOr<SignatureHistoryPageDto> result = await _handler.Handle(
            query, TestContext.Current.CancellationToken);

        Assert.Equal(1, result.Value.TotalCount);
        Assert.Equal("mid", result.Value.Items.Single().NewValue);
    }

    [Fact]
    public async Task Handle_OrgScoped_DoesNotLeakOtherTenants()
    {
        await SeedAccessAsync(ORG_ID, WORKFLOW_TYPE_ID);
        await SeedAccessAsync(OTHER_ORG_ID, WORKFLOW_TYPE_ID);

        SeedAuditRow(WORKFLOW_TYPE_ID, FIELD_A, SignatureAuditAction.CREATED, previous: null, @new: "ours", actorId: "u1", actorEmail: "u1@e.com", approvers: [], occurredAt: DateTime.UtcNow, orgId: ORG_ID);
        SeedAuditRow(WORKFLOW_TYPE_ID, FIELD_A, SignatureAuditAction.CREATED, previous: null, @new: "theirs", actorId: "u2", actorEmail: "u2@e.com", approvers: [], occurredAt: DateTime.UtcNow, orgId: OTHER_ORG_ID);
        await _context.SaveChangesAsync(TestContext.Current.CancellationToken);

        var query = new GetSignatureHistoryQuery(ORG_ID, USER_ID, WORKFLOW_TYPE_ID);
        ErrorOr<SignatureHistoryPageDto> result = await _handler.Handle(
            query, TestContext.Current.CancellationToken);

        Assert.Equal(1, result.Value.TotalCount);
        Assert.Equal("ours", result.Value.Items.Single().NewValue);
    }

    [Fact]
    public async Task Handle_WorkflowTypeScoped_DoesNotLeakOtherWorkflows()
    {
        await SeedAccessAsync(ORG_ID, WORKFLOW_TYPE_ID);
        await SeedAccessAsync(ORG_ID, OTHER_WORKFLOW_TYPE_ID);

        SeedAuditRow(WORKFLOW_TYPE_ID, FIELD_A, SignatureAuditAction.CREATED, previous: null, @new: "this-wf", actorId: "u", actorEmail: "u@e.com", approvers: [], occurredAt: DateTime.UtcNow);
        SeedAuditRow(OTHER_WORKFLOW_TYPE_ID, FIELD_A, SignatureAuditAction.CREATED, previous: null, @new: "other-wf", actorId: "u", actorEmail: "u@e.com", approvers: [], occurredAt: DateTime.UtcNow);
        await _context.SaveChangesAsync(TestContext.Current.CancellationToken);

        var query = new GetSignatureHistoryQuery(ORG_ID, USER_ID, WORKFLOW_TYPE_ID);
        ErrorOr<SignatureHistoryPageDto> result = await _handler.Handle(
            query, TestContext.Current.CancellationToken);

        Assert.Equal(1, result.Value.TotalCount);
        Assert.Equal("this-wf", result.Value.Items.Single().NewValue);
    }

    [Fact]
    public async Task Handle_Pagination_RespectsSkipAndTake()
    {
        await SeedAccessAsync(ORG_ID, WORKFLOW_TYPE_ID);
        for (int i = 0; i < 5; i++)
        {
            SeedAuditRow(
                WORKFLOW_TYPE_ID,
                FIELD_A,
                SignatureAuditAction.UPDATED,
                previous: $"v{i - 1}",
                @new: $"v{i}",
                actorId: "u",
                actorEmail: "u@e.com",
                approvers: [],
                occurredAt: DateTime.UtcNow.AddMinutes(i));
        }

        await _context.SaveChangesAsync(TestContext.Current.CancellationToken);

        var query = new GetSignatureHistoryQuery(ORG_ID, USER_ID, WORKFLOW_TYPE_ID, Skip: 1, Take: 2);
        ErrorOr<SignatureHistoryPageDto> result = await _handler.Handle(
            query, TestContext.Current.CancellationToken);

        Assert.Equal(5, result.Value.TotalCount);
        Assert.Equal(2, result.Value.Items.Count);
        // Skip 1 (newest), take 2 (second + third newest); v3 then v2.
        Assert.Equal("v3", result.Value.Items[0].NewValue);
        Assert.Equal("v2", result.Value.Items[1].NewValue);
    }

    [Fact]
    public async Task Handle_MalformedApproversJson_ReturnsEmptyApproverListWithoutThrowing()
    {
        // Defensive: a corrupt `ApproversJson` (e.g. a column truncated by a buggy
        // future writer) should not blow up the History page. The handler catches
        // `JsonException`, logs a warning, and projects the row with an empty
        // approver list so the rest of the page still renders.
        await SeedAccessAsync(ORG_ID, WORKFLOW_TYPE_ID);
        _ = _context.SignatureFieldOverrideAuditLogs.Add(new SignatureFieldOverrideAuditLog
        {
            OrganizationId = ORG_ID,
            WorkflowTypeId = WORKFLOW_TYPE_ID,
            BlockId = BLOCK_ID,
            PlaceholderName = FIELD_A,
            Action = SignatureAuditAction.CREATED,
            NewValue = "v",
            ActorUserId = "u",
            ActorUserEmail = "u@e.com",
            ApproversJson = "{not json",
            CreatedAt = DateTime.UtcNow,
        });
        await _context.SaveChangesAsync(TestContext.Current.CancellationToken);

        var query = new GetSignatureHistoryQuery(ORG_ID, USER_ID, WORKFLOW_TYPE_ID);
        ErrorOr<SignatureHistoryPageDto> result = await _handler.Handle(
            query, TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        Assert.Equal(1, result.Value.TotalCount);
        Assert.Empty(result.Value.Items.Single().Approvers);
    }

    [Fact]
    public async Task Handle_RowWithReferencedBlockNames_SurfacesParsedNamesAndNullBlockId()
    {
        // Post-hoist row: no block association, block context carried as a JSON name snapshot.
        await SeedAccessAsync(ORG_ID, WORKFLOW_TYPE_ID);
        _ = _context.SignatureFieldOverrideAuditLogs.Add(new SignatureFieldOverrideAuditLog
        {
            OrganizationId = ORG_ID,
            WorkflowTypeId = WORKFLOW_TYPE_ID,
            BlockId = null,
            PlaceholderName = FIELD_A,
            Action = SignatureAuditAction.CREATED,
            NewValue = "v",
            ActorUserId = "u",
            ActorUserEmail = "u@e.com",
            ReferencedBlockNamesJson = JsonSerializer.Serialize(new List<string> { "Block A", "Block B" }),
            CreatedAt = DateTime.UtcNow,
        });
        await _context.SaveChangesAsync(TestContext.Current.CancellationToken);

        var query = new GetSignatureHistoryQuery(ORG_ID, USER_ID, WORKFLOW_TYPE_ID);
        ErrorOr<SignatureHistoryPageDto> result = await _handler.Handle(
            query, TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        SignatureHistoryEntryDto entry = result.Value.Items.Single();
        Assert.Null(entry.BlockId);
        Assert.NotNull(entry.UsedByBlockNames);
        Assert.Equal(["Block A", "Block B"], entry.UsedByBlockNames);
    }

    [Fact]
    public async Task Handle_LegacyRowWithBlockId_SurfacesBlockIdAndNullUsedByBlockNames()
    {
        // Pre-hoist row: frozen block association, no referenced-name snapshot.
        await SeedAccessAsync(ORG_ID, WORKFLOW_TYPE_ID);
        _ = _context.SignatureFieldOverrideAuditLogs.Add(new SignatureFieldOverrideAuditLog
        {
            OrganizationId = ORG_ID,
            WorkflowTypeId = WORKFLOW_TYPE_ID,
            BlockId = BLOCK_ID,
            PlaceholderName = FIELD_A,
            Action = SignatureAuditAction.CREATED,
            NewValue = "v",
            ActorUserId = "u",
            ActorUserEmail = "u@e.com",
            ReferencedBlockNamesJson = null,
            CreatedAt = DateTime.UtcNow,
        });
        await _context.SaveChangesAsync(TestContext.Current.CancellationToken);

        var query = new GetSignatureHistoryQuery(ORG_ID, USER_ID, WORKFLOW_TYPE_ID);
        ErrorOr<SignatureHistoryPageDto> result = await _handler.Handle(
            query, TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        SignatureHistoryEntryDto entry = result.Value.Items.Single();
        Assert.Equal(BLOCK_ID, entry.BlockId);
        Assert.Null(entry.UsedByBlockNames);
    }

    private async Task SeedAccessAsync(string orgId, string workflowTypeId)
    {
        _ = _context.WorkflowTypeOrganizationAccess.Add(new WorkflowTypeOrganizationAccess
        {
            WorkflowTypeId = workflowTypeId,
            OrganizationId = orgId,
        });
        _ = await _context.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    private void SeedAuditRow(
        string workflowTypeId,
        string placeholderName,
        string action,
        string? previous,
        string? @new,
        string actorId,
        string actorEmail,
        List<SignatureHistoryApproverDto> approvers,
        DateTime occurredAt,
        string? orgId = null)
    {
        _ = _context.SignatureFieldOverrideAuditLogs.Add(new SignatureFieldOverrideAuditLog
        {
            OrganizationId = orgId ?? ORG_ID,
            WorkflowTypeId = workflowTypeId,
            BlockId = BLOCK_ID,
            PlaceholderName = placeholderName,
            Action = action,
            PreviousValue = previous,
            NewValue = @new,
            ActorUserId = actorId,
            ActorUserEmail = actorEmail,
            ApproversJson = JsonSerializer.Serialize(approvers),
            CreatedAt = occurredAt,
        });
    }
}
