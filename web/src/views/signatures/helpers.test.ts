import { describe, expect, it } from 'vitest'
import type {
  PendingOverrideDto,
  SignatureBlockDto,
  SignatureFieldDto,
  SignatureHistoryEntryDto,
  SignatureSectionDto,
  SuggestionDto,
} from '@/api/signatures/schema'
import {
  buildBlocksById,
  buildFieldsByName,
  canUserVote,
  computeVoteProgress,
  countAwaitingMyVote,
  countAwaitingProposerAction,
  historyFieldLabel,
  normaliseDepTag,
  resolveBlockFields,
  resolveConnections,
  resolvePendingState,
  summariseSuggestions,
} from './helpers'

function makeField(overrides: Partial<SignatureFieldDto> = {}): SignatureFieldDto {
  return {
    name: 'field_a',
    displayName: 'Field A',
    description: '',
    defaultValue: 'd',
    value: 'd',
    isOverridden: false,
    usedByBlockIds: ['block-1'],
    pending: null,
    ...overrides,
  }
}

function makePending(overrides: Partial<PendingOverrideDto> = {}): PendingOverrideDto {
  return {
    id: 'pending-1',
    proposerUserId: 'user-1',
    proposedValue: 'new value',
    createdAt: '2026-01-01T00:00:00Z',
    approveCount: 1,
    rejectCount: 0,
    myVote: null,
    comments: [],
    suggestions: [],
    ...overrides,
  }
}

function makeSuggestion(overrides: Partial<SuggestionDto> = {}): SuggestionDto {
  return {
    voteId: 'vote-1',
    voterUserId: 'user-2',
    suggestedValue: 'a suggestion',
    comment: null,
    createdAt: '2026-01-02T00:00:00Z',
    ...overrides,
  }
}

function makeBlock(overrides: Partial<SignatureBlockDto> = {}): SignatureBlockDto {
  return {
    blockId: 'block-1',
    blockName: 'Block 1',
    blockType: 'AiQuery',
    replacementTag: 'block1',
    dependsOn: [],
    fieldNames: [],
    ...overrides,
  }
}

function makeSection(overrides: Partial<SignatureSectionDto> = {}): SignatureSectionDto {
  return {
    sectionId: 'section-1',
    sectionTitle: 'Section 1',
    sectionOrder: 0,
    blocks: [],
    ...overrides,
  }
}

function makeHistoryRow(
  overrides: Partial<SignatureHistoryEntryDto> = {},
): SignatureHistoryEntryDto {
  return {
    id: 'row-1',
    blockId: null,
    placeholderName: 'field_a',
    action: 'Created',
    previousValue: null,
    newValue: 'v',
    actorUserId: 'user-1',
    actorUserEmail: 'user-1@example.com',
    approvers: [],
    usedByBlockNames: null,
    occurredAt: '2026-01-01T00:00:00Z',
    ...overrides,
  }
}

describe('normaliseDepTag', () => {
  it('returns the input unchanged when there are no braces', () => {
    expect(normaliseDepTag('PL_Revenue')).toBe('PL_Revenue')
  })

  it('strips a parameterized segment and collapses underscores', () => {
    expect(normaliseDepTag('PL_Revenue_{PERIOD_ID}')).toBe('PL_Revenue')
  })

  it('handles multiple braces', () => {
    expect(normaliseDepTag('{A}PL_{B}Revenue_{C}')).toBe('PL_Revenue')
  })

  it('collapses runs of underscores created by stripping', () => {
    expect(normaliseDepTag('foo__{X}__bar')).toBe('foo_bar')
  })

  it('returns empty string when the whole tag is a placeholder', () => {
    expect(normaliseDepTag('{JUST_A_PLACEHOLDER}')).toBe('')
  })
})

