import type {
  PendingOverrideDto,
  SignatureBlockDto,
  SignatureFieldDto,
  SignatureHistoryEntryDto,
  SignatureSectionDto,
  SuggestionDto,
} from '@/api/signatures/schema'

export interface Connection {
  blockId: string
  blockName: string
  sectionId: string
  sectionTitle: string
  replacementTag: string
}

export function normaliseDepTag(tag: string): string {
  return tag
    .replace(/\{[^}]*\}/g, '')
    .replace(/__+/g, '_')
    .replace(/^_|_$/g, '')
}

export function resolveConnections(
  tags: string[],
  blocksByTag: Map<string, { block: SignatureBlockDto; sectionId: string }>,
  sections: SignatureSectionDto[],
): Connection[] {
  const out: Connection[] = []
  const seen = new Set<string>()
  for (const tag of tags) {
    const direct = blocksByTag.get(tag)
    let entry = direct ?? null
    if (!entry) {
      const normalised = normaliseDepTag(tag)
      if (normalised && normalised !== tag) {
        entry = blocksByTag.get(normalised) ?? null
      }
    }
    if (!entry) {
      continue
    }
    if (seen.has(entry.block.blockId)) {
      continue
    }
    seen.add(entry.block.blockId)
    const section = sections.find((s) => s.sectionId === entry.sectionId)
    out.push({
      blockId: entry.block.blockId,
      blockName: entry.block.blockName,
      sectionId: entry.sectionId,
      sectionTitle: section?.sectionTitle ?? '',
      replacementTag: entry.block.replacementTag,
    })
  }
  return out
}

/** Index the workflow-level field definitions by their `name` identity. */
export function buildFieldsByName(
  fields: SignatureFieldDto[],
): Map<string, SignatureFieldDto> {
  const map = new Map<string, SignatureFieldDto>()
  for (const field of fields) {
    map.set(field.name, field)
  }
  return map
}

/**
 * Resolve a block's `fieldNames` to full field DTOs, preserving the block's
 * order. Names that don't resolve (a rename/delete race between the block
 * list and the field list) are dropped defensively rather than rendered as
 * holes.
 */
export function resolveBlockFields(
  block: SignatureBlockDto,
  fieldsByName: Map<string, SignatureFieldDto>,
): SignatureFieldDto[] {
  const out: SignatureFieldDto[] = []
  for (const name of block.fieldNames) {
    const field = fieldsByName.get(name)
    if (field) {
      out.push(field)
    }
  }
  return out
}

export interface BlockLookupEntry {
  block: SignatureBlockDto
  sectionId: string
  sectionTitle: string
}

/** Index every block in the workflow by id, with its owning section. */
export function buildBlocksById(
  sections: SignatureSectionDto[],
): Map<string, BlockLookupEntry> {
  const map = new Map<string, BlockLookupEntry>()
  for (const section of sections) {
    for (const block of section.blocks) {
      map.set(block.blockId, {
        block,
        sectionId: section.sectionId,
        sectionTitle: section.sectionTitle,
      })
    }
  }
  return map
}

/**
 * Field label for a history row.
 * - Legacy rows (written before overrides moved to the workflow level) carry a
 *   non-null `blockId` → "blockName · fieldName" via the block-id lookup.
 * - New rows have `blockId: null` and may carry `usedByBlockNames` (a snapshot
 *   of the referencing block names at write time) → "fieldName · used by A, B",
 *   falling back to the field name alone when the snapshot is null/empty.
 *
 * `formatUsedBy` renders the localized "used by A, B" suffix so this helper
 * stays free of i18n plumbing.
 */
export function historyFieldLabel(
  row: Pick<
    SignatureHistoryEntryDto,
    'blockId' | 'placeholderName' | 'usedByBlockNames'
  >,
  blockLabelById: Map<string, string>,
  displayNameByField: Map<string, string>,
  formatUsedBy: (blockNames: string) => string = (names) => `used by ${names}`,
): string {
  const fieldName =
    displayNameByField.get(row.placeholderName) ?? row.placeholderName
  if (row.blockId !== null) {
    const blockName = blockLabelById.get(row.blockId) ?? row.blockId
    return `${blockName} · ${fieldName}`
  }
  const blockNames = row.usedByBlockNames ?? []
  if (blockNames.length === 0) {
    return fieldName
  }
  return `${fieldName} · ${formatUsedBy(blockNames.join(', '))}`
}

/* ──────────────────────────────────────────────────────────────────────
 * Phase 2 review-queue UI helpers — pure, no Vue runtime dependencies.
 * ──────────────────────────────────────────────────────────────────── */

