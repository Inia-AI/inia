using IniaAi.Domain.Core.Interfaces;
using IniaAi.Domain.Entities.Signatures;
using IniaAi.Domain.Persistence.UserData.Repositories;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace IniaAi.Application.Commands.Signatures;

/// <summary>
/// One <c>(placeholderName, value)</c> update to apply to a workflow type's active
/// signature overrides. A <see langword="null"/> <see cref="Value"/> means delete the
/// row so the staff-authored default applies again. <see cref="ReferencedBlockNamesJson"/>
/// is the block-names snapshot stamped onto the audit row (see
/// <see cref="SignatureOverrideWriter.SerializeBlockNames"/>).
/// </summary>
public record SignatureFieldUpdate(
    string PlaceholderName,
    string? Value,
    string? ReferencedBlockNamesJson = null);

/// <summary>
/// A single approver snapshotted onto a <see cref="SignatureFieldOverrideAuditLog"/>
/// row at promotion time. Stored as JSON in <c>ApproversJson</c> so the History UI can
/// render "signed off by" without joining against the now-deleted pending/vote rows.
/// </summary>
public record SignatureAuditApprover(string UserId, string? Email);

/// <summary>
/// Canonical writer for <see cref="SignatureFieldOverride"/> rows. Used by the
/// pending-override promotion path when a tenant threshold is met. Upserts by
/// <c>(OrganizationId, WorkflowTypeId, PlaceholderName)</c> — the workflow-level field
/// name is the identity; a null value deletes the row.
/// Does not call <c>CompleteAsync</c> — callers commit their own unit of work so the
/// override write joins whatever transaction they're running (e.g. alongside a pending
/// row delete). Callers must NOT call <c>SaveChanges</c> mid-batch or audit-row
/// emission ordering across iterations becomes meaningful.
///
/// <para>
/// Each non-noop value transition also emits a <see cref="SignatureFieldOverrideAuditLog"/>
/// row in the same transaction so the History UI has a permanent record after the
/// pending/vote rows are deleted on resolution.
/// </para>
/// </summary>
public static class SignatureOverrideWriter
{
    // Pinned PascalCase round-trip — both SignatureAuditApprover (write side) and
    // SignatureHistoryApproverDto (read side in the query handler) are PascalCase
    // records. Pinning rather than relying on JsonSerializer defaults guards against
    // a future global JsonSerializerOptions.Default flip to camelCase silently
    // breaking the round-trip.
    internal static JsonSerializerOptions ApproverJsonOptions { get; } = new()
    {
        PropertyNamingPolicy = null,
    };

    /// <summary>
    /// Serializes the referencing-block-names snapshot for
    /// <see cref="SignatureFieldOverrideAuditLog.ReferencedBlockNamesJson"/>
    /// (<c>["Block A","Block B"]</c>). Truncates to fit the column rather than fail the
    /// promotion over an oversized cosmetic snapshot.
    /// </summary>
    public static string SerializeBlockNames(IEnumerable<string> blockNames)
    {
        const int maxLength = 4000;
        List<string> names = [.. blockNames];
        string json = JsonSerializer.Serialize(names);
        while (json.Length > maxLength && names.Count > 0)
        {
            names.RemoveAt(names.Count - 1);
            json = JsonSerializer.Serialize(names);
        }

        return json;
    }

