import { describe, it, expect, beforeEach, afterEach, vi } from 'vitest'
import { createPinia, setActivePinia } from 'pinia'
import useAgentActivityStore from '@/store/agentActivity'
import { WorkflowState } from '@/enums'
import type {
  AgentActivityDigest,
  AgentBlockActivity,
} from '@/api/agentActivity/schema'

const digest = (beats: number): AgentActivityDigest => ({
  schemaVersion: 1,
  outcome: 'Running',
  phases: [{ label: 'Getting ready', gapLabel: 'Getting ready…' }],
  beats: Array.from({ length: beats }, (_, index) => ({
    atSeconds: index,
    phaseIndex: 0,
    kind: 'Normal',
    label: `beat ${index}`,
  })),
  receipt: {
    documentNames: [],
    skillNames: [],
    pagesViewed: 0,
    codeRuns: 0,
    checksReported: 0,
  },
})

const leg = (overrides: Partial<AgentBlockActivity> = {}): AgentBlockActivity => ({
  blockId: 'block-1',
  blockName: 'Income Statement (AGT)',
  round: 1,
  settled: false,
  activity: digest(3),
  ...overrides,
})

const report = (
  overrides: Partial<{
    id: string
    title: string
    workflowState: WorkflowState
    agentActivity: unknown[]
  }> = {},
) => ({
  id: 'exec-1',
  title: 'Part 1 - Corporates',
  workflowState: WorkflowState.Running,
  agentActivity: [leg()],
  ...overrides,
})

