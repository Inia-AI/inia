import { describe, expect, it } from 'vitest'
import {
  DEFAULT_QUERY_STATE,
  MATCH_QUERY_CAP,
  buildChildrenOfRootsQuery,
  buildContextParentsQuery,
  buildHasChildrenProbe,
  buildMatchQuery,
  buildRootsQuery,
  buildSingleParentChildrenQuery,
  isFiltering,
  parseQueryState,
  serializeQueryState,
  type CounterpartiesQueryState,
} from './counterpartiesQuery'

const withDefaults = (
  partial: Partial<CounterpartiesQueryState>,
): CounterpartiesQueryState => ({ ...DEFAULT_QUERY_STATE, ...partial })

describe('isFiltering', () => {
  it('is false at defaults', () => {
    expect(isFiltering(DEFAULT_QUERY_STATE)).toBe(false)
  })

  it('is true when search has non-whitespace content', () => {
    expect(isFiltering(withDefaults({ search: 'ab' }))).toBe(true)
  })

  it('treats whitespace-only search as no search', () => {
    expect(isFiltering(withDefaults({ search: '   ' }))).toBe(false)
  })

  it('is true when any type id is selected', () => {
    expect(isFiltering(withDefaults({ typeIds: ['t1'] }))).toBe(true)
  })
})

describe('buildRootsQuery (Mode A — default tree view)', () => {
  it('paginates roots only, with $count and the configured orderBy', () => {
    expect(buildRootsQuery(DEFAULT_QUERY_STATE)).toEqual({
      $skip: 0,
      $top: 25,
      $count: true,
      $orderby: 'name asc',
      $filter: 'parentId eq null',
    })
  })

  it('computes $skip from page * pageSize', () => {
    expect(buildRootsQuery(withDefaults({ page: 2, pageSize: 25 }))).toMatchObject({
      $skip: 50,
      $top: 25,
    })
  })

  it('passes orderBy through', () => {
    expect(
      buildRootsQuery(withDefaults({ orderBy: 'id desc' })).$orderby,
    ).toBe('id desc')
  })

  it('ignores typeIds (those belong to the match query in Mode B)', () => {
    const q = buildRootsQuery(withDefaults({ typeIds: ['t1', 't2'] }))
    expect(q).not.toHaveProperty('counterpartyTypeIds')
  })
})

describe('buildMatchQuery (Mode B — search/filter)', () => {
  it('caps at MATCH_QUERY_CAP and asks for $count', () => {
    const q = buildMatchQuery(withDefaults({ search: 'a' }))
    expect(MATCH_QUERY_CAP).toBe(500)
    expect(q.$top).toBe(500)
    expect(q.$skip).toBe(0)
    expect(q.$count).toBe(true)
  })

  it('builds an OR clause over name + id + nameVariants for search', () => {
    const q = buildMatchQuery(withDefaults({ search: 'kestrel' }))
    expect(q.$filter).toBe(
      "(contains(tolower(name), 'kestrel') or contains(tolower(id), 'kestrel') or contains(tolower(nameVariants), 'kestrel'))",
    )
  })

  it("escapes single quotes in the (lowercased) search literal", () => {
    const q = buildMatchQuery(withDefaults({ search: "Amy's" }))
    expect(q.$filter).toContain("'amy''s'")
  })

  it('lowercases the search literal so case is folded against tolower(field)', () => {
    const q = buildMatchQuery(withDefaults({ search: 'KESTREL' }))
    expect(q.$filter).toContain("'kestrel'")
    expect(q.$filter).not.toContain("'KESTREL'")
  })

  it('does NOT wrap the already-lowercased literal in another tolower() call', () => {
    const q = buildMatchQuery(withDefaults({ search: 'kestrel' }))
    expect(q.$filter).not.toContain("tolower('kestrel')")
  })

  it('does NOT include parentId eq null — search spans roots + children', () => {
    const q = buildMatchQuery(withDefaults({ search: 'kestrel' }))
    expect(q.$filter).not.toContain('parentId')
  })

  it('omits $filter when search is empty (type-only filter)', () => {
    const q = buildMatchQuery(withDefaults({ typeIds: ['t1'] }))
    expect('$filter' in q).toBe(false)
  })

  it('passes counterpartyTypeIds as a CSV sibling param, not inside $filter', () => {
    const q = buildMatchQuery(
      withDefaults({ search: 'ab', typeIds: ['t1', 't2'] }),
    )
    expect(q.counterpartyTypeIds).toBe('t1,t2')
    expect(q.$filter).toBe(
      "(contains(tolower(name), 'ab') or contains(tolower(id), 'ab') or contains(tolower(nameVariants), 'ab'))",
    )
  })

  it('omits counterpartyTypeIds when no types selected', () => {
    const q = buildMatchQuery(withDefaults({ search: 'ab' }))
    expect('counterpartyTypeIds' in q).toBe(false)
  })

  it('passes orderBy through (so the 500-cap is deterministic)', () => {
    const q = buildMatchQuery(withDefaults({ search: 'a', orderBy: 'id asc' }))
    expect(q.$orderby).toBe('id asc')
  })
})

