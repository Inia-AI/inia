import { describe, it, expect } from 'vitest'
import { buildReportsFilter, reportsSearchFilter } from './reportsQuery'

const emptyState = {
  workflowTypeIds: [],
  counterpartyIds: [],
  userIds: [],
}

describe('buildReportsFilter', () => {
  it('returns undefined when nothing is filtered', () => {
    expect(buildReportsFilter(emptyState)).toBeUndefined()
  })

  it('builds an in-clause per id filter and joins with and', () => {
    expect(
      buildReportsFilter({
        ...emptyState,
        workflowTypeIds: ['wt-1'],
        counterpartyIds: ['cp-1', 'cp-2'],
        userIds: ['u-1'],
      }),
    ).toBe(
      "workflowTypeId in ('wt-1') and counterpartyId in ('cp-1','cp-2') and userId in ('u-1')",
    )
  })

  it('escapes single quotes in ids', () => {
    expect(
      buildReportsFilter({ ...emptyState, counterpartyIds: ["o'brien"] }),
    ).toBe("counterpartyId in ('o''brien')")
  })

  it('joins the clauses of extension filters after the id filters', () => {
    expect(buildReportsFilter(emptyState, ['isBaseline eq true'])).toBe('isBaseline eq true')
    expect(buildReportsFilter({ ...emptyState, counterpartyIds: ['cp-1'] }, ['isBaseline eq true'])).toBe(
      "counterpartyId in ('cp-1') and isBaseline eq true",
    )
  })
})

describe('reportsSearchFilter', () => {
  it('returns an empty clause for a blank term', () => {
    expect(reportsSearchFilter('')).toBe('')
    expect(reportsSearchFilter('   ')).toBe('')
  })

  it('matches counterparty, title and workflow type id, case-insensitively', () => {
    expect(reportsSearchFilter('Fence')).toBe(
      "(contains(tolower(counterpartyName), 'fence') or contains(tolower(title), 'fence') or contains(tolower(workflowTypeId), 'fence'))",
    )
  })

  it('escapes single quotes in the term', () => {
    expect(reportsSearchFilter("o'brien")).toContain("'o''brien'")
  })
})
