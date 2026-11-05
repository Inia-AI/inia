import { describe, expect, it } from 'vitest'

import type { DataOutputCitation } from '@/api/workflowReports/schema'
import { renderDataOutputJson } from './dataOutputJson'

const cite = (id: string, path: string, value: string): DataOutputCitation => ({
  id,
  path,
  value,
  sources: [{ type: 'Document', citedText: `Line ${value}`, pageId: 'page-2', pageNumber: 2 }],
})

const textOf = (html: string): string => {
  const element = document.createElement('pre')
  element.innerHTML = html
  return element.textContent ?? ''
}

describe('renderDataOutputJson', () => {
  const data = {
    currency: 'NIS',
    facilities: [{ limit: 120000, note: 'Overdraft of 120,000 renewed; 7 cheques returned' }],
    'account/movement': { total_credits: 318204.4, audited: false, owner: null },
  }

  it('reads as the indented JSON itself', () => {
    const html = renderDataOutputJson(data, [])

    expect(textOf(html)).toBe(JSON.stringify(data, null, 2))
  })

  it('marks each cited figure with the id of its citation', () => {
    const html = renderDataOutputJson(data, [
      cite('c0', '/facilities/0/limit', '120000'),
      cite('c0', '/facilities/0/note', '120,000'),
      cite('c1', '/facilities/0/note', '7'),
      cite('c2', '/account~1movement/total_credits', '318204.4'),
    ])

    expect(html).toContain('<fact id="c0">120000</fact>')
    expect(html).toContain('Overdraft of <fact id="c0">120,000</fact> renewed; <fact id="c1">7</fact> cheques')
    expect(html).toContain('<fact id="c2">318204.4</fact>')
    expect(textOf(html)).toBe(JSON.stringify(data, null, 2))
  })

  it('leaves a figure the citation names in another value unmarked', () => {
    const html = renderDataOutputJson({ a: 18, b: 18 }, [cite('c0', '/b', '18')])

    expect(html.match(/<fact /g)).toHaveLength(1)
    expect(html).toContain('&quot;b&quot;</span>: <fact id="c0">18</fact>')
  })

  it('escapes markup inside the JSON', () => {
    const html = renderDataOutputJson({ note: '<img src=x onerror=alert(1)> & co' }, [])

    expect(html).not.toContain('<img')
    expect(textOf(html)).toContain('<img src=x onerror=alert(1)> & co')
  })

  it('shows an empty object and an empty list as JSON does', () => {
    const value = { none: {}, nothing: [] }

    expect(textOf(renderDataOutputJson(value, []))).toBe(JSON.stringify(value, null, 2))
  })
})
