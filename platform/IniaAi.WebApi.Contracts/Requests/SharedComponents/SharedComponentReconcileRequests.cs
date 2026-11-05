using System.Text.Json;
using IniaAi.WebApi.Contracts.Enums;

namespace IniaAi.WebApi.Contracts.Requests.SharedComponents;

/// <summary>
/// Optional draft for a component-scoped reconcile preview. Without a candidate the preview
/// reports the blast radius every definition change shares; with one it also diffs the candidate
/// against the stored head and lists the override rows the update would orphan.
/// </summary>
/// <param name="CandidateDefinition">The draft definition to diff against the head, or null for blast radius only.</param>
public record PreviewSharedComponentReconcileRequest(JsonElement? CandidateDefinition = null);

/// <summary>
/// One batch of per-instance reconcile decisions against one template, applied atomically with
/// every fan-out they trigger.
/// </summary>
/// <param name="BaseVersionId">The template head version the decisions were made against — the anchor concurrency token.</param>
/// <param name="Decisions">The per-instance decisions.</param>
public record ApplySharedComponentReconcileRequest(
    string BaseVersionId,
    List<SharedComponentReconcileDecisionDto> Decisions);

/// <summary>
/// One instance-level reconcile decision. Address the instance by exactly one of
/// <paramref name="TargetStableId"/> or <paramref name="TargetPath"/>.
/// </summary>
/// <param name="SharedComponentId">The component the caller believes the instance is a copy of — cross-checked server-side.</param>
/// <param name="TargetStableId">The instance's stable id (block or section), or null when addressing by path.</param>
/// <param name="TargetPath">The instance's canonical node path (<c>Sections[i]</c> or <c>Sections[i].Blocks[j]</c>), or null when addressing by stable id.</param>
/// <param name="Action">The decision.</param>
/// <param name="ExpectedHeadVersionId">The component head the decision was made against; required for UpdateShared, AdoptHead, and Relink.</param>
/// <param name="AcknowledgeSignatureOrphans">Consent for the override rows this decision's grafts would delete or leave inert.</param>
/// <param name="AcknowledgeOverwrittenEntries">
/// Consent for local edits the fan-out would discard in consumers' referenced entry slots that no
/// decision named. Without it such a graft is refused with
/// <c>SharedComponent.OverwritesLocalEntries</c>, which names the workflow type and slots.
/// </param>
/// <param name="EntryKey">Scopes the decision to one entry of a section copy by definition slot tag; exactly one of this or <paramref name="EntryStableId"/>, or neither for a whole-section decision.</param>
/// <param name="EntryStableId">Scopes the decision to one entry of a section copy by the live block's stable id.</param>
/// <param name="ExpectedRefHeadVersionId">For a ref-entry UpdateShared or AdoptHead: the referenced Block component's head the decision was made against.</param>
public record SharedComponentReconcileDecisionDto(
    string SharedComponentId,
    string? TargetStableId,
    string? TargetPath,
    SharedComponentReconcileAction Action,
    string? ExpectedHeadVersionId = null,
    bool AcknowledgeSignatureOrphans = false,
    string? EntryKey = null,
    string? EntryStableId = null,
    string? ExpectedRefHeadVersionId = null,
    bool AcknowledgeOverwrittenEntries = false);

/// <summary>
/// A replacement definition for a shared component; the server re-normalizes it, appends it as
/// the new head, and rewrites every Linked consumer in the same transaction.
/// </summary>
/// <param name="Definition">The definition JSON — a section with inner blocks, or a single block, matching the component's kind.</param>
/// <param name="ExpectedHeadVersionId">The component head this edit was based on — the concurrency token.</param>
/// <param name="AcknowledgeSignatureOrphans">Consent for the override rows the update would delete or leave inert.</param>
/// <param name="AcknowledgeOverwrittenEntries">
/// Consent for local edits the fan-out would discard in consumers' referenced entry slots that no
/// decision named. Without it such a graft is refused with
/// <c>SharedComponent.OverwritesLocalEntries</c>, which names the slots.
/// </param>
public record SaveSharedComponentContentRequest(
    JsonElement Definition,
    string ExpectedHeadVersionId,
    bool AcknowledgeSignatureOrphans = false,
    bool AcknowledgeOverwrittenEntries = false);

/// <summary>
/// Makes a stored version the new head. Token-only by design: the server re-appends its own
/// stored payload, callers never round-trip definition content.
/// </summary>
/// <param name="ExpectedHeadVersionId">The component head this restore was decided against — the concurrency token.</param>
/// <param name="AcknowledgeSignatureOrphans">Consent for the override rows the restore's grafts would delete or leave inert.</param>
public record RestoreSharedComponentVersionRequest(
    string ExpectedHeadVersionId,
    bool AcknowledgeSignatureOrphans = false);
