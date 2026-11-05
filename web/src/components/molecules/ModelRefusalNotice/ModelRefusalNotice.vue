<script setup lang="ts">
import { computed } from 'vue'
import { useI18n } from 'vue-i18n'
import { RouterLink } from 'vue-router'
import IniaIcon from '@/components/atoms/IniaIcon/IniaIcon.vue'
import type { WorkflowExecutionLogSummary } from '@/api/workflowExecutions/schema'
import { refusalReason } from '@/utils/modelRefusal'

const props = defineProps<{
  executionId: string
  logs?: WorkflowExecutionLogSummary[] | null
}>()

const { t: $t } = useI18n()

const refused = computed(() => {
  const groups = new Map<string, { log: WorkflowExecutionLogSummary; count: number }>()
  for (const log of props.logs ?? []) {
    if (!log.refusalCategory) {
      continue
    }
    const key = `${log.sectionName ?? ''}\u0000${log.blockName}\u0000${log.refusalCategory}`
    const group = groups.get(key)
    if (group) {
      group.count += 1
    } else {
      groups.set(key, { log, count: 1 })
    }
  }
  return [...groups.values()]
})

const refusedCount = computed(() => refused.value.reduce((total, group) => total + group.count, 0))
</script>

<template>
  <div v-if="refused.length" class="banner banner-warning model-refusal-notice" role="status">
    <IniaIcon name="circle-exclamation" class="model-refusal-icon" />
    <div class="model-refusal-text">
      <p class="model-refusal-label">{{ $t('shared.modelRefusal.label', refusedCount) }}</p>
      <p class="model-refusal-body">{{ $t('shared.modelRefusal.body') }}</p>
      <ul class="model-refusal-list">
        <li v-for="{ log, count } in refused" :key="log.id">
          <span class="model-refusal-name">{{ log.blockName }}</span>{{ ' ' }}<template v-if="log.sectionName">{{ $t('shared.modelRefusal.inSection', { section: log.sectionName }) }}{{ ' ' }}</template>{{ refusalReason($t, log.refusalCategory) }}<template v-if="count > 1">{{ ' ' }}{{ $t('shared.modelRefusal.times', { count }) }}</template>
        </li>
      </ul>
      <RouterLink class="model-refusal-link" :to="{ name: 'ExecutionLogs', params: { id: executionId } }">
        {{ $t('shared.modelRefusal.openLogs') }}
      </RouterLink>
    </div>
  </div>
</template>

<style scoped>
.model-refusal-notice {
  display: flex;
  align-items: flex-start;
  gap: var(--spacing-sm);
  border: 1px solid var(--accent-warning-border);
}

.model-refusal-icon {
  flex-shrink: 0;
  margin-top: 2px;
}

.model-refusal-text {
  display: flex;
  flex-direction: column;
  gap: 2px;
  min-width: 0;
}

.model-refusal-label {
  font-size: 14px;
  font-weight: 600;
}

.model-refusal-body,
.model-refusal-list {
  overflow-wrap: anywhere;
}

.model-refusal-list {
  margin: var(--spacing-xs) 0;
  padding-left: var(--spacing-md);
  list-style: disc;
}

.model-refusal-name {
  font-weight: 600;
}

.model-refusal-link {
  text-decoration: underline;
}
</style>
