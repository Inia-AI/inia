import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import { defineComponent, h, nextTick } from 'vue'

const { getCounterpartyMock, updateCounterpartyMock, toastMock } = vi.hoisted(
  () => ({
    getCounterpartyMock: vi.fn(),
    updateCounterpartyMock: vi.fn(),
    toastMock: vi.fn(),
  }),
)

vi.mock('@/api', () => ({
  default: {
    Counterparties: {
      useGetCounterparty: () => ({ mutateAsync: getCounterpartyMock }),
      updateCounterparty: () => ({ mutateAsync: updateCounterpartyMock }),
    },
  },
}))

vi.mock('@/components/ui/toast/use-toast', () => ({
  useToast: () => ({ toast: toastMock, toasts: [], dismiss: () => {} }),
}))

vi.mock('vue-i18n', () => ({
  useI18n: () => ({
    t: (key: string, params?: Record<string, unknown>) =>
      params ? `${key}:${JSON.stringify(params)}` : key,
  }),
}))

// reka-ui's Dialog portals out of the wrapper tree under jsdom, so stub
// the dialog primitives with transparent pass-throughs so we can read the
// form inputs directly.
vi.mock('@/components/ui/dialog', () => {
  const passthrough = (label: string) =>
    defineComponent({
      name: `${label}Stub`,
      setup(_, { slots }) {
        return () => h('div', { 'data-stub': label }, slots.default?.())
      },
    })
  return {
    Dialog: defineComponent({
      name: 'DialogStub',
      props: ['open'],
      emits: ['update:open'],
      setup(props, { slots }) {
        return () =>
          h(
            'div',
            { 'data-stub': 'Dialog', 'data-open': String(props.open) },
            slots.default?.(),
          )
      },
    }),
    DialogContent: passthrough('DialogContent'),
    DialogHeader: passthrough('DialogHeader'),
    DialogTitle: passthrough('DialogTitle'),
    DialogDescription: passthrough('DialogDescription'),
  }
})

import EditCounterpartyDialog from './EditCounterpartyDialog.vue'

const baseDetails = {
  id: 'cp-1',
  name: 'Acme Corp',
  nameVariants: 'Acme Corp',
  counterpartyTypeIds: ['type-a'],
  parentId: undefined,
  email: 'hello@acme.test',
  phoneNumber: '+1 555 0000',
  website: 'https://acme.test',
  address: '1 Acme Way',
  lastFinancialAnalysisResult: undefined,
}

async function mountOpen(props?: Partial<{ counterpartyId: string | null }>) {
  const wrapper = mount(EditCounterpartyDialog, {
    props: { open: true, counterpartyId: 'cp-1', ...props },
  })
  await flushPromises()
  await nextTick()
  return wrapper
}

