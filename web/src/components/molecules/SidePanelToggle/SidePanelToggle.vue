<script lang="ts">
const panelsAwaitingFocus = new Set<string>()
</script>

<script setup lang="ts">
import { computed, nextTick, onMounted, useTemplateRef } from 'vue'
import { useI18n } from 'vue-i18n'

const props = defineProps<{
  expanded: boolean
  controls: string
}>()

const emit = defineEmits<{ toggle: [] }>()

const { t: $t } = useI18n()

const label = computed(() => (props.expanded ? $t('common.collapsePanel') : $t('common.expandPanel')))

const button = useTemplateRef<HTMLButtonElement>('button')

const toggle = () => {
  panelsAwaitingFocus.add(props.controls)
  emit('toggle')
  void nextTick(() => panelsAwaitingFocus.delete(props.controls))
}

onMounted(() => {
  if (panelsAwaitingFocus.delete(props.controls)) {
    button.value?.focus()
  }
})
</script>

<template>
  <button
    ref="button"
    type="button"
    :class="expanded ? 'collapse-button-simple' : 'expand-button-simple'"
    :title="label"
    :aria-label="label"
    :aria-expanded="expanded"
    :aria-controls="expanded ? controls : undefined"
    @click="toggle"
  >
    <span aria-hidden="true">{{ expanded ? '‹' : '›' }}</span>
  </button>
</template>

<style scoped>
.collapse-button-simple {
  flex-shrink: 0;
  background: transparent;
  border: 1px solid var(--stroke);
  color: var(--text-secondary);
  padding: 2px 8px;
  border-radius: 4px;
  cursor: pointer;
  font-size: 20px;
  line-height: 1;
  transition: all 0.2s;
}

.collapse-button-simple:hover {
  background: var(--bg-2);
  color: var(--text-primary);
  border-color: var(--text-secondary);
}

.expand-button-simple {
  background: var(--bg-1);
  border: 1px solid var(--stroke);
  color: var(--text-secondary);
  padding: 8px 12px;
  border-radius: 4px;
  cursor: pointer;
  font-size: 24px;
  line-height: 1;
  transition: all 0.2s;
}

.expand-button-simple:hover {
  background: var(--bg-2);
  color: var(--text-primary);
  border-color: var(--text-secondary);
}
</style>
