<script setup lang="ts">
import useThemeStore from '@/store/theme'
import { computed } from 'vue'

const themeStore = useThemeStore()

const isDark = computed(() => themeStore.theme === 'dark')

const toggleTheme = () => {
  themeStore.toggleTheme()
}
</script>

<template>
  <button
    type="button"
    @click="toggleTheme"
    class="theme-toggle"
    :aria-label="isDark ? 'Switch to light mode' : 'Switch to dark mode'"
  >
    <span class="theme-icon">{{ isDark ? '☀' : '🌙' }}</span>
  </button>
</template>

<style scoped>
.theme-toggle {
  position: relative;
  width: 40px;
  height: 40px;
  border-radius: var(--radius-button);
  background: var(--bg-1);
  border: 1px solid var(--stroke);
  display: flex;
  align-items: center;
  justify-content: center;
  cursor: pointer;
  transition: all 0.2s ease;
}

.theme-toggle:hover {
  background: var(--bg-2);
  border-color: var(--stroke-hover);
  transform: scale(1.05);
}

.dark .theme-toggle {
  background: #ffffff;
  border-color: var(--nav-stroke);
}

.dark .theme-toggle:hover {
  background: var(--nav-hover);
  border-color: var(--nav-ink-muted);
}

.theme-icon {
  font-size: 20px;
  line-height: 1;
  transition: transform 0.3s ease;
  filter: none;
}

/* Blue moon in light mode, yellow sun in dark mode */
:not(.dark) .theme-icon {
  filter: brightness(0) saturate(100%) invert(47%) sepia(96%) saturate(2457%)
    hue-rotate(201deg) brightness(101%) contrast(101%);
}

.dark .theme-icon {
  filter: none;
}

.theme-toggle:hover .theme-icon {
  transform: rotate(20deg) scale(1.1);
}
</style>