describe('EditCounterpartyDialog', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    getCounterpartyMock.mockResolvedValue({ ...baseDetails })
    updateCounterpartyMock.mockResolvedValue(undefined)
  })

  it('prefills the name input from the fetched counterparty details', async () => {
    const wrapper = await mountOpen()
    expect(getCounterpartyMock).toHaveBeenCalledWith('cp-1')
    const input = wrapper.get('input')
    expect((input.element as HTMLInputElement).value).toBe('Acme Corp')
  })

  it('disables submit when the name is unchanged or empty', async () => {
    const wrapper = await mountOpen()
    const submit = wrapper.get('button[type="submit"]')
    expect(submit.attributes('disabled')).toBeDefined()

    const input = wrapper.get('input')
    await input.setValue('')
    expect(submit.attributes('disabled')).toBeDefined()

    await input.setValue('Acme Corp 2')
    expect(submit.attributes('disabled')).toBeUndefined()
  })

  it('updates nameVariants to the new name when current variants equal current name', async () => {
    const wrapper = await mountOpen()
    await wrapper.get('input').setValue('Acme Holdings')
    await wrapper.get('form').trigger('submit.prevent')
    await flushPromises()

    expect(updateCounterpartyMock).toHaveBeenCalledTimes(1)
    expect(updateCounterpartyMock).toHaveBeenCalledWith({
      id: 'cp-1',
      params: expect.objectContaining({
        name: 'Acme Holdings',
        nameVariants: 'Acme Holdings',
        counterpartyTypeIds: ['type-a'],
        email: 'hello@acme.test',
      }),
    })
  })

  it('preserves existing nameVariants when variants differ from the current name', async () => {
    getCounterpartyMock.mockResolvedValue({
      ...baseDetails,
      nameVariants: 'Acme Corp\nAcme Inc\nAcme Corporation',
    })
    const wrapper = await mountOpen()
    await wrapper.get('input').setValue('Acme Holdings')
    await wrapper.get('form').trigger('submit.prevent')
    await flushPromises()

    expect(updateCounterpartyMock).toHaveBeenCalledWith({
      id: 'cp-1',
      params: expect.objectContaining({
        name: 'Acme Holdings',
        nameVariants: 'Acme Corp\nAcme Inc\nAcme Corporation',
      }),
    })
  })

  it('emits updated and update:open=false on success and toasts editSuccess', async () => {
    const wrapper = await mountOpen()
    await wrapper.get('input').setValue('Acme Holdings')
    await wrapper.get('form').trigger('submit.prevent')
    await flushPromises()

    expect(wrapper.emitted('updated')).toEqual([
      [{ id: 'cp-1', name: 'Acme Holdings' }],
    ])
    const openEvents = wrapper.emitted('update:open') ?? []
    expect(openEvents.at(-1)).toEqual([false])
    expect(toastMock).toHaveBeenCalledWith(
      expect.objectContaining({
        description: expect.stringContaining(
          'shared.components.counterpartySelect.editSuccess',
        ),
      }),
    )
  })

  it('toasts a destructive error and keeps the dialog open when the update fails', async () => {
    updateCounterpartyMock.mockRejectedValueOnce(new Error('500'))
    const wrapper = await mountOpen()
    await wrapper.get('input').setValue('Acme Holdings')
    await wrapper.get('form').trigger('submit.prevent')
    await flushPromises()

    expect(toastMock).toHaveBeenCalledWith(
      expect.objectContaining({
        description: 'shared.components.counterpartySelect.editError',
        variant: 'destructive',
      }),
    )
    expect(wrapper.emitted('updated')).toBeUndefined()
    expect(wrapper.emitted('update:open') ?? []).not.toContainEqual([false])
  })

  it('treats whitespace-only differences as no-op (collapses runs, trims edges)', async () => {
    const wrapper = await mountOpen()
    const submit = wrapper.get('button[type="submit"]')
    const input = wrapper.get('input')

    // Pure trailing whitespace — same as original name, submit stays disabled.
    await input.setValue('Acme Corp   ')
    expect(submit.attributes('disabled')).toBeDefined()

    // Internal double-space — still equivalent after collapse.
    await input.setValue('Acme  Corp')
    expect(submit.attributes('disabled')).toBeDefined()

    // Actual change with surrounding whitespace — trims and submits clean value.
    await input.setValue('   Acme   Holdings  ')
    expect(submit.attributes('disabled')).toBeUndefined()
    await wrapper.get('form').trigger('submit.prevent')
    await flushPromises()

    expect(updateCounterpartyMock).toHaveBeenCalledWith({
      id: 'cp-1',
      params: expect.objectContaining({
        name: 'Acme Holdings',
        nameVariants: 'Acme Holdings',
      }),
    })
  })

  it('toasts destructive and closes the dialog when the initial fetch fails', async () => {
    getCounterpartyMock.mockReset()
    getCounterpartyMock.mockRejectedValueOnce(new Error('500'))
    const wrapper = await mountOpen()

    expect(toastMock).toHaveBeenCalledWith(
      expect.objectContaining({
        description: 'shared.components.counterpartySelect.editError',
        variant: 'destructive',
      }),
    )
    expect(updateCounterpartyMock).not.toHaveBeenCalled()
    const openEvents = wrapper.emitted('update:open') ?? []
    expect(openEvents.at(-1)).toEqual([false])
  })

  it('does not fetch when opened without a counterpartyId', async () => {
    mount(EditCounterpartyDialog, {
      props: { open: true, counterpartyId: null },
    })
    await flushPromises()
    expect(getCounterpartyMock).not.toHaveBeenCalled()
  })

  it('refetches when counterpartyId changes while the dialog is open', async () => {
    const wrapper = mount(EditCounterpartyDialog, {
      props: { open: true, counterpartyId: 'cp-1' },
    })
    await flushPromises()
    expect(getCounterpartyMock).toHaveBeenCalledWith('cp-1')
    expect(getCounterpartyMock).toHaveBeenCalledTimes(1)

    getCounterpartyMock.mockResolvedValueOnce({
      ...baseDetails,
      id: 'cp-2',
      name: 'Globex Inc',
    })
    await wrapper.setProps({ counterpartyId: 'cp-2' })
    await flushPromises()

    expect(getCounterpartyMock).toHaveBeenCalledWith('cp-2')
    expect(getCounterpartyMock).toHaveBeenCalledTimes(2)
    expect((wrapper.get('input').element as HTMLInputElement).value).toBe(
      'Globex Inc',
    )
  })

  it('refetches when the dialog is closed and reopened on the same id', async () => {
    const wrapper = mount(EditCounterpartyDialog, {
      props: { open: true, counterpartyId: 'cp-1' },
    })
    await flushPromises()
    expect(getCounterpartyMock).toHaveBeenCalledTimes(1)

    await wrapper.setProps({ open: false })
    await flushPromises()
    await wrapper.setProps({ open: true })
    await flushPromises()

    expect(getCounterpartyMock).toHaveBeenCalledTimes(2)
  })
})
