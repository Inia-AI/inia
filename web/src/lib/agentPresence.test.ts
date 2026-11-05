import { describe, it, expect } from 'vitest'
import {
  beatKeysOf,
  collapseRepeatedBeats,
  freshnessOf,
  FRESHNESS_WINDOW_MS,
} from '@/lib/agentPresence'
import type { AgentActivityBeat } from '@/api/agentActivity/schema'

const beat = (
  overrides: Partial<AgentActivityBeat> = {},
): AgentActivityBeat => ({
  atSeconds: 10,
  phaseIndex: 0,
  kind: 'Normal',
  label: 'Planning the extraction.',
  ...overrides,
})

describe('freshnessOf', () => {
  it('is full the moment a note arrives', () => {
    expect(freshnessOf(1_000, 1_000)).toBe(1)
  })

  it('decays to zero over the window and stays there', () => {
    expect(freshnessOf(0, FRESHNESS_WINDOW_MS / 2)).toBeCloseTo(0.5)
    expect(freshnessOf(0, FRESHNESS_WINDOW_MS)).toBe(0)
    expect(freshnessOf(0, FRESHNESS_WINDOW_MS * 2)).toBe(0)
  })

  it('is zero without a timestamp', () => {
    expect(freshnessOf(null, 1_000)).toBe(0)
  })
})

describe('beatKeysOf', () => {
  it('derives keys from the beat itself, stable across wholesale replacement', () => {
    const feed = [
      beat({ atSeconds: 3 }),
      beat({ atSeconds: 9, label: 'Assembling the answer.' }),
    ]

    const first = beatKeysOf(feed)
    const second = beatKeysOf([
      beat({ atSeconds: 3 }),
      beat({ atSeconds: 9, label: 'Assembling the answer.' }),
    ])

    expect(second).toEqual(first)
    expect(new Set(first).size).toBe(2)
  })

  it('disambiguates beats whose data is identical', () => {
    const keys = beatKeysOf([beat(), beat(), beat()])

    expect(new Set(keys).size).toBe(3)
  })
})

describe('collapseRepeatedBeats', () => {
  const working = (atSeconds: number) =>
    beat({ atSeconds, label: 'Recorded a working file' })

  it('folds a run of identical beats into the first, counting them', () => {
    const collapsed = collapseRepeatedBeats([
      working(274),
      working(279),
      working(289),
      working(297),
      working(311),
    ])

    expect(collapsed).toHaveLength(1)
    expect(collapsed[0]?.repeat).toBe(5)
    expect(collapsed[0]?.beat.atSeconds).toBe(274)
  })

  it('keeps a repeat that different work interrupted on its own line', () => {
    const collapsed = collapseRepeatedBeats([
      working(274),
      working(279),
      beat({ atSeconds: 315, label: 'Checking the nine-category sums' }),
      working(329),
    ])

    expect(
      collapsed.map(entry => [entry.beat.atSeconds, entry.repeat]),
    ).toEqual([
      [274, 2],
      [315, 1],
      [329, 1],
    ])
  })

  it('leaves a feed with nothing repeated exactly as it found it', () => {
    const feed = [
      beat({ atSeconds: 331, label: 'Assembling the answer.' }),
      beat({ atSeconds: 370, label: 'Composed the final answer' }),
    ]

    expect(collapseRepeatedBeats(feed).map(entry => entry.beat)).toEqual(feed)
    expect(
      collapseRepeatedBeats(feed).every(entry => entry.repeat === 1),
    ).toBe(true)
  })

  it('does not fold beats that only look alike', () => {
    const collapsed = collapseRepeatedBeats([
      working(274),
      beat({ atSeconds: 279, label: 'Recorded a working file', kind: 'Check' }),
      beat({
        atSeconds: 289,
        label: 'Recorded a working file',
        detail: 'out/',
      }),
    ])

    expect(collapsed).toHaveLength(3)
  })

  it('gives every surviving beat a distinct key', () => {
    const collapsed = collapseRepeatedBeats([
      working(274),
      working(274),
      beat({ atSeconds: 274, label: 'Checking the nine-category sums' }),
    ])

    const keys = collapsed.map(entry => entry.key)
    expect(new Set(keys).size).toBe(keys.length)
  })

  it('survives an empty feed', () => {
    expect(collapseRepeatedBeats([])).toEqual([])
  })
})
