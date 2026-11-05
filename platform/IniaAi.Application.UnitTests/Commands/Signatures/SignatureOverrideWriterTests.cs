using IniaAi.Application.Commands.Signatures;
using IniaAi.Domain.Core.Interfaces;
using IniaAi.Domain.Entities.Signatures;
using IniaAi.Infrastructure.Persistence.Core.Contexts;
using IniaAi.Testing.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System.Text.Json;

namespace IniaAi.Application.UnitTests.Commands.Signatures;

public class SignatureOverrideWriterTests : IDisposable
{
    private const string ORG_ID = "org-test";
    private const string WORKFLOW_TYPE_ID = "WT-Test";
    private const string FIELD_A = "field_a";
    private const string FIELD_B = "field_b";
    private const string PROPOSER_ID = "user-proposer";
    private const string PROPOSER_EMAIL = "proposer@example.com";

    private readonly Microsoft.Extensions.DependencyInjection.ServiceProvider _serviceProvider;
    private readonly CoreDbContext _context;
    private readonly ICoreUnitOfWork _coreUnitOfWork;

    public SignatureOverrideWriterTests()
    {
        var services = new ServiceCollection();
        _ = services.AddTestDatabaseServices();

        _serviceProvider = services.BuildServiceProvider();
        _context = _serviceProvider.GetRequiredService<CoreDbContext>();
        _coreUnitOfWork = _serviceProvider.GetRequiredService<ICoreUnitOfWork>();
    }

