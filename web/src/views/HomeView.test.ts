/**
 * Desk idle-state tests. The two quiet flavours must read differently: a desk
 * with reports waiting for review points at the review pile, a genuinely empty
 * desk invites the next report. Copy assertions live on the i18n keys — the
 * strings themselves are covered by the locale compile test.
 */
import { afterEach, describe, expect, it, vi, beforeEach } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { CAPABILITIES } from '@/constants/capabilities'
import { makeCapabilitiesVary, resetCapabilities, without } from '@/test/helpers/capabilities'

const { pushMock } = vi.hoisted(() => ({ pushMock: vi.fn() }))
const activeTenants = vi.hoisted(() => ({ list: [] as { id: string; name: string; capabilities: string[] }[] }))
const { inProgressRef, reviewDocketRef, categoriesRef, validationSpies } = vi.hoisted(() => ({
  inProgressRef: { value: null as unknown },
  reviewDocketRef: { value: null as unknown },
  categoriesRef: { value: null as unknown },
  validationSpies: { useHealth: vi.fn(), useThresholds: vi.fn() },
}))

vi.mock('@/api', async () => {
  const { ref: vueRef } = await vi.importActual<typeof import('vue')>('vue')
  inProgressRef.value = vueRef(null)
  reviewDocketRef.value = vueRef(null)
  categoriesRef.value = vueRef([])

  return {
    default: {
      WorkflowReports: {
        useGetWorkflowReports: (params: { value: { $filter?: string } }) => ({
          data: params.value.$filter?.includes("'Draft'")
            ? reviewDocketRef.value
            : inProgressRef.value,
          isLoading: vueRef(false),
        }),
        useDeleteWorkflowReport: () => ({ mutateAsync: vi.fn() }),
        service: { getWorkflowReport: vi.fn() },
      },
      Users: {
        useResolveUsers: () => ({ data: vueRef([]) }),
        useGetTenants: () => ({ data: vueRef(activeTenants.list), isPending: vueRef(false) }),
      },
      Counterparties: {
        useGetCounterparties: () => ({ data: vueRef(null) }),
      },
      WorkflowCategories: {
        useGetWorkflowCategorySummaries: () => ({ data: categoriesRef.value }),
      },
      Workflows: {
        useGetWorkflowTypeOptions: () => ({
          data: vueRef([{ workflowTypeId: 'qa-fixture', workflowTypeName: 'QA fixture' }]),
        }),
      },
      Validation: validationSpies,
      WorkflowExecutions: {
        useCreateWorkflowExecution: () => ({
          mutateAsync: vi.fn(),
          isPending: vueRef(false),
        }),
      },
    },
  }
})

vi.mock('vue-i18n', async importOriginal => {
  const actual = await importOriginal<typeof import('vue-i18n')>()
  const { EN } = await vi.importActual<typeof import('@/assets/locales/en')>(
    '@/assets/locales/en',
  )
  return {
    ...actual,
    useI18n: () => ({
      t: (key: string, params?: Record<string, unknown>) =>
        params ? `${key}:${JSON.stringify(params)}` : key,
      rt: (message: string) => message,
      tm: (key: string) =>
        key === 'shared.agentActivity.premiseIdleInvites'
          ? EN.shared.agentActivity.premiseIdleInvites
          : [],
    }),
  }
})

vi.mock('vue-router', () => ({
  useRouter: () => ({ push: pushMock }),
  RouterLink: { props: ['to'], template: '<a><slot /></a>' },
}))

vi.mock('@tanstack/vue-query', () => ({
  useQueries: () => ({ value: [] }),
  useQueryClient: () => ({
    fetchQuery: vi.fn(),
    invalidateQueries: vi.fn(),
  }),
}))

vi.mock('@/components/ui/toast/use-toast', () => ({
  useToast: () => ({ toast: vi.fn() }),
}))

vi.mock('@/composables/useCancelExecution', async () => {
  const { ref: vueRef } = await vi.importActual<typeof import('vue')>('vue')
  return {
    useCancelExecution: () => ({
      prompt: vi.fn(),
      confirm: vi.fn(),
      isPending: vueRef(false),
      showDialog: vueRef(false),
    }),
    isCancellableState: () => false,
  }
})

