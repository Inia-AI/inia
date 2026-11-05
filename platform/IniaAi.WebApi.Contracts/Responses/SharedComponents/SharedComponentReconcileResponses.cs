using System.Text.Json;
using IniaAi.WebApi.Contracts.Enums;

namespace IniaAi.WebApi.Contracts.Responses.SharedComponents;

/// <summary>
/// The reconcile surface of one template: every block or section carrying a shared-component link
/// record, classified against the live registry.
/// </summary>
/// <param name="WorkflowTypeId">The template.</param>
/// <param name="BaseVersionId">The template's current head version — the anchor token a reconcile apply must echo.</param>
/// <param name="BaseContentHash">The template head's content hash.</param>
/// <param name="Instances">One row per linked or broken-out instance, in template order.</param>
public record WorkflowSharedComponentReconcileResponse(
    string WorkflowTypeId,
    string? BaseVersionId,
    string? BaseContentHash,
    List<SharedComponentInstanceReconcileDto> Instances);

/// <summary>
/// One template instance carrying a shared-component link record, classified against the registry.
/// </summary>
/// <param name="TargetPath">The instance's canonical node path.</param>
/// <param name="TargetStableId">The instance's stable id, when minted.</param>
/// <param name="Kind">Whether the instance is a block or a section copy.</param>
/// <param name="SharedComponentId">The component the instance is a copy of.</param>
/// <param name="ComponentName">The registry entry's name, or null when the id resolves to nothing.</param>
/// <param name="ComponentState">The registry entry's lifecycle state, or null when the id resolves to nothing.</param>
/// <param name="LinkState">Whether the copy follows the head or was forked.</param>
/// <param name="Relation">The three-hash classification of the copy against the component.</param>
/// <param name="InstanceContentHash">The copy's content hash, reverse-normalized through its bindings.</param>
/// <param name="LinkVersionHash">The component version hash stamped on the copy.</param>
/// <param name="HeadVersionId">The component's current head version id, when one exists.</param>
/// <param name="HeadContentHash">The component's current head content hash, when one exists.</param>
/// <param name="LinkHashStale">True when the copy's content is at the head but its stamp lags; any future fan-out restamps it.</param>
/// <param name="OldDefinitionAvailable">True when this deployment's chain still holds a payload for the definition the link hash was stamped from.</param>
/// <param name="AvailableActions">The sanctioned decisions for this instance, ordered default-first.</param>
/// <param name="Entries">Per-entry rows for a section copy, in definition order plus local inserts; null for block copies. Entry deviations never change the section row's <paramref name="Relation"/>.</param>
/// <param name="OrderDeviates">True when the copy's live block order differs from the definition's entry order.</param>
/// <param name="HasLocalOverrides">True when recorded per-entry overrides (hidden or broken-out slots) were reconstructed out of <paramref name="InstanceContentHash"/> — the copy deviates deliberately even when <paramref name="Relation"/> reads in-sync.</param>
/// <param name="LocalInsertCount">How many of <paramref name="Entries"/> are local inserts — blocks this copy carries beyond the definition.</param>
public record SharedComponentInstanceReconcileDto(
    string TargetPath,
    string? TargetStableId,
    SharedComponentKind Kind,
    string SharedComponentId,
    string? ComponentName,
    SharedComponentState? ComponentState,
    SharedComponentLinkState LinkState,
    SharedComponentRelation Relation,
    string InstanceContentHash,
    string? LinkVersionHash,
    string? HeadVersionId,
    string? HeadContentHash,
    bool LinkHashStale,
    bool OldDefinitionAvailable,
    List<SharedComponentReconcileAction> AvailableActions,
    List<SharedComponentEntryReconcileDto>? Entries = null,
    bool OrderDeviates = false,
    bool HasLocalOverrides = false,
    int LocalInsertCount = 0);

