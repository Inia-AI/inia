import { createPinia, setActivePinia } from 'pinia'
import { afterEach, beforeEach, describe, expect, it } from 'vitest'
import { nextTick } from 'vue'

import useThemeStore from './theme'

beforeEach(() => {
  localStorage.clear()
  document.documentElement.classList.remove('dark')
  setActivePinia(createPinia())
})

afterEach(() => {
  localStorage.clear()
  document.documentElement.classList.remove('dark')
})

describe('theme store', () => {
  it('defaults to dark when nothing is saved', () => {
    const store = useThemeStore()
    expect(store.theme).toBe('dark')
    expect(document.documentElement.classList.contains('dark')).toBe(true)
  })

  it('reads saved theme from localStorage on init', () => {
    localStorage.setItem('theme', 'light')
    const store = useThemeStore()
    expect(store.theme).toBe('light')
    expect(document.documentElement.classList.contains('dark')).toBe(false)
  })

  it('toggles between dark and light', async () => {
    const store = useThemeStore()
    store.toggleTheme()
    await nextTick()
    expect(store.theme).toBe('light')
    expect(document.documentElement.classList.contains('dark')).toBe(false)

    store.toggleTheme()
    await nextTick()
    expect(store.theme).toBe('dark')
    expect(document.documentElement.classList.contains('dark')).toBe(true)
  })

  it('setTheme persists to localStorage', async () => {
    const store = useThemeStore()
    store.setTheme('light')
    await nextTick()
    expect(localStorage.getItem('theme')).toBe('light')
  })
})
