import type { RefinementTurn } from '@/api/contentRefinement/schema'
import { describe, expect, it } from 'vitest'
import { appendPairWithWindow } from './historyWindow'

describe('appendPairWithWindow', () => {
  it('appends both turns when under the cap', () => {
    const history: RefinementTurn[] = []
    appendPairWithWindow(history, 'ask', 'reply', 3)
    expect(history).toEqual([
      { role: 'user', content: 'ask' },
      { role: 'assistant', content: 'reply' },
    ])
  })

  it('keeps only the most-recent N pairs when exceeded', () => {
    const history: RefinementTurn[] = []
    for (let i = 0; i < 5; i++) {
      appendPairWithWindow(history, `u${i}`, `a${i}`, 3)
    }
    expect(history).toHaveLength(6)
    expect(history.map(t => t.content)).toEqual(['u2', 'a2', 'u3', 'a3', 'u4', 'a4'])
  })

  it('drops the oldest pair atomically (never a half-pair)', () => {
    const history: RefinementTurn[] = []
    for (let i = 0; i < 10; i++) {
      appendPairWithWindow(history, `u${i}`, `a${i}`, 4)
    }
    // Every even index must be a user turn, every odd index an assistant turn.
    history.forEach((turn, i) => {
      expect(turn.role).toBe(i % 2 === 0 ? 'user' : 'assistant')
    })
  })

  it('catches up when history was already over the cap', () => {
    const history: RefinementTurn[] = Array.from({ length: 20 }, (_, i) => ({
      role: i % 2 === 0 ? 'user' : 'assistant',
      content: `seed${i}`,
    }))
    appendPairWithWindow(history, 'new-u', 'new-a', 2)
    expect(history).toHaveLength(4)
    expect(history.map(t => t.content)).toEqual(['seed18', 'seed19', 'new-u', 'new-a'])
  })
})
