import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { flushPromises, mount, type VueWrapper } from '@vue/test-utils'
import { createI18n } from 'vue-i18n'
import { createMemoryHistory, createRouter, type Router } from 'vue-router'
import { EN } from '@/assets/locales/en'

const { logsRef, executionRef, operator, refetchLogsMock } = vi.hoisted(() => ({
  logsRef: { value: null as unknown },
  refetchLogsMock: vi.fn(),
  executionRef: { value: null as unknown },
  operator: { value: true },
}))

vi.mock('@/api', async () => {
  const { ref: vueRef } = await vi.importActual<typeof import('vue')>('vue')
  logsRef.value = vueRef([])
  executionRef.value = vueRef(undefined)
  return {
    default: {
      WorkflowExecutions: {
        useGetWorkflowExecutionLogs: () => ({
          data: logsRef.value,
          isLoading: vueRef(false),
          refetch: refetchLogsMock,
        }),
        getLogDetail: vi.fn(),
        getLogPlaceholders: vi.fn(),
      },
      WorkflowReports: {
        useGetWorkflowReportById: () => ({
          data: executionRef.value,
          isLoading: vueRef(false),
        }),
      },
    },
  }
})

vi.mock('@/composables/useCurrentUserRoles', () => ({
  useCurrentUserRoles: () => ({
    isSuperAdmin: { value: false },
    isOperator: operator,
    isValidationRunner: { value: false },
  }),
}))

vi.mock('@/composables/useDownloadExecutedWorkflow', async () => {
  const { ref: vueRef } = await vi.importActual<typeof import('vue')>('vue')
  return {
    useDownloadExecutedWorkflow: () => ({ download: vi.fn(), isPending: vueRef(false) }),
  }
})

import API from '@/api'
import { WorkflowState } from '@/enums'
import WorkflowExecutionLogs from './WorkflowExecutionLogs.vue'
import { withDocumentTitle } from '@/test/helpers/documentTitle'

const REPORT_ID = 'exec-1'
const LOGS_PATH = `/workflow-reports/${REPORT_ID}/logs`
const REPORT_PATH = `/workflow-reports/${REPORT_ID}/edit`

const i18n = createI18n({ legacy: false, locale: 'en', messages: { en: EN } })

const setLogs = (...entries: Record<string, unknown>[]) => {
  ;(logsRef.value as { value: unknown[] }).value = entries
}

const setReport = (report: Record<string, unknown> | undefined) => {
  ;(executionRef.value as { value: unknown }).value = report
}

const log = (overrides: Record<string, unknown> = {}) => ({
  id: 'log-1',
  blockName: 'Convert Date',
  sectionName: 'PREP',
  status: 'Completed',
  shouldDisplayInLogs: true,
  createdAt: '2026-06-16T19:12:00Z',
  ...overrides,
})

let router: Router
let wrapper: VueWrapper | null = null

const LOG_ITEM_STUB = {
  props: ['log'],
  template: '<div class="log-item">{{ log.blockName }}</div>',
}

const openLogs = async (history: string[] = [LOGS_PATH], { realLogItems = false } = {}) => {
  router = createRouter({
    history: createMemoryHistory(),
    routes: [
      { path: '/workflow-reports', name: 'WorkflowReports', component: { template: '<div/>' } },
      { path: '/workflow-reports/:id/logs', name: 'ExecutionLogs', component: WorkflowExecutionLogs, meta: { title: 'page.workflowExecutionLogs.title' } },
      { path: '/workflow-reports/:id/:action', name: 'WorkflowDetails', component: { template: '<div/>' } },
    ],
  })
  for (const path of history) {
    await router.push(path)
  }
  await router.isReady()
  wrapper = mount(withDocumentTitle(WorkflowExecutionLogs), {
    global: {
      plugins: [router, i18n],
      stubs: {
        ...(realLogItems ? {} : { WorkflowExecutionLogItem: LOG_ITEM_STUB }),
        IniaIcon: true,
        IniaLoader: true,
      },
    },
  })
  return wrapper
}

beforeEach(() => {
  setReport({ id: REPORT_ID, title: 'QA graded run', counterpartyName: 'Acme Corp' })
})

afterEach(() => {
  wrapper?.unmount()
  wrapper = null
})

