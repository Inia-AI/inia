/**
 * The counterparty type page lists the shared types and lets an editor add, rename, redescribe and
 * delete them. A built-in type keeps its name and cannot be deleted.
 */
import { describe, expect, it, vi, beforeEach } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import { dialogStubs } from '@/test/helpers/rekaStubs'

const { createMock, updateMock, deleteMock, toastMock, typesRef } = vi.hoisted(() => ({
  createMock: vi.fn(),
  updateMock: vi.fn(),
  deleteMock: vi.fn(),
  toastMock: vi.fn(),
  typesRef: { value: [] as unknown[] },
}))

vi.mock('@/api', async () => {
  const { ref: vueRef } = await vi.importActual<typeof import('vue')>('vue')
  return {
    default: {
      CounterpartyTypes: {
        useGetCounterpartyTypes: () => ({
          data: vueRef({ value: typesRef.value, count: typesRef.value.length }),
          isLoading: vueRef(false),
        }),
        useCreateCounterpartyType: () => ({ mutateAsync: createMock, isPending: vueRef(false) }),
        useUpdateCounterpartyType: () => ({ mutateAsync: updateMock, isPending: vueRef(false) }),
        useDeleteCounterpartyType: () => ({ mutateAsync: deleteMock, isPending: vueRef(false) }),
      },
    },
  }
})

vi.mock('vue-i18n', () => ({
  useI18n: () => ({
    t: (key: string, params?: Record<string, unknown>) => (params ? `${key}:${JSON.stringify(params)}` : key),
  }),
}))

vi.mock('@/components/ui/toast/use-toast', () => ({ useToast: () => ({ toast: toastMock }) }))

import CounterpartyTypeManagement from './CounterpartyTypeManagement.vue'

const hedgeFund = { id: 'type-hf', name: 'Hedge Fund', description: 'Pooled fund', counterpartyCount: 41, isBuiltIn: true }
const sovereign = { id: 'type-sov', name: 'Sovereign', description: 'A state borrower', counterpartyCount: 3, isBuiltIn: false }

const mountView = async () => {
  const wrapper = mount(CounterpartyTypeManagement, {
    global: { stubs: { ...dialogStubs, IniaIcon: true, IniaLoader: true } },
  })
  await flushPromises()
  return wrapper
}

const row = (wrapper: Awaited<ReturnType<typeof mountView>>, name: string) =>
  wrapper.findAll('tbody tr').find(tr => tr.text().includes(name))!

describe('CounterpartyTypeManagement.vue', () => {
  beforeEach(() => {
    typesRef.value = [hedgeFund, sovereign]
    createMock.mockReset().mockResolvedValue({ ...sovereign, id: 'type-new' })
    updateMock.mockReset().mockResolvedValue(sovereign)
    deleteMock.mockReset().mockResolvedValue(undefined)
    toastMock.mockReset()
  })

  it('lists every type with its use count, and marks the built-in ones', async () => {
    const wrapper = await mountView()

    expect(row(wrapper, 'Hedge Fund').text()).toContain('41')
    expect(row(wrapper, 'Hedge Fund').find('[data-testid="built-in-badge"]').exists()).toBe(true)
    expect(row(wrapper, 'Sovereign').find('[data-testid="built-in-badge"]').exists()).toBe(false)
  })

  it('refuses to delete a built-in type', async () => {
    const wrapper = await mountView()

    expect(row(wrapper, 'Hedge Fund').find('[data-testid="delete-type"]').attributes('disabled')).toBeDefined()
    expect(row(wrapper, 'Sovereign').find('[data-testid="delete-type"]').attributes('disabled')).toBeUndefined()
  })

  it('adds a type with a trimmed name and its description', async () => {
    const wrapper = await mountView()

    await wrapper.find('[data-testid="add-type"]').trigger('click')
    await wrapper.find('[data-testid="type-name"]').setValue('  Supranational  ')
    await wrapper.find('[data-testid="type-description"]').setValue('Development banks')
    await wrapper.find('[data-testid="type-form"]').trigger('submit')
    await flushPromises()

    expect(createMock).toHaveBeenCalledWith({ name: 'Supranational', description: 'Development banks' })
  })

  it('keeps the name of a built-in type locked while its description changes', async () => {
    const wrapper = await mountView()

    await row(wrapper, 'Hedge Fund').find('[data-testid="edit-type"]').trigger('click')
    expect(wrapper.find('[data-testid="type-name"]').attributes('disabled')).toBeDefined()
    await wrapper.find('[data-testid="type-description"]').setValue('A pooled investment fund')
    await wrapper.find('[data-testid="type-form"]').trigger('submit')
    await flushPromises()

    expect(updateMock).toHaveBeenCalledWith({
      id: 'type-hf',
      data: { name: 'Hedge Fund', description: 'A pooled investment fund' },
    })
  })

  it('confirms a delete with the number of counterparties that carry the type', async () => {
    const wrapper = await mountView()

    await row(wrapper, 'Sovereign').find('[data-testid="delete-type"]').trigger('click')
    expect(wrapper.find('[data-testid="delete-confirmation"]').text()).toContain('"count":3')
    await wrapper.find('[data-testid="confirm-delete"]').trigger('click')
    await flushPromises()

    expect(deleteMock).toHaveBeenCalledWith('type-sov')
  })

  it('shows the reason the server gives when it refuses a change', async () => {
    createMock.mockRejectedValue({
      isAxiosError: true,
      response: { status: 409, data: { title: "A counterparty type named 'Sovereign' already exists." } },
    })
    const wrapper = await mountView()

    await wrapper.find('[data-testid="add-type"]').trigger('click')
    await wrapper.find('[data-testid="type-name"]').setValue('Sovereign')
    await wrapper.find('[data-testid="type-form"]').trigger('submit')
    await flushPromises()

    expect(toastMock).toHaveBeenCalledWith(expect.objectContaining({
      variant: 'destructive',
      description: "A counterparty type named 'Sovereign' already exists.",
    }))
  })
})
