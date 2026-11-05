import { describe, expect, it } from 'vitest'
import type { Counterparty } from '@/api/counterparties/schema'
import {
  groupMatchesIntoTree,
  type DisplayGroup,
} from './counterpartiesTree'

const cp = (over: Partial<Counterparty> & { id: string }): Counterparty => ({
  id: over.id,
  parentId: over.parentId ?? null,
  counterpartyTypeIds: over.counterpartyTypeIds ?? [],
  name: over.name ?? `name-${over.id}`,
  nameVariants: over.nameVariants ?? '',
})

describe('groupMatchesIntoTree', () => {
  it('returns no groups when there are no matches', () => {
    expect(
      groupMatchesIntoTree({
        matches: [],
        contextParents: [],
        childrenOfMatchedRoots: [],
        orderBy: 'name asc',
      }),
    ).toEqual([])
  })

  it('groups matched roots as kind="matched-root" with their full child list', () => {
    const root = cp({ id: 'r1', name: 'AB Brewhaus', parentId: null })
    const child1 = cp({ id: 'c1', name: 'KESTREL PLC', parentId: 'r1' })
    const child2 = cp({ id: 'c2', name: 'AB Capital', parentId: 'r1' })

    const groups = groupMatchesIntoTree({
      matches: [root],
      contextParents: [],
      childrenOfMatchedRoots: [child1, child2],
      orderBy: 'name asc',
    })

    expect(groups).toHaveLength(1)
    expect(groups[0]?.kind).toBe('matched-root')
    expect(groups[0]?.root.id).toBe('r1')
    expect(groups[0]?.children.map(c => c.id)).toEqual(['c2', 'c1']) // name asc
  })

  it('groups child-only matches under their context parent', () => {
    const parent = cp({ id: 'r1', name: 'AB Brewhaus', parentId: null })
    const matchedChild = cp({ id: 'c1', name: 'KESTREL PLC', parentId: 'r1' })

    const groups = groupMatchesIntoTree({
      matches: [matchedChild],
      contextParents: [parent],
      childrenOfMatchedRoots: [],
      orderBy: 'name asc',
    })

    expect(groups).toHaveLength(1)
    expect(groups[0]?.kind).toBe('context-parent')
    expect(groups[0]?.root.id).toBe('r1')
    expect(groups[0]?.children.map(c => c.id)).toEqual(['c1'])
  })

  it('marks which children matched on context-parent groups', () => {
    const parent = cp({ id: 'r1', name: 'AB Brewhaus', parentId: null })
    const matchedChild = cp({ id: 'c1', name: 'KESTREL PLC', parentId: 'r1' })

    const [group] = groupMatchesIntoTree({
      matches: [matchedChild],
      contextParents: [parent],
      childrenOfMatchedRoots: [],
      orderBy: 'name asc',
    })

    expect(group?.matchedChildIds.has('c1')).toBe(true)
  })

  it('marks matched children on matched-root groups too (for child highlighting)', () => {
    const root = cp({ id: 'r1', name: 'AB Brewhaus', parentId: null })
    const matchedChild = cp({ id: 'c1', name: 'KESTREL', parentId: 'r1' })
    const unmatchedChild = cp({ id: 'c2', name: 'AB Capital', parentId: 'r1' })

    const [group] = groupMatchesIntoTree({
      matches: [root, matchedChild],
      contextParents: [],
      childrenOfMatchedRoots: [matchedChild, unmatchedChild],
      orderBy: 'name asc',
    })

    expect(group?.kind).toBe('matched-root')
    expect(group?.matchedChildIds.has('c1')).toBe(true)
    expect(group?.matchedChildIds.has('c2')).toBe(false)
  })

  it('does not create a context-parent group when the parent is itself a matched root', () => {
    const root = cp({ id: 'r1', name: 'AB Brewhaus', parentId: null })
    const matchedChild = cp({ id: 'c1', name: 'KESTREL', parentId: 'r1' })

    const groups = groupMatchesIntoTree({
      matches: [root, matchedChild],
      // Caller may still pass the parent in contextParents (e.g. race condition);
      // the grouper should dedupe.
      contextParents: [root],
      childrenOfMatchedRoots: [matchedChild],
      orderBy: 'name asc',
    })

    expect(groups).toHaveLength(1)
    expect(groups[0]?.kind).toBe('matched-root')
  })

  it('orders top-level groups by the configured orderBy (name desc)', () => {
    const a = cp({ id: 'a', name: 'Acme', parentId: null })
    const b = cp({ id: 'b', name: 'Beta Co', parentId: null })
    const c = cp({ id: 'c', name: 'Capital', parentId: null })

    const groups = groupMatchesIntoTree({
      matches: [a, b, c],
      contextParents: [],
      childrenOfMatchedRoots: [],
      orderBy: 'name desc',
    })

    expect(groups.map(g => g.root.name)).toEqual(['Capital', 'Beta Co', 'Acme'])
  })

  it('orders top-level groups by id when orderBy is id asc', () => {
    const a = cp({ id: '401', name: 'Z', parentId: null })
    const b = cp({ id: '406', name: 'A', parentId: null })

    const groups = groupMatchesIntoTree({
      matches: [a, b],
      contextParents: [],
      childrenOfMatchedRoots: [],
      orderBy: 'id asc',
    })

    expect(groups.map(g => g.root.id)).toEqual(['401', '406'])
  })

  it('mixes matched roots and context parents, sorted together', () => {
    const matchedRoot = cp({ id: 'r1', name: 'AB', parentId: null })
    const contextParent = cp({ id: 'r2', name: 'Charlie', parentId: null })
    const matchedChild = cp({ id: 'c1', name: 'KESTREL', parentId: 'r2' })

    const groups = groupMatchesIntoTree({
      matches: [matchedRoot, matchedChild],
      contextParents: [contextParent],
      childrenOfMatchedRoots: [],
      orderBy: 'name asc',
    })

    expect(groups.map(g => ({ kind: g.kind, id: g.root.id }))).toEqual([
      { kind: 'matched-root', id: 'r1' },
      { kind: 'context-parent', id: 'r2' },
    ])
  })

  it('drops orphan child matches whose parent is missing from contextParents', () => {
    const orphanChild = cp({ id: 'c1', name: 'KESTREL', parentId: 'missing' })

    const groups = groupMatchesIntoTree({
      matches: [orphanChild],
      contextParents: [],
      childrenOfMatchedRoots: [],
      orderBy: 'name asc',
    })

    expect(groups).toEqual([])
  })

  it('paginates top-level groups when page/pageSize is provided', () => {
    const roots: Counterparty[] = Array.from({ length: 30 }, (_, i) =>
      cp({ id: `r${String(i).padStart(2, '0')}`, name: `Root ${i}`, parentId: null }),
    )

    const page0 = groupMatchesIntoTree({
      matches: roots,
      contextParents: [],
      childrenOfMatchedRoots: [],
      orderBy: 'id asc',
      page: 0,
      pageSize: 25,
    })
    const page1 = groupMatchesIntoTree({
      matches: roots,
      contextParents: [],
      childrenOfMatchedRoots: [],
      orderBy: 'id asc',
      page: 1,
      pageSize: 25,
    })

    expect(page0).toHaveLength(25)
    expect(page1).toHaveLength(5)
    expect(page0[0]?.root.id).toBe('r00')
    expect(page1[0]?.root.id).toBe('r25')
  })

  it('returns paginated groups even when both kinds are present', () => {
    const matchedRoot = cp({ id: 'm1', name: 'Matched Root', parentId: null })
    const cp1 = cp({ id: 'cp1', name: 'A Context', parentId: null })
    const cp2 = cp({ id: 'cp2', name: 'B Context', parentId: null })
    const c1 = cp({ id: 'c1', name: 'kestrel1', parentId: 'cp1' })
    const c2 = cp({ id: 'c2', name: 'kestrel2', parentId: 'cp2' })

    const result: DisplayGroup[] = groupMatchesIntoTree({
      matches: [matchedRoot, c1, c2],
      contextParents: [cp1, cp2],
      childrenOfMatchedRoots: [],
      orderBy: 'name asc',
      page: 0,
      pageSize: 2,
    })

    expect(result).toHaveLength(2)
    expect(result.map(g => g.root.name)).toEqual(['A Context', 'B Context'])
  })

  it('computes total top-level group count separately from the paginated slice', () => {
    const roots: Counterparty[] = Array.from({ length: 7 }, (_, i) =>
      cp({ id: `r${i}`, parentId: null }),
    )
    const result = groupMatchesIntoTree({
      matches: roots,
      contextParents: [],
      childrenOfMatchedRoots: [],
      orderBy: 'id asc',
      page: 0,
      pageSize: 3,
    })
    expect(result.length).toBe(3)
  })
})
