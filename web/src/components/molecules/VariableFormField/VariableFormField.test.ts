import { describe, expect, it } from 'vitest'
import { flushPromises, mount } from '@vue/test-utils'
import { defineComponent, h } from 'vue'
import { useForm } from 'vee-validate'
import { createI18n } from 'vue-i18n'

import { EN } from '@/assets/locales/en'
import { Input } from '@/components/ui/input'
import ListFormField from '@/components/molecules/ListFormField/ListFormField.vue'
import { WorkflowVariableTypes } from '@/enums'
import VariableFormField from './VariableFormField.vue'
import type { VariableFieldDefinition } from './types'

const textField: VariableFieldDefinition = {
  component: Input,
  name: 'AssessmentYear',
  label: 'Assessment Year',
  type: WorkflowVariableTypes.String,
  description: 'The year the financial year ends in.',
  isRequired: true,
}

const mountField = (field: VariableFieldDefinition, extra: Record<string, unknown> = {}) =>
  mount(
    defineComponent({
      setup() {
        useForm({ initialValues: { variables: { [field.name]: field.type === WorkflowVariableTypes.List ? [''] : '' } } })
        return () =>
          h(VariableFormField, {
            field,
            notes: [],
            notesId: `field-notes-${field.name}`,
            showControl: true,
            ...extra,
          })
      },
    }),
    {
      attachTo: document.body,
      global: {
        plugins: [createI18n({ legacy: false, locale: 'en', messages: { en: EN }, missingWarn: false })],
        stubs: { IniaIcon: true },
      },
    },
  )

describe('VariableFormField', () => {
  it('shows the description under the field and links it to the control', async () => {
    const wrapper = mountField(textField)
    await flushPromises()

    const description = wrapper.get('p.field-hint')
    expect(description.text()).toBe('The year the financial year ends in.')
    expect(wrapper.get('input').attributes('aria-describedby')?.split(' ')).toContain(description.attributes('id'))
    expect(wrapper.get('label').text()).toBe('Assessment Year*')
    wrapper.unmount()
  })

  it('links the rule notes to the control and lists them under the field', async () => {
    const wrapper = mountField(textField, { notes: ['Enter a year.'] })
    await flushPromises()

    const notes = wrapper.get('[data-testid="field-rule-messages"]')
    expect(notes.attributes('id')).toBe('field-notes-AssessmentYear')
    expect(notes.text()).toBe('Enter a year.')
    expect(wrapper.get('input').attributes('aria-describedby')?.split(' ')).toContain('field-notes-AssessmentYear')
    expect(wrapper.get('input').attributes('aria-invalid')).toBe('true')
    wrapper.unmount()
  })

  it('marks a field a re-run needs again, in its label', async () => {
    const wrapper = mountField(textField, { reRunBadge: 'New field' })
    await flushPromises()

    expect(wrapper.get('[data-rerun-field]').attributes('data-rerun-field')).toBe('AssessmentYear')
    expect(wrapper.get('.rerun-field-badge').text()).toBe(', New field')
    wrapper.unmount()
  })

  it('renders a list variable as a list headed by its label, with its description', async () => {
    const wrapper = mountField({
      component: ListFormField,
      name: 'Websites',
      label: 'Websites',
      type: WorkflowVariableTypes.List,
      description: 'Pages about the company.',
    })
    await flushPromises()

    expect(wrapper.text()).toContain('Websites')
    expect(wrapper.get('p.field-hint').text()).toBe('Pages about the company.')
    wrapper.unmount()
  })
})
