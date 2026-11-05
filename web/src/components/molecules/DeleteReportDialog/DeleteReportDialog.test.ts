import { describe, it, expect, vi } from 'vitest'
import { mount } from '@vue/test-utils'

vi.mock('vue-i18n', () => ({
  useI18n: () => ({ t: (key: string) => key }),
}))

vi.mock('@/components/ConfirmDialog.vue', () => ({
  default: {
    name: 'ConfirmDialog',
    props: [
      'modelValue',
      'title',
      'description',
      'confirmLabel',
      'cancelLabel',
    ],
    emits: ['update:modelValue', 'confirm'],
    template: `
      <div data-testid="confirm-dialog-stub"
           :data-open="String(modelValue)"
           :data-title="title"
           :data-description="description"
           :data-confirm-label="confirmLabel"
           :data-cancel-label="cancelLabel">
        <button class="confirm" @click="$emit('confirm')">confirm</button>
        <button class="cancel" @click="$emit('update:modelValue', false)">close</button>
      </div>
    `,
  },
}))

import DeleteReportDialog from './DeleteReportDialog.vue'

describe('DeleteReportDialog', () => {
  it('passes the shared i18n strings down to ConfirmDialog', () => {
    const wrapper = mount(DeleteReportDialog, {
      props: { modelValue: true },
    })
    const stub = wrapper.get('[data-testid="confirm-dialog-stub"]')

    expect(stub.attributes('data-open')).toBe('true')
    expect(stub.attributes('data-title')).toBe('shared.deleteReport.title')
    expect(stub.attributes('data-description')).toBe(
      'shared.deleteReport.message',
    )
    expect(stub.attributes('data-confirm-label')).toBe(
      'shared.deleteReport.confirm',
    )
    expect(stub.attributes('data-cancel-label')).toBe(
      'shared.deleteReport.dismiss',
    )
  })

  it('forwards confirm and update:modelValue events', async () => {
    const wrapper = mount(DeleteReportDialog, {
      props: { modelValue: true },
    })

    await wrapper.get('button.confirm').trigger('click')
    await wrapper.get('button.cancel').trigger('click')

    expect(wrapper.emitted('confirm')).toHaveLength(1)
    expect(wrapper.emitted('update:modelValue')).toEqual([[false]])
  })
})
