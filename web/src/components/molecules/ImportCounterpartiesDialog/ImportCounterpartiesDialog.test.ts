import { describe, expect, it, vi, beforeEach } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import { dialogStubs } from '@/test/helpers/rekaStubs'

const { importMock, toastMock } = vi.hoisted(() => ({
  importMock: vi.fn(),
  toastMock: vi.fn(),
}))

vi.mock('@/api', async () => {
  const { ref: vueRef } = await vi.importActual<typeof import('vue')>('vue')
  return {
    default: {
      Counterparties: {
        useImportBundle: () => ({
          mutateAsync: importMock,
          isPending: vueRef(false),
        }),
      },
    },
  }
})

vi.mock('@/components/ui/toast/use-toast', () => ({
  useToast: () => ({ toast: toastMock, toasts: [], dismiss: () => {} }),
}))

vi.mock('vue-i18n', () => ({
  useI18n: () => ({
    t: (key: string, params?: Record<string, unknown>) =>
      params ? `${key}:${JSON.stringify(params)}` : key,
  }),
}))

import ImportCounterpartiesDialog from './ImportCounterpartiesDialog.vue'
import {
  CounterpartyImportDisposition,
  CounterpartyImportSkipReason,
  type CounterpartyBundle,
  type CounterpartyBundleEntry,
  type CounterpartyImportPlan,
  type CounterpartyImportRow,
} from '@/api/counterparties/schema'

const entry = (id: string, name: string): CounterpartyBundleEntry => ({
  id,
  name,
  parentId: null,
  nameVariants: '',
  email: null,
  phoneNumber: null,
  website: null,
  address: null,
  lastFinancialAnalysisResult: null,
  properties: null,
  counterpartyTypeNames: ['Hedge Fund'],
})

const bundle = (): CounterpartyBundle => ({
  bundleFormatVersion: 1,
  sourceInstance: 'acme-prod',
  sourceOrganizationId: 'Acme',
  sourceOrganizationName: 'Acme',
  exportedAt: '2026-09-16T12:05:31Z',
  counterpartyTypes: [{ name: 'Hedge Fund', description: null }],
  counterparties: [
    entry('a', 'Ashgrove'),
    entry('b', 'Pinebrook'),
    entry('c', 'Cedar Capital'),
  ],
})

const row = (
  sourceId: string,
  name: string,
  disposition: CounterpartyImportDisposition,
  overrides: Partial<CounterpartyImportRow> = {},
): CounterpartyImportRow => ({
  sourceId,
  name,
  disposition,
  targetId: sourceId,
  skipReason: null,
  detail: null,
  ...overrides,
})

const plan = (
  overrides: Partial<CounterpartyImportPlan> = {},
): CounterpartyImportPlan => ({
  applied: false,
  createCount: 1,
  updateByIdCount: 0,
  resurrectCount: 0,
  updateByNameCount: 1,
  unchangedCount: 0,
  skipCount: 1,
  counterpartyTypesToCreate: ['Hedge Fund'],
  rows: [
    row('a', 'Ashgrove', CounterpartyImportDisposition.Create),
    row('b', 'Pinebrook', CounterpartyImportDisposition.UpdateByName, {
      targetId: 'b-local',
    }),
    row('c', 'Cedar Capital', CounterpartyImportDisposition.Skip, {
      targetId: null,
      skipReason: CounterpartyImportSkipReason.NameCollision,
      detail: 'A live counterparty 7f2 already carries the name Cedar Capital.',
    }),
  ],
  ...overrides,
})

const allUnchanged = (): CounterpartyImportPlan =>
  plan({
    createCount: 0,
    updateByNameCount: 0,
    skipCount: 0,
    unchangedCount: 3,
    counterpartyTypesToCreate: [],
    rows: plan().rows.map(r =>
      row(r.sourceId, r.name, CounterpartyImportDisposition.Unchanged),
    ),
  })

