using ErrorOr;
using IniaAi.Application.Common.Interfaces.Services;
using IniaAi.Application.Common.Workflows;
using IniaAi.Application.Errors;
using IniaAi.Domain.Common;
using IniaAi.Domain.Entities.Documents;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Enums;
using IniaAi.Domain.Specifications.Common;
using IniaAi.Infrastructure.Persistence.Core.Contexts;
using IniaAi.Infrastructure.Persistence.UserData.Contexts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;

namespace IniaAi.Infrastructure.Services;

/// <summary>
/// Core and UserData are separate databases with no transaction across them. A retirement writes
/// UserData first: a repeat call redoes that step harmlessly, while a type already stamped retired
/// can no longer be found to finish the job.
/// </summary>
public sealed class WorkflowTypeRetirement : IWorkflowTypeRetirement
{
    private readonly CoreDbContext _core;
    private readonly UserDataDbContext _userData;
    private readonly WorkflowTypeAccess _access;
    private readonly ModuleWorkflowTypes _moduleWorkflowTypes;
    private readonly ILogger<WorkflowTypeRetirement> _logger;

    public WorkflowTypeRetirement(
        CoreDbContext core,
        UserDataDbContext userData,
        WorkflowTypeAccess access,
        ModuleWorkflowTypes moduleWorkflowTypes,
        ILogger<WorkflowTypeRetirement> logger)
    {
        _core = core;
        _userData = userData;
        _access = access;
        _moduleWorkflowTypes = moduleWorkflowTypes;
        _logger = logger;
    }

    public async Task<ErrorOr<Deleted>> RetireAsync(string organizationId, string userId, string workflowTypeId, CancellationToken ct = default)
    {
        if (IsPlatformOwned(workflowTypeId))
        {
            return WorkflowTypeErrorFactory.Reserved(workflowTypeId);
        }

        bool retired = await _core.WorkflowTypes.AnyAsync(type => type.Id == workflowTypeId && type.RetiredAt != null, ct);
        if (retired || !await _access.CanReachAsync(organizationId, workflowTypeId, ct))
        {
            return WorkflowTypeErrorFactory.NotFound(workflowTypeId);
        }

        if (await _access.IsReachedByAnotherAsync(organizationId, workflowTypeId, ct))
        {
            return WorkflowTypeErrorFactory.Shared(workflowTypeId);
        }

        bool hasActiveRun = await _userData.WorkflowExecutions
            .Where(run => run.OrganizationId == organizationId && run.WorkflowTypeId == workflowTypeId)
            .Where(WorkflowExecutionExpressions.IsInActiveState)
            .AnyAsync(ct);
        if (hasActiveRun)
        {
            return WorkflowTypeErrorFactory.RunInProgress(workflowTypeId);
        }

        var stamp = new DeletionStamp(DateTime.UtcNow, userId);
        await _userData.Database.CreateExecutionStrategy().ExecuteAsync(
            _ => SoftDeleteOrganizationDataAsync(organizationId, workflowTypeId, stamp, ct), ct);
        await _core.Database.CreateExecutionStrategy().ExecuteAsync(
            _ => RetireInCoreAsync(workflowTypeId, stamp, ct), ct);

        return Result.Deleted;
    }

