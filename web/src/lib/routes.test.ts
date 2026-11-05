import { describe, expect, it } from 'vitest'

import {
  documentDetailsRoute,
  reportRoute,
} from './routes'

describe('route builders', () => {
  it('points a document at its details page', () => {
    expect(documentDetailsRoute('d1')).toEqual({ name: 'DocumentDetails', params: { id: 'd1' } })
  })

  it('points a report at its page', () => {
    expect(reportRoute('r1')).toEqual({ name: 'WorkflowDetails', params: { id: 'r1', action: 'edit' } })
  })
})