describe('resolveConnections', () => {
  const blockA = makeBlock({ blockId: 'a', blockName: 'A', replacementTag: 'tagA' })
  const blockB = makeBlock({ blockId: 'b', blockName: 'B', replacementTag: 'tagB' })
  const sectionA = makeSection({ sectionId: 'sec-A', sectionTitle: 'SecA', blocks: [blockA] })
  const sectionB = makeSection({ sectionId: 'sec-B', sectionTitle: 'SecB', blocks: [blockB] })
  const blocksByTag = new Map([
    ['tagA', { block: blockA, sectionId: 'sec-A' }],
    ['tagB', { block: blockB, sectionId: 'sec-B' }],
  ])

  it('resolves a direct tag hit to a full Connection', () => {
    const result = resolveConnections(['tagA'], blocksByTag, [sectionA, sectionB])
    expect(result).toHaveLength(1)
    expect(result[0]).toMatchObject({
      blockId: 'a',
      blockName: 'A',
      sectionId: 'sec-A',
      sectionTitle: 'SecA',
      replacementTag: 'tagA',
    })
  })

  it('falls back to the normalised tag when the raw tag does not match', () => {
    const result = resolveConnections(
      ['tagA_{PERIOD_ID}'],
      blocksByTag,
      [sectionA],
    )
    expect(result).toHaveLength(1)
    expect(result[0]?.blockId).toBe('a')
  })

  it('deduplicates when multiple tags resolve to the same block', () => {
    const result = resolveConnections(
      ['tagA', 'tagA_{PERIOD_ID}'],
      blocksByTag,
      [sectionA],
    )
    expect(result).toHaveLength(1)
  })

  it('skips tags with no match', () => {
    const result = resolveConnections(['nonexistent'], blocksByTag, [sectionA])
    expect(result).toEqual([])
  })
})

describe('buildFieldsByName', () => {
  it('indexes fields by their name identity', () => {
    const a = makeField({ name: 'field_a' })
    const b = makeField({ name: 'field_b', displayName: 'Field B' })
    const map = buildFieldsByName([a, b])
    expect(map.size).toBe(2)
    expect(map.get('field_a')).toBe(a)
    expect(map.get('field_b')).toBe(b)
  })

  it('returns an empty map for an empty list', () => {
    expect(buildFieldsByName([]).size).toBe(0)
  })
})

describe('resolveBlockFields', () => {
  const fieldA = makeField({ name: 'field_a' })
  const fieldB = makeField({ name: 'field_b', displayName: 'Field B' })
  const fieldsByName = buildFieldsByName([fieldA, fieldB])

  it('resolves fieldNames to full field DTOs in the block order', () => {
    const block = makeBlock({ fieldNames: ['field_b', 'field_a'] })
    const result = resolveBlockFields(block, fieldsByName)
    expect(result).toEqual([fieldB, fieldA])
  })

  it('drops names with no matching workflow-level definition', () => {
    const block = makeBlock({ fieldNames: ['field_a', 'field_gone'] })
    const result = resolveBlockFields(block, fieldsByName)
    expect(result).toEqual([fieldA])
  })

  it('returns an empty list for a block with no fields', () => {
    expect(resolveBlockFields(makeBlock(), fieldsByName)).toEqual([])
  })
})

describe('buildBlocksById', () => {
  it('indexes every block with its owning section', () => {
    const blockA = makeBlock({ blockId: 'a', blockName: 'A' })
    const blockB = makeBlock({ blockId: 'b', blockName: 'B' })
    const sections = [
      makeSection({ sectionId: 'sec-1', sectionTitle: 'One', blocks: [blockA] }),
      makeSection({ sectionId: 'sec-2', sectionTitle: 'Two', blocks: [blockB] }),
    ]

    const map = buildBlocksById(sections)

    expect(map.size).toBe(2)
    expect(map.get('a')).toEqual({
      block: blockA,
      sectionId: 'sec-1',
      sectionTitle: 'One',
    })
    expect(map.get('b')).toEqual({
      block: blockB,
      sectionId: 'sec-2',
      sectionTitle: 'Two',
    })
  })
})

describe('historyFieldLabel', () => {
  const blockLabelById = new Map([['block-1', 'Analysis Block']])
  const displayNameByField = new Map([['field_a', 'Field A']])

  it('renders legacy rows (non-null blockId) as "blockName · fieldName"', () => {
    const row = makeHistoryRow({ blockId: 'block-1' })
    expect(historyFieldLabel(row, blockLabelById, displayNameByField)).toBe(
      'Analysis Block · Field A',
    )
  })

  it('falls back to the raw block id when the legacy block is gone', () => {
    const row = makeHistoryRow({ blockId: 'block-gone' })
    expect(historyFieldLabel(row, blockLabelById, displayNameByField)).toBe(
      'block-gone · Field A',
    )
  })

  it('renders new rows with a snapshot as "fieldName · used by A, B"', () => {
    const row = makeHistoryRow({ usedByBlockNames: ['A', 'B'] })
    expect(historyFieldLabel(row, blockLabelById, displayNameByField)).toBe(
      'Field A · used by A, B',
    )
  })

  it('renders the field name alone when the snapshot is null', () => {
    const row = makeHistoryRow({ usedByBlockNames: null })
    expect(historyFieldLabel(row, blockLabelById, displayNameByField)).toBe('Field A')
  })

  it('renders the field name alone when the snapshot is empty', () => {
    const row = makeHistoryRow({ usedByBlockNames: [] })
    expect(historyFieldLabel(row, blockLabelById, displayNameByField)).toBe('Field A')
  })

  it('renders Renamed rows by the same new-row rules', () => {
    const row = makeHistoryRow({
      action: 'Renamed',
      previousValue: 'same value',
      newValue: 'same value',
      usedByBlockNames: ['A'],
    })
    expect(historyFieldLabel(row, blockLabelById, displayNameByField)).toBe(
      'Field A · used by A',
    )
  })

  it('falls back to the raw placeholder when the field left the workflow', () => {
    const row = makeHistoryRow({ placeholderName: 'field_gone' })
    expect(historyFieldLabel(row, blockLabelById, displayNameByField)).toBe('field_gone')
  })

  it('uses the injected used-by formatter for localisation', () => {
    const row = makeHistoryRow({ usedByBlockNames: ['A', 'B'] })
    const label = historyFieldLabel(
      row,
      blockLabelById,
      displayNameByField,
      (names) => `verwendet von ${names}`,
    )
    expect(label).toBe('Field A · verwendet von A, B')
  })
})

