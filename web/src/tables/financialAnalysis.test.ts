import { describe, expect, it, vi } from 'vitest'
import { mount } from '@vue/test-utils'
import { defineComponent, h } from 'vue'
import DataTable from '@/components/organisms/DataTable/DataTable.vue'
import type { ColumnDef } from '@tanstack/vue-table'
import useFinancialAnalysisTable from './financialAnalysis'
import type { WorkflowReportSummary } from '@/api/schema'
import { formatDateTimeSeconds } from '@/lib/utils'

vi.mock('vue-router', () => ({ useRouter: () => ({ push: vi.fn() }) }))
vi.mock('vue-i18n', () => ({
  useI18n: () => ({
    t: (key: string, params?: Record<string, unknown>) => (params ? `${key}:${JSON.stringify(params)}` : key),
  }),
}))

const columnIds = (extraColumns?: ColumnDef<WorkflowReportSummary>[]) => {
  let ids: (string | undefined)[] = []
  mount(
    defineComponent({
      setup() {
        ids = useFinancialAnalysisTable(extraColumns).columns.map(
          column => column.id ?? (column as { accessorKey?: string }).accessorKey,
        )
        return () => h('div')
      },
    }),
  )
  return ids
}

describe('financialAnalysis table', () => {
  it('has no baseline column of its own', () => {
    expect(columnIds()).not.toContain('baseline')
    expect(columnIds().at(-1)).toBe('actions')
  })

  it('puts the columns an extension adds before the actions column', () => {
    const ids = columnIds([{ id: 'extension' }])

    expect(ids.at(-2)).toBe('extension')
    expect(ids.at(-1)).toBe('actions')
  })
})

const viewReportButtons = (rows: Partial<WorkflowReportSummary>[]) => {
  const wrapper = mount(
    defineComponent({
      setup() {
        const { columns } = useFinancialAnalysisTable()
        return () => h(DataTable, { columns, data: rows })
      },
    }),
    { global: { stubs: { LiveDuration: true, WorkflowStatus: true, ReplayProvenanceBadge: true } } },
  )
  return wrapper.findAll('tbody button')
}

describe('financialAnalysis table View Report button', () => {
  it('names the report it opens by its type, counterparty and date added', () => {
    const [button] = viewReportButtons([
      {
        id: 'r1',
        workflowTypeId: 'credit-memo',
        workflowTypeName: 'Credit Memo',
        counterpartyName: 'Pinebrook Capital',
        createdAt: '2026-10-01T09:30:00',
      },
    ])

    expect(button!.text()).toBe('page.financialAnalysis.viewReport')
    expect(button!.attributes('aria-label')).toBe(
      `page.financialAnalysis.viewReportOf:${JSON.stringify({
        report: `page.financialAnalysis.reportAdded:${JSON.stringify({
          report: 'page.financialAnalysis.reportTypeForCounterparty:{"type":"Credit Memo","counterparty":"Pinebrook Capital"}',
          date: formatDateTimeSeconds('2026-10-01T09:30:00'),
        })}`,
      })}`,
    )
  })

  it('names the report by its type alone when it has no counterparty or date', () => {
    const [button] = viewReportButtons([{ id: 'r2', workflowTypeId: 'bank-analysis', workflowTypeName: 'Bank Analysis' }])

    expect(button!.attributes('aria-label')).toBe('page.financialAnalysis.viewReportOf:{"report":"Bank Analysis"}')
  })

  it('keeps the visible text as the name when the report has no type to add', () => {
    const [button] = viewReportButtons([{ id: 'r3', counterpartyName: 'Pinebrook Capital' }])

    expect(button!.attributes('aria-label')).toBeUndefined()
    expect(button!.text()).toBe('page.financialAnalysis.viewReport')
  })
})

describe('financialAnalysis table headers', () => {
  it('takes every column caption from the locale', () => {
    const wrapper = mount(
      defineComponent({
        setup() {
          const { columns } = useFinancialAnalysisTable()
          return () => h(DataTable, { columns, data: [] })
        },
      }),
    )

    expect(wrapper.findAll('thead th').map(th => th.text())).toEqual([
      'page.financialAnalysis.columns.analystName',
      'page.financialAnalysis.columns.counterparty',
      'page.financialAnalysis.columns.type',
      'page.financialAnalysis.columns.status',
      'page.financialAnalysis.columns.dateAdded',
      'page.financialAnalysis.columns.duration',
      'page.financialAnalysis.columns.actions',
    ])
  })
})
