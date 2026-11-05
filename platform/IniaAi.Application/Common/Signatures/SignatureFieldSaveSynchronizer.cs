using ErrorOr;
using IniaAi.Application.Common.Workflows;
using IniaAi.Common.Interfaces.Persistence;
using IniaAi.Common.Text;
using IniaAi.Domain.Entities.Signatures;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Blocks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace IniaAi.Application.Common.Signatures;

/// <summary>A staff-side signature field rename to carry through the override tables on save.</summary>
public record SignatureFieldRename(string OldName, string NewName);

/// <summary>Override bookkeeping from a template save, surfaced in the staff post-save toast.</summary>
public record SignatureSyncResult(int OverridesRenamed, int OverridesDeleted, int PendingsDeleted)
{
    public static SignatureSyncResult Empty { get; } = new(0, 0, 0);
}

/// <summary>
/// Keeps the signature override tables in step with workflow-level field definitions during a
/// template save, inside the save transaction. With name-keyed overrides the only events that
/// can touch client data are staff-side renames (the override rows follow the new name, across
/// every organization) and definition deletions (the rows cascade-delete). Guards:
/// <list type="bullet">
/// <item>Renames are collapsed into per-original-name chains (A→B then B→C applies as A→C;
/// reverted renames drop out), and a rename whose final target is not a defined field on the
/// saved workflow is discarded — a stale record from a replaced editor session must not re-key
/// live rows.</item>
/// <item>A deletion whose <c>{token}</c> is still referenced by a bearing block's templates is
/// refused.</item>
/// <item>A deletion that would destroy existing override/pending rows is refused unless the
/// caller explicitly confirmed it (<c>confirmDeletions</c>) — uploads, version restores, and API
/// saves reach this path without ever having shown the editor's confirmation dialog.</item>
/// <item>Deletions are applied and flushed before renames so a rename can land on a name whose
/// old field was deleted in the same save without tripping the unique index.</item>
/// </list>
/// </summary>
public static class SignatureFieldSaveSynchronizer
{
    public const string DELETED_FIELD_STILL_REFERENCED_CODE = "Signatures.DeletedFieldStillReferenced";
    public const string DELETION_REQUIRES_CONFIRMATION_CODE = "Signatures.DeletionRequiresConfirmation";
    public const string RENAME_TOUCHES_AGENT_SKILL_CODE = "Signatures.RenameTouchesAgentSkill";