/// <summary>
/// One entry of a section copy, classified against the section definition. Entry-scoped decisions
/// address it by exactly one of <paramref name="EntryKey"/> or <paramref name="EntryStableId"/>.
/// </summary>
/// <param name="EntryKey">The definition slot tag the entry fills; null for local inserts.</param>
/// <param name="EntryStableId">The live block's stable id; null for hidden entries (nothing is materialized).</param>
/// <param name="EntryPath">The live block's canonical node path; null for hidden entries.</param>
/// <param name="DisplayName">The block's display name — taken from the definition for hidden entries.</param>
/// <param name="Origin">Whether the definition provides the entry inline, by component reference, or not at all (a local insert).</param>
/// <param name="OverrideState">The per-entry deviation recorded on the section copy.</param>
/// <param name="RefSharedComponentId">The referenced Block component, for ref entries and Library-linked local inserts.</param>
/// <param name="RefComponentName">The referenced component's registry name, when the reference resolves.</param>
/// <param name="RefComponentState">The referenced component's lifecycle state, when the reference resolves.</param>
/// <param name="RefHeadVersionId">The referenced component's current head version id — the token a ref-entry push or adopt must echo.</param>
/// <param name="RefHeadContentHash">The referenced component's current head content hash.</param>
/// <param name="Relation">The entry's classification against its definition counterpart; null when it has none (local inserts) or nothing is materialized (hidden).</param>
/// <param name="InstanceContentHash">The live block's content hash; null for hidden entries.</param>
/// <param name="DeletedByDefinition">True when the definition no longer provides the entry's slot while the local block survives.</param>
/// <param name="AvailableActions">The sanctioned entry-scoped decisions, ordered default-first.</param>
/// <param name="InsertLinkState">Whether a Library-linked local insert still follows its component; null for every other entry. A broken-out insert whose component is absent is a fork, not a broken link — export carries only actively-linked components.</param>
public record SharedComponentEntryReconcileDto(
    string? EntryKey,
    string? EntryStableId,
    string? EntryPath,
    string DisplayName,
    SharedComponentEntryOrigin Origin,
    SharedComponentEntryOverrideState OverrideState,
    string? RefSharedComponentId,
    string? RefComponentName,
    SharedComponentState? RefComponentState,
    string? RefHeadVersionId,
    string? RefHeadContentHash,
    SharedComponentRelation? Relation,
    string? InstanceContentHash,
    bool DeletedByDefinition,
    List<SharedComponentReconcileAction> AvailableActions,
    SharedComponentLinkState? InsertLinkState = null);

/// <summary>
/// A component-scoped impact preview: the blast radius of any definition change, plus the slot
/// delta and orphaned override rows of a specific candidate when one was supplied. Previews are
/// advisory — the apply path re-computes everything inside its transaction.
/// </summary>
/// <param name="SharedComponentId">The component.</param>
/// <param name="HeadVersionId">The current head version — the token a follow-up write must echo.</param>
/// <param name="HeadContentHash">The current head's content hash.</param>
/// <param name="CandidateContentHash">The normalized candidate's content hash, or null when no candidate was supplied.</param>
/// <param name="Consumers">The workflow types holding live linked copies.</param>
/// <param name="UpdateSharedImpact">What making the candidate (or any new definition) the head would do beyond the rewrites themselves.</param>
public record SharedComponentReconcilePreviewResponse(
    string SharedComponentId,
    string HeadVersionId,
    string HeadContentHash,
    string? CandidateContentHash,
    List<ConsumingWorkflowTypeDto> Consumers,
    SharedComponentUpdateImpactDto UpdateSharedImpact);

/// <summary>
/// What a definition change does beyond rewriting the consumer copies themselves.
/// </summary>
/// <param name="AffectedBaselines">Regression baseline registrations on consuming types, with their freshness at read time — fresh ones are what the fan-out would stale.</param>
/// <param name="OrphanedSignatureOverrides">Client override rows the change would delete or leave inert.</param>
/// <param name="SlotDelta">The slot-level diff between head and candidate, for section components with a candidate.</param>
public record SharedComponentUpdateImpactDto(
    List<SharedComponentBaselineImpactDto> AffectedBaselines,
    List<SharedComponentOverrideImpactDto> OrphanedSignatureOverrides,
    SharedComponentSlotDeltaDto? SlotDelta);