describe('WorkflowExecutionLogs entry visibility', () => {
  it('renders skipped blocks, which the removed default filter hid', async () => {
    setLogs(
      log({ id: 'log-1', blockName: 'Completed Block' }),
      log({ id: 'log-2', blockName: 'Skipped Block', status: 'Skipped' }),
    )

    const page = await openLogs()

    expect(page.text()).toContain('Completed Block')
    expect(page.text()).toContain('Skipped Block')
  })

  it('names each navigation entry after its block and section, so one block in two sections gets two names', async () => {
    setLogs(
      log({ id: 'log-1', blockName: 'Bank statement prep', sectionName: 'Account 1' }),
      log({ id: 'log-2', blockName: 'Bank statement prep', sectionName: 'Account 2' }),
    )

    const page = await openLogs()

    const names = page.findAll('.sidebar-log-item').map(entry => entry.text())
    expect(names).toHaveLength(2)
    expect(names[0]).toContain('Account 1')
    expect(names[1]).toContain('Account 2')
  })

  it('renders blocks the backend flags as not-for-display', async () => {
    setLogs(
      log({ id: 'log-3', blockName: 'Hidden Block', shouldDisplayInLogs: false }),
    )

    expect((await openLogs()).text()).toContain('Hidden Block')
  })

  it('renders non-terminal statuses', async () => {
    setLogs(
      log({ id: 'log-4', blockName: 'Running Block', status: 'Running' }),
      log({ id: 'log-5', blockName: 'Failed Block', status: 'Failed' }),
    )

    const page = await openLogs()

    expect(page.text()).toContain('Running Block')
    expect(page.text()).toContain('Failed Block')
  })

  it('preserves the backend ordering rather than sorting by timestamp', async () => {
    setLogs(
      log({ id: 'log-6', blockName: 'Second Authored', createdAt: '2026-06-16T19:00:00Z' }),
      log({ id: 'log-7', blockName: 'First Authored', createdAt: '2026-06-16T18:00:00Z' }),
    )

    const rendered = (await openLogs())
      .findAll('.log-item')
      .map(node => node.text())

    expect(rendered).toEqual(['Second Authored', 'First Authored'])
  })

  it('offers no visibility filter', async () => {
    setLogs(log())

    const page = await openLogs()

    expect(page.find('#logFilter').exists()).toBe(false)
    expect(page.find('select').exists()).toBe(false)
  })
})

describe('WorkflowExecutionLogs while the run goes on', () => {
  it('loads the logs again each time the run changes state', async () => {
    setReport({ id: REPORT_ID, title: 'QA graded run', workflowState: WorkflowState.Running })
    await openLogs()
    refetchLogsMock.mockClear()

    setReport({ id: REPORT_ID, title: 'QA graded run', workflowState: WorkflowState.Draft })
    await flushPromises()

    expect(refetchLogsMock).toHaveBeenCalledOnce()
  })
})

describe('WorkflowExecutionLogs workflow download', () => {
  afterEach(() => {
    operator.value = true
  })

  it('offers the workflow download to an operator', async () => {
    setLogs(log())

    expect((await openLogs()).find('.download-executed-workflow-btn').exists()).toBe(true)
  })

  it('offers no workflow download to anyone else', async () => {
    operator.value = false
    setLogs(log())

    expect((await openLogs()).find('.download-executed-workflow-btn').exists()).toBe(false)
  })
})

describe('WorkflowExecutionLogs header', () => {
  const header = (page: VueWrapper) => page.get('.page-header-with-action')
  const backToReport = (page: VueWrapper) => page.findAll('a').find(link => link.text() === 'Back to report')

  it('names its report under the Execution Logs heading', async () => {
    const page = await openLogs()

    expect(header(page).get('h1').text()).toBe('Execution Logs')
    expect(header(page).text()).toContain('QA graded run')
  })

  it('names the report by its workflow type when it has no title', async () => {
    setReport({ id: REPORT_ID, workflowTypeName: 'SME Credit Report' })

    expect(header(await openLogs()).text()).toContain('SME Credit Report')
  })

  it.each([
    ['opened by address', [LOGS_PATH]],
    ['opened from the report', [REPORT_PATH, LOGS_PATH]],
    ['opened from another page', ['/workflow-reports', LOGS_PATH]],
  ])('offers Back to the report page when %s', async (_, history) => {
    const back = backToReport(await openLogs(history))

    expect(back).toBeDefined()
    expect(back!.attributes('href')).toBe(REPORT_PATH)

    await back!.trigger('click')
    await flushPromises()

    expect(router.currentRoute.value.fullPath).toBe(REPORT_PATH)
  })

  it('offers Back before the report has loaded', async () => {
    setReport(undefined)

    expect(backToReport(await openLogs())!.attributes('href')).toBe(REPORT_PATH)
  })
})

describe('WorkflowExecutionLogs browser tab title', () => {
  it('reads Execution Logs until the report has loaded, then names the report', async () => {
    setReport(undefined)

    await openLogs()

    expect(document.title).toBe('Execution Logs · Inia AI')

    setReport({ id: REPORT_ID, title: 'QA graded run' })
    await flushPromises()

    expect(document.title).toBe('QA graded run · Execution Logs · Inia AI')
  })
})

describe('WorkflowExecutionLogs prompt with no preview', () => {
  it('says no prompt preview was recorded when the platform sends none, and asks for it once', async () => {
    const getLogDetail = vi.mocked(API.WorkflowExecutions.getLogDetail)
    getLogDetail.mockClear()
    getLogDetail.mockResolvedValue({ id: 'log-1', inputs: {}, prompt: undefined, output: undefined })
    setLogs(log({ id: 'log-1', blockName: 'Balance sheet', blockType: 'AiQuery', hasPrompt: true }))
    const page = await openLogs([LOGS_PATH], { realLogItems: true })
    const toggle = () => page.get('button[aria-controls$="-prompt"]')
    const region = () => page.get(`#${toggle().attributes('aria-controls')}`)

    await toggle().trigger('click')
    await flushPromises()

    expect(region().text()).toBe('No prompt preview was recorded for this block.')

    await toggle().trigger('click')
    await toggle().trigger('click')
    await flushPromises()

    expect(region().text()).toBe('No prompt preview was recorded for this block.')
    expect(getLogDetail).toHaveBeenCalledTimes(1)
  })
})