    public static async Task<ErrorOr<SignatureSyncResult>> SyncAsync(
        ICoreDbContext db,
        string workflowTypeId,
        string actorUserId,
        Workflow? oldWorkflow,
        Workflow savedWorkflow,
        IReadOnlyList<SignatureFieldRename>? renames,
        bool confirmDeletions,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        List<SignatureFieldRename> effectiveRenames = CollapseRenames(renames, savedWorkflow);

        foreach (SignatureFieldRename rename in effectiveRenames)
        {
            // Rename rewrites walk the reflection template caches, which skip agent worker
            // skills — a skill reference would silently keep the old name, so refuse instead.
            List<AgentTemplateGrammar.SkillFieldReference> touchedSkills =
                AgentTemplateGrammar.FindSkillsReferencingField(savedWorkflow, rename.OldName);
            if (touchedSkills.Count > 0)
            {
                string details = string.Join(", ", touchedSkills.Select(
                    r => $"{r.Location.Description} of agent block '{r.BlockTag}'"));
                string advice = touchedSkills.All(r => r.Location.IsSkill)
                    ? "Renames never rewrite skill content — update the skill references first, then rename."
                    : "Renames never rewrite agent skills or prompts — update those references first, then rename.";
                return Error.Validation(
                    RENAME_TOUCHES_AGENT_SKILL_CODE,
                    $"Signature field '{rename.OldName}' cannot be renamed to '{rename.NewName}': "
                    + $"it is referenced under the agent-template grammar by {details}. {advice}");
            }
        }

        ErrorOr<List<string>> deletions = CollectDeletions(oldWorkflow, savedWorkflow, effectiveRenames);
        if (deletions.IsError)
        {
            return deletions.Errors;
        }

        int overridesDeleted = 0;
        int pendingsDeleted = 0;
        if (deletions.Value.Count > 0)
        {
            ErrorOr<Success> confirmed = await EnsureDeletionsConfirmedAsync(
                db, workflowTypeId, deletions.Value, confirmDeletions, cancellationToken);
            if (confirmed.IsError)
            {
                return confirmed.Errors;
            }

            foreach (string name in deletions.Value)
            {
                (int overrides, int pendings) = await ApplyDeleteAsync(
                    db, workflowTypeId, actorUserId, name, cancellationToken);
                overridesDeleted += overrides;
                pendingsDeleted += pendings;
            }

            // Flush so a rename landing on a just-deleted name cannot trip the unique index on
            // (Org, WorkflowTypeId, PlaceholderName); the caller's transaction still spans both.
            _ = await db.SaveChangesAsync(cancellationToken);
        }

        int overridesRenamed = 0;
        Dictionary<string, List<Block>> references = savedWorkflow.GetSignatureFieldReferences();
        foreach (SignatureFieldRename rename in effectiveRenames)
        {
            overridesRenamed += await ApplyRenameAsync(
                db, workflowTypeId, actorUserId, rename, references, cancellationToken);
        }

        if (overridesRenamed > 0 || overridesDeleted > 0 || pendingsDeleted > 0)
        {
            logger.LogInformation(
                "Signature sync for {WorkflowTypeId}: {Renamed} override(s) renamed, "
                + "{Deleted} override(s) and {Pendings} pending(s) deleted",
                LogSanitizer.ForLog(workflowTypeId),
                overridesRenamed,
                overridesDeleted,
                pendingsDeleted);
        }

        return new SignatureSyncResult(overridesRenamed, overridesDeleted, pendingsDeleted);
    }

    /// <summary>
    /// The refusals a save of <paramref name="savedWorkflow"/> over <paramref name="oldWorkflow"/>
    /// would meet on its signature fields, with no renames and no confirmation, writing nothing: a
    /// deleted field a template still references, and a deletion that would destroy client data.
    /// </summary>
    public static async Task<List<Error>> CheckDeletionsAsync(
        ICoreDbContext db,
        string workflowTypeId,
        Workflow? oldWorkflow,
        Workflow savedWorkflow,
        CancellationToken cancellationToken)
    {
        ErrorOr<List<string>> deletions = CollectDeletions(oldWorkflow, savedWorkflow, []);
        if (deletions.IsError)
        {
            return deletions.Errors;
        }

        if (deletions.Value.Count == 0)
        {
            return [];
        }

        ErrorOr<Success> confirmed = await EnsureDeletionsConfirmedAsync(
            db, workflowTypeId, deletions.Value, confirmDeletions: false, cancellationToken);

        return confirmed.IsError ? confirmed.Errors : [];
    }

    /// <summary>
    /// Collapses the session's rename records into at most one rename per original name (chains
    /// merge, reverts cancel out), dropping records whose final target is not a defined field on
    /// the saved workflow. Each surviving rename therefore has a distinct original OldName, so no
    /// DB row is matched by more than one rename.
    /// </summary>
    internal static List<SignatureFieldRename> CollapseRenames(
        IReadOnlyList<SignatureFieldRename>? renames,
        Workflow savedWorkflow)
    {
        var chain = new List<(string Old, string Current)>();
        foreach (SignatureFieldRename rename in renames ?? [])
        {
            if (string.IsNullOrWhiteSpace(rename.OldName) || string.IsNullOrWhiteSpace(rename.NewName))
            {
                continue;
            }

            int index = chain.FindIndex(e =>
                string.Equals(e.Current, rename.OldName, StringComparison.OrdinalIgnoreCase));
            if (index >= 0)
            {
                chain[index] = (chain[index].Old, rename.NewName);
            }
            else
            {
                chain.Add((rename.OldName, rename.NewName));
            }
        }

        return [.. chain
            .Where(e => !string.Equals(e.Old, e.Current, StringComparison.Ordinal))
            .Where(e => savedWorkflow.SignatureFields.Any(f =>
                string.Equals(f.Name, e.Current, StringComparison.OrdinalIgnoreCase)))
            .Select(e => new SignatureFieldRename(e.Old, e.Current))];
    }