    public async Task<RetiredWorkflowTypePurgeResult> PurgeRetiredAsync(string organizationId, int retentionDays, CancellationToken ct = default)
    {
        DateTime cutoff = DateTime.UtcNow.AddDays(-retentionDays);

        // Ignoring the filters is what lets the subqueries see the retiring organization's soft-deleted access row.
        List<string> dueTypeIds = await _core.WorkflowTypes
            .IgnoreQueryFilters()
            .Where(type => type.RetiredAt <= cutoff)
            .Where(type => _core.WorkflowTypeOrganizationAccess.Any(access =>
                access.WorkflowTypeId == type.Id && access.OrganizationId == organizationId && access.IsDeleted))
            .Where(type => !_core.WorkflowTypeOrganizationAccess.Any(access =>
                access.WorkflowTypeId == type.Id && !access.IsDeleted))
            .Select(type => type.Id)
            .ToListAsync(ct);

        var purged = new List<string>();
        var errors = new List<string>();
        foreach (string workflowTypeId in dueTypeIds)
        {
            try
            {
                await _core.Database.CreateExecutionStrategy().ExecuteAsync(
                    _ => PurgeTypeAsync(workflowTypeId, ct), ct);
                purged.Add(workflowTypeId);
                _logger.LogInformation(
                    "Retired workflow type {WorkflowTypeId} of organization {OrganizationId} purged",
                    workflowTypeId,
                    organizationId);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(
                    ex,
                    "Purging retired workflow type {WorkflowTypeId} of organization {OrganizationId} failed",
                    workflowTypeId,
                    organizationId);
                errors.Add($"Workflow type {workflowTypeId}: {ex.Message}");
            }
        }

        return new RetiredWorkflowTypePurgeResult(purged, errors);
    }

    /// <summary>
    /// The core seeding recreates a module's types at every start, so a retired one would come back empty.
    /// </summary>
    private bool IsPlatformOwned(string workflowTypeId)
    {
        return workflowTypeId is WorkflowType.GENERIC or WorkflowType.TEST_WORKFLOW_TYPE
            || _moduleWorkflowTypes.All.Any(type => type.Id == workflowTypeId);
    }

    /// <summary>
    /// A document of the type stays when a run of another type used it. A document's file record, and
    /// for the database storage provider the row that holds its bytes, are soft-deleted with it,
    /// because the cleanup releases bytes only for deleted rows. Display filters that name the type go
    /// now, since they hold nothing to keep.
    /// </summary>
    private async Task SoftDeleteOrganizationDataAsync(string organizationId, string workflowTypeId, DeletionStamp stamp, CancellationToken ct)
    {
        await using IDbContextTransaction transaction = await _userData.Database.BeginTransactionAsync(ct);

        await SoftDeleteAsync(
            _userData.WorkflowExecutions.Where(run => run.OrganizationId == organizationId && run.WorkflowTypeId == workflowTypeId),
            stamp,
            ct);
        await SoftDeleteAsync(
            _userData.Workflows.Where(workflow => workflow.OrganizationId == organizationId && workflow.WorkflowTypeId == workflowTypeId),
            stamp,
            ct);
        await SoftDeleteAsync(
            _userData.WorkflowReports.Where(report => report.OrganizationId == organizationId && report.WorkflowTypeId == workflowTypeId),
            stamp,
            ct);
        await SoftDeleteAsync(
            _userData.ValidationCases.Where(validationCase => validationCase.OrganizationId == organizationId && validationCase.WorkflowTypeId == workflowTypeId),
            stamp,
            ct);

        IQueryable<Document> documents = _userData.Documents
            .Where(document => document.OrganizationId == organizationId
                && document.WorkflowTypeId == workflowTypeId
                && !_userData.DocumentInfos.Any(info => info.DocumentId == document.Id
                    && _userData.WorkflowExecutions.Any(run => run.JobId == info.JobId && run.WorkflowTypeId != workflowTypeId)));
        IQueryable<FileMetadata> fileRecords = _userData.FileMetadata
            .Where(file => documents.Any(document => document.FileMetadataId == file.Id));
        _ = await _userData.Files
            .Where(bytes => fileRecords.Any(file => file.StorageProvider == StorageProvider.Database && file.ExternalStoragePath == bytes.Id))
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(bytes => bytes.IsDeleted, true)
                .SetProperty(bytes => bytes.DeletedAt, stamp.At)
                .SetProperty(bytes => bytes.DeletedByUserId, stamp.UserId), ct);
        await SoftDeleteAsync(fileRecords, stamp, ct);
        await SoftDeleteAsync(documents, stamp, ct);

