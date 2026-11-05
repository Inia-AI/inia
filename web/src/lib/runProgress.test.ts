import { describe, expect, it } from 'vitest'
import { runProgressLayoutOf, showsAgents, showsSections } from './runProgress'

describe('runProgressLayoutOf', () => {
  it('shows both when a run has agents and section progress', () => {
    const layout = runProgressLayoutOf({ agentCount: 2, hasSectionProgress: true, hasStateLine: false })
    expect(layout).toBe('both')
    expect(showsAgents(layout)).toBe(true)
    expect(showsSections(layout)).toBe(true)
  })

  it('shows only the agents when the run has no section progress', () => {
    const layout = runProgressLayoutOf({ agentCount: 3, hasSectionProgress: false, hasStateLine: false })
    expect(layout).toBe('agents')
    expect(showsAgents(layout)).toBe(true)
    expect(showsSections(layout)).toBe(false)
  })

  it('shows only the sections when the run has no agents', () => {
    const layout = runProgressLayoutOf({ agentCount: 0, hasSectionProgress: true, hasStateLine: false })
    expect(layout).toBe('sections')
    expect(showsAgents(layout)).toBe(false)
    expect(showsSections(layout)).toBe(true)
  })

  it('falls back to the state line when the run has neither', () => {
    expect(runProgressLayoutOf({ agentCount: 0, hasSectionProgress: false, hasStateLine: true })).toBe('state')
  })

  it('shows nothing when the run has neither and no state line', () => {
    const layout = runProgressLayoutOf({ agentCount: 0, hasSectionProgress: false, hasStateLine: false })
    expect(layout).toBe('none')
    expect(showsAgents(layout)).toBe(false)
    expect(showsSections(layout)).toBe(false)
  })

  it('prefers progress over the state line', () => {
    expect(runProgressLayoutOf({ agentCount: 1, hasSectionProgress: false, hasStateLine: true })).toBe('agents')
    expect(runProgressLayoutOf({ agentCount: 0, hasSectionProgress: true, hasStateLine: true })).toBe('sections')
  })
})
