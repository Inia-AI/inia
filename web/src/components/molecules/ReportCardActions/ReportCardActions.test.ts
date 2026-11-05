import { describe, it, expect, vi } from 'vitest'
import { mount } from '@vue/test-utils'

vi.mock('vue-i18n', () => ({
  useI18n: () => ({
    t: (key: string, params?: Record<string, unknown>) =>
      params ? `${key} ${JSON.stringify(params)}` : key,
  }),
}))

import ReportCardActions from './ReportCardActions.vue'

describe('ReportCardActions', () => {
  it('names its menu button after the report it acts on', () => {
    const wrapper = mount(ReportCardActions, {
      props: { canReRun: true, reportTitle: 'Q2 Credit Review' },
      global: { stubs: { IniaIcon: true } },
    })

    expect(wrapper.get('button.options-trigger').attributes('aria-label')).toBe(
      'shared.components.reportCardActions.menuAria {"title":"Q2 Credit Review"}',
    )
  })
})