describe('resolvePendingState', () => {
  it('returns "default" for a clean field with no override and no pending', () => {
    const field = makeField({ isOverridden: false, pending: null })
    expect(resolvePendingState(field, 'user-1')).toBe('default')
  })

  it('returns "override" when isOverridden but no pending', () => {
    const field = makeField({ isOverridden: true, pending: null })
    expect(resolvePendingState(field, 'user-1')).toBe('override')
  })

  it('returns "pending-mine" when the proposer is the current user', () => {
    const field = makeField({ pending: makePending({ proposerUserId: 'user-1' }) })
    expect(resolvePendingState(field, 'user-1')).toBe('pending-mine')
  })

  it('returns "pending-other" when proposer differs from current user', () => {
    const field = makeField({ pending: makePending({ proposerUserId: 'user-2' }) })
    expect(resolvePendingState(field, 'user-1')).toBe('pending-other')
  })

  it('treats a pending as someone else\'s when myUserId is null', () => {
    const field = makeField({ pending: makePending({ proposerUserId: 'user-2' }) })
    expect(resolvePendingState(field, null)).toBe('pending-other')
  })

  it('treats undefined `pending` (legacy response) the same as null', () => {
    const { pending: _omit, ...rest } = makeField({ isOverridden: true })
    void _omit
    expect(resolvePendingState(rest as SignatureFieldDto, 'user-1')).toBe('override')
  })
})

describe('canUserVote', () => {
  it('is false when the user has no signature roles', () => {
    const field = makeField({ pending: makePending({ proposerUserId: 'user-2' }) })
    expect(canUserVote(field, 'user-1', false)).toBe(false)
  })

  it('is false when there is no pending override', () => {
    const field = makeField({ pending: null, isOverridden: true })
    expect(canUserVote(field, 'user-1', true)).toBe(false)
  })

  it('is false when the current user is the proposer', () => {
    const field = makeField({ pending: makePending({ proposerUserId: 'user-1' }) })
    expect(canUserVote(field, 'user-1', true)).toBe(false)
  })

  it('is false when the current user has already voted', () => {
    const field = makeField({
      pending: makePending({ proposerUserId: 'user-2', myVote: 'Approve' }),
    })
    expect(canUserVote(field, 'user-1', true)).toBe(false)
  })

  it('is true for an eligible non-proposer with no prior vote', () => {
    const field = makeField({
      pending: makePending({ proposerUserId: 'user-2', myVote: null }),
    })
    expect(canUserVote(field, 'user-1', true)).toBe(true)
  })

  it('treats null myUserId conservatively (not the proposer, can still vote if BE allows)', () => {
    const field = makeField({
      pending: makePending({ proposerUserId: 'user-2', myVote: null }),
    })
    expect(canUserVote(field, null, true)).toBe(true)
  })
})

describe('countAwaitingMyVote', () => {
  const votable = () =>
    makeField({
      name: 'votable',
      pending: makePending({ proposerUserId: 'user-2', myVote: null }),
    })

  it('counts each votable field once', () => {
    const fields = [
      votable(),
      makeField({ name: 'no_pending', pending: null }),
      makeField({
        name: 'mine',
        pending: makePending({ proposerUserId: 'user-1' }),
      }),
      makeField({
        name: 'voted',
        pending: makePending({ proposerUserId: 'user-2', myVote: 'Reject' }),
      }),
    ]
    expect(countAwaitingMyVote(fields, 'user-1', true)).toBe(1)
  })

  it('counts a shared field once when given the flat workflow field list', () => {
    const shared = makeField({
      name: 'shared',
      usedByBlockIds: ['block-1', 'block-2', 'block-3'],
      pending: makePending({ proposerUserId: 'user-2', myVote: null }),
    })
    expect(countAwaitingMyVote([shared], 'user-1', true)).toBe(1)
  })

  it('is zero without a reviewer role', () => {
    expect(countAwaitingMyVote([votable()], 'user-1', false)).toBe(0)
  })
})