export type PendingUiState = 'default' | 'override' | 'pending-mine' | 'pending-other'

/**
 * Resolve the visual state of a signature field for the review-queue UI:
 * - no pending + not isOverridden → 'default'
 * - no pending + isOverridden     → 'override'
 * - pending && proposer is me     → 'pending-mine'
 * - pending && proposer is not me → 'pending-other'
 *
 * `myUserId` may be null (e.g. token not yet decoded), in which case any
 * pending is treated as someone else's.
 */
export function resolvePendingState(
  field: SignatureFieldDto,
  myUserId: string | null,
): PendingUiState {
  const pending = field.pending ?? null
  if (!pending) {
    return field.isOverridden ? 'override' : 'default'
  }
  if (myUserId !== null && pending.proposerUserId === myUserId) {
    return 'pending-mine'
  }
  return 'pending-other'
}

/**
 * Whether the current user can cast a vote on this field's pending override.
 * True iff:
 * - the user has at least one of the allowed signature roles
 * - the field has a pending override
 * - the user is not the proposer
 * - the user has not already voted (`pending.myVote` is null)
 */
export function canUserVote(
  field: SignatureFieldDto,
  myUserId: string | null,
  hasAnyRole: boolean,
): boolean {
  if (!hasAnyRole) {
    return false
  }
  const pending = field.pending ?? null
  if (!pending) {
    return false
  }
  if (myUserId !== null && pending.proposerUserId === myUserId) {
    return false
  }
  if (pending.myVote !== null) {
    return false
  }
  return true
}

export interface VoteProgress {
  approvePct: number
  rejectPct: number
  approveRemaining: number
  rejectRemaining: number
}

/**
 * Compute progress bar % and remaining vote counts. Percentages are clamped
 * to [0, 100]. A non-positive `threshold` is defensive: it should never
 * happen (server min is 2) but the helper returns zero remaining and 100%
 * progress to avoid division-by-zero / NaN.
 */
export function computeVoteProgress(input: {
  approveCount: number
  rejectCount: number
  threshold: number
}): VoteProgress {
  const { approveCount, rejectCount, threshold } = input
  if (threshold <= 0) {
    return {
      approvePct: 100,
      rejectPct: 100,
      approveRemaining: 0,
      rejectRemaining: 0,
    }
  }
  const approvePct = Math.max(0, Math.min(100, (approveCount / threshold) * 100))
  const rejectPct = Math.max(0, Math.min(100, (rejectCount / threshold) * 100))
  return {
    approvePct,
    rejectPct,
    approveRemaining: Math.max(0, threshold - approveCount),
    rejectRemaining: Math.max(0, threshold - rejectCount),
  }
}

/**
 * Return the suggestions on a pending override, ordered by `createdAt`
 * ascending (stable). Defensive: filters out any entries with a null
 * `suggestedValue` even though the BE projection drops them.
 */
export function summariseSuggestions(
  pending: PendingOverrideDto | null,
): SuggestionDto[] {
  if (!pending) {
    return []
  }
  const suggestions = pending.suggestions ?? []
  // `suggestedValue` is non-nullable in the typed contract, but defend
  // against the BE shipping a null in a future shape.
  const filtered = suggestions.filter(
    (s) => (s.suggestedValue as string | null) !== null,
  )
  return [...filtered].sort((a, b) => {
    if (a.createdAt < b.createdAt) {
      return -1
    }
    if (a.createdAt > b.createdAt) {
      return 1
    }
    return 0
  })
}

/**
 * Count the fields whose pending override is waiting for this user's vote.
 * Callers choose the scope: pass the workflow's flat `fields` for a
 * once-per-field total, or a block's resolved fields for a per-block
 * navigation pill (a shared field then counts once per referencing block).
 */
export function countAwaitingMyVote(
  fields: SignatureFieldDto[],
  myUserId: string | null,
  hasAnyRole: boolean,
): number {
  let count = 0
  for (const field of fields) {
    if (canUserVote(field, myUserId, hasAnyRole)) {
      count++
    }
  }
  return count
}

/**
 * Count the fields where the caller is the proposer of the pending override
 * AND a reviewer has left at least one counter-suggestion awaiting the
 * proposer's accept / edit / cancel. Same scoping rules as
 * `countAwaitingMyVote`.
 */
export function countAwaitingProposerAction(
  fields: SignatureFieldDto[],
  myUserId: string | null,
): number {
  if (myUserId === null) {
    return 0
  }
  let count = 0
  for (const field of fields) {
    const pending = field.pending
    if (
      pending
      && pending.proposerUserId === myUserId
      && summariseSuggestions(pending).length > 0
    ) {
      count++
    }
  }
  return count
}