vi.mock('@/lib/utils', async importOriginal => {
  const actual = await importOriginal<typeof import('@/lib/utils')>()
  return {
    ...actual,
    getDecodedToken: () => null,
  }
})

import HomeView from './HomeView.vue'
import useAgentActivityStore, { type AgentActivityReport } from '@/store/agentActivity'
import { EN } from '@/assets/locales/en'

const state = () => ({
  inProgress: inProgressRef.value as { value: unknown },
  reviewDocket: reviewDocketRef.value as { value: unknown },
})

const odata = (rows: unknown[], count = rows.length) => ({
  count,
  skip: 0,
  top: 10,
  value: rows,
})

const DRAFT_ROW = {
  id: 'draft-1',
  title: 'Q2 Credit Review',
  workflowState: 'Draft',
  analystName: 'jane',
  createdAt: '2026-08-21T09:00:00Z',
  duration: '00:03:00',
}

const RUNNING_ROW = {
  id: 'run-1',
  title: 'Acme Refresh',
  workflowState: 'Running',
  analystName: 'jane',
  createdAt: '2026-08-21T10:00:00Z',
  startedAt: '2026-08-21T10:00:05Z',
}

const mountView = async (stubOverrides: Record<string, boolean> = {}) => {
  const wrapper = mount(HomeView, {
    global: {
      plugins: [createPinia()],
      stubs: {
        ConfirmDialog: true,
        CancelExecutionDialog: true,
        ReRunChoiceDialog: true,
        ReportCardActions: true,
        AgentPresenceLine: true,
        IniaLoader: true,
        IniaIcon: true,
        LiveDuration: true,
        ...stubOverrides,
      },
    },
  })
  await flushPromises()
  return wrapper
}

beforeEach(() => {
  pushMock.mockReset()
  state().inProgress.value = odata([])
  state().reviewDocket.value = odata([])
  ;(categoriesRef.value as { value: unknown[] }).value = []
  validationSpies.useHealth.mockReset()
  validationSpies.useThresholds.mockReset()
})

describe('HomeView.vue report tiles', () => {
  const menuLabelOf = (row: { find: (selector: string) => { attributes: (name: string) => string | undefined } }) =>
    row.find('button.options-trigger').attributes('aria-label')

  it('names the workflow type on a ready-for-review row whose list payload carries only a type id', async () => {
    state().reviewDocket.value = odata([
      { ...DRAFT_ROW, title: '', workflowTypeId: 'qa-fixture', counterpartyName: 'QA Counterparty' },
    ])
    const wrapper = await mountView({ ReportCardActions: false })

    const row = wrapper.find('.docket-row')
    expect(row.find('.docket-name').text()).toBe('QA Counterparty')
    expect(row.find('.docket-receipt').text()).toContain('QA fixture')
    expect(menuLabelOf(row)).toBe('shared.components.reportCardActions.menuAria:{"title":"QA fixture"}')
  })

  it('names the workflow type on a running row whose list payload carries only a type id', async () => {
    state().inProgress.value = odata([
      { ...RUNNING_ROW, title: '', workflowTypeId: 'qa-fixture', counterpartyName: 'QA Counterparty' },
    ])
    const wrapper = await mountView({ ReportCardActions: false })

    const row = wrapper.find('.ledger-row')
    expect(row.find('.row-brief').text()).toBe('QA fixture')
    expect(menuLabelOf(row)).toBe('shared.components.reportCardActions.menuAria:{"title":"QA fixture"}')
  })

  it('falls back to the type id, as the Reports page does, when the type is not offered', async () => {
    state().reviewDocket.value = odata([
      { ...DRAFT_ROW, title: '', workflowTypeId: 'RetiredType', counterpartyName: 'QA Counterparty' },
    ])
    const wrapper = await mountView({ ReportCardActions: false })

    const row = wrapper.find('.docket-row')
    expect(row.find('.docket-receipt').text()).toContain('RetiredType')
    expect(menuLabelOf(row)).toBe('shared.components.reportCardActions.menuAria:{"title":"RetiredType"}')
  })

  it('keeps the report title when the report has one', async () => {
    state().reviewDocket.value = odata([
      { ...DRAFT_ROW, workflowTypeId: 'qa-fixture', counterpartyName: 'QA Counterparty' },
    ])
    const wrapper = await mountView({ ReportCardActions: false })

    const row = wrapper.find('.docket-row')
    expect(row.find('.docket-receipt').text()).toContain('Q2 Credit Review')
    expect(menuLabelOf(row)).toBe('shared.components.reportCardActions.menuAria:{"title":"Q2 Credit Review"}')
  })
})

