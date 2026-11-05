import { describe, expect, it } from 'vitest'
import { inFlightRefetchInterval } from './workflowRefetch'
import { WorkflowState } from '@/enums'

const buildQuery = (state?: WorkflowState) => ({
  state: { data: state ? { workflowState: state } : undefined },
})

describe('inFlightRefetchInterval', () => {
  it('returns the configured interval while running', () => {
    const policy = inFlightRefetchInterval(2_000)
    expect(policy(buildQuery(WorkflowState.Running))).toBe(2_000)
  })

  it('returns the configured interval while processing documents or queued', () => {
    const policy = inFlightRefetchInterval()
    expect(policy(buildQuery(WorkflowState.ProcessingDocuments))).toBe(5_000)
    expect(policy(buildQuery(WorkflowState.Queued))).toBe(5_000)
  })

  it('stops polling once the run reaches a terminal state', () => {
    const policy = inFlightRefetchInterval()
    expect(policy(buildQuery(WorkflowState.Draft))).toBe(false)
    expect(policy(buildQuery(WorkflowState.Confirmed))).toBe(false)
    expect(policy(buildQuery(WorkflowState.Failed))).toBe(false)
    expect(policy(buildQuery(WorkflowState.Cancelled))).toBe(false)
  })

  it('stops polling when no data is available yet', () => {
    const policy = inFlightRefetchInterval()
    expect(policy(buildQuery(undefined))).toBe(false)
  })

  it('defaults the interval to 5 seconds', () => {
    const policy = inFlightRefetchInterval()
    expect(policy(buildQuery(WorkflowState.Running))).toBe(5_000)
  })
})