const mountOpen = async (file = bundle()) => {
  const wrapper = mount(ImportCounterpartiesDialog, {
    props: { bundle: file },
    global: { stubs: { ...dialogStubs, IniaLoader: true } },
  })
  await flushPromises()
  return wrapper
}

describe('ImportCounterpartiesDialog', () => {
  beforeEach(() => {
    importMock.mockReset()
    toastMock.mockReset()
  })

  it('checks the file against the instance as a dry run the moment it opens, and shows the plan', async () => {
    importMock.mockResolvedValue(plan())

    const wrapper = await mountOpen()

    expect(importMock).toHaveBeenCalledWith({ bundle: bundle(), dryRun: true })
    expect(wrapper.find('[data-testid="import-source"]').text()).toContain(
      '"organization":"Acme"',
    )
    expect(wrapper.find('[data-testid="import-state"]').text()).toBe(
      'page.counterpartyManagement.importDialog.planned',
    )
    expect(
      wrapper.find('[data-testid="import-count-Create"]').text(),
    ).toContain('1')
    expect(
      wrapper.find('[data-testid="import-count-UpdateByName"]').text(),
    ).toContain('1')
    expect(wrapper.find('[data-testid="import-count-Skip"]').text()).toContain(
      '1',
    )
    expect(
      wrapper.find('[data-testid="import-count-Unchanged"]').exists(),
    ).toBe(false)
    expect(wrapper.find('[data-testid="import-types"]').text()).toContain(
      'Hedge Fund',
    )
    expect(
      wrapper.find('[data-testid="import-apply"]').attributes('disabled'),
    ).toBeUndefined()
  })

  // A skipped row is the one the admin has to act on, so it leads and carries both the reason
  // and the server's sentence; a name match says where the row lands, since it keeps that id.
  it('lists the skipped rows first with their reason, and says where a name match lands', async () => {
    importMock.mockResolvedValue(plan())

    const wrapper = await mountOpen()
    const headings = wrapper
      .findAll('.import-heading')
      .map(heading => heading.text())

    expect(headings[0]).toContain('disposition.Skip')
    const skipped = wrapper.find('[data-testid="import-rows-Skip"]')
    expect(skipped.text()).toContain('Cedar Capital')
    expect(skipped.text()).toContain('skipReason.NameCollision')
    expect(skipped.text()).toContain('already carries the name Cedar Capital')
    expect(
      wrapper.find('[data-testid="import-rows-UpdateByName"]').text(),
    ).toContain('"targetId":"b-local"')
    expect(wrapper.find('[data-testid="import-rows-Unchanged"]').exists()).toBe(
      false,
    )
  })

  // A first import of a whole organization is thousands of rows; the dialog names the first
  // hundred of a group and counts the rest, rather than painting every one.
  it('caps each group at a hundred named rows and counts the rest', async () => {
    const rows = Array.from({ length: 150 }, (_, i) =>
      row(`n-${i}`, `New ${i}`, CounterpartyImportDisposition.Create),
    )
    importMock.mockResolvedValue(
      plan({ createCount: 150, updateByNameCount: 0, skipCount: 0, rows }),
    )

    const wrapper = await mountOpen()
    const group = wrapper.find('[data-testid="import-rows-Create"]')

    expect(group.findAll('.import-member')).toHaveLength(100)
    expect(wrapper.find('[data-testid="import-more"]').text()).toBe(
      'page.counterpartyManagement.importDialog.moreRows:{"n":50}',
    )
    expect(
      wrapper.find('[data-testid="import-count-Create"]').text(),
    ).toContain('150')
  })

  it('says there is nothing to apply and keeps the apply button off when every row is unchanged', async () => {
    importMock.mockResolvedValue(allUnchanged())

    const wrapper = await mountOpen()

    expect(wrapper.find('[data-testid="import-state"]').text()).toBe(
      'page.counterpartyManagement.importDialog.nothingToApply',
    )
    expect(
      wrapper.find('[data-testid="import-count-Unchanged"]').text(),
    ).toContain('3')
    expect(
      wrapper.find('[data-testid="import-apply"]').attributes('disabled'),
    ).toBeDefined()
  })

  it('applies the same file for real and then shows the outcome with a close button', async () => {
    importMock.mockResolvedValueOnce(plan())
    importMock.mockResolvedValueOnce(plan({ applied: true }))

    const wrapper = await mountOpen()
    await wrapper.find('[data-testid="import-apply"]').trigger('click')
    await flushPromises()

    expect(importMock).toHaveBeenLastCalledWith({
      bundle: bundle(),
      dryRun: false,
    })
    expect(wrapper.find('[data-testid="import-state"]').text()).toBe(
      'page.counterpartyManagement.importDialog.applied',
    )
    expect(wrapper.find('[data-testid="import-apply"]').exists()).toBe(false)
    expect(wrapper.find('[data-testid="import-close"]').exists()).toBe(true)

    await wrapper.find('[data-testid="import-close"]').trigger('click')
    expect(wrapper.emitted('close')).toHaveLength(1)
  })

  it('closes with the error when the file cannot be checked', async () => {
    importMock.mockRejectedValue(
      Object.assign(new Error('400'), {
        isAxiosError: true,
        response: { data: { detail: 'Bundle format 9 is not supported.' } },
      }),
    )

    const wrapper = await mountOpen()

    expect(toastMock).toHaveBeenCalledWith(
      expect.objectContaining({
        variant: 'destructive',
        description: 'Bundle format 9 is not supported.',
      }),
    )
    expect(wrapper.emitted('close')).toHaveLength(1)
  })

  it('names the organization by its id when the file carries no organization name', async () => {
    importMock.mockResolvedValue(plan())

    const wrapper = await mountOpen({
      ...bundle(),
      sourceOrganizationName: null,
      sourceInstance: null,
    })

    expect(wrapper.find('[data-testid="import-source"]').text()).toContain(
      '"organization":"Acme"',
    )
    expect(wrapper.find('[data-testid="import-source"]').text()).toContain(
      '"instance":"—"',
    )
  })

  // The admin can close the dialog while an apply is in flight and open another file; the first
  // file's outcome must not land in the second file's dialog.
  it('drops an apply outcome that arrives after the file changed', async () => {
    let resolveApply: (value: CounterpartyImportPlan) => void = () => {}
    importMock.mockResolvedValueOnce(plan())
    importMock.mockImplementationOnce(
      () =>
        new Promise<CounterpartyImportPlan>(
          resolve => (resolveApply = resolve),
        ),
    )
    importMock.mockResolvedValueOnce(allUnchanged())

    const wrapper = await mountOpen()
    await wrapper.find('[data-testid="import-apply"]').trigger('click')
    await wrapper.setProps({
      bundle: { ...bundle(), sourceOrganizationId: 'Other' },
    })
    await flushPromises()
    resolveApply(plan({ applied: true }))
    await flushPromises()

    expect(wrapper.find('[data-testid="import-state"]').text()).toBe(
      'page.counterpartyManagement.importDialog.nothingToApply',
    )
  })

  it('keeps the plan on screen when applying fails', async () => {
    importMock.mockResolvedValueOnce(plan())
    importMock.mockRejectedValueOnce(new Error('network'))

    const wrapper = await mountOpen()
    await wrapper.find('[data-testid="import-apply"]').trigger('click')
    await flushPromises()

    expect(toastMock).toHaveBeenCalledWith(
      expect.objectContaining({
        variant: 'destructive',
        description: 'page.counterpartyManagement.importDialog.applyError',
      }),
    )
    expect(wrapper.find('[data-testid="import-state"]').text()).toBe(
      'page.counterpartyManagement.importDialog.planned',
    )
    expect(wrapper.find('[data-testid="import-apply"]').exists()).toBe(true)
    expect(wrapper.emitted('close')).toBeUndefined()
  })
})