/// <summary>
/// One regression baseline registration a definition change would affect.
/// </summary>
/// <param name="RegistrationId">The baseline registration.</param>
/// <param name="WorkflowTypeId">The consuming workflow type.</param>
/// <param name="OrganizationId">The organization holding the registration.</param>
/// <param name="CounterpartyId">The counterparty scope of the registration; empty for type-wide.</param>
/// <param name="CounterpartyName">The counterparty's display name, when recorded.</param>
/// <param name="IsCurrentlyFresh">True when the blessed hash equals the type's live head — exactly what a fan-out save would stale.</param>
public record SharedComponentBaselineImpactDto(
    string RegistrationId,
    string WorkflowTypeId,
    string OrganizationId,
    string CounterpartyId,
    string? CounterpartyName,
    bool IsCurrentlyFresh);

/// <summary>
/// One client signature-override row a definition change touches.
/// </summary>
/// <param name="OrganizationId">The organization owning the override.</param>
/// <param name="WorkflowTypeId">The consuming workflow type.</param>
/// <param name="StableBlockId">The stable id of the instance block the row keys on.</param>
/// <param name="BlockTag">The instance block's replacement tag, for display.</param>
/// <param name="PlaceholderName">The placeholder the row overrides.</param>
/// <param name="IsPending">True for a pending review proposal rather than an active override.</param>
/// <param name="Kind">Whether the row is deleted with its block or merely stops being consulted.</param>
public record SharedComponentOverrideImpactDto(
    string OrganizationId,
    string WorkflowTypeId,
    string StableBlockId,
    string BlockTag,
    string PlaceholderName,
    bool IsPending,
    SharedComponentOverrideImpactKind Kind);

/// <summary>
/// The slot-level diff between two section definitions. Slots correlate by tag; an unambiguous
/// pair with equal per-block content is reported as a rename, and a remaining pair matched by
/// name or position heuristics as a rescue.
/// </summary>
/// <param name="MatchedSlotTags">Slots present in both definitions.</param>
/// <param name="AddedSlotTags">Slots only the new definition has.</param>
/// <param name="RemovedSlotTags">Slots only the old definition has; their instance blocks drop.</param>
/// <param name="RenamedSlots">Pure tag renames (old tag → new tag); the blocks survive.</param>
/// <param name="RescuedSlots">Fuzzy-matched slot pairs — matched with new content; the blocks survive and take the new definition's content.</param>
public record SharedComponentSlotDeltaDto(
    List<string> MatchedSlotTags,
    List<string> AddedSlotTags,
    List<string> RemovedSlotTags,
    Dictionary<string, string> RenamedSlots,
    List<SharedComponentSlotRescueDto> RescuedSlots);

/// <summary>
/// One fuzzy-matched slot pair: the old and new definitions disagree on both tag and content, but
/// the pair was still correlated, so consumer blocks survive under the new tag instead of being
/// dropped and re-added.
/// </summary>
/// <param name="OldTag">The slot's tag in the old definition.</param>
/// <param name="NewTag">The slot's tag in the new definition.</param>
/// <param name="Reason">Which heuristic correlated the pair.</param>
public record SharedComponentSlotRescueDto(
    string OldTag,
    string NewTag,
    SharedComponentSlotRescueReason Reason);

/// <summary>
/// The outcome of one reconcile apply: per-decision outcomes plus successor tokens for everything
/// the transaction touched.
/// </summary>
/// <param name="WorkflowTypeId">The anchor template.</param>
/// <param name="DecisionOutcomes">One outcome per submitted decision, in submission order.</param>
/// <param name="WorkflowSaves">Every template the apply saved — the anchor plus each fanned-out consumer.</param>
/// <param name="ComponentHeads">The current head tokens of every component the decisions referenced.</param>
public record SharedComponentReconcileApplyResponse(
    string WorkflowTypeId,
    List<SharedComponentReconcileDecisionOutcomeDto> DecisionOutcomes,
    List<SharedComponentWorkflowSaveDto> WorkflowSaves,
    List<SharedComponentReconciledHeadDto> ComponentHeads);

/// <summary>
/// The outcome of one applied decision.
/// </summary>
/// <param name="SharedComponentId">The component the decision addressed.</param>
/// <param name="TargetPath">The instance's canonical node path, resolved server-side.</param>
/// <param name="TargetStableId">The instance's stable id, when minted.</param>
/// <param name="Action">The applied decision.</param>
/// <param name="AlreadyCurrent">True when an UpdateShared found the derived definition already at the head — nothing was appended.</param>
public record SharedComponentReconcileDecisionOutcomeDto(
    string SharedComponentId,
    string TargetPath,
    string? TargetStableId,
    SharedComponentReconcileAction Action,
    bool AlreadyCurrent);