    /// <summary>
    /// Old-workflow field names whose definitions no longer survive. A name carried forward by a
    /// rename is not a deletion; a name that only appears on the saved workflow because a rename
    /// landed on it (its own definition was deleted this session) IS a deletion. Errors when a
    /// deleted name's token is still referenced by a bearing block's templates.
    /// </summary>
    private static ErrorOr<List<string>> CollectDeletions(
        Workflow? oldWorkflow,
        Workflow savedWorkflow,
        List<SignatureFieldRename> renames)
    {
        List<string> deletions = [];
        if (oldWorkflow is null)
        {
            return deletions;
        }

        HashSet<string> renamedOldNames = new(
            renames.Select(r => r.OldName), StringComparer.OrdinalIgnoreCase);
        HashSet<string> renameTargets = new(
            renames.Select(r => r.NewName), StringComparer.OrdinalIgnoreCase);
        HashSet<string> savedNames = new(
            savedWorkflow.SignatureFields.Select(f => f.Name), StringComparer.OrdinalIgnoreCase);

        foreach (WorkflowSignatureField removed in oldWorkflow.SignatureFields)
        {
            if (renamedOldNames.Contains(removed.Name))
            {
                continue;
            }

            bool survivedDirectly = savedNames.Contains(removed.Name)
                && !renameTargets.Contains(removed.Name);
            if (survivedDirectly)
            {
                continue;
            }

            bool stillReferenced = savedWorkflow.GetSignatureBearingBlocks().Any(b =>
                b.Properties.HasPlaceholder(
                    Workflow.SignatureFieldToken(removed.Name), StringComparison.OrdinalIgnoreCase));
            if (stillReferenced && !renameTargets.Contains(removed.Name))
            {
                return Error.Validation(
                    DELETED_FIELD_STILL_REFERENCED_CODE,
                    $"Signature field '{removed.Name}' was deleted but a block template still "
                    + $"references {{{removed.Name}}}. Remove the token first.");
            }

            deletions.Add(removed.Name);
        }

        return deletions;
    }

    private static async Task<ErrorOr<Success>> EnsureDeletionsConfirmedAsync(
        ICoreDbContext db,
        string workflowTypeId,
        List<string> deletions,
        bool confirmDeletions,
        CancellationToken cancellationToken)
    {
        if (confirmDeletions)
        {
            return Result.Success;
        }

        List<string> affected = [];
        foreach (string name in deletions)
        {
            string loweredName = name.ToLowerInvariant();
            int overrideRows = await db.SignatureFieldOverrides
                .CountAsync(
                    o => o.WorkflowTypeId == workflowTypeId
                        && o.PlaceholderName.ToLower() == loweredName,
                    cancellationToken);
            int pendingRows = await db.PendingSignatureOverrides
                .CountAsync(
                    p => p.WorkflowTypeId == workflowTypeId
                        && p.PlaceholderName.ToLower() == loweredName,
                    cancellationToken);
            if (overrideRows + pendingRows > 0)
            {
                affected.Add($"'{name}' ({overrideRows} override(s), {pendingRows} pending(s))");
            }
        }

        if (affected.Count == 0)
        {
            return Result.Success;
        }

        return Error.Validation(
            DELETION_REQUIRES_CONFIRMATION_CODE,
            "This save deletes signature field(s) holding client data across organizations: "
            + string.Join(", ", affected)
            + ". Confirm the deletion to proceed.");
    }

