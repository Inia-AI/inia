import { describe, it, expect } from 'vitest'
import { activityViewOf } from '@/lib/workflowActivity'
import type { WorkflowActivity } from '@/api/workflowReports/schema'

const activity = (overrides: Partial<WorkflowActivity> = {}): WorkflowActivity => ({
  schemaVersion: 1,
  currentStepIndex: 1,
  steps: [
    { label: 'Reading the filings', status: 'Done' },
    { label: 'Extracting the income statement', status: 'Active' },
    { label: 'Extracting the balance sheet', status: 'Active' },
    { label: 'Assembling the report', status: 'Pending' },
  ],
  ...overrides,
})

describe('activityViewOf', () => {
  it('surfaces every active step with the done steps and count', () => {
    expect(activityViewOf(activity())).toEqual({
      activeLabels: ['Extracting the income statement', 'Extracting the balance sheet'],
      doneLabels: ['Reading the filings'],
      doneCount: 1,
      stepCount: 4,
    })
  })

  it('falls back to the current step when the server sends no statuses', () => {
    expect(
      activityViewOf(
        activity({
          steps: [{ label: 'Reading the filings' }, { label: 'Extracting the figures' }],
          currentStepIndex: 1,
        }),
      ),
    ).toEqual({
      activeLabels: ['Extracting the figures'],
      doneLabels: ['Reading the filings'],
      doneCount: 1,
      stepCount: 2,
    })
  })

  it('treats a status vocabulary it does not know as absent', () => {
    expect(
      activityViewOf(
        activity({
          steps: [
            { label: 'Reading the filings', status: 'Paused' as never },
            { label: 'Extracting the figures', status: 'Sideways' as never },
          ],
          currentStepIndex: 0,
        }),
      ),
    ).toEqual({
      activeLabels: ['Reading the filings'],
      doneLabels: [],
      doneCount: 0,
      stepCount: 2,
    })
  })

  it('reports zero done at zero progress on the fallback path', () => {
    expect(
      activityViewOf(
        activity({
          steps: [{ label: 'Reading the filings' }, { label: 'Extracting the figures' }],
          currentStepIndex: 0,
        }),
      ),
    ).toEqual({
      activeLabels: ['Reading the filings'],
      doneLabels: [],
      doneCount: 0,
      stepCount: 2,
    })
  })

  it('yields nothing for an absent payload', () => {
    expect(activityViewOf(null)).toBeNull()
    expect(activityViewOf(undefined)).toBeNull()
  })

  it('refuses a schema version it does not know', () => {
    expect(activityViewOf(activity({ schemaVersion: 2 }))).toBeNull()
  })

  it('refuses steps that are not an array', () => {
    expect(
      activityViewOf(activity({ steps: null as unknown as WorkflowActivity['steps'] })),
    ).toBeNull()
  })

  it('refuses an index outside the script when falling back', () => {
    expect(
      activityViewOf(
        activity({ steps: [{ label: 'Reading the filings' }], currentStepIndex: 7 }),
      ),
    ).toBeNull()
  })

  it('skips an active step with no label rather than rendering a blank line', () => {
    expect(
      activityViewOf(
        activity({
          steps: [
            { label: '', status: 'Active' },
            { label: 'Extracting the figures', status: 'Active' },
          ],
        }),
      )?.activeLabels,
    ).toEqual(['Extracting the figures'])
  })
})
