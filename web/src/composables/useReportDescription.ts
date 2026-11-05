import { useI18n } from 'vue-i18n'
import type { WorkflowReportSummary } from '@/api/schema'
import { formatDateTimeSeconds } from '@/lib/utils'

export const useReportDescription = () => {
  const { t } = useI18n()

  return (report: WorkflowReportSummary) => {
    const type = report.workflowTypeName || report.workflowTypeId
    if (!type) return undefined
    const counterparty = report.counterpartyName || report.counterpartyId
    const subject = counterparty ? t('page.financialAnalysis.reportTypeForCounterparty', { type, counterparty }) : type
    return report.createdAt
      ? t('page.financialAnalysis.reportAdded', { report: subject, date: formatDateTimeSeconds(report.createdAt) })
      : subject
  }
}