describe('HomeView.vue idle states', () => {
  const premiseOf = (wrapper: {
    find: (selector: string) => { text: () => string }
  }) => wrapper.find('.brief-status').text()

  it('invites the next report even when reports wait for review', async () => {
    state().reviewDocket.value = odata([DRAFT_ROW], 5)
    const wrapper = await mountView()

    expect(EN.shared.agentActivity.premiseIdleInvites).toContain(
      premiseOf(wrapper),
    )

    const idleCard = wrapper.find('.strata-idle-card')
    expect(idleCard.exists()).toBe(true)
    expect(idleCard.text()).toContain(
      'shared.agentActivity.desk.emptyDeskInvite',
    )
    expect(idleCard.text()).not.toContain('nothingInProgress')
  })

  it('invites the next report when the desk is genuinely quiet', async () => {
    const wrapper = await mountView()

    expect(EN.shared.agentActivity.premiseIdleInvites).toContain(
      premiseOf(wrapper),
    )

    const idleCard = wrapper.find('.strata-idle-card')
    expect(idleCard.exists()).toBe(true)
    expect(idleCard.text()).toContain(
      'shared.agentActivity.desk.emptyDeskInvite',
    )
    expect(idleCard.find('button').exists()).toBe(false)

    expect(wrapper.text()).toContain('shared.agentActivity.desk.inProgress')
    expect(wrapper.text()).toContain('shared.agentActivity.desk.readyForReview')
  })

  it('keeps the empty review line inside the docket panel', async () => {
    const wrapper = await mountView()

    const empty = wrapper.find('.docket > .strata-empty')
    expect(empty.exists()).toBe(true)
    expect(empty.text()).toContain('shared.agentActivity.desk.noReportsReady')
  })

  it('defaults to the personal view, listed before the team view', async () => {
    const wrapper = await mountView()

    const pills = wrapper.findAll('.view-pill')
    expect(pills[0].attributes('aria-label')).toBe(
      'page.home.viewToggle.personalView',
    )
    expect(pills[1].attributes('aria-label')).toBe(
      'page.home.viewToggle.teamView',
    )
    expect(pills[0].classes()).toContain('active')
    expect(pills[1].classes()).not.toContain('active')
  })

  it('offers the two views as a named pair of pressed buttons, not as tabs without panels', async () => {
    const wrapper = await mountView()

    const group = wrapper.get('.view-toggle-pills')
    expect(group.attributes('role')).toBe('group')
    expect(group.attributes('aria-label')).toBe('page.home.viewToggle.label')
    const pills = group.findAll('button')
    expect(pills.map(pill => pill.attributes('role'))).toEqual([undefined, undefined])
    expect(pills.map(pill => pill.attributes('aria-pressed'))).toEqual(['true', 'false'])

    await pills[1].trigger('click')

    expect(pills.map(pill => pill.attributes('aria-pressed'))).toEqual(['false', 'true'])
  })

  it('drops the idle card while reports are running', async () => {
    state().inProgress.value = odata([RUNNING_ROW])
    const wrapper = await mountView()

    expect(wrapper.find('.strata-idle-card').exists()).toBe(false)
    expect(wrapper.find('.brief-status').text()).toContain(
      'shared.agentActivity.premiseReports',
    )
  })
})

describe('HomeView.vue without a paid module', () => {
  it('shows no regression health and asks the validation API nothing', async () => {
    const wrapper = await mountView()

    expect(wrapper.find('#strata-health').exists()).toBe(false)
    expect(validationSpies.useHealth).not.toHaveBeenCalled()
    expect(validationSpies.useThresholds).not.toHaveBeenCalled()
  })
})

