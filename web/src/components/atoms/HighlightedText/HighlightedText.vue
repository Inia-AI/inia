<script setup lang="ts">
import { computed } from 'vue'

const props = defineProps<{
  text: string | null | undefined
  query?: string
}>()

const escapeRegex = (raw: string): string =>
  raw.replace(/[.*+?^${}()|[\]\\]/g, '\\$&')

const segments = computed<Array<{ text: string; match: boolean }>>(() => {
  const text = props.text ?? ''
  const q = (props.query ?? '').trim()
  if (!q || !text) return [{ text, match: false }]

  const qLower = q.toLowerCase()
  const re = new RegExp(`(${escapeRegex(q)})`, 'ig')
  return text
    .split(re)
    .filter(p => p !== '')
    .map(p => ({ text: p, match: p.toLowerCase() === qLower }))
})
</script>

<template>
  <span class="highlighted-text">
    <template v-for="(seg, i) in segments" :key="i">
      <mark v-if="seg.match" class="search-mark">{{ seg.text }}</mark>
      <template v-else>{{ seg.text }}</template>
    </template>
  </span>
</template>

<style scoped>
.highlighted-text {
  display: inline;
}

.search-mark {
  background-color: var(--accent-warning-light);
  color: inherit;
  border-radius: 2px;
  padding: 0 2px;
}
</style>
