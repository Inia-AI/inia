using ErrorOr;
using IniaAi.Application.Common.Interfaces.Services;
using IniaAi.Application.Errors;
using IniaAi.Common.Enums;
using IniaAi.Domain.Entities.Documents;
using IniaAi.Domain.Entities.Signatures;
using IniaAi.Domain.Entities.UserManagement;
using IniaAi.Domain.Entities.Validation;
using IniaAi.Domain.Entities.WorkflowExecutions;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Blocks;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties;
using IniaAi.Domain.Entities.Workflows.Enums;
using IniaAi.Domain.Enums;
using IniaAi.Infrastructure.Services;
using IniaAi.Infrastructure.Tests.Helpers;
using IniaAi.Testing.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace IniaAi.Infrastructure.Tests.Services;

/// <summary>
/// Deleting a workflow type: retiring hides it from its one organization at once and soft-deletes
/// what that organization ran with it, and the purge removes the type from Core only after the
/// retention window, and only when the type was retired.
/// </summary>
public sealed class WorkflowTypeRetirementTests : IDisposable
{
    private const string ORG = "org-retire";
    private const string OTHER_ORG = "org-other";
    private const string TYPE = "tmw_test_detect";
    private const string OTHER_TYPE = "tmw_dev_detect";
    private const string USER = "user-1";
    private const string MODULE_TYPE = "module_monitoring";
    private const int RETENTION_DAYS = 30;

    private static readonly DateTime _pastRetention = DateTime.UtcNow.AddDays(-(RETENTION_DAYS + 1));
    private static readonly DateTime _insideRetention = DateTime.UtcNow.AddDays(-(RETENTION_DAYS - 1));

    private readonly SqliteCoreDatabase _coreDatabase = new();
    private readonly SqliteCleanupServiceHarness _cleanup = new();
    private readonly SqliteCoreDbContext _core;
    private readonly SqliteUserDataDbContext _userData;
    private readonly WorkflowTypeRetirement _retirement;

    public WorkflowTypeRetirementTests()
    {
        _core = _coreDatabase.Context;
        _userData = _cleanup.Context;
        _userData.Organizations.AddRange(new Organization("Retiring", ORG), new Organization("Other", OTHER_ORG));
        _ = _userData.Users.Add(new User { Id = USER, OrganizationId = ORG, UserName = USER, FirstName = "Prompt", LastName = "Engineer" });
        _ = _userData.SaveChanges();
        _userData.ChangeTracker.Clear();

        _retirement = new WorkflowTypeRetirement(
            _core,
            _userData,
            new ModuleWorkflowTypes([new ModuleWorkflowType(MODULE_TYPE, MODULE_TYPE, () => new Workflow(MODULE_TYPE))]),
            NullLogger<WorkflowTypeRetirement>.Instance);
    }

    public void Dispose()
    {
        _coreDatabase.Dispose();
        _cleanup.Dispose();
    }

    [Fact]
    public async Task Retire_StampsTheType_AndHidesItAndItsTemplateFromTheOrganization()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        Workflow template = await SeedTypeAsync(TYPE, [ORG], ct);

        ErrorOr<Deleted> result = await _retirement.RetireAsync(ORG, USER, TYPE, ct);

