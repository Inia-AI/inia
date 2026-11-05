/**
 * DTOs for the Signatures API.
 *
 * These shapes must match the C# contracts under
 * `IniaAi.WebApi.Contracts/Responses/Signatures/` and `.../Requests/Signatures/`.
 * If the backend drifts, the mismatch will surface here.
 *
 * Identity model: signature fields are defined at the WORKFLOW level and keyed by
 * `name`. The GET's top-level `fields` list is the single source of truth (each
 * field appears once); blocks reference fields by `fieldNames` and the client
 * joins by name. Overrides are shared per workflow type — one value per field,
 * applied to every block that references it. Writes key on the field name, so a
 * staff rename/delete of a field between GET and POST surfaces as a 409
 * (`Signatures.Stale`) and the client shows the "view outdated" banner;
 * `sectionId` / `blockId` remain session-valid Core GUIDs used only for
 * navigation and replay matching.
 */

/**
 * Vote outcome on a pending signature override. Mirrors the
 * `SignatureVoteKind` C# enum which serialises as a string.
 */
export type SignatureVoteKind = 'Approve' | 'Reject'

/**
 * A reviewer comment attached to a vote on a pending override. Includes
 * approvals and rejections; suggestions are surfaced separately via
 * `SuggestionDto` even though the same vote row may carry both fields.
 */
export interface VoteCommentDto {
  voteId: string
  voterUserId: string
  voteKind: SignatureVoteKind
  comment: string
  createdAt: string
}

/**
 * A counter-suggestion left on a reject vote. The proposer can accept any
 * suggestion to replace the pending value (resets all votes).
 */
export interface SuggestionDto {
  voteId: string
  voterUserId: string
  suggestedValue: string
  comment: string | null
  createdAt: string
}

/**
 * A pending override awaiting reviewer votes. Lives until either approves
 * or rejects reach the tenant threshold. `proposedValue: null` means
 * "remove the override" (clears any active value once approved).
 */
export interface PendingOverrideDto {
  id: string
  proposerUserId: string
  proposedValue: string | null
  createdAt: string
  approveCount: number
  rejectCount: number
  myVote: SignatureVoteKind | null
  comments: VoteCommentDto[]
  suggestions: SuggestionDto[]
}

export interface SignatureFieldDto {
  /** Workflow-level field identity. Server stores overrides under this exact casing. */
  name: string
  displayName: string
  description: string
  defaultValue: string
  value: string
  isOverridden: boolean
  /**
   * StableBlockIds of every block referencing this field, in (section, block)
   * order. Length > 1 means the field is shared — its value / pending / draft
   * state is identical across all referencing blocks.
   */
  usedByBlockIds: string[]
  /**
   * In-flight pending override for this field, if any. Backend returns this
   * inline alongside the active values on the GET. Optional / nullable
   * because older responses (and fields with no in-flight pending) omit it.
   */
  pending?: PendingOverrideDto | null
}

/**
 * Block kind discriminator. Mirrors the `BlockTypes` C# enum's `nameof` string.
 * Currently only AI Query and Merge blocks expose signature fields, but the
 * `(string & {})` tail keeps the union open: if the backend ships a new block
 * type before the FE is updated, the value still type-checks at runtime instead
 * of silently mismatching. Known values still autocomplete in the IDE.
 */
export type SignatureBlockType = 'AiQuery' | 'Merge' | (string & {})

export interface SignatureBlockDto {
  blockId: string
  blockName: string
  /**
   * Block kind. Drives FE branching, e.g. the AI-prompt-replay Test dialog only
   * applies to `AiQuery` blocks since `Merge` blocks have no prompt to replay.
   */
  blockType: SignatureBlockType
  replacementTag: string
  dependsOn: string[]
  /**
   * Names of the workflow-level fields this block references, ordered by first
   * appearance in the block's templates. Join against the top-level `fields`
   * list to resolve the full field DTOs.
   */
  fieldNames: string[]
}

export interface SignatureSectionDto {
  sectionId: string
  sectionTitle: string
  sectionOrder: number
  blocks: SignatureBlockDto[]
}

export interface WorkflowSignaturesDto {
  workflowTypeId: string
  workflowName: string
  /**
   * Per-tenant approve/reject vote count required to resolve a pending override.
   * Server enforces a minimum of 2; clients should render this against live
   * `approveCount` / `rejectCount` instead of hard-coding a default.
   */
  reviewThreshold: number
  /**
   * Workflow-level field definitions — the single source of truth. Each field
   * appears exactly once, ordered by first appearance in block templates.
   */
  fields: SignatureFieldDto[]
  sections: SignatureSectionDto[]
}

/** Single override update. A `null` value reverts the field to its staff default. */
export interface SignatureUpdate {
  placeholderName: string
  value: string | null
}

export interface UpdateSignaturesRequest {
  updates: SignatureUpdate[]
}

/**
 * Action recorded on a row of the signature override history. Mirrors the
 * `SignatureAuditAction` C# constants. The tail `(string & {})` keeps the union
 * open so an unrecognised value from the backend still type-checks instead of
 * crashing the table render.
 */
export type SignatureHistoryAction =
  | 'Created'
  | 'Updated'
  | 'Removed'
  | 'Renamed'
  | (string & {})

/**
 * A user who approved the pending whose resolution caused the value change.
 * Snapshotted at promotion time, so this list is preserved even after the
 * pending and vote rows are deleted on resolution.
 */
export interface SignatureHistoryApproverDto {
  userId: string
  email: string | null
}

/**
 * One value transition on a signature override. `previousValue` is null on
 * `Created`; `newValue` is null on `Removed`. On `Renamed` (an override
 * carried to a new name by a staff rename) `previousValue === newValue`.
 */
export interface SignatureHistoryEntryDto {
  id: string
  /**
   * Non-null only on legacy rows written before overrides moved to the
   * workflow level. New rows have `blockId: null` and carry
   * `usedByBlockNames` instead.
   */
  blockId: string | null
  placeholderName: string
  action: SignatureHistoryAction
  previousValue: string | null
  newValue: string | null
  actorUserId: string | null
  actorUserEmail: string | null
  approvers: SignatureHistoryApproverDto[]
  /**
   * Snapshot of the names of the blocks referencing the field at write time.
   * Null on legacy rows (and possibly on new rows with no snapshot).
   */
  usedByBlockNames: string[] | null
  occurredAt: string
}

/** One page of override history entries, ordered newest first. */
export interface SignatureHistoryPageDto {
  items: SignatureHistoryEntryDto[]
  totalCount: number
  skip: number
  take: number
}

/**
 * Query parameters for the History tab. All filter fields are optional;
 * unset filters do not narrow the result set.
 */
export interface SignatureHistoryFilters {
  placeholderName?: string
  actorUserId?: string
  fromUtc?: string
  toUtc?: string
  skip?: number
  take?: number
}