describe('countAwaitingProposerAction', () => {
  it('counts fields where the caller proposed and a counter-suggestion waits', () => {
    const fields = [
      makeField({
        name: 'actionable',
        pending: makePending({
          proposerUserId: 'user-1',
          suggestions: [makeSuggestion()],
        }),
      }),
      makeField({
        name: 'no_suggestions',
        pending: makePending({ proposerUserId: 'user-1', suggestions: [] }),
      }),
      makeField({
        name: 'not_mine',
        pending: makePending({
          proposerUserId: 'user-2',
          suggestions: [makeSuggestion()],
        }),
      }),
    ]
    expect(countAwaitingProposerAction(fields, 'user-1')).toBe(1)
  })

  it('is zero when myUserId is null', () => {
    const fields = [
      makeField({
        pending: makePending({
          proposerUserId: 'user-1',
          suggestions: [makeSuggestion()],
        }),
      }),
    ]
    expect(countAwaitingProposerAction(fields, null)).toBe(0)
  })
})

describe('computeVoteProgress', () => {
  it('computes percentages and remaining for a partial approve count', () => {
    const result = computeVoteProgress({ approveCount: 1, rejectCount: 0, threshold: 2 })
    expect(result).toEqual({
      approvePct: 50,
      rejectPct: 0,
      approveRemaining: 1,
      rejectRemaining: 2,
    })
  })

  it('clamps approve % at 100 and remaining at zero when count exceeds threshold', () => {
    const result = computeVoteProgress({ approveCount: 5, rejectCount: 1, threshold: 2 })
    expect(result.approvePct).toBe(100)
    expect(result.approveRemaining).toBe(0)
    expect(result.rejectPct).toBe(50)
    expect(result.rejectRemaining).toBe(1)
  })

  it('returns zero progress and threshold-as-remaining when both counts are zero', () => {
    const result = computeVoteProgress({ approveCount: 0, rejectCount: 0, threshold: 3 })
    expect(result).toEqual({
      approvePct: 0,
      rejectPct: 0,
      approveRemaining: 3,
      rejectRemaining: 3,
    })
  })

  it('does not divide by zero or NaN when threshold is non-positive', () => {
    const result = computeVoteProgress({ approveCount: 0, rejectCount: 0, threshold: 0 })
    expect(Number.isNaN(result.approvePct)).toBe(false)
    expect(Number.isNaN(result.rejectPct)).toBe(false)
    expect(result.approveRemaining).toBe(0)
    expect(result.rejectRemaining).toBe(0)
  })
})

describe('summariseSuggestions', () => {
  it('returns an empty array when pending is null', () => {
    expect(summariseSuggestions(null)).toEqual([])
  })

  it('returns an empty array when pending has no suggestions', () => {
    expect(summariseSuggestions(makePending({ suggestions: [] }))).toEqual([])
  })

  it('orders suggestions by createdAt ascending and is stable for equal keys', () => {
    const a = makeSuggestion({ voteId: 'a', createdAt: '2026-01-03T00:00:00Z' })
    const b = makeSuggestion({ voteId: 'b', createdAt: '2026-01-01T00:00:00Z' })
    const c = makeSuggestion({ voteId: 'c', createdAt: '2026-01-02T00:00:00Z' })
    const cDup = makeSuggestion({ voteId: 'c-dup', createdAt: '2026-01-02T00:00:00Z' })

    const out = summariseSuggestions(makePending({ suggestions: [a, b, c, cDup] }))

    expect(out.map((s) => s.voteId)).toEqual(['b', 'c', 'c-dup', 'a'])
  })

  it('defensively filters suggestions where suggestedValue is null', () => {
    const real = makeSuggestion({ voteId: 'real' })
    const broken = makeSuggestion({
      voteId: 'broken',
      // Cast through unknown — the typed contract says suggestedValue is
      // non-nullable, but we want defensive runtime behaviour for legacy data.
      suggestedValue: null as unknown as string,
    })
    const out = summariseSuggestions(
      makePending({ suggestions: [real, broken] }),
    )
    expect(out).toHaveLength(1)
    expect(out[0]?.voteId).toBe('real')
  })
})
