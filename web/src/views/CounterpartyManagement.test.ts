/**
 * Wiring tests for the counterparty page's export and import controls. The page's list,
 * filter and tree behaviour is covered by the pure-function tests under `lib/`.
 */
import { describe, expect, it, vi, beforeEach } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import type { Ref } from 'vue'
import { dialogStubs } from '@/test/helpers/rekaStubs'

const { rolesRef } = vi.hoisted(() => ({
  rolesRef: { isAdmin: null as Ref<boolean> | null, isEditor: null as Ref<boolean> | null },
}))
const { exportMock, downloadMock, toastMock } = vi.hoisted(() => ({
  exportMock: vi.fn(),
  downloadMock: vi.fn(),
  toastMock: vi.fn(),
}))
const { counterpartyQueries, getDetailsMock, updateMock } = vi.hoisted(() => ({
  counterpartyQueries: { calls: 0, roots: [] as unknown[] },
  getDetailsMock: vi.fn(),
  updateMock: vi.fn(),
}))

vi.mock('@/api', async () => {
  const { ref: vueRef } = await vi.importActual<typeof import('vue')>('vue')
  const idle = () => ({ data: vueRef(null), isLoading: vueRef(false) })
  const mutation = () => ({ mutateAsync: vi.fn(), isPending: vueRef(false) })
  return {
    default: {
      Counterparties: {
        useGetCounterparties: () =>
          counterpartyQueries.calls++ === 0
            ? { data: vueRef({ value: counterpartyQueries.roots, count: counterpartyQueries.roots.length }), isLoading: vueRef(false) }
            : idle(),
        useGetCounterparty: () => ({ mutateAsync: getDetailsMock, isPending: vueRef(false) }),
        useCreateCounterparty: mutation,
        updateCounterparty: () => ({ mutateAsync: updateMock, isPending: vueRef(false) }),
        deleteCounterparty: mutation,
        useExportBundle: () => ({
          mutateAsync: exportMock,
          isPending: vueRef(false),
        }),
        useImportBundle: mutation,
      },
      CounterpartyTypes: {
        useGetCounterpartyTypes: () => ({
          data: vueRef({ value: [], count: 0 }),
        }),
      },
    },
  }
})

vi.mock('@/lib/downloadFile', () => ({
  downloadBlob: downloadMock,
  readBlobProblemDetail: () => Promise.resolve(undefined),
  parseFilenameFromContentDisposition: () => null,
}))

vi.mock('vue-i18n', async importOriginal => {
  const actual = await importOriginal<typeof import('vue-i18n')>()
  return {
    ...actual,
    useI18n: () => ({
      t: (key: string, params?: Record<string, unknown>) =>
        params ? `${key}:${JSON.stringify(params)}` : key,
    }),
  }
})

vi.mock('@/components/ui/toast/use-toast', () => ({
  useToast: () => ({ toast: toastMock }),
}))

vi.mock('@/composables/useCurrentUserRoles', async () => {
  const { ref: vueRef } = await vi.importActual<typeof import('vue')>('vue')
  rolesRef.isAdmin = vueRef(true)
  rolesRef.isEditor = vueRef(false)
  return {
    useCurrentUserRoles: () => ({
      isAdmin: rolesRef.isAdmin,
      isSuperAdmin: vueRef(false),
      isValidationRunner: vueRef(false),
      isEditor: rolesRef.isEditor,
    }),
  }
})

vi.mock('vue-router', async () => {
  const actual =
    await vi.importActual<typeof import('vue-router')>('vue-router')
  return {
    ...actual,
    useRoute: () => ({ query: {} }),
    useRouter: () => ({ push: vi.fn(), replace: vi.fn() }),
  }
})

import CounterpartyManagement from './CounterpartyManagement.vue'
import counterpartyMessages from '@/assets/locales/en/page/counterpartyManagement.json'

const ImportDialogStub = {
  name: 'ImportCounterpartiesDialog',
  props: { bundle: Object },
  template:
    '<div v-if="bundle" data-testid="import-dialog">{{ bundle.sourceOrganizationName }}</div>',
}

const mountView = async (stubOverrides: Record<string, boolean> = {}) => {
  const wrapper = mount(CounterpartyManagement, {
    global: {
      stubs: {
        ...dialogStubs,
        ImportCounterpartiesDialog: ImportDialogStub,
        MultiSelect: true,
        DataTable: true,
        CounterpartyForm: true,
        IniaIcon: true,
        IniaLoader: true,
        RouterLink: { props: ['to'], template: '<a :href="to"><slot /></a>' },
        ...stubOverrides,
      },
    },
  })
  await flushPromises()
  return wrapper
}

const pick = async (
  wrapper: Awaited<ReturnType<typeof mountView>>,
  file: File,
) => {
  const input = wrapper.find('[data-testid="counterparties-import-input"]')
  Object.defineProperty(input.element, 'files', {
    value: [file],
    configurable: true,
  })
  await input.trigger('change')
  await flushPromises()
}

const bundleFile = () =>
  new File(
    [
      JSON.stringify({
        bundleFormatVersion: 1,
        sourceInstance: 'acme-prod',
        sourceOrganizationId: 'Acme',
        sourceOrganizationName: 'Acme',
        exportedAt: '2026-09-16T12:05:31Z',
        counterpartyTypes: [],
        counterparties: [],
      }),
    ],
    'counterparties Acme 2026-09-16.json',
    { type: 'application/json' },
  )