describe('agentActivity store', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
  })

  describe('classic workflow activity', () => {
    afterEach(() => {
      vi.useRealTimers()
    })

    const classicReport = (
      overrides: Partial<{
        id: string
        workflowState: WorkflowState
        workflowActivity: unknown
      }> = {},
    ) => ({
      id: 'exec-classic',
      title: 'PE Monitoring',
      workflowState: WorkflowState.Running,
      agentActivity: null,
      workflowActivity: {
        schemaVersion: 1,
        currentStepIndex: 0,
        steps: [
          { label: 'Reading the fund reports' },
          { label: 'Assembling the monitoring report' },
        ],
      },
      ...overrides,
    })

    it('ingests a polled classic report into a current-step line', () => {
      const store = useAgentActivityStore()

      store.ingestReport(classicReport())

      const activity = store.classicActivityOf('exec-classic')
      expect(activity?.activeLabels).toEqual(['Reading the fund reports'])
      expect(activity?.doneCount).toBe(0)
      expect(activity?.stepCount).toBe(2)
    })

    it('marks a run watched from before its activity began', () => {
      const store = useAgentActivityStore()

      store.ingestReport(
        classicReport({ workflowState: WorkflowState.Queued, workflowActivity: null }),
      )
      store.ingestReport(classicReport())

      expect(store.classicActivityOf('exec-classic')?.watchedFromStart).toBe(true)
    })

    it('does not claim a run first seen mid-flight was watched from birth', () => {
      const store = useAgentActivityStore()

      store.ingestReport(classicReport())

      expect(store.classicActivityOf('exec-classic')?.watchedFromStart).toBe(false)
    })

    it('keeps every concurrently active step from a statused payload', () => {
      const store = useAgentActivityStore()

      store.ingestReport(
        classicReport({
          workflowActivity: {
            schemaVersion: 1,
            currentStepIndex: 1,
            steps: [
              { label: 'Reading the fund reports', status: 'Done' },
              { label: 'Extracting holdings', status: 'Active' },
              { label: 'Extracting valuations', status: 'Active' },
              { label: 'Assembling the monitoring report', status: 'Pending' },
            ],
          },
        }),
      )

      const activity = store.classicActivityOf('exec-classic')
      expect(activity?.activeLabels).toEqual(['Extracting holdings', 'Extracting valuations'])
      expect(activity?.doneCount).toBe(1)
      expect(activity?.stepCount).toBe(4)
    })

    it('advances the line when a re-poll moves the current step', () => {
      const store = useAgentActivityStore()

      store.ingestReport(classicReport())
      store.ingestReport(
        classicReport({
          workflowActivity: {
            schemaVersion: 1,
            currentStepIndex: 1,
            steps: [
              { label: 'Reading the fund reports' },
              { label: 'Assembling the monitoring report' },
            ],
          },
        }),
      )

      expect(store.classicActivityOf('exec-classic')?.activeLabels).toEqual([
        'Assembling the monitoring report',
      ])
      expect(store.classicActivityOf('exec-classic')?.doneCount).toBe(1)
    })

    it('clears the line when a later poll carries no activity', () => {
      const store = useAgentActivityStore()

      store.ingestReport(classicReport())
      store.ingestReport(classicReport({ workflowActivity: null }))

      expect(store.classicActivityOf('exec-classic')).toBeNull()
    })

    it('refuses a schema version it does not know', () => {
      const store = useAgentActivityStore()

      store.ingestReport(
        classicReport({
          workflowActivity: {
            schemaVersion: 99,
            currentStepIndex: 0,
            steps: [{ label: 'Reading the fund reports' }],
          },
        }),
      )

      expect(store.classicActivityOf('exec-classic')).toBeNull()
    })

    it('refuses a current step outside the script', () => {
      const store = useAgentActivityStore()

      store.ingestReport(
        classicReport({
          workflowActivity: {
            schemaVersion: 1,
            currentStepIndex: 7,
            steps: [{ label: 'Reading the fund reports' }],
          },
        }),
      )

      expect(store.classicActivityOf('exec-classic')).toBeNull()
    })

    it('refuses activity whose steps are not an array', () => {
      const store = useAgentActivityStore()

      store.ingestReport(
        classicReport({
          workflowActivity: {
            schemaVersion: 1,
            currentStepIndex: 0,
            steps: null,
          },
        }),
      )

      expect(store.classicActivityOf('exec-classic')).toBeNull()
    })

    it('keeps the quiet state line for a run that is not yet running', () => {
      const store = useAgentActivityStore()

      store.ingestReport(classicReport({ workflowState: WorkflowState.Queued }))

      expect(store.classicActivityOf('exec-classic')).toBeNull()
    })

    it('prunes a line the poll stopped refreshing', () => {
      vi.useFakeTimers()
      vi.setSystemTime(0)
      const store = useAgentActivityStore()

      store.ingestReport(classicReport())
      vi.setSystemTime(16 * 60 * 1000)
      store.prune()

      expect(store.classicActivityOf('exec-classic')).toBeNull()
    })
  })

  it('ingests a polled report into rail entries', () => {
    const store = useAgentActivityStore()

    store.ingestReport(report())

    expect(store.railEntries).toHaveLength(1)
    expect(store.railEntries[0].blockName).toBe('Income Statement (AGT)')
    expect(store.railEntries[0].executionTitle).toBe('Part 1 - Corporates')
    expect(store.railEntries[0].digest?.beats).toHaveLength(3)
    expect(store.railEntries[0].settled).toBe(false)
  })

  it('replaces the digest wholesale on re-poll, so beats never duplicate', () => {
    const store = useAgentActivityStore()

    store.ingestReport(report())
    store.ingestReport(report())
    store.ingestReport(
      report({
        agentActivity: [
          {
            blockId: 'block-1',
            blockName: 'Income Statement (AGT)',
            settled: false,
            activity: digest(5),
          },
        ],
      }),
    )

    expect(store.railEntries).toHaveLength(1)
    expect(store.railEntries[0].digest?.beats).toHaveLength(5)
  })

  it('keeps the last digest when a later poll carries none', () => {
    const store = useAgentActivityStore()

    store.ingestReport(report())
    store.ingestReport(
      report({
        agentActivity: [
          {
            blockId: 'block-1',
            blockName: 'Income Statement (AGT)',
            settled: true,
            activity: null,
          },
        ],
      }),
    )

    expect(store.railEntries[0].digest?.beats).toHaveLength(3)
    expect(store.railEntries[0].settled).toBe(true)
  })

  it('a settled leg never reverts to running on an out-of-order poll', () => {
    const store = useAgentActivityStore()

    store.ingestReport(report({ agentActivity: [leg({ settled: true })] }))
    store.ingestReport(report({ agentActivity: [leg({ settled: false })] }))

    expect(store.railEntries[0].settled).toBe(true)
  })

  it('a newer leg for the same block reads as running, though the older one settled', () => {
    const store = useAgentActivityStore()

    store.ingestReport(report({ agentActivity: [leg({ round: 1, settled: true })] }))
    store.ingestReport(
      report({
        agentActivity: [
          leg({ round: 1, settled: true }),
          leg({ round: 2, settled: false }),
        ],
      }),
    )

    const row = store.railEntries[0]
    expect(store.railEntries).toHaveLength(1)
    expect(row.round).toBe(2)
    expect(row.settled).toBe(false)
  })

  it('keeps both legs of a redone member, so the settled one stays readable', () => {
    const store = useAgentActivityStore()

    store.ingestReport(report({ agentActivity: [leg({ round: 1, settled: true })] }))
    const roundOneKey = store.railEntries[0].key
    store.ingestReport(
      report({
        agentActivity: [
          leg({ round: 1, settled: true }),
          leg({ round: 2, settled: false }),
        ],
      }),
    )

    store.openPanel(roundOneKey)

    expect(store.panelEntry?.round).toBe(1)
    expect(store.panelEntry?.settled).toBe(true)
  })

  it('a final digest for round one does not settle round two', () => {
    const store = useAgentActivityStore()

    store.ingestReport(report({ agentActivity: [leg({ round: 1, settled: true })] }))
    const roundOneKey = store.railEntries[0].key
    store.ingestReport(
      report({
        agentActivity: [
          leg({ round: 1, settled: true }),
          leg({ round: 2, settled: false }),
        ],
      }),
    )

    store.setFinalDigest(roundOneKey, { ...digest(7), outcome: 'Succeeded' })

    const row = store.railEntries[0]
    expect(row.round).toBe(2)
    expect(row.settled).toBe(false)
    expect(row.displayDigest?.beats).toHaveLength(3)
  })

  it('a machinery retry inside one round stays one leg, not a second row', () => {
    const store = useAgentActivityStore()

    store.ingestReport(report({ agentActivity: [leg({ round: 1, activity: digest(3) })] }))
    store.ingestReport(report({ agentActivity: [leg({ round: 1, activity: digest(6) })] }))

    // One row, showing the newer pass. A store that keyed on the run the platform mints would
    // hold two entries here and go on showing the first pass, since both are round one.
    expect(store.railEntries).toHaveLength(1)
    expect(store.railEntries[0].round).toBe(1)
    expect(store.railEntries[0].digest?.beats).toHaveLength(6)
  })

  it('reads a verifier leg as the verifier, and everything else as a worker', () => {
    const store = useAgentActivityStore()

    store.ingestReport(
      report({
        agentActivity: [
          leg({ blockId: 'block-1', role: 'w' }),
          leg({ blockId: 'block-2', role: 'v' }),
          leg({ blockId: 'block-3' }),
        ],
      }),
    )

    expect(store.railEntries.map(entry => entry.role)).toEqual([
      'worker',
      'verifier',
      'worker',
    ])
  })

  it('a payload with no leg identity still renders one entry per block', () => {
    const store = useAgentActivityStore()

    store.ingestReport(
      report({
        agentActivity: [
          { blockId: 'block-1', blockName: 'B', settled: false, activity: digest(3) },
          { blockId: 'block-2', blockName: 'C', settled: false, activity: digest(3) },
        ],
      }),
    )

    expect(store.railEntries).toHaveLength(2)
    expect(store.railEntries.map(entry => entry.round)).toEqual([1, 1])
  })

  it('keeps a running redo when its own earlier round has gone stale', () => {
    vi.useFakeTimers()
    vi.setSystemTime(0)
    const store = useAgentActivityStore()

    store.ingestReport(report({ agentActivity: [leg({ round: 1, settled: true })] }))

    // The feed lets a settled leg go long before the redo it started finishes, so round one
    // stops being refreshed while round two is still billing.
    vi.setSystemTime(16 * 60 * 1000)
    store.ingestReport(report({ agentActivity: [leg({ round: 2, settled: false })] }))
    store.prune()

    expect(store.railEntries).toHaveLength(1)
    expect(store.railEntries[0].round).toBe(2)
    vi.useRealTimers()
  })

  it('drops every leg of a block once its newest one goes stale', () => {
    vi.useFakeTimers()
    vi.setSystemTime(0)
    const store = useAgentActivityStore()

    store.ingestReport(
      report({
        agentActivity: [
          leg({ round: 1, settled: true }),
          leg({ round: 2, settled: false }),
        ],
      }),
    )

    store.prune(16 * 60 * 1000)

    expect(store.railEntries).toHaveLength(0)
    vi.useRealTimers()
  })

  it('dismissing a redone member removes every leg, not just the current one', () => {
    const store = useAgentActivityStore()

    store.ingestReport(
      report({
        agentActivity: [
          leg({ round: 1, settled: true }),
          leg({ round: 2, settled: false }),
        ],
      }),
    )

    store.dismiss(store.railEntries[0].key)

    expect(store.railEntries).toHaveLength(0)
  })

  it('the persisted digest swaps in after settle and wins over the live one', () => {
    const store = useAgentActivityStore()
    store.ingestReport(report())
    const key = store.railEntries[0].key

    store.setFinalDigest(key, { ...digest(7), outcome: 'Succeeded' })

    expect(store.railEntries[0].displayDigest?.beats).toHaveLength(7)
    expect(store.railEntries[0].displayDigest?.outcome).toBe('Succeeded')
  })

  it('tracks the open panel entry and closes it', () => {
    const store = useAgentActivityStore()
    store.ingestReport(report())
    const key = store.railEntries[0].key

    store.openPanel(key)
    expect(store.panelEntry?.key).toBe(key)

    store.closePanel()
    expect(store.panelEntry).toBeNull()
  })

  it('a run that ends without reporting its members drops them from the rail', () => {
    const store = useAgentActivityStore()
    store.ingestReport(
      report({ agentActivity: [leg(), leg({ blockId: 'block-2', blockName: 'Balance Sheet (AGT)' })] }),
    )
    expect(store.railEntries).toHaveLength(2)

    store.ingestReport(report({ workflowState: WorkflowState.Cancelled, agentActivity: [] }))

    expect(store.railEntries).toHaveLength(0)
  })

  it('a run that ends keeps the members it still reports', () => {
    const store = useAgentActivityStore()
    store.ingestReport(
      report({ agentActivity: [leg(), leg({ blockId: 'block-2', blockName: 'Balance Sheet (AGT)' })] }),
    )

    store.ingestReport(
      report({ workflowState: WorkflowState.Draft, agentActivity: [leg({ settled: true })] }),
    )

    expect(store.railEntries.map(entry => entry.blockId)).toEqual(['block-1'])
    expect(store.railEntries[0].settled).toBe(true)
  })

  it('a running report that omits a member leaves that member on the rail', () => {
    const store = useAgentActivityStore()
    store.ingestReport(
      report({ agentActivity: [leg(), leg({ blockId: 'block-2', blockName: 'Balance Sheet (AGT)' })] }),
    )

    store.ingestReport(report({ agentActivity: [leg()] }))

    expect(store.railEntries).toHaveLength(2)
  })

  it('a member whose panel is open survives its run ending unreported, as settled', () => {
    const store = useAgentActivityStore()
    store.ingestReport(report())
    const key = store.railEntries[0].key
    store.openPanel(key)

    store.ingestReport(report({ workflowState: WorkflowState.Cancelled, agentActivity: [] }))

    expect(store.panelEntry?.key).toBe(key)
    expect(store.railEntries[0].settled).toBe(true)
  })

  it('another run ending does not touch this run\'s members', () => {
    const store = useAgentActivityStore()
    store.ingestReport(report())

    store.ingestReport(
      report({ id: 'exec-2', workflowState: WorkflowState.Cancelled, agentActivity: [] }),
    )

    expect(store.railEntries).toHaveLength(1)
  })

  it('dismissing an entry removes it and closes its panel', () => {
    const store = useAgentActivityStore()
    store.ingestReport(report())
    const key = store.railEntries[0].key
    store.openPanel(key)

    store.dismiss(key)

    expect(store.railEntries).toHaveLength(0)
    expect(store.panelEntry).toBeNull()
  })

  it('prunes stale entries but never the one open in the panel', () => {
    const store = useAgentActivityStore()
    store.ingestReport(report())
    store.ingestReport(
      report({
        id: 'exec-2',
        agentActivity: [
          {
            blockId: 'block-2',
            blockName: 'Other',
            settled: true,
            activity: digest(1),
          },
        ],
      }),
    )
    store.openPanel(store.railEntries[1].key)

    store.prune(Date.now() + 16 * 60 * 1000)

    expect(store.railEntries).toHaveLength(1)
    expect(store.railEntries[0].blockId).toBe('block-2')
  })

  it('entries from two executions keep separate identities', () => {
    const store = useAgentActivityStore()

    store.ingestReport(report())
    store.ingestReport(report({ id: 'exec-2', title: 'Other workflow' }))

    expect(store.railEntries).toHaveLength(2)
    expect(new Set(store.railEntries.map(entry => entry.key)).size).toBe(2)
  })

  it('a later poll that omits the title never erases a known one', () => {
    const store = useAgentActivityStore()

    store.ingestReport(report())
    store.ingestReport({ id: 'exec-1', agentActivity: report().agentActivity })

    expect(store.railEntries[0].executionTitle).toBe('Part 1 - Corporates')
  })

  it('carries the counterparty name and keeps it when a later poll omits it', () => {
    const store = useAgentActivityStore()

    store.ingestReport({ ...report(), counterpartyName: 'Amazon' })
    store.ingestReport(report())

    expect(store.railEntries[0].counterpartyName).toBe('Amazon')
  })

  describe('lastBeatAt', () => {
    afterEach(() => {
      vi.useRealTimers()
    })

    it('is stamped when the entry appears and again only when beats grow', () => {
      vi.useFakeTimers()
      vi.setSystemTime(1_000)
      const store = useAgentActivityStore()

      store.ingestReport(report())
      expect(store.railEntries[0].lastBeatAt).toBe(1_000)

      vi.setSystemTime(9_000)
      store.ingestReport(report())
      expect(store.railEntries[0].lastBeatAt).toBe(1_000)

      vi.setSystemTime(20_000)
      store.ingestReport(
        report({
          agentActivity: [
            {
              blockId: 'block-1',
              blockName: 'Income Statement (AGT)',
              settled: false,
              activity: digest(5),
            },
          ],
        }),
      )
      expect(store.railEntries[0].lastBeatAt).toBe(20_000)
    })

    it('prefers the digest’s own run anchor over the arrival clock', () => {
      vi.useFakeTimers()
      vi.setSystemTime(Date.parse('2026-08-13T14:00:00Z'))
      const store = useAgentActivityStore()

      store.ingestReport(
        report({
          agentActivity: [
            {
              blockId: 'block-1',
              blockName: 'Income Statement (AGT)',
              settled: false,
              activity: {
                ...digest(3),
                startedAtUtc: '2026-08-13T13:38:58.532Z',
              },
            },
          ],
        }),
      )

      expect(store.railEntries[0].lastBeatAt).toBe(
        Date.parse('2026-08-13T13:38:58.532Z') + 2_000,
      )
    })

    it('falls back to the arrival clock when the anchor is unreadable', () => {
      vi.useFakeTimers()
      vi.setSystemTime(5_000)
      const store = useAgentActivityStore()

      store.ingestReport(
        report({
          agentActivity: [
            {
              blockId: 'block-1',
              blockName: 'Income Statement (AGT)',
              settled: false,
              activity: { ...digest(3), startedAtUtc: 'not-a-date' },
            },
          ],
        }),
      )

      expect(store.railEntries[0].lastBeatAt).toBe(5_000)
    })

    it('survives a poll that carries no digest', () => {
      vi.useFakeTimers()
      vi.setSystemTime(1_000)
      const store = useAgentActivityStore()

      store.ingestReport(report())
      vi.setSystemTime(30_000)
      store.ingestReport(
        report({
          agentActivity: [
            {
              blockId: 'block-1',
              blockName: 'Income Statement (AGT)',
              settled: false,
              activity: null,
            },
          ],
        }),
      )

      expect(store.railEntries[0].lastBeatAt).toBe(1_000)
    })
  })
})