/// <summary>
/// One template saved by a component write — successor tokens plus what the save did to override
/// rows.
/// </summary>
/// <param name="WorkflowTypeId">The saved template.</param>
/// <param name="VersionId">The template's new head version id — the next <c>baseVersionId</c>.</param>
/// <param name="ContentHash">The template's content hash after the save.</param>
/// <param name="WasDeduped">True when the save was a byte-identical no-op.</param>
/// <param name="Warnings">Non-blocking structural issues found in the template after the rewrite.</param>
/// <param name="SignatureSync">Signature-field sync bookkeeping from the save.</param>
/// <param name="OrphanedOverrides">The override rows in this template the write deleted or left inert.</param>
public record SharedComponentWorkflowSaveDto(
    string WorkflowTypeId,
    string VersionId,
    string ContentHash,
    bool WasDeduped,
    List<WorkflowTemplates.WorkflowTemplateWarningDto> Warnings,
    SignatureSyncDto SignatureSync,
    List<SharedComponentOverrideImpactDto> OrphanedOverrides);

/// <summary>
/// Counts of what a save's signature-field sync did.
/// </summary>
/// <param name="OverridesRenamed">Override rows carried to a renamed field.</param>
/// <param name="OverridesDeleted">Override rows cascade-deleted with their field definition.</param>
/// <param name="PendingsDeleted">Pending proposals cascade-deleted with their field definition.</param>
public record SignatureSyncDto(
    int OverridesRenamed,
    int OverridesDeleted,
    int PendingsDeleted);

/// <summary>
/// A component's head tokens after an apply.
/// </summary>
/// <param name="SharedComponentId">The component.</param>
/// <param name="HeadVersionId">The current head version id.</param>
/// <param name="HeadContentHash">The current head's content hash.</param>
/// <param name="Appended">True when this apply created the head.</param>
public record SharedComponentReconciledHeadDto(
    string SharedComponentId,
    string HeadVersionId,
    string HeadContentHash,
    bool Appended);

/// <summary>
/// The outcome of a component definition write: the component's new head tokens plus successor
/// tokens for every consumer template the fan-out rewrote.
/// </summary>
/// <param name="SharedComponentId">The component.</param>
/// <param name="HeadVersionId">The head version after the write — new, or the prior head when deduped.</param>
/// <param name="HeadContentHash">The head's content hash.</param>
/// <param name="WasDeduped">True when the definition matched the head byte-for-byte and nothing was appended or fanned out.</param>
/// <param name="ConsumerSaves">Every consumer template the fan-out saved.</param>
public record SharedComponentAppendResponse(
    string SharedComponentId,
    string HeadVersionId,
    string HeadContentHash,
    bool WasDeduped,
    List<SharedComponentWorkflowSaveDto> ConsumerSaves);

/// <summary>
/// The outcome of retiring or unretiring a component.
/// </summary>
/// <param name="SharedComponentId">The component.</param>
/// <param name="State">The component's lifecycle state after the call.</param>
/// <param name="BrokenOutInstanceCount">How many Linked copies the retirement flipped to BrokenOut.</param>
/// <param name="WorkflowSaves">Every template the retirement's break-outs and restamps saved.</param>
/// <param name="InlinedContainerIds">Section components whose definitions referenced the retired block and had its head content baked in as an inline slot.</param>
public record SharedComponentRetireResponse(
    string SharedComponentId,
    SharedComponentState State,
    int BrokenOutInstanceCount,
    List<SharedComponentWorkflowSaveDto> WorkflowSaves,
    List<string> InlinedContainerIds);

/// <summary>
/// One stored version's definition JSON served verbatim, plus its identifying tokens.
/// </summary>
/// <param name="SharedComponentId">The component.</param>
/// <param name="VersionId">The version whose content is served.</param>
/// <param name="ContentHash">The version's content hash.</param>
/// <param name="Content">The definition JSON, verbatim.</param>
public record SharedComponentVersionContentResponse(
    string SharedComponentId,
    string VersionId,
    string ContentHash,
    JsonElement Content);
