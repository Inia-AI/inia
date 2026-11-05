import API from '@/api'
import { WorkflowState } from '@/enums'
import { useConfirmedMutation, type ConfirmedTarget } from './useConfirmedMutation'

export type CancellableExecution = ConfirmedTarget

// Single source of truth for which execution states can be cancelled. Mirrors
// the backend's contract: in-flight runs are stoppable, anything else is
// either not started or already terminal.
export const CANCELLABLE_STATES: ReadonlySet<WorkflowState> = new Set([
  WorkflowState.Running,
  WorkflowState.ProcessingDocuments,
  WorkflowState.Queued,
])

export function isCancellableState(state: WorkflowState | undefined): boolean {
  return state !== undefined && CANCELLABLE_STATES.has(state)
}

interface UseCancelExecutionOptions {
  onSuccess?: (target: CancellableExecution) => void
}

/**
 * Cancel an in-flight execution by transitioning it to `Cancelled` via PATCH
 * on `/workflow-executions/{id}/state`. Wraps `useConfirmedMutation` with the
 * cancel-specific mutation hook and i18n copy.
 */
export function useCancelExecution(options: UseCancelExecutionOptions = {}) {
  return useConfirmedMutation<CancellableExecution>({
    mutation: API.WorkflowExecutions.useCancelWorkflowExecution(),
    successKey: 'shared.cancelExecution.success',
    errorKey: 'shared.cancelExecution.error',
    onSuccess: options.onSuccess,
  })
}
