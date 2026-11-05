import { describe, expect, it } from 'vitest'
import {
  buildCounterpartyOptions,
  buildExecutionOptions,
  pickExecutionPlaceholder,
  type ExecutionLite,
} from './signatureTestPickers'

describe('buildCounterpartyOptions', () => {
  it('prepends the "Any" sentinel with value ""', () => {
    const out = buildCounterpartyOptions(
      [{ id: '1', name: 'AB Brewhaus' }],
      'Any',
    )
    expect(out[0]).toEqual({ label: 'Any', value: '' })
    expect(out).toHaveLength(2)
  })

  it('returns just the sentinel when the counterparty list is empty', () => {
    expect(buildCounterpartyOptions([], 'Any')).toEqual([
      { label: 'Any', value: '' },
    ])
  })

  it('preserves order and uses counterparty.name as the label', () => {
    const out = buildCounterpartyOptions(
      [
        { id: '1', name: 'Acme' },
        { id: '2', name: 'Beta' },
      ],
      'Any',
    )
    expect(out.map(o => o.label)).toEqual(['Any', 'Acme', 'Beta'])
    expect(out.map(o => o.value)).toEqual(['', '1', '2'])
  })

  it('uses the provided sentinel label literally (no fallback)', () => {
    expect(buildCounterpartyOptions([], '— any —')[0]?.label).toBe('— any —')
  })
})

describe('buildExecutionOptions', () => {
  const fmt = {
    fmtDate: (v: string | undefined) => (v ? `D(${v})` : ''),
    counterpartyLabel: (id: string | undefined | null) =>
      id ? `CP(${id})` : '',
  }

  it('joins available parts with " · " in date / title / cp / analyst order', () => {
    const exec: ExecutionLite = {
      id: 'e1',
      createdAt: '2026-05-14T10:00:00Z',
      title: 'Q1 run',
      counterpartyId: 'cp-1',
      analystName: 'Alice',
    }
    expect(buildExecutionOptions([exec], fmt)).toEqual([
      { label: 'D(2026-05-14T10:00:00Z) · Q1 run · CP(cp-1) · Alice', value: 'e1' },
    ])
  })

  it('skips title when null/empty', () => {
    const out = buildExecutionOptions(
      [{ id: 'e1', createdAt: 't', title: null, analystName: 'Alice' }],
      fmt,
    )
    expect(out[0]?.label).toBe('D(t) · Alice')
  })

  it('skips counterparty segment when id is missing or lookup returns empty', () => {
    const fmtMissing = {
      ...fmt,
      counterpartyLabel: () => '',
    }
    const out = buildExecutionOptions(
      [{ id: 'e1', createdAt: 't', counterpartyId: 'cp-1', analystName: 'a' }],
      fmtMissing,
    )
    expect(out[0]?.label).toBe('D(t) · a')
  })

  it('omits the date segment when fmtDate returns empty', () => {
    const out = buildExecutionOptions(
      [{ id: 'e1', createdAt: undefined, title: 'just title' }],
      fmt,
    )
    expect(out[0]?.label).toBe('just title')
  })

  it('yields an empty-labelled option when no parts apply', () => {
    const out = buildExecutionOptions([{ id: 'e1' }], fmt)
    expect(out[0]).toEqual({ label: '', value: 'e1' })
  })

  it('preserves array order', () => {
    const out = buildExecutionOptions(
      [
        { id: 'e1', title: 'first' },
        { id: 'e2', title: 'second' },
      ],
      fmt,
    )
    expect(out.map(o => o.value)).toEqual(['e1', 'e2'])
  })
})

describe('pickExecutionPlaceholder', () => {
  const labels = { loading: 'L', empty: 'E', ready: 'R' }

  it('returns the loading label when isLoading is true (even if list is empty)', () => {
    expect(
      pickExecutionPlaceholder({ isLoading: true, isEmpty: true }, labels),
    ).toBe('L')
  })

  it('returns the empty label when not loading and the list is empty', () => {
    expect(
      pickExecutionPlaceholder({ isLoading: false, isEmpty: true }, labels),
    ).toBe('E')
  })

  it('returns the ready label otherwise', () => {
    expect(
      pickExecutionPlaceholder({ isLoading: false, isEmpty: false }, labels),
    ).toBe('R')
  })

  it('prioritises loading over empty', () => {
    expect(
      pickExecutionPlaceholder({ isLoading: true, isEmpty: false }, labels),
    ).toBe('L')
  })
})
