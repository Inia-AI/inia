import { odataContainsAny, odataIn } from '@/utils/odata'

// workflowTypeId, not workflowTypeName: the reports OData model does not expose a type-name
// property, and the id is the readable slug the workspace shows (e.g. Eng175FenceProbe).
const REPORT_SEARCH_FIELDS = ['counterpartyName', 'title', 'workflowTypeId'] as const

export function reportsSearchFilter(term: string): string {
  return odataContainsAny(REPORT_SEARCH_FIELDS, term)
}

export interface ReportsFilterState {
  workflowTypeIds: string[]
  counterpartyIds: string[]
  userIds: string[]
}

export function buildReportsFilter(
  state: ReportsFilterState,
  extraFilters: string[] = [],
): string | undefined {
  const filters: string[] = []
  if (state.workflowTypeIds.length) {
    filters.push(odataIn('workflowTypeId', state.workflowTypeIds))
  }
  if (state.counterpartyIds.length) {
    filters.push(odataIn('counterpartyId', state.counterpartyIds))
  }
  if (state.userIds.length) {
    filters.push(odataIn('userId', state.userIds))
  }
  filters.push(...extraFilters)
  return filters.length ? filters.join(' and ') : undefined
}
