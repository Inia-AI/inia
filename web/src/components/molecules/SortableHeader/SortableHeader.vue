<script setup lang="ts">
import { computed, type Component } from 'vue'
import { ArrowDown, ArrowUp } from '@lucide/vue'

const ARIA_SORT_BY_DIRECTION: Record<string, 'ascending' | 'descending'> = {
  asc: 'ascending',
  desc: 'descending',
}

const props = withDefaults(
  defineProps<{
    direction?: string | false | null
    as?: string | Component
    fill?: boolean
  }>(),
  { direction: null, as: 'th', fill: false },
)

const emit = defineEmits<{ sort: [] }>()

const ariaSort = computed(() => (props.direction && ARIA_SORT_BY_DIRECTION[props.direction]) || 'none')
</script>

<template>
  <component :is="as" :aria-sort="ariaSort">
    <button
      type="button"
      class="sortable-header focus-ring"
      :class="{ 'sortable-header--fill': fill }"
      @click="emit('sort')"
    >
      <slot />
      <ArrowUp v-if="direction === 'asc'" class="sortable-header-arrow" aria-hidden="true" />
      <ArrowDown v-else-if="direction === 'desc'" class="sortable-header-arrow" aria-hidden="true" />
    </button>
  </component>
</template>

<style scoped>
.sortable-header {
  display: inline-flex;
  align-items: center;
  gap: 4px;
  text-transform: inherit;
  cursor: pointer;
}

.sortable-header--fill {
  display: flex;
  width: 100%;
  gap: 8px;
}

.sortable-header-arrow {
  width: 1em;
  height: 1em;
  flex-shrink: 0;
}
</style>
