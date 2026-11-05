import { WorkflowState } from '@/enums'

export const IN_FLIGHT_STATES: ReadonlySet<WorkflowState> = new Set([
  WorkflowState.Queued,
  WorkflowState.Running,
  WorkflowState.ProcessingDocuments,
])

/**
 * Vue Query `refetchInterval` factory for a single workflow execution.
 *
 * Polls every {@link intervalMs} (default 5s) while the run is in-flight so
 * the cached data — and any UI that derives from it (e.g. live-ticking
 * duration falling back to the server `duration` once terminal) — picks up
 * the terminal state without a manual reload. Returns `false` once the run
 * settles, which stops the polling loop.
 */
export const inFlightRefetchInterval = <
  T extends { workflowState?: WorkflowState },
>(
  intervalMs: number = 5_000,
) => {
  return (query: { state: { data?: T } }): number | false => {
    const state = query.state.data?.workflowState
    return state && IN_FLIGHT_STATES.has(state) ? intervalMs : false
  }
}