    public void Dispose()
    {
        _context.Dispose();
        _serviceProvider.Dispose();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task ApplyUpdates_FirstWrite_EmitsCreated()
    {
        await SignatureOverrideWriter.ApplyUpdatesAsync(
            ORG_ID,
            WORKFLOW_TYPE_ID,
            PROPOSER_ID,
            PROPOSER_EMAIL,
            approvers: [],
            updates: [new SignatureFieldUpdate(FIELD_A, "v1")],
            _coreUnitOfWork,
            TestContext.Current.CancellationToken);
        _ = await _coreUnitOfWork.CompleteAsync(TestContext.Current.CancellationToken);

        SignatureFieldOverride row = await _context.SignatureFieldOverrides
            .AsNoTracking()
            .SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal("v1", row.Value);

        SignatureFieldOverrideAuditLog audit = await _context.SignatureFieldOverrideAuditLogs
            .AsNoTracking()
            .SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal(SignatureAuditAction.CREATED, audit.Action);
        Assert.Null(audit.PreviousValue);
        Assert.Equal("v1", audit.NewValue);
        Assert.Equal(PROPOSER_ID, audit.ActorUserId);
        Assert.Equal(PROPOSER_EMAIL, audit.ActorUserEmail);
        Assert.Equal(WORKFLOW_TYPE_ID, audit.WorkflowTypeId);
        // Post-hoist rows carry no block identity; the legacy column stays null.
        Assert.Null(audit.BlockId);
        Assert.Equal(FIELD_A, audit.PlaceholderName);
    }

    [Fact]
    public async Task ApplyUpdates_ChangeValue_EmitsUpdatedWithPreviousValue()
    {
        await SeedOverrideAsync(FIELD_A, "old");

        await SignatureOverrideWriter.ApplyUpdatesAsync(
            ORG_ID,
            WORKFLOW_TYPE_ID,
            PROPOSER_ID,
            PROPOSER_EMAIL,
            approvers: [],
            updates: [new SignatureFieldUpdate(FIELD_A, "new")],
            _coreUnitOfWork,
            TestContext.Current.CancellationToken);
        _ = await _coreUnitOfWork.CompleteAsync(TestContext.Current.CancellationToken);

        SignatureFieldOverrideAuditLog audit = await _context.SignatureFieldOverrideAuditLogs
            .AsNoTracking()
            .SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal(SignatureAuditAction.UPDATED, audit.Action);
        Assert.Equal("old", audit.PreviousValue);
        Assert.Equal("new", audit.NewValue);
    }

    [Fact]
    public async Task ApplyUpdates_SameValue_EmitsNoAuditRow()
    {
        await SeedOverrideAsync(FIELD_A, "same");

        await SignatureOverrideWriter.ApplyUpdatesAsync(
            ORG_ID,
            WORKFLOW_TYPE_ID,
            PROPOSER_ID,
            PROPOSER_EMAIL,
            approvers: [],
            updates: [new SignatureFieldUpdate(FIELD_A, "same")],
            _coreUnitOfWork,
            TestContext.Current.CancellationToken);
        _ = await _coreUnitOfWork.CompleteAsync(TestContext.Current.CancellationToken);

        int auditCount = await _context.SignatureFieldOverrideAuditLogs
            .AsNoTracking()
            .CountAsync(TestContext.Current.CancellationToken);
        Assert.Equal(0, auditCount);
    }

    [Fact]
    public async Task ApplyUpdates_NullValueOnExistingRow_EmitsRemoved()
    {
        await SeedOverrideAsync(FIELD_A, "existing");

        await SignatureOverrideWriter.ApplyUpdatesAsync(
            ORG_ID,
            WORKFLOW_TYPE_ID,
            PROPOSER_ID,
            PROPOSER_EMAIL,
            approvers: [],
            updates: [new SignatureFieldUpdate(FIELD_A, null)],
            _coreUnitOfWork,
            TestContext.Current.CancellationToken);
        _ = await _coreUnitOfWork.CompleteAsync(TestContext.Current.CancellationToken);

        bool overrideExists = await _context.SignatureFieldOverrides
            .AsNoTracking()
            .AnyAsync(TestContext.Current.CancellationToken);
        Assert.False(overrideExists);

        SignatureFieldOverrideAuditLog audit = await _context.SignatureFieldOverrideAuditLogs
            .AsNoTracking()
            .SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal(SignatureAuditAction.REMOVED, audit.Action);
        Assert.Equal("existing", audit.PreviousValue);
        Assert.Null(audit.NewValue);
    }

    [Fact]
    public async Task ApplyUpdates_NullValueOnMissingRow_EmitsNothing()
    {
        await SignatureOverrideWriter.ApplyUpdatesAsync(
            ORG_ID,
            WORKFLOW_TYPE_ID,
            PROPOSER_ID,
            PROPOSER_EMAIL,
            approvers: [],
            updates: [new SignatureFieldUpdate(FIELD_A, null)],
            _coreUnitOfWork,
            TestContext.Current.CancellationToken);
        _ = await _coreUnitOfWork.CompleteAsync(TestContext.Current.CancellationToken);

        int auditCount = await _context.SignatureFieldOverrideAuditLogs
            .AsNoTracking()
            .CountAsync(TestContext.Current.CancellationToken);
        Assert.Equal(0, auditCount);
    }

    [Fact]
    public async Task ApplyUpdates_NullActor_AcceptedAndPersisted()
    {
        await SignatureOverrideWriter.ApplyUpdatesAsync(
            ORG_ID,
            WORKFLOW_TYPE_ID,
            actorUserId: null,
            actorUserEmail: null,
            approvers: [],
            updates: [new SignatureFieldUpdate(FIELD_A, "v1")],
            _coreUnitOfWork,
            TestContext.Current.CancellationToken);
        _ = await _coreUnitOfWork.CompleteAsync(TestContext.Current.CancellationToken);

        SignatureFieldOverrideAuditLog audit = await _context.SignatureFieldOverrideAuditLogs
            .AsNoTracking()
            .SingleAsync(TestContext.Current.CancellationToken);
        Assert.Null(audit.ActorUserId);
        Assert.Null(audit.ActorUserEmail);
    }

    [Fact]
    public async Task ApplyUpdates_ApproversList_SerializedIntoApproversJson()
    {
        List<SignatureAuditApprover> approvers =
        [
            new("user-1", "u1@example.com"),
            new("user-2", null),
        ];

        await SignatureOverrideWriter.ApplyUpdatesAsync(
            ORG_ID,
            WORKFLOW_TYPE_ID,
            PROPOSER_ID,
            PROPOSER_EMAIL,
            approvers,
            updates: [new SignatureFieldUpdate(FIELD_A, "v1")],
            _coreUnitOfWork,
            TestContext.Current.CancellationToken);
        _ = await _coreUnitOfWork.CompleteAsync(TestContext.Current.CancellationToken);

        SignatureFieldOverrideAuditLog audit = await _context.SignatureFieldOverrideAuditLogs
            .AsNoTracking()
            .SingleAsync(TestContext.Current.CancellationToken);

        Assert.NotNull(audit.ApproversJson);
        // Round-trip through the writer's pinned options so a future global
        // JsonSerializer default change can't silently break the read side's
        // PascalCase deserialize.
        List<SignatureAuditApprover>? roundTripped = JsonSerializer.Deserialize<List<SignatureAuditApprover>>(
            audit.ApproversJson!,
            SignatureOverrideWriter.ApproverJsonOptions);
        Assert.NotNull(roundTripped);
        Assert.Equal(2, roundTripped!.Count);
        Assert.Contains(roundTripped, a => a.UserId == "user-1" && a.Email == "u1@example.com");
        Assert.Contains(roundTripped, a => a.UserId == "user-2" && a.Email is null);
    }

    [Fact]
    public async Task ApplyUpdates_ApproversJson_UsesPascalCaseProperties()
    {
        // The pinned `JsonSerializerOptions { PropertyNamingPolicy = null }` exists
        // specifically to guard against a future global default flip to camelCase
        // silently breaking the round-trip with the query handler's deserializer.
        // Pin the contract explicitly by asserting the raw JSON's property casing.
        await SignatureOverrideWriter.ApplyUpdatesAsync(
            ORG_ID,
            WORKFLOW_TYPE_ID,
            PROPOSER_ID,
            PROPOSER_EMAIL,
            approvers: [new SignatureAuditApprover("user-1", "u1@example.com")],
            updates: [new SignatureFieldUpdate(FIELD_A, "v1")],
            _coreUnitOfWork,
            TestContext.Current.CancellationToken);
        _ = await _coreUnitOfWork.CompleteAsync(TestContext.Current.CancellationToken);

        SignatureFieldOverrideAuditLog audit = await _context.SignatureFieldOverrideAuditLogs
            .AsNoTracking()
            .SingleAsync(TestContext.Current.CancellationToken);
        Assert.NotNull(audit.ApproversJson);
        Assert.Contains("\"UserId\"", audit.ApproversJson);
        Assert.Contains("\"Email\"", audit.ApproversJson);
        Assert.DoesNotContain("\"userId\"", audit.ApproversJson);
        Assert.DoesNotContain("\"email\"", audit.ApproversJson);
    }

    [Fact]
    public async Task ApplyUpdates_EmptyApprovers_SerializesAsEmptyArray()
    {
        await SignatureOverrideWriter.ApplyUpdatesAsync(
            ORG_ID,
            WORKFLOW_TYPE_ID,
            PROPOSER_ID,
            PROPOSER_EMAIL,
            approvers: [],
            updates: [new SignatureFieldUpdate(FIELD_A, "v1")],
            _coreUnitOfWork,
            TestContext.Current.CancellationToken);
        _ = await _coreUnitOfWork.CompleteAsync(TestContext.Current.CancellationToken);

        SignatureFieldOverrideAuditLog audit = await _context.SignatureFieldOverrideAuditLogs
            .AsNoTracking()
            .SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal("[]", audit.ApproversJson);
    }

    [Fact]
    public async Task ApplyUpdates_MixedBatch_EmitsOneRowPerNonNoopChange()
    {
        await SeedOverrideAsync(FIELD_A, "old-a");
        // FIELD_B does not exist yet — expect Created.

        await SignatureOverrideWriter.ApplyUpdatesAsync(
            ORG_ID,
            WORKFLOW_TYPE_ID,
            PROPOSER_ID,
            PROPOSER_EMAIL,
            approvers: [],
            updates:
            [
                new SignatureFieldUpdate(FIELD_A, "new-a"),
                new SignatureFieldUpdate(FIELD_B, "first-b"),
            ],
            _coreUnitOfWork,
            TestContext.Current.CancellationToken);
        _ = await _coreUnitOfWork.CompleteAsync(TestContext.Current.CancellationToken);

        List<SignatureFieldOverrideAuditLog> audits = await _context.SignatureFieldOverrideAuditLogs
            .AsNoTracking()
            .OrderBy(x => x.PlaceholderName)
            .ToListAsync(TestContext.Current.CancellationToken);
        Assert.Equal(2, audits.Count);
        Assert.Equal(SignatureAuditAction.UPDATED, audits[0].Action);
        Assert.Equal("old-a", audits[0].PreviousValue);
        Assert.Equal("new-a", audits[0].NewValue);
        Assert.Equal(SignatureAuditAction.CREATED, audits[1].Action);
        Assert.Null(audits[1].PreviousValue);
        Assert.Equal("first-b", audits[1].NewValue);
    }

    private async Task SeedOverrideAsync(string placeholderName, string value)
    {
        _ = _context.SignatureFieldOverrides.Add(new SignatureFieldOverride
        {
            OrganizationId = ORG_ID,
            WorkflowTypeId = WORKFLOW_TYPE_ID,
            PlaceholderName = placeholderName,
            Value = value,
        });
        _ = await _context.SaveChangesAsync(TestContext.Current.CancellationToken);
    }
}