        Assert.False(result.IsError);
        _core.ChangeTracker.Clear();
        WorkflowType retired = await _core.WorkflowTypes.SingleAsync(type => type.Id == TYPE, ct);
        Assert.NotNull(retired.RetiredAt);
        Assert.False(await _core.WorkflowTypeOrganizationAccess.AnyAsync(access => access.WorkflowTypeId == TYPE, ct));
        WorkflowTypeOrganizationAccess hidden = await _core.WorkflowTypeOrganizationAccess.IgnoreQueryFilters()
            .SingleAsync(access => access.WorkflowTypeId == TYPE, ct);
        Assert.True(hidden.IsDeleted);
        Assert.NotNull(hidden.DeletedAt);
        Assert.False(await _core.Workflows.AnyAsync(workflow => workflow.Id == template.Id, ct));
        Assert.True(await _core.Workflows.IgnoreQueryFilters().AnyAsync(workflow => workflow.Id == template.Id && workflow.IsDeleted, ct));
    }

    [Fact]
    public async Task Retire_SoftDeletesWhatTheOrganizationRanWithTheType_AndNothingElse()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        _ = await SeedTypeAsync(TYPE, [ORG], ct);

        WorkflowExecution run = SeedRun(ORG, TYPE, WorkflowState.Confirmed, out Workflow executedClone, out WorkflowReport report);
        WorkflowExecution otherTypeRun = SeedRun(ORG, OTHER_TYPE, WorkflowState.Confirmed, out Workflow otherClone, out _);
        WorkflowExecution otherOrgRun = SeedRun(OTHER_ORG, TYPE, WorkflowState.Confirmed, out _, out _);
        Document uploaded = SeedDocument(ORG, TYPE);
        Document sharedWithOtherType = SeedDocument(ORG, TYPE);
        _ = _userData.DocumentInfos.Add(new DocumentInfo(sharedWithOtherType.Id) { JobId = otherTypeRun.JobId });
        ValidationCase validationCase = SeedValidationCase(ORG, TYPE);
        _ = _userData.UserWorkflowTypeVisibilities.Add(new UserWorkflowTypeVisibility { UserId = USER, WorkflowTypeId = TYPE });
        _ = _userData.UserWorkflowTypeVisibilities.Add(new UserWorkflowTypeVisibility { UserId = USER, WorkflowTypeId = OTHER_TYPE });
        _ = await _userData.SaveChangesAsync(ct);
        _userData.ChangeTracker.Clear();

        ErrorOr<Deleted> result = await _retirement.RetireAsync(ORG, USER, TYPE, ct);

        Assert.False(result.IsError);
        _userData.ChangeTracker.Clear();
        Assert.True(await IsSoftDeletedAsync(_userData.WorkflowExecutions, run.Id, ct));
        Assert.True(await IsSoftDeletedAsync(_userData.Workflows, executedClone.Id, ct));
        Assert.True(await IsSoftDeletedAsync(_userData.WorkflowReports, report.Id, ct));
        Assert.True(await IsSoftDeletedAsync(_userData.Documents, uploaded.Id, ct));
        Assert.True(await IsSoftDeletedAsync(_userData.ValidationCases, validationCase.Id, ct));
        Assert.False(await IsSoftDeletedAsync(_userData.Documents, sharedWithOtherType.Id, ct));
        Assert.False(await IsSoftDeletedAsync(_userData.WorkflowExecutions, otherTypeRun.Id, ct));
        Assert.False(await IsSoftDeletedAsync(_userData.Workflows, otherClone.Id, ct));
        Assert.False(await IsSoftDeletedAsync(_userData.WorkflowExecutions, otherOrgRun.Id, ct));
        Assert.Equal(
            [OTHER_TYPE],
            await _userData.UserWorkflowTypeVisibilities.Select(visibility => visibility.WorkflowTypeId).ToListAsync(ct));
    }

    [Fact]
    public async Task Retire_RefusesATypeAnotherOrganizationCanReach()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        _ = await SeedTypeAsync(TYPE, [ORG, OTHER_ORG], ct);

        ErrorOr<Deleted> result = await _retirement.RetireAsync(ORG, USER, TYPE, ct);

        Assert.True(result.IsError);
        Assert.Equal(ErrorType.Conflict, result.FirstError.Type);
        Assert.Equal(WorkflowTypeErrorFactory.SHARED_CODE, result.FirstError.Code);
        await AssertUntouchedAsync(TYPE, accessRows: 2, ct);
    }

    [Fact]
    public async Task Retire_RefusesWhileARunOfTheTypeIsActive()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        _ = await SeedTypeAsync(TYPE, [ORG], ct);
        WorkflowExecution running = SeedRun(ORG, TYPE, WorkflowState.Running, out _, out _);
        _ = await _userData.SaveChangesAsync(ct);
        _userData.ChangeTracker.Clear();

        ErrorOr<Deleted> result = await _retirement.RetireAsync(ORG, USER, TYPE, ct);

        Assert.True(result.IsError);
        Assert.Equal(ErrorType.Conflict, result.FirstError.Type);
        Assert.Equal(WorkflowTypeErrorFactory.RUN_IN_PROGRESS_CODE, result.FirstError.Code);
        await AssertUntouchedAsync(TYPE, accessRows: 1, ct);
        Assert.False(await IsSoftDeletedAsync(_userData.WorkflowExecutions, running.Id, ct));
    }

    [Fact]
    public async Task Retire_ReportsNotFound_ForATypeTheOrganizationCannotReach()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        _ = await SeedTypeAsync(TYPE, [], ct);
        _ = await SeedTypeAsync(OTHER_TYPE, [OTHER_ORG], ct);

        ErrorOr<Deleted> unreachable = await _retirement.RetireAsync(ORG, USER, TYPE, ct);
        ErrorOr<Deleted> othersOnly = await _retirement.RetireAsync(ORG, USER, OTHER_TYPE, ct);
        ErrorOr<Deleted> missing = await _retirement.RetireAsync(ORG, USER, "no-such-type", ct);

        Assert.Equal(WorkflowTypeErrorFactory.NOT_FOUND_CODE, unreachable.FirstError.Code);
        Assert.Equal(ErrorType.NotFound, unreachable.FirstError.Type);
        Assert.Equal(WorkflowTypeErrorFactory.NOT_FOUND_CODE, othersOnly.FirstError.Code);
        Assert.Equal(WorkflowTypeErrorFactory.NOT_FOUND_CODE, missing.FirstError.Code);
        await AssertUntouchedAsync(TYPE, accessRows: 0, ct);
        await AssertUntouchedAsync(OTHER_TYPE, accessRows: 1, ct);
    }

    [Fact]
    public async Task Retire_ReportsNotFound_ForATypeAlreadyRetired()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        _ = await SeedTypeAsync(TYPE, [ORG], ct);
        _ = await _retirement.RetireAsync(ORG, USER, TYPE, ct);

        ErrorOr<Deleted> again = await _retirement.RetireAsync(ORG, USER, TYPE, ct);

        Assert.Equal(WorkflowTypeErrorFactory.NOT_FOUND_CODE, again.FirstError.Code);
    }

    [Fact]
    public async Task Retire_RefusesAReservedType()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        _ = await SeedTypeAsync(WorkflowType.GENERIC, [ORG], ct);

        ErrorOr<Deleted> result = await _retirement.RetireAsync(ORG, USER, WorkflowType.GENERIC, ct);

        Assert.Equal(ErrorType.Validation, result.FirstError.Type);
        Assert.Equal(WorkflowTypeErrorFactory.RESERVED_CODE, result.FirstError.Code);
        await AssertUntouchedAsync(WorkflowType.GENERIC, accessRows: 1, ct);
    }

    [Fact]
    public async Task Retire_RefusesAModuleType_WhichTheSeedingWouldRecreate()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        _ = await SeedTypeAsync(MODULE_TYPE, [ORG], ct);

        ErrorOr<Deleted> result = await _retirement.RetireAsync(ORG, USER, MODULE_TYPE, ct);

        Assert.Equal(WorkflowTypeErrorFactory.RESERVED_CODE, result.FirstError.Code);
        await AssertUntouchedAsync(MODULE_TYPE, accessRows: 1, ct);
    }

    [Fact]
    public async Task Retire_HidesTheTypesVersionsOverridesAndSchemas_AndStampsTheDeletingUser()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        _ = await SeedTypeAsync(TYPE, [ORG], ct);
        WorkflowVersion version = SeedVersion(TYPE, parentVersionId: null);
        SeedSignatureRows(TYPE, out SignatureFieldOverride signatureOverride, out PendingSignatureOverride pending, out _);
        ValidationSchema schema = SeedValidationSchema(TYPE);
        _ = await _core.SaveChangesAsync(ct);
        WorkflowExecution run = SeedRun(ORG, TYPE, WorkflowState.Confirmed, out _, out _);
        _ = await _userData.SaveChangesAsync(ct);
        _core.ChangeTracker.Clear();
        _userData.ChangeTracker.Clear();

        _ = await _retirement.RetireAsync(ORG, USER, TYPE, ct);

        Assert.True(await IsSoftDeletedByAsync(_core.WorkflowVersions, version.Id, ct));
        Assert.True(await IsSoftDeletedByAsync(_core.SignatureFieldOverrides, signatureOverride.Id, ct));
        Assert.True(await IsSoftDeletedByAsync(_core.PendingSignatureOverrides, pending.Id, ct));
        Assert.True(await IsSoftDeletedByAsync(_core.ValidationSchemas, schema.Id, ct));
        Assert.True(await IsSoftDeletedByAsync(_core.WorkflowTypeOrganizationAccess, (await _core.WorkflowTypeOrganizationAccess.IgnoreQueryFilters().SingleAsync(access => access.WorkflowTypeId == TYPE, ct)).Id, ct));
        Assert.True(await IsSoftDeletedByAsync(_userData.WorkflowExecutions, run.Id, ct));
    }

    [Fact]
    public async Task Retire_ThenTheCleanup_RemovesTheTypesDocumentsWithTheirStoredFiles()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        _ = await SeedTypeAsync(TYPE, [ORG], ct);
        Document uploaded = SeedDocument(ORG, TYPE);
        var storedBytes = new Domain.Entities.Documents.File { OrganizationId = ORG, Name = "consent.pdf" };
        _ = _userData.Files.Add(storedBytes);
        _ = await _userData.SaveChangesAsync(ct);
        string fileMetadataId = uploaded.FileMetadataId;
        _ = await _userData.FileMetadata
            .Where(file => file.Id == fileMetadataId)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(file => file.StorageProvider, StorageProvider.Database)
                .SetProperty(file => file.ExternalStoragePath, storedBytes.Id), ct);
        _userData.ChangeTracker.Clear();
        _ = await _retirement.RetireAsync(ORG, USER, TYPE, ct);
        _ = await _userData.Documents.IgnoreQueryFilters()
            .Where(document => document.Id == uploaded.Id)
            .ExecuteUpdateAsync(setters => setters.SetProperty(document => document.DeletedAt, _pastRetention), ct);
        _ = await _userData.FileMetadata.IgnoreQueryFilters()
            .Where(file => file.Id == fileMetadataId)
            .ExecuteUpdateAsync(setters => setters.SetProperty(file => file.DeletedAt, _pastRetention), ct);
        _ = await _userData.Files.IgnoreQueryFilters()
            .Where(file => file.Id == storedBytes.Id)
            .ExecuteUpdateAsync(setters => setters.SetProperty(file => file.DeletedAt, _pastRetention), ct);

        CleanupResult cleanup = await _cleanup.Service.CleanupAsync(ORG, RETENTION_DAYS, ct);

        Assert.Empty(cleanup.Errors);
        Assert.False(await _userData.Documents.IgnoreQueryFilters().AnyAsync(document => document.Id == uploaded.Id, ct));
        Assert.False(await _userData.FileMetadata.IgnoreQueryFilters().AnyAsync(file => file.Id == fileMetadataId, ct));
        Assert.False(await _userData.Files.IgnoreQueryFilters().AnyAsync(file => file.Id == storedBytes.Id, ct));
    }

    [Fact]
    public async Task Purge_RemovesARetiredTypePastRetention_WithItsTemplateVersionsAndOverrides_AndKeepsItsAuditLog()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        Workflow template = await SeedTypeAsync(TYPE, [ORG], ct);
        Workflow otherTemplate = await SeedTypeAsync(OTHER_TYPE, [ORG], ct);
        string sectionId = template.Sections.Single().Id;
        WorkflowVersion root = SeedVersion(TYPE, parentVersionId: null);
        WorkflowVersion head = SeedVersion(TYPE, parentVersionId: root.Id);
        WorkflowVersion otherVersion = SeedVersion(OTHER_TYPE, parentVersionId: null);
        SeedSignatureRows(TYPE, out SignatureFieldOverride signatureOverride, out PendingSignatureOverride pending, out SignatureFieldOverrideAuditLog auditLog);
        ValidationSchema schema = SeedValidationSchema(TYPE);
        _ = await _core.SaveChangesAsync(ct);
        _core.ChangeTracker.Clear();
        _ = await _retirement.RetireAsync(ORG, USER, TYPE, ct);
        await BackdateRetirementAsync(TYPE, _pastRetention, ct);

        RetiredWorkflowTypePurgeResult result = await _retirement.PurgeRetiredAsync(ORG, RETENTION_DAYS, ct);

        Assert.Empty(result.Errors);
        Assert.Equal([TYPE], result.PurgedWorkflowTypeIds);
        _core.ChangeTracker.Clear();
        Assert.False(await _core.WorkflowTypes.AnyAsync(type => type.Id == TYPE, ct));
        Assert.False(await _core.WorkflowTypeOrganizationAccess.IgnoreQueryFilters().AnyAsync(access => access.WorkflowTypeId == TYPE, ct));
        Assert.False(await _core.Workflows.IgnoreQueryFilters().AnyAsync(workflow => workflow.Id == template.Id, ct));
        Assert.False(await _core.Sections.IgnoreQueryFilters().AnyAsync(section => section.Id == sectionId, ct));
        Assert.False(await _core.Blocks.IgnoreQueryFilters().AnyAsync(block => block.SectionId == sectionId, ct));
        Assert.False(await _core.WorkflowVersions.IgnoreQueryFilters().AnyAsync(version => version.Id == root.Id || version.Id == head.Id, ct));
        Assert.False(await _core.SignatureFieldOverrides.IgnoreQueryFilters().AnyAsync(row => row.Id == signatureOverride.Id, ct));
        Assert.False(await _core.PendingSignatureOverrides.IgnoreQueryFilters().AnyAsync(row => row.Id == pending.Id, ct));
        Assert.False(await _core.ValidationSchemas.IgnoreQueryFilters().AnyAsync(row => row.Id == schema.Id, ct));
        Assert.True(await _core.SignatureFieldOverrideAuditLogs.IgnoreQueryFilters().AnyAsync(row => row.Id == auditLog.Id, ct));
        Assert.True(await _core.WorkflowTypes.AnyAsync(type => type.Id == OTHER_TYPE, ct));
        Assert.True(await _core.Workflows.AnyAsync(workflow => workflow.Id == otherTemplate.Id, ct));
        Assert.True(await _core.WorkflowVersions.AnyAsync(version => version.Id == otherVersion.Id, ct));
    }

    [Fact]
    public async Task Purge_KeepsARetiredTypeInsideRetention()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        _ = await SeedTypeAsync(TYPE, [ORG], ct);
        _ = await _retirement.RetireAsync(ORG, USER, TYPE, ct);
        await BackdateRetirementAsync(TYPE, _insideRetention, ct);

        RetiredWorkflowTypePurgeResult result = await _retirement.PurgeRetiredAsync(ORG, RETENTION_DAYS, ct);

        Assert.Empty(result.PurgedWorkflowTypeIds);
        Assert.True(await _core.WorkflowTypes.AnyAsync(type => type.Id == TYPE, ct));
    }

    [Fact]
    public async Task Purge_NeverRemovesATypeThatWasNotRetired_EvenWhenNoOrganizationCanReachIt()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        _ = await SeedTypeAsync(TYPE, [], ct);
        _ = await SeedTypeAsync(OTHER_TYPE, [ORG], ct);
        _ = await _core.WorkflowTypeOrganizationAccess
            .Where(access => access.WorkflowTypeId == OTHER_TYPE)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(access => access.IsDeleted, true)
                .SetProperty(access => access.DeletedAt, _pastRetention), ct);

        RetiredWorkflowTypePurgeResult result = await _retirement.PurgeRetiredAsync(ORG, RETENTION_DAYS, ct);

        Assert.Empty(result.PurgedWorkflowTypeIds);
        Assert.Equal(2, await _core.WorkflowTypes.CountAsync(type => type.Id == TYPE || type.Id == OTHER_TYPE, ct));
    }

    [Fact]
    public async Task Purge_LeavesATypeRetiredByAnotherOrganization()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        _ = await SeedTypeAsync(TYPE, [OTHER_ORG], ct);
        _ = await _retirement.RetireAsync(OTHER_ORG, USER, TYPE, ct);
        await BackdateRetirementAsync(TYPE, _pastRetention, ct);

        RetiredWorkflowTypePurgeResult result = await _retirement.PurgeRetiredAsync(ORG, RETENTION_DAYS, ct);

        Assert.Empty(result.PurgedWorkflowTypeIds);
        Assert.True(await _core.WorkflowTypes.AnyAsync(type => type.Id == TYPE, ct));
    }

    private async Task<Workflow> SeedTypeAsync(string typeId, string[] organizationIds, CancellationToken ct)
    {
        _ = _core.WorkflowTypes.Add(new WorkflowType(typeId, typeId));
        foreach (string organizationId in organizationIds)
        {
            _core.GrantWorkflowTypeAccess(typeId, organizationId);
        }

        var template = new Workflow(typeId) { Id = Guid.NewGuid().ToString(), WorkflowTypeId = typeId };
        var section = new Section("Check", template) { Id = Guid.NewGuid().ToString() };
        Block block = TestBlocks.AiQuery(Guid.NewGuid().ToString(), "Check ID card", "check_id_card", DateTime.UtcNow, new AiQueryProperties());
        block.SectionId = section.Id;
        _ = _core.Workflows.Add(template);
        _ = _core.Blocks.Add(block);
        _ = await _core.SaveChangesAsync(ct);
        _core.ChangeTracker.Clear();
        return template;
    }

    private WorkflowExecution SeedRun(string organizationId, string typeId, WorkflowState state, out Workflow executedClone, out WorkflowReport report)
    {
        executedClone = new Workflow(typeId) { Id = Guid.NewGuid().ToString(), WorkflowTypeId = typeId, OrganizationId = organizationId };
        var run = new WorkflowExecution
        {
            Id = Guid.NewGuid().ToString(),
            OrganizationId = organizationId,
            WorkflowId = executedClone.Id,
            WorkflowTypeId = typeId,
            JobId = Guid.NewGuid().ToString(),
            WorkflowState = state,
        };
        report = new WorkflowReport { OrganizationId = organizationId, WorkflowExecutionId = run.Id, WorkflowTypeId = typeId, Title = "Report" };
        _ = _userData.Workflows.Add(executedClone);
        _ = _userData.WorkflowExecutions.Add(run);
        _ = _userData.WorkflowReports.Add(report);
        return run;
    }

    private Document SeedDocument(string organizationId, string typeId)
    {
        var fileMetadata = new FileMetadata(organizationId, "consent.pdf", [0x25, 0x50, 0x44, 0x46]);
        var document = new Document(fileMetadata, MediaType.ApplicationPdf) { Name = "consent", WorkflowTypeId = typeId };
        _ = _userData.FileMetadata.Add(fileMetadata);
        _ = _userData.Documents.Add(document);
        return document;
    }

    private ValidationCase SeedValidationCase(string organizationId, string typeId)
    {
        var validationCase = new ValidationCase
        {
            OrganizationId = organizationId,
            Name = "Consent form",
            SchemaId = Guid.NewGuid().ToString(),
            WorkflowTypeId = typeId,
        };
        _ = _userData.ValidationCases.Add(validationCase);
        return validationCase;
    }

    private WorkflowVersion SeedVersion(string typeId, string? parentVersionId)
    {
        var version = new WorkflowVersion
        {
            WorkflowTypeId = typeId,
            ContentHash = Guid.NewGuid().ToString("N"),
            ParentVersionId = parentVersionId,
            CreatedByUserId = USER,
        };
        _ = _core.WorkflowVersions.Add(version);
        return version;
    }

    private void SeedSignatureRows(
        string typeId,
        out SignatureFieldOverride signatureOverride,
        out PendingSignatureOverride pending,
        out SignatureFieldOverrideAuditLog auditLog)
    {
        signatureOverride = new SignatureFieldOverride { OrganizationId = ORG, WorkflowTypeId = typeId, PlaceholderName = "analyst", Value = "A. Analyst" };
        pending = new PendingSignatureOverride { OrganizationId = ORG, WorkflowTypeId = typeId, PlaceholderName = "reviewer", ProposedValue = "R. Reviewer", ProposerUserId = USER };
        auditLog = new SignatureFieldOverrideAuditLog { OrganizationId = ORG, WorkflowTypeId = typeId, PlaceholderName = "analyst", Action = "Set", NewValue = "A. Analyst" };
        _ = _core.SignatureFieldOverrides.Add(signatureOverride);
        _ = _core.PendingSignatureOverrides.Add(pending);
        _ = _core.SignatureFieldOverrideAuditLogs.Add(auditLog);
    }

    private ValidationSchema SeedValidationSchema(string typeId)
    {
        var schema = new ValidationSchema { Name = "Consent form", OrganizationId = ORG, WorkflowTypeId = typeId, SchemaJson = "{}" };
        _ = _core.ValidationSchemas.Add(schema);
        return schema;
    }

    private async Task BackdateRetirementAsync(string typeId, DateTime retiredAt, CancellationToken ct)
    {
        _ = await _core.WorkflowTypes
            .Where(type => type.Id == typeId)
            .ExecuteUpdateAsync(setters => setters.SetProperty(type => type.RetiredAt, retiredAt), ct);
        _core.ChangeTracker.Clear();
    }

    private async Task AssertUntouchedAsync(string typeId, int accessRows, CancellationToken ct)
    {
        _core.ChangeTracker.Clear();
        WorkflowType type = await _core.WorkflowTypes.SingleAsync(candidate => candidate.Id == typeId, ct);
        Assert.Null(type.RetiredAt);
        Assert.Equal(accessRows, await _core.WorkflowTypeOrganizationAccess.CountAsync(access => access.WorkflowTypeId == typeId, ct));
        Assert.True(await _core.Workflows.AnyAsync(workflow => workflow.WorkflowTypeId == typeId, ct));
    }

    private static async Task<bool> IsSoftDeletedAsync<T>(DbSet<T> rows, string id, CancellationToken ct)
        where T : Domain.Common.Entity<T>
    {
        return await rows.IgnoreQueryFilters().AnyAsync(row => row.Id == id && row.IsDeleted && row.DeletedAt != null, ct);
    }

    private static async Task<bool> IsSoftDeletedByAsync<T>(DbSet<T> rows, string id, CancellationToken ct)
        where T : Domain.Common.Entity<T>
    {
        return await rows.IgnoreQueryFilters().AnyAsync(row => row.Id == id && row.IsDeleted && row.DeletedByUserId == USER, ct);
    }
}
