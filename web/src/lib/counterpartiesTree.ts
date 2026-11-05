import type { Counterparty } from '@/api/counterparties/schema'

export interface DisplayGroup {
  kind: 'matched-root' | 'context-parent'
  root: Counterparty
  children: Counterparty[]
  matchedChildIds: Set<string>
}

export interface GroupInput {
  matches: Counterparty[]
  contextParents: Counterparty[]
  childrenOfMatchedRoots: Counterparty[]
  orderBy: string
  page?: number
  pageSize?: number
}

const compareByField = (
  a: Counterparty,
  b: Counterparty,
  field: 'name' | 'id',
  direction: 'asc' | 'desc',
): number => {
  const av = a[field] ?? ''
  const bv = b[field] ?? ''
  const cmp = av.localeCompare(bv, undefined, { numeric: true })
  return direction === 'asc' ? cmp : -cmp
}

const parseOrderBy = (
  orderBy: string,
): { field: 'name' | 'id'; direction: 'asc' | 'desc' } => {
  const [rawField, rawDir] = orderBy.split(/\s+/)
  const field = rawField === 'id' ? 'id' : 'name'
  const direction = rawDir === 'desc' ? 'desc' : 'asc'
  return { field, direction }
}

export function groupMatchesIntoTree(input: GroupInput): DisplayGroup[] {
  const { matches, contextParents, childrenOfMatchedRoots, orderBy } = input

  const matchedRoots = matches.filter(m => m.parentId == null)
  const matchedChildren = matches.filter(m => m.parentId != null)

  const matchedRootIds = new Set(matchedRoots.map(r => r.id))
  const matchedChildIds = new Set(matchedChildren.map(c => c.id))

  const { field, direction } = parseOrderBy(orderBy)

  const childByParent = new Map<string, Counterparty[]>()
  for (const child of childrenOfMatchedRoots) {
    if (child.parentId == null) continue
    const list = childByParent.get(child.parentId) ?? []
    list.push(child)
    childByParent.set(child.parentId, list)
  }

  const matchedRootGroups: DisplayGroup[] = matchedRoots.map(root => {
    const children = [...(childByParent.get(root.id) ?? [])].sort((a, b) =>
      compareByField(a, b, 'name', 'asc'),
    )
    const groupMatchedChildIds = new Set<string>()
    for (const child of children) {
      if (matchedChildIds.has(child.id)) groupMatchedChildIds.add(child.id)
    }
    return {
      kind: 'matched-root',
      root,
      children,
      matchedChildIds: groupMatchedChildIds,
    }
  })

  const contextParentsById = new Map<string, Counterparty>()
  for (const p of contextParents) {
    if (matchedRootIds.has(p.id)) continue
    contextParentsById.set(p.id, p)
  }

  const childrenByContextParent = new Map<string, Counterparty[]>()
  for (const c of matchedChildren) {
    if (!c.parentId) continue
    if (!contextParentsById.has(c.parentId)) continue
    const list = childrenByContextParent.get(c.parentId) ?? []
    list.push(c)
    childrenByContextParent.set(c.parentId, list)
  }

  const contextParentGroups: DisplayGroup[] = []
  for (const [parentId, kids] of childrenByContextParent) {
    const root = contextParentsById.get(parentId)
    if (!root) continue
    const sortedKids = [...kids].sort((a, b) =>
      compareByField(a, b, 'name', 'asc'),
    )
    contextParentGroups.push({
      kind: 'context-parent',
      root,
      children: sortedKids,
      matchedChildIds: new Set(sortedKids.map(k => k.id)),
    })
  }

  const allGroups = [...matchedRootGroups, ...contextParentGroups].sort(
    (a, b) => compareByField(a.root, b.root, field, direction),
  )

  if (input.page == null || input.pageSize == null) return allGroups
  const start = input.page * input.pageSize
  return allGroups.slice(start, start + input.pageSize)
}

export function countTopLevelGroups(
  input: Pick<GroupInput, 'matches' | 'contextParents'>,
): number {
  const matchedRootIds = new Set(
    input.matches.filter(m => m.parentId == null).map(m => m.id),
  )
  const contextParentIds = new Set(input.contextParents.map(p => p.id))
  const usefulContextParentIds = new Set<string>()
  for (const c of input.matches) {
    if (c.parentId == null) continue
    if (matchedRootIds.has(c.parentId)) continue
    if (contextParentIds.has(c.parentId)) usefulContextParentIds.add(c.parentId)
  }
  return matchedRootIds.size + usefulContextParentIds.size
}
