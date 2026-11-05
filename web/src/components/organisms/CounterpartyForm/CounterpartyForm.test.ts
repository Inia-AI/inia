import { describe, it, expect, vi } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import { defineComponent, h } from 'vue'

vi.mock('vue-i18n', () => ({
  useI18n: () => ({
    t: (key: string) => key,
  }),
}))

vi.mock('@/components/molecules', async () => ({
  MultiSelect: defineComponent({
    name: 'MultiSelectStub',
    props: ['modelValue', 'options'],
    setup: () => () => h('div', { 'data-stub': 'MultiSelect' }),
  }),
  KeyValueEditor: (
    await import('@/components/molecules/KeyValueEditor/KeyValueEditor.vue')
  ).default,
}))

import CounterpartyForm from './CounterpartyForm.vue'

const HEDGE_FUND_TYPE_ID = 'type-hedge-fund'
const BROKER_TYPE_ID = 'type-broker'

const counterpartyTypes = [
  { label: 'Hedge Fund', value: HEDGE_FUND_TYPE_ID },
  { label: 'Broker-Dealer', value: BROKER_TYPE_ID },
]

const ADD_LABEL = 'page.counterpartyManagement.form.properties.addLabel'
const REMOVE_LABEL = 'page.counterpartyManagement.form.properties.removeLabel'

const mountForm = (initialValues: Record<string, unknown>, options: { attachTo?: HTMLElement } = {}) =>
  mount(CounterpartyForm, {
    ...options,
    props: { counterpartyTypes, initialValues },
  })

const settle = async () => {
  for (let i = 0; i < 5; i++) {
    await new Promise(resolve => setTimeout(resolve, 5))
    await flushPromises()
  }
}

const buttonByText = (wrapper: ReturnType<typeof mountForm>, text: string) => {
  const button = wrapper.findAll('button').find(b => b.text() === text)
  expect(button).toBeTruthy()
  return button!
}

const submittedPayload = async (wrapper: ReturnType<typeof mountForm>) => {
  await wrapper.find('form').trigger('submit')
  await settle()
  const submissions = wrapper.emitted('submit')
  expect(submissions).toBeTruthy()
  return (submissions as unknown[][])[0][0] as Record<string, unknown>
}

describe('CounterpartyForm properties', () => {
  it('shows the properties editor for any counterparty type', () => {
    const wrapper = mountForm({
      id: 'broker-1',
      name: 'Broker One',
      nameVariants: '',
      counterpartyTypeIds: [BROKER_TYPE_ID],
    })

    expect(wrapper.text()).toContain(
      'page.counterpartyManagement.form.properties.heading',
    )
  })

  it('emits an empty record when there are no properties', async () => {
    const wrapper = mountForm({
      id: 'broker-1',
      name: 'Broker One',
      nameVariants: '',
      counterpartyTypeIds: [BROKER_TYPE_ID],
    })

    const payload = await submittedPayload(wrapper)

    expect(payload.properties).toEqual({})
  })

  it('populates rows from an initial record and emits them verbatim', async () => {
    const wrapper = mountForm({
      id: 'fund-1',
      name: 'Fund One',
      nameVariants: '',
      counterpartyTypeIds: [HEDGE_FUND_TYPE_ID],
      properties: {
        reportingStyle: '{"currency":"JPY"}',
        notes: 'call the PM',
      },
    })

    const payload = await submittedPayload(wrapper)

    expect(payload.properties).toEqual({
      reportingStyle: '{"currency":"JPY"}',
      notes: 'call the PM',
    })
  })

  it('adds a row and emits it in the record', async () => {
    const wrapper = mountForm({
      id: 'fund-1',
      name: 'Fund One',
      nameVariants: '',
      counterpartyTypeIds: [HEDGE_FUND_TYPE_ID],
    })

    await buttonByText(wrapper, ADD_LABEL).trigger('click')
    await wrapper.find('input[name="property-key-0"]').setValue('marketCap')
    await wrapper.find('textarea[name="property-value-0"]').setValue('1.2B')

    const payload = await submittedPayload(wrapper)

    expect(payload.properties).toEqual({ marketCap: '1.2B' })
  })

  it('drops a row whose key is left blank', async () => {
    const wrapper = mountForm({
      id: 'fund-1',
      name: 'Fund One',
      nameVariants: '',
      counterpartyTypeIds: [HEDGE_FUND_TYPE_ID],
      properties: { keep: 'yes' },
    })

    await buttonByText(wrapper, ADD_LABEL).trigger('click')
    await wrapper.find('textarea[name="property-value-1"]').setValue('orphan value')

    const payload = await submittedPayload(wrapper)

    expect(payload.properties).toEqual({ keep: 'yes' })
  })

  it('removes a row so it no longer appears in the record', async () => {
    const wrapper = mountForm({
      id: 'fund-1',
      name: 'Fund One',
      nameVariants: '',
      counterpartyTypeIds: [HEDGE_FUND_TYPE_ID],
      properties: { drop: 'me', keep: 'this' },
    })

    await buttonByText(wrapper, REMOVE_LABEL).trigger('click')

    const payload = await submittedPayload(wrapper)

    expect(payload.properties).toEqual({ keep: 'this' })
  })
})

describe('CounterpartyForm buttons', () => {
  const CANCEL_LABEL = 'page.counterpartyManagement.form.cancelLabel'
  const SUBMIT_LABEL = 'page.counterpartyManagement.form.submitLabel'
  const EXISTING_COUNTERPARTY = {
    id: 'fund-1',
    name: 'Fund One',
    nameVariants: '',
    counterpartyTypeIds: [HEDGE_FUND_TYPE_ID],
  }

  const mountConnected = () => mountForm(EXISTING_COUNTERPARTY, { attachTo: document.body })

  it('renders Cancel as a plain button', () => {
    const wrapper = mountConnected()

    expect(buttonByText(wrapper, CANCEL_LABEL).attributes('type')).toBe('button')
    wrapper.unmount()
  })

  it('makes Submit the default button, so Enter in a field submits and never cancels', async () => {
    const wrapper = mountConnected()
    const defaultButton = wrapper
      .findAll('button')
      .find(button => (button.element as HTMLButtonElement).type === 'submit')

    expect(defaultButton?.text()).toBe(SUBMIT_LABEL)

    ;(defaultButton!.element as HTMLButtonElement).click()
    await settle()

    expect(wrapper.emitted('submit')).toHaveLength(1)
    expect(wrapper.emitted('cancel')).toBeUndefined()
    wrapper.unmount()
  })

  it('cancels without submitting the form', async () => {
    const wrapper = mountConnected()
    const onSubmit = vi.fn()
    wrapper.find('form').element.addEventListener('submit', onSubmit)

    ;(buttonByText(wrapper, CANCEL_LABEL).element as HTMLButtonElement).click()
    await settle()

    expect(wrapper.emitted('cancel')).toHaveLength(1)
    expect(onSubmit).not.toHaveBeenCalled()
    expect(wrapper.emitted('submit')).toBeUndefined()
    wrapper.unmount()
  })
})
