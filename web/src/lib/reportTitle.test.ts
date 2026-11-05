import { describe, expect, it } from 'vitest'

import { reportTitle } from './reportTitle'

describe('reportTitle', () => {
  it('uses the report title when there is one', () => {
    expect(reportTitle({ title: 'Q2 Credit Review', workflowTypeName: 'Credit' })).toBe('Q2 Credit Review')
  })

  it.each([undefined, null, ''])('falls back to the workflow type name when the title is %j', title => {
    expect(reportTitle({ title, workflowTypeName: 'Credit' })).toBe('Credit')
  })

  it('has no name when neither is set', () => {
    expect(reportTitle({ title: '', workflowTypeName: '' })).toBeUndefined()
  })
})
