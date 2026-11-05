import { defineStore } from 'pinia'
import { ref, watch } from 'vue'

export default defineStore('theme', () => {
  // Get initial theme from localStorage or default to 'dark'
  const savedTheme = localStorage.getItem('theme') as 'light' | 'dark' | null
  const theme = ref<'light' | 'dark'>(savedTheme || 'dark')

  // Apply theme to document
  const applyTheme = (newTheme: 'light' | 'dark') => {
    if (newTheme === 'dark') {
      document.documentElement.classList.add('dark')
    } else {
      document.documentElement.classList.remove('dark')
    }
  }

  // Initialize theme on load
  applyTheme(theme.value)

  // Toggle theme
  const toggleTheme = () => {
    theme.value = theme.value === 'dark' ? 'light' : 'dark'
  }

  // Set specific theme
  const setTheme = (newTheme: 'light' | 'dark') => {
    theme.value = newTheme
  }

  // Watch for theme changes and persist
  watch(theme, (newTheme) => {
    localStorage.setItem('theme', newTheme)
    applyTheme(newTheme)
  })

  return {
    theme,
    toggleTheme,
    setTheme,
  }
})