describe('HomeView.vue run progress', () => {
  const SECTION_ACTIVITY = {
    schemaVersion: 1,
    currentStepIndex: 1,
    steps: [
      { label: 'Reading the filings', status: 'Done' },
      { label: 'Extracting the statements', status: 'Active' },
    ],
  }
  const AGENTS = [{ blockId: 'is', blockName: 'Income Statement', settled: false, activity: null }]

  const mountWithActivity = async (
    report: { agentActivity?: unknown[]; workflowActivity?: unknown },
  ) => {
    const pinia = createPinia()
    setActivePinia(pinia)
    const store = useAgentActivityStore()
    store.ingestReport({ ...RUNNING_ROW, ...report } as AgentActivityReport)
    state().inProgress.value = odata([RUNNING_ROW])
    const wrapper = mount(HomeView, {
      global: {
        plugins: [pinia],
        stubs: {
          ConfirmDialog: true,
          CancelExecutionDialog: true,
          ReRunChoiceDialog: true,
          ReportCardActions: true,
          AgentPresenceLine: true,
          WorkflowActivityLine: true,
          IniaLoader: true,
          IniaIcon: true,
          LiveDuration: true,
        },
      },
    })
    await flushPromises()
    return { wrapper, store }
  }

  const labelsOf = (wrapper: Awaited<ReturnType<typeof mountWithActivity>>['wrapper']) =>
    wrapper.findAll('.row-progress-label').map(label => label.text())

  it('shows the sections and the agents together, each under its label', async () => {
    const { wrapper } = await mountWithActivity({ agentActivity: AGENTS, workflowActivity: SECTION_ACTIVITY })

    expect(wrapper.find('.row-classic').exists()).toBe(true)
    expect(wrapper.findAll('.row-member')).toHaveLength(1)
    expect(labelsOf(wrapper)).toEqual([
      'shared.agentActivity.desk.sectionsHeading',
      'shared.agentActivity.desk.agentsHeading',
    ])
  })

  it('shows only the agents, unlabelled, when the run has no section progress', async () => {
    const { wrapper } = await mountWithActivity({ agentActivity: AGENTS })

    expect(wrapper.find('.row-classic').exists()).toBe(false)
    expect(wrapper.findAll('.row-member')).toHaveLength(1)
    expect(labelsOf(wrapper)).toEqual([])
  })

  it('shows only the sections, unlabelled, when the run has no agents', async () => {
    const { wrapper } = await mountWithActivity({ workflowActivity: SECTION_ACTIVITY })

    expect(wrapper.find('.row-classic').exists()).toBe(true)
    expect(wrapper.find('.row-team').exists()).toBe(false)
    expect(labelsOf(wrapper)).toEqual([])
  })

  it('keeps the sections in place when agents start mid-run', async () => {
    const { wrapper, store } = await mountWithActivity({ workflowActivity: SECTION_ACTIVITY })

    store.ingestReport({
      ...RUNNING_ROW,
      agentActivity: AGENTS,
      workflowActivity: SECTION_ACTIVITY,
    } as AgentActivityReport)
    await flushPromises()

    expect(wrapper.find('.row-classic').exists()).toBe(true)
    expect(wrapper.findAll('.row-member')).toHaveLength(1)
  })
})

describe('HomeView.vue and the organization’s capabilities', () => {
  afterEach(resetCapabilities)

  it('offers no new report and no team view to an organization without run and team-view, and says why the desk is empty', async () => {
    activeTenants.list = makeCapabilitiesVary(without(CAPABILITIES.RUN, CAPABILITIES.TEAM_VIEW))

    const wrapper = await mountView()

    expect(wrapper.find('.btn-create-report').exists()).toBe(false)
    expect(wrapper.find('.view-toggle-pills').exists()).toBe(false)
    expect(wrapper.find('.strata-idle-card').text()).toContain('shared.agentActivity.desk.emptyDeskNoNewReports')
  })

  it('keeps both where no organization can lack a capability', async () => {
    const wrapper = await mountView()

    expect(wrapper.find('.btn-create-report').exists()).toBe(true)
    expect(wrapper.find('.view-toggle-pills').exists()).toBe(true)
  })
})