    public static async Task ApplyUpdatesAsync(
        string organizationId,
        string workflowTypeId,
        string? actorUserId,
        string? actorUserEmail,
        IReadOnlyList<SignatureAuditApprover> approvers,
        IEnumerable<SignatureFieldUpdate> updates,
        ICoreUnitOfWork coreUnitOfWork,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(organizationId);
        ArgumentException.ThrowIfNullOrEmpty(workflowTypeId);
        ArgumentNullException.ThrowIfNull(approvers);
        ArgumentNullException.ThrowIfNull(updates);
        ArgumentNullException.ThrowIfNull(coreUnitOfWork);

        List<SignatureFieldUpdate> list = [.. updates];
        if (list.Count == 0)
        {
            return;
        }

        IRepository<SignatureFieldOverride> repo = coreUnitOfWork.Repository<SignatureFieldOverride>();
        IRepository<SignatureFieldOverrideAuditLog> auditRepo =
            coreUnitOfWork.Repository<SignatureFieldOverrideAuditLog>();

        HashSet<string> placeholders = [];
        foreach (SignatureFieldUpdate u in list)
        {
            _ = placeholders.Add(u.PlaceholderName);
        }

        List<SignatureFieldOverride> existing = await repo
            .GetAll()
            .Where(o => o.OrganizationId == organizationId
                && o.WorkflowTypeId == workflowTypeId
                && placeholders.Contains(o.PlaceholderName))
            .ToListAsync(cancellationToken);

        Dictionary<string, SignatureFieldOverride> existingByPlaceholder = [];
        foreach (SignatureFieldOverride row in existing)
        {
            existingByPlaceholder[row.PlaceholderName] = row;
        }

        string approversJson = JsonSerializer.Serialize(approvers, ApproverJsonOptions);

        foreach (SignatureFieldUpdate update in list)
        {
            existingByPlaceholder.TryGetValue(update.PlaceholderName, out SignatureFieldOverride? row);

            if (update.Value is null)
            {
                if (row is not null)
                {
                    repo.HardRemove(row);
                    await EmitAuditAsync(
                        auditRepo,
                        organizationId,
                        workflowTypeId,
                        update.PlaceholderName,
                        SignatureAuditAction.REMOVED,
                        previousValue: row.Value,
                        newValue: null,
                        actorUserId,
                        actorUserEmail,
                        approversJson,
                        update.ReferencedBlockNamesJson);
                }

                continue;
            }

            if (row is null)
            {
                var inserted = new SignatureFieldOverride
                {
                    OrganizationId = organizationId,
                    WorkflowTypeId = workflowTypeId,
                    PlaceholderName = update.PlaceholderName,
                    Value = update.Value,
                };
                await repo.AddAsync(inserted);
                await EmitAuditAsync(
                    auditRepo,
                    organizationId,
                    workflowTypeId,
                    update.PlaceholderName,
                    SignatureAuditAction.CREATED,
                    previousValue: null,
                    newValue: update.Value,
                    actorUserId,
                    actorUserEmail,
                    approversJson,
                    update.ReferencedBlockNamesJson);
            }
            else
            {
                if (string.Equals(row.Value, update.Value, StringComparison.Ordinal))
                {
                    continue;
                }

                string previous = row.Value;
                row.Value = update.Value;
                await EmitAuditAsync(
                    auditRepo,
                    organizationId,
                    workflowTypeId,
                    update.PlaceholderName,
                    SignatureAuditAction.UPDATED,
                    previousValue: previous,
                    newValue: update.Value,
                    actorUserId,
                    actorUserEmail,
                    approversJson,
                    update.ReferencedBlockNamesJson);
            }
        }
    }

    private static async Task EmitAuditAsync(
        IRepository<SignatureFieldOverrideAuditLog> auditRepo,
        string organizationId,
        string workflowTypeId,
        string placeholderName,
        string action,
        string? previousValue,
        string? newValue,
        string? actorUserId,
        string? actorUserEmail,
        string approversJson,
        string? referencedBlockNamesJson)
    {
        var row = new SignatureFieldOverrideAuditLog
        {
            OrganizationId = organizationId,
            WorkflowTypeId = workflowTypeId,
            PlaceholderName = placeholderName,
            Action = action,
            PreviousValue = previousValue,
            NewValue = newValue,
            ActorUserId = actorUserId,
            ActorUserEmail = actorUserEmail,
            ApproversJson = approversJson,
            ReferencedBlockNamesJson = referencedBlockNamesJson,
        };
        await auditRepo.AddAsync(row);
    }
}
