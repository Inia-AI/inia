import { beforeAll, describe, expect, it, vi, beforeEach } from 'vitest'
import { flushPromises } from '@vue/test-utils'
import { loadReportsList, mountReportsList, reportParams, reportRows } from '@/test/helpers/reportsListMocks'

vi.mock('@/api', async () => (await import('@/test/helpers/reportsListMocks')).apiModule)
vi.mock('vue-i18n', async importOriginal =>
  (await import('@/test/helpers/reportsListMocks')).keyEchoI18nModule(await importOriginal<typeof import('vue-i18n')>()),
)

const emptyState = (wrapper: Awaited<ReturnType<typeof mountReportsList>>) =>
  wrapper.find('[data-testid="reports-empty"]')

describe('WorkflowReports.vue', () => {
  beforeAll(async () => {
    await loadReportsList()
  })

  beforeEach(() => {
    reportRows.value = { value: [], count: 0 }
  })

  it('names every filter combobox after its caption', async () => {
    const wrapper = await mountReportsList()

    expect(wrapper.findAll('[role="combobox"]').map(box => box.attributes('aria-label'))).toEqual([
      'page.financialAnalysis.filters.reportType',
      'page.financialAnalysis.filters.counterparty',
      'page.financialAnalysis.filters.user',
    ])
  })

  it('says each filter is set to all when nothing is chosen', async () => {
    const wrapper = await mountReportsList()

    expect(wrapper.findAll('[role="combobox"]').map(box => box.text())).toEqual([
      'page.financialAnalysis.filters.all',
      'page.financialAnalysis.filters.all',
      'page.financialAnalysis.filters.all',
    ])
  })

  it('says nothing was ever created only when no filter is applied', async () => {
    const wrapper = await mountReportsList()

    expect(emptyState(wrapper).text()).toContain('page.financialAnalysis.emptyFinancialAnalysesText')
    expect(wrapper.find('[data-testid="reports-clear-filters"]').exists()).toBe(false)
  })

  it('says the filters matched nothing, and offers to clear them, when a filter is applied', async () => {
    const wrapper = await mountReportsList('/workflow-reports?type=PrivateEquityMonitoring')

    expect(emptyState(wrapper).text()).toContain('page.financialAnalysis.emptyFilteredText')
    expect(emptyState(wrapper).text()).not.toContain('page.financialAnalysis.emptyFinancialAnalysesText')

    await wrapper.find('[data-testid="reports-clear-filters"]').trigger('click')
    await flushPromises()

    expect(emptyState(wrapper).text()).toContain('page.financialAnalysis.emptyFinancialAnalysesText')
  })

  it('names each row type from the type options, since the list payload carries only the id', async () => {
    reportRows.value = {
      value: [
        { id: 'r1', title: '', workflowState: 'Draft', workflowTypeId: 'PrivateEquityMonitoring' },
        { id: 'r2', title: '', workflowState: 'Draft', workflowTypeId: 'RetiredType' },
      ],
      count: 2,
    }
    const wrapper = await mountReportsList()

    const rows = wrapper.findComponent({ name: 'DataTable' }).props('data') as { workflowTypeName?: string }[]
    expect(rows.map(row => row.workflowTypeName)).toEqual(['Private Equity', 'RetiredType'])
  })

  it('names each row link by the report type, so the link sits in the Type cell', async () => {
    reportRows.value = {
      value: [{ id: 'r1', title: '', workflowState: 'Draft', workflowTypeId: 'PrivateEquityMonitoring' }],
      count: 1,
    }
    const wrapper = await mountReportsList('/workflow-reports', { realTable: true })

    const link = wrapper.get('tbody a[href="/workflow-reports/r1/edit"]')
    const headers = wrapper.findAll('thead th').map(th => th.text())
    const cells = wrapper.findAll('tbody tr:first-child td')
    expect(link.text()).toBe('Private Equity')
    expect(cells[headers.indexOf('page.financialAnalysis.columns.type')]!.element.contains(link.element)).toBe(true)
  })

  it('ignores a filter no registered module offers, and shows no column of its own', async () => {
    reportRows.value = {
      value: [{ id: 'r1', title: '', workflowState: 'Draft', workflowTypeId: 'PrivateEquityMonitoring', isBaseline: true }],
      count: 1,
    }
    const wrapper = await mountReportsList('/workflow-reports?baseline=true', { realTable: true })

    expect(wrapper.findAll('thead th').map(th => th.text())).not.toContain('Baseline')
    expect(wrapper.find('[data-star-state]').exists()).toBe(false)
    expect((reportParams.value as { value: { $filter?: string } }).value.$filter).toBeUndefined()
  })
})
