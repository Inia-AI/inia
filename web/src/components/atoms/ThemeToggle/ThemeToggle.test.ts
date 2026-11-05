import { mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { afterEach, beforeEach, describe, expect, it } from 'vitest'

import useThemeStore from '@/store/theme'
import ThemeToggle from './ThemeToggle.vue'

beforeEach(() => {
  localStorage.clear()
  document.documentElement.classList.remove('dark')
  setActivePinia(createPinia())
})

afterEach(() => {
  localStorage.clear()
  document.documentElement.classList.remove('dark')
})

describe('ThemeToggle', () => {
  it('renders a button with an accessible label', () => {
    const wrapper = mount(ThemeToggle)
    const button = wrapper.get('button')
    expect(button.attributes('aria-label')).toBe('Switch to light mode')
  })

  it('toggles the theme on click', async () => {
    const wrapper = mount(ThemeToggle)
    const store = useThemeStore()
    expect(store.theme).toBe('dark')

    await wrapper.get('button').trigger('click')
    expect(store.theme).toBe('light')
    expect(wrapper.get('button').attributes('aria-label')).toBe('Switch to dark mode')

    await wrapper.get('button').trigger('click')
    expect(store.theme).toBe('dark')
  })

  it('shows a sun icon in dark mode and a moon in light mode', async () => {
    const wrapper = mount(ThemeToggle)
    expect(wrapper.text()).toContain('☀')

    await wrapper.get('button').trigger('click')
    expect(wrapper.text()).toContain('🌙')
  })
})