describe('CounterpartyManagement.vue bundles', () => {
  beforeEach(() => {
    counterpartyQueries.calls = 0
    counterpartyQueries.roots = []
    rolesRef.isAdmin!.value = true
    rolesRef.isEditor!.value = false
    exportMock.mockReset()
    downloadMock.mockReset()
    toastMock.mockReset()
  })

  it('exports the whole organization and downloads what the server named the file', async () => {
    exportMock.mockResolvedValue({
      blob: new Blob(['{}']),
      filename: 'counterparties Inia AI 2026-09-16.json',
    })

    const wrapper = await mountView()
    await wrapper.find('[data-testid="counterparties-export"]').trigger('click')
    await flushPromises()

    expect(exportMock).toHaveBeenCalledOnce()
    expect(downloadMock).toHaveBeenCalledWith(
      expect.any(Blob),
      'counterparties Inia AI 2026-09-16.json',
    )
  })

  it('offers the counterparty types page to an editor only', async () => {
    const wrapper = await mountView()
    expect(wrapper.find('[data-testid="manage-counterparty-types"]').exists()).toBe(false)

    rolesRef.isEditor!.value = true
    await flushPromises()

    expect(wrapper.find('[data-testid="manage-counterparty-types"]').attributes('href')).toBe('/counterparty-types')
  })

  it('reads the picked file and hands the bundle to the import dialog', async () => {
    const wrapper = await mountView()
    await pick(wrapper, bundleFile())

    const dialog = wrapper.find('[data-testid="import-dialog"]')
    expect(dialog.exists()).toBe(true)
    expect(dialog.text()).toBe('Acme')
  })

  // A file that is not the export's JSON is refused before any request, naming the file so the
  // admin who picked the wrong one sees which.
  it('refuses a file that is not a counterparty bundle', async () => {
    const wrapper = await mountView()
    await pick(
      wrapper,
      new File(['not json'], 'notes.txt', { type: 'text/plain' }),
    )

    expect(wrapper.find('[data-testid="import-dialog"]').exists()).toBe(false)
    expect(toastMock).toHaveBeenCalledWith(
      expect.objectContaining({
        variant: 'destructive',
        description:
          'page.counterpartyManagement.importNotABundle:{"name":"notes.txt"}',
      }),
    )
  })

  it('refuses JSON that is not shaped like a bundle', async () => {
    const wrapper = await mountView()
    await pick(
      wrapper,
      new File(
        [JSON.stringify({ bundleFormatVersion: 1, counterparties: [] })],
        'half.json',
        {
          type: 'application/json',
        },
      ),
    )

    expect(wrapper.find('[data-testid="import-dialog"]').exists()).toBe(false)
    expect(toastMock).toHaveBeenCalledWith(
      expect.objectContaining({
        description:
          'page.counterpartyManagement.importNotABundle:{"name":"half.json"}',
      }),
    )
  })

  it('hides both controls from users who are not admins', async () => {
    rolesRef.isAdmin!.value = false

    const wrapper = await mountView()

    expect(wrapper.find('[data-testid="counterparties-export"]').exists()).toBe(
      false,
    )
    expect(wrapper.find('[data-testid="counterparties-import"]').exists()).toBe(
      false,
    )
    expect(
      wrapper.find('[data-testid="counterparties-import-input"]').exists(),
    ).toBe(false)
  })
})

describe('CounterpartyManagement.vue filters', () => {
  it('says the type filter is set to all when no type is chosen', async () => {
    const wrapper = await mountView()

    expect(wrapper.findComponent({ name: 'MultiSelect' }).props('placeholder')).toBe(
      'page.counterpartyManagement.filters.all',
    )
  })
})

describe('CounterpartyManagement.vue edit', () => {
  const FUND = { id: 'fund-1', name: 'Pinebrook Capital', nameVariants: '', counterpartyTypeIds: [] }

  beforeEach(() => {
    counterpartyQueries.calls = 0
    counterpartyQueries.roots = [FUND]
    getDetailsMock.mockReset().mockResolvedValue({ ...FUND, properties: {} })
    updateMock.mockReset().mockResolvedValue(undefined)
    toastMock.mockReset()
  })

  it('names the saved name in the success toast after a rename', async () => {
    const wrapper = await mountView({ DataTable: false })

    await wrapper.get(`[aria-label='page.counterpartyManagement.actions.edit:{"name":"Pinebrook Capital"}']`).trigger('click')
    await flushPromises()
    wrapper.findComponent({ name: 'CounterpartyForm' }).vm.$emit('submit', { ...FUND, name: 'Pinebrook Capital Partners' })
    await flushPromises()

    expect(updateMock).toHaveBeenCalledWith(
      expect.objectContaining({ id: 'fund-1', params: expect.objectContaining({ name: 'Pinebrook Capital Partners' }) }),
    )
    expect(toastMock).toHaveBeenCalledWith(
      expect.objectContaining({
        description: 'page.counterpartyManagement.toast.editSuccess:{"name":"Pinebrook Capital Partners"}',
      }),
    )
  })

  it('warns that a delete takes every counterparty beneath the one deleted', async () => {
    const wrapper = await mountView({ DataTable: false })

    await wrapper.get(`[aria-label='page.counterpartyManagement.actions.delete:{"name":"Pinebrook Capital"}']`).trigger('click')
    await flushPromises()

    expect(wrapper.text()).toContain('page.counterpartyManagement.dialog.deleteConfirm:{"name":"Pinebrook Capital"}')
    expect(counterpartyMessages.dialog.deleteConfirm).toBe(
      "Delete {name}? Every @:{'terms.company'} beneath it is deleted too.",
    )
  })
})
