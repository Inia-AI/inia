import { defineComponent, h, ref } from 'vue'
import { flushPromises, mount } from '@vue/test-utils'
import { createMemoryHistory, createRouter } from 'vue-router'
import { withReportsListExtensions } from '@/modules/extensionPoints'

/** The page of reports that the mocked list query below returns. */
export const reportRows = ref<unknown>({ value: [], count: 0 })

/** The query parameters the reports list last asked for. */
export const reportParams: { value: unknown } = { value: null }

export const apiModule = {
  default: {
    Workflows: {
      useGetWorkflowTypeOptions: () => ({
        data: ref([{ workflowTypeId: 'PrivateEquityMonitoring', workflowTypeName: 'Private Equity' }]),
      }),
    },
    Counterparties: { useGetCounterparties: () => ({ data: ref({ value: [] }) }) },
    Users: {
      useGetUsers: () => ({ data: ref({ value: [] }) }),
      useResolveUsers: () => ({ data: ref([]) }),
    },
    WorkflowReports: {
      useGetWorkflowReports: (params: unknown) => {
        reportParams.value = params
        return { data: reportRows, isLoading: ref(false) }
      },
    },
  },
}

export const keyEchoI18nModule = <T extends object>(actual: T) => ({
  ...actual,
  useI18n: () => ({ t: (key: string) => key }),
})

export const namedCombobox = defineComponent({
  setup: (_, { attrs }) => () =>
    h('button', { role: 'combobox', 'aria-label': attrs['aria-label'] }, attrs.placeholder as string | undefined),
})

/** Imports the reports list and its registered extensions; a cold import nearly fills a test's 5 s timeout. */
export const loadReportsList = () => withReportsListExtensions(import('@/views/WorkflowReports.vue'))

/** Mounts the reports list as its route does, with every reports-list extension registered so far. */
export const mountReportsList = async (
  path = '/workflow-reports',
  { realTable = false, stubs = {} as Record<string, boolean> } = {},
) => {
  const view = await loadReportsList()
  const router = createRouter({
    history: createMemoryHistory(),
    routes: [
      { path: '/workflow-reports', component: view, name: 'WorkflowReports' },
      { path: '/workflow-reports/create', component: { template: '<div/>' }, name: 'CreateWorkflow' },
      { path: '/workflow-reports/:id/:action', component: { template: '<div/>' }, name: 'WorkflowDetails' },
    ],
  })
  router.push(path)
  await router.isReady()
  const wrapper = mount(view, {
    global: {
      plugins: [router],
      stubs: {
        ...(realTable ? {} : { DataTable: true }),
        IniaIcon: true,
        IniaLoader: true,
        MultiSelect: namedCombobox,
        IniaSelect: namedCombobox,
        ...stubs,
      },
    },
  })
  await flushPromises()
  return wrapper
}