        _ = await _userData.UserWorkflowTypeVisibilities
            .Where(visibility => visibility.WorkflowTypeId == workflowTypeId)
            .ExecuteDeleteAsync(ct);

        await transaction.CommitAsync(ct);
    }

    /// <summary>
    /// Everything the type owns in Core goes behind the soft-delete filter with it, so no read of a
    /// version, an override or a schema can reach a retired type before the purge removes it.
    /// </summary>
    private async Task RetireInCoreAsync(string workflowTypeId, DeletionStamp stamp, CancellationToken ct)
    {
        await using IDbContextTransaction transaction = await _core.Database.BeginTransactionAsync(ct);

        _ = await _core.WorkflowTypes
            .Where(type => type.Id == workflowTypeId)
            .ExecuteUpdateAsync(setters => setters.SetProperty(type => type.RetiredAt, stamp.At), ct);
        await SoftDeleteAsync(_core.WorkflowTypeOrganizationAccess.Where(access => access.WorkflowTypeId == workflowTypeId), stamp, ct);
        await SoftDeleteAsync(_core.Workflows.Where(workflow => workflow.WorkflowTypeId == workflowTypeId), stamp, ct);
        await SoftDeleteAsync(_core.WorkflowVersions.Where(version => version.WorkflowTypeId == workflowTypeId), stamp, ct);
        await SoftDeleteAsync(_core.SignatureFieldOverrides.Where(row => row.WorkflowTypeId == workflowTypeId), stamp, ct);
        await SoftDeleteAsync(_core.PendingSignatureOverrides.Where(row => row.WorkflowTypeId == workflowTypeId), stamp, ct);
        await SoftDeleteAsync(_core.ValidationSchemas.Where(schema => schema.WorkflowTypeId == workflowTypeId), stamp, ct);

        await transaction.CommitAsync(ct);
    }

    /// <summary>
    /// Deleting the template removes its sections, blocks, document classes and block templates
    /// through their cascading foreign keys, and deleting the type removes its access and
    /// counterparty mappings the same way. The signature audit log is append-only and stays.
    /// </summary>
    private async Task PurgeTypeAsync(string workflowTypeId, CancellationToken ct)
    {
        await using IDbContextTransaction transaction = await _core.Database.BeginTransactionAsync(ct);

        _ = await _core.PendingSignatureOverrides.IgnoreQueryFilters()
            .Where(pending => pending.WorkflowTypeId == workflowTypeId)
            .ExecuteDeleteAsync(ct);
        _ = await _core.SignatureFieldOverrides.IgnoreQueryFilters()
            .Where(signatureOverride => signatureOverride.WorkflowTypeId == workflowTypeId)
            .ExecuteDeleteAsync(ct);
        _ = await _core.ValidationSchemas.IgnoreQueryFilters()
            .Where(schema => schema.WorkflowTypeId == workflowTypeId)
            .ExecuteDeleteAsync(ct);
        _ = await _core.WorkflowVersions.IgnoreQueryFilters()
            .Where(version => version.WorkflowTypeId == workflowTypeId)
            .ExecuteDeleteAsync(ct);
        _ = await _core.Workflows.IgnoreQueryFilters()
            .Where(workflow => workflow.WorkflowTypeId == workflowTypeId)
            .ExecuteDeleteAsync(ct);
        _ = await _core.WorkflowTypes
            .Where(type => type.Id == workflowTypeId)
            .ExecuteDeleteAsync(ct);

        await transaction.CommitAsync(ct);
    }

    private static async Task SoftDeleteAsync<T>(IQueryable<T> rows, DeletionStamp stamp, CancellationToken ct)
        where T : Entity<T>
    {
        _ = await rows.ExecuteUpdateAsync(setters => setters
            .SetProperty(row => row.IsDeleted, true)
            .SetProperty(row => row.DeletedAt, stamp.At)
            .SetProperty(row => row.DeletedByUserId, stamp.UserId), ct);
    }

    private sealed record DeletionStamp(DateTime At, string UserId);
}