    private static async Task<int> ApplyRenameAsync(
        ICoreDbContext db,
        string workflowTypeId,
        string actorUserId,
        SignatureFieldRename rename,
        Dictionary<string, List<Block>> references,
        CancellationToken cancellationToken)
    {
        string loweredOldName = rename.OldName.ToLowerInvariant();

        List<SignatureFieldOverride> overrides = await db.SignatureFieldOverrides
            .Where(o => o.WorkflowTypeId == workflowTypeId
                && o.PlaceholderName.ToLower() == loweredOldName)
            .ToListAsync(cancellationToken);

        string? blockNamesJson = references.TryGetValue(rename.NewName, out List<Block>? blocks)
            ? Commands.Signatures.SignatureOverrideWriter.SerializeBlockNames(blocks.Select(b => b.Name))
            : null;

        var touchedOrgs = new HashSet<string>(StringComparer.Ordinal);
        foreach (SignatureFieldOverride row in overrides)
        {
            row.PlaceholderName = rename.NewName;
            _ = touchedOrgs.Add(row.OrganizationId);
            db.SignatureFieldOverrideAuditLogs.Add(new SignatureFieldOverrideAuditLog
            {
                OrganizationId = row.OrganizationId,
                WorkflowTypeId = workflowTypeId,
                PlaceholderName = rename.NewName,
                Action = SignatureAuditAction.RENAMED,
                PreviousValue = row.Value,
                NewValue = row.Value,
                ActorUserId = actorUserId,
                ReferencedBlockNamesJson = blockNamesJson,
            });
        }

        List<PendingSignatureOverride> pendings = await db.PendingSignatureOverrides
            .Where(p => p.WorkflowTypeId == workflowTypeId
                && p.PlaceholderName.ToLower() == loweredOldName)
            .ToListAsync(cancellationToken);
        foreach (PendingSignatureOverride pending in pendings)
        {
            pending.PlaceholderName = rename.NewName;
            if (touchedOrgs.Add(pending.OrganizationId))
            {
                db.SignatureFieldOverrideAuditLogs.Add(new SignatureFieldOverrideAuditLog
                {
                    OrganizationId = pending.OrganizationId,
                    WorkflowTypeId = workflowTypeId,
                    PlaceholderName = rename.NewName,
                    Action = SignatureAuditAction.RENAMED,
                    PreviousValue = null,
                    NewValue = null,
                    ActorUserId = actorUserId,
                    ReferencedBlockNamesJson = blockNamesJson,
                });
            }
        }

        return overrides.Count;
    }

    private static async Task<(int Overrides, int Pendings)> ApplyDeleteAsync(
        ICoreDbContext db,
        string workflowTypeId,
        string actorUserId,
        string name,
        CancellationToken cancellationToken)
    {
        string loweredName = name.ToLowerInvariant();

        List<SignatureFieldOverride> overrides = await db.SignatureFieldOverrides
            .Where(o => o.WorkflowTypeId == workflowTypeId
                && o.PlaceholderName.ToLower() == loweredName)
            .ToListAsync(cancellationToken);
        foreach (SignatureFieldOverride row in overrides)
        {
            db.SignatureFieldOverrides.Remove(row);
            db.SignatureFieldOverrideAuditLogs.Add(new SignatureFieldOverrideAuditLog
            {
                OrganizationId = row.OrganizationId,
                WorkflowTypeId = workflowTypeId,
                PlaceholderName = name,
                Action = SignatureAuditAction.REMOVED,
                PreviousValue = row.Value,
                NewValue = null,
                ActorUserId = actorUserId,
            });
        }

        // Explicit vote removal: the FK cascades on real providers, but the in-memory
        // test provider does not cascade, and being explicit keeps parity.
        List<PendingSignatureOverride> pendings = await db.PendingSignatureOverrides
            .Include(p => p.Votes)
            .Where(p => p.WorkflowTypeId == workflowTypeId
                && p.PlaceholderName.ToLower() == loweredName)
            .ToListAsync(cancellationToken);
        foreach (PendingSignatureOverride pending in pendings)
        {
            db.SignatureOverrideVotes.RemoveRange(pending.Votes);
            db.PendingSignatureOverrides.Remove(pending);
        }

        return (overrides.Count, pendings.Count);
    }
}
