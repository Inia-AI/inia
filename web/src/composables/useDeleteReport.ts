import API from '@/api'
import { useConfirmedMutation, type ConfirmedTarget } from './useConfirmedMutation'

export type DeletableReport = ConfirmedTarget

interface UseDeleteReportOptions {
  onSuccess?: (target: DeletableReport) => void
}

/**
 * Delete a settled report (Draft / Confirmed / Failed / Cancelled) — removes
 * it from the list entirely. Cancel of an in-flight run goes through
 * `useCancelExecution` instead. Wraps `useConfirmedMutation` with the
 * delete-specific mutation hook and i18n copy.
 */
export function useDeleteReport(options: UseDeleteReportOptions = {}) {
  return useConfirmedMutation<DeletableReport>({
    mutation: API.WorkflowReports.useDeleteWorkflowReport(),
    successKey: 'shared.deleteReport.success',
    errorKey: 'shared.deleteReport.error',
    onSuccess: options.onSuccess,
  })
}
