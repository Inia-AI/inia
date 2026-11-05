import type { WorkflowReportSummary } from '@/api/schema'
import { reportRoute } from '@/lib/routes'
import type { ColumnDef } from '@tanstack/vue-table'
import { h } from 'vue'
import { useRouter } from 'vue-router'
import WorkflowStatus from '@/components/molecules/WorkflowStatus/WorkflowStatus.vue'
import { Button } from '@/components/ui/button'
import { useI18n } from 'vue-i18n'
import { useReportDescription } from '@/composables/useReportDescription'
import { formatDateTimeSeconds } from '@/lib/utils'
import LiveDuration from '@/components/atoms/LiveDuration/LiveDuration.vue'
import ReplayProvenanceBadge from '@/components/molecules/ReplayProvenanceBadge/ReplayProvenanceBadge.vue'

const EMPTY_CELL = '-'

export default (extraColumns: ColumnDef<WorkflowReportSummary>[] = []) => {
  const router = useRouter()
  const { t: $t } = useI18n()

  const describeReport = useReportDescription()

  const viewReportLabel = (report: WorkflowReportSummary) => {
    const description = describeReport(report)
    return description ? $t('page.financialAnalysis.viewReportOf', { report: description }) : undefined
  }

  const columns: ColumnDef<WorkflowReportSummary>[] = [
    {
      accessorKey: 'analystName',
      header: () => $t('page.financialAnalysis.columns.analystName'),
      enableSorting: true,
      cell: ({ row }) => {
        const value = row.original.analystName
        return value ? h('div', { class: 'max-w-sm' }, value) : h('div', { class: 'w-24 text-center' }, EMPTY_CELL)
      },
    },
    {
      accessorKey: 'counterpartyName',
      header: () => $t('page.financialAnalysis.columns.counterparty'),
      enableSorting: true,
      cell: ({ row }) => h('div', {}, row.original.counterpartyName || row.original.counterpartyId || EMPTY_CELL),
    },
    {
      accessorKey: 'workflowTypeName',
      header: () => $t('page.financialAnalysis.columns.type'),
      meta: { rowLinkLabel: row => describeReport(row.original) },
      cell: ({ row }) => h('div', {}, row.original.workflowTypeName || row.original.workflowTypeId || EMPTY_CELL),
    },
    {
      accessorKey: 'workflowState',
      enableSorting: true,
      header: () => h('div', { class: 'text-center w-full' }, $t('page.financialAnalysis.columns.status')),
      cell: ({ row }) =>
        h(
          'div',
          {
            class: 'flex items-center justify-center gap-2 min-w-max cursor-pointer',
          },
          [
            h(WorkflowStatus, {
              status: row.getValue('workflowState') as string,
              failureKind: row.original.failureKind,
              failureDetail: row.original.failureDetail,
            }),
            ...(row.original.freezeSourceExecutionId
              ? [
                  h(ReplayProvenanceBadge, {
                    freezeSourceExecutionId: row.original.freezeSourceExecutionId,
                    freezeReplayedCount: row.original.freezeReplayedCount,
                    freezeEligibleCount: row.original.freezeEligibleCount,
                    workflowState: row.original.workflowState,
                  }),
                ]
              : []),
          ],
        ),
    },
    {
      accessorKey: 'createdAt',
      enableSorting: true,
      header: () => h('div', { class: 'text-center w-full' }, $t('page.financialAnalysis.columns.dateAdded')),
      cell: ({ row }) => h('div', { class: 'text-center lowercase' }, formatDateTimeSeconds(row.original.createdAt)),
    },
    {
      accessorKey: 'duration',
      enableSorting: false,
      header: () => h('div', { class: 'text-center w-full' }, $t('page.financialAnalysis.columns.duration')),
      cell: ({ row }) =>
        h(
          'div',
          {
            class: 'text-center font-mono text-xs tabular-nums text-[var(--text-secondary)]',
          },
          [
            h(LiveDuration, {
              startedAt: row.original.startedAt,
              duration: row.original.duration,
              workflowState: row.original.workflowState,
            }),
          ],
        ),
    },
    ...extraColumns,
    {
      id: 'actions',
      header: () => h('div', { class: 'text-center w-full' }, $t('page.financialAnalysis.columns.actions')),
      // Everything but View Report (Cancel / Re-run / Documents / Download / Delete) lives on
      // the report detail page to keep this list scannable.
      cell: ({ row }) => {
        return h(
          'div',
          { class: 'text-center w-full flex gap-2 justify-center' },
          [
            h(
              Button,
              {
                variant: 'outline',
                size: 'sm',
                // The utility rank — the pale-blue-fill treatment the design
                // mock gives this exact button.
                class: 'btn-utility',
                'aria-label': viewReportLabel(row.original),
                onClick: () => router.push(reportRoute(row.original.id)),
              },
              () => $t('page.financialAnalysis.viewReport'),
            ),
          ],
        )
      },
    },
  ]

  return { columns, rowLinkColumn: 'workflowTypeName' }
}