describe('buildContextParentsQuery', () => {
  it('uses `id in (...)` with quoted, comma-separated ids', () => {
    expect(buildContextParentsQuery(['406', 'abc-1'])).toEqual({
      $top: 2,
      $filter: "id in ('406','abc-1')",
    })
  })

  it("escapes single quotes inside ids", () => {
    expect(buildContextParentsQuery(["it's"]).$filter).toBe(
      "id in ('it''s')",
    )
  })

  it('returns a no-op query for an empty input', () => {
    expect(buildContextParentsQuery([])).toEqual({ $top: 0 })
  })
})

describe('buildChildrenOfRootsQuery', () => {
  it("uses `parentId in (...)`, sorted name asc", () => {
    expect(buildChildrenOfRootsQuery(['406', '407'])).toEqual({
      $top: 10000,
      $orderby: 'name asc',
      $filter: "parentId in ('406','407')",
    })
  })

  it('returns a no-op query for an empty input', () => {
    expect(buildChildrenOfRootsQuery([])).toEqual({ $top: 0 })
  })
})

describe('buildHasChildrenProbe', () => {
  it("asks only for the parentId column on rows whose parentId is in the given set", () => {
    expect(buildHasChildrenProbe(['406', '407'])).toEqual({
      $top: 10000,
      $select: 'parentId',
      $filter: "parentId in ('406','407')",
    })
  })

  it('returns a no-op query for an empty input', () => {
    expect(buildHasChildrenProbe([])).toEqual({ $top: 0 })
  })
})

describe('buildSingleParentChildrenQuery', () => {
  it("filters by `parentId eq '<id>'`, sorted name asc, full page", () => {
    expect(buildSingleParentChildrenQuery('406')).toEqual({
      $top: 10000,
      $orderby: 'name asc',
      $filter: "parentId eq '406'",
    })
  })

  it('escapes single quotes in the id', () => {
    expect(buildSingleParentChildrenQuery("it's").$filter).toBe(
      "parentId eq 'it''s'",
    )
  })
})

describe('parseQueryState', () => {
  it('returns defaults for empty query', () => {
    expect(parseQueryState({})).toEqual(DEFAULT_QUERY_STATE)
  })

  it('reads known keys and coerces numerics', () => {
    expect(
      parseQueryState({
        page: '2',
        pageSize: '50',
        orderby: 'id asc',
        search: 'kestrel',
        type: 't1,t2',
      }),
    ).toEqual({
      page: 2,
      pageSize: 50,
      orderBy: 'id asc',
      search: 'kestrel',
      typeIds: ['t1', 't2'],
    })
  })

  it('parses repeated `type` entries as well as comma-separated', () => {
    expect(parseQueryState({ type: ['t1', 't2,t3'] }).typeIds).toEqual([
      't1',
      't2',
      't3',
    ])
  })

  it('drops empty token entries in type list', () => {
    expect(parseQueryState({ type: ',,t1,,' }).typeIds).toEqual(['t1'])
  })

  it('clamps page to >= 0 and pageSize to [10, 100]', () => {
    expect(parseQueryState({ page: '-3' }).page).toBe(0)
    expect(parseQueryState({ pageSize: '9999' }).pageSize).toBe(100)
    expect(parseQueryState({ pageSize: '5' }).pageSize).toBe(10)
  })

  it('falls back to defaults for non-numeric page/pageSize', () => {
    expect(parseQueryState({ page: 'abc', pageSize: 'x' })).toMatchObject({
      page: 0,
      pageSize: 25,
    })
  })

  it('takes the first value when search is array-valued', () => {
    expect(parseQueryState({ search: ['a', 'b'] }).search).toBe('a')
  })
})

describe('serializeQueryState', () => {
  it('omits every entry equal to its default', () => {
    expect(serializeQueryState(DEFAULT_QUERY_STATE)).toEqual({})
  })

  it('emits non-default entries as strings', () => {
    const out = serializeQueryState(
      withDefaults({
        page: 2,
        pageSize: 50,
        orderBy: 'id desc',
        search: 'kestrel',
        typeIds: ['t1', 't2'],
      }),
    )
    expect(out).toEqual({
      page: '2',
      pageSize: '50',
      orderby: 'id desc',
      search: 'kestrel',
      type: 't1,t2',
    })
  })

  it('round-trips parse(serialize(state)) === state', () => {
    const cases: CounterpartiesQueryState[] = [
      DEFAULT_QUERY_STATE,
      withDefaults({ page: 4 }),
      withDefaults({
        search: "Amy's",
        typeIds: ['t1', 't2'],
        orderBy: 'id asc',
        pageSize: 50,
      }),
    ]
    for (const s of cases) {
      expect(parseQueryState(serializeQueryState(s))).toEqual(s)
    }
  })
})
