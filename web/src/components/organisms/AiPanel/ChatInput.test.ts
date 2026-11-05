import { describe, it, expect, vi } from 'vitest'
import { mount } from '@vue/test-utils'

vi.mock('vue-i18n', () => ({
  useI18n: () => ({ t: (key: string) => key }),
}))

import ChatInput from './ChatInput.vue'

describe('ChatInput send button', () => {
  it.each([
    ['disabled while the question is empty', ''],
    ['enabled once a question is typed', 'test'],
  ])('carries an accessible name when %s', (_state, modelValue) => {
    const wrapper = mount(ChatInput, { props: { modelValue, label: 'Question' } })
    const send = wrapper.get('button.chat-send')

    expect(send.attributes('aria-label')).toBe('shared.aiPanel.send')
    expect(send.attributes('type')).toBe('button')
    expect(send.attributes('disabled')).toBe(modelValue ? undefined : '')
  })
})

describe('ChatInput text field', () => {
  it('is named by its label rather than by its placeholder alone', () => {
    const wrapper = mount(ChatInput, { props: { modelValue: '', label: 'Question about this result', placeholder: 'Ask…' } })

    expect(wrapper.get('textarea').attributes('aria-label')).toBe('Question about this result')
  })
})
