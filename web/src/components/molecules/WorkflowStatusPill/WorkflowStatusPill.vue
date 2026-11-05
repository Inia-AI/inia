<script setup lang="ts">
import { useI18n } from 'vue-i18n'
import WorkflowStatus from '@/components/molecules/WorkflowStatus/WorkflowStatus.vue'

withDefaults(
  defineProps<{
    status: string
    // Tailwind text-* class forwarded to the inner icon.
    iconSize?: string
  }>(),
  { iconSize: 'text-xl' },
)

const { t: $t } = useI18n()
</script>

<template>
  <div class="workflow-status-pill">
    <WorkflowStatus :status="status" :font-size="iconSize" />
    <span class="status-label">
      {{ $t(`shared.enums.workflowState.${status}`) }}
    </span>
  </div>
</template>

<style scoped>
.workflow-status-pill {
  /* `flex-shrink: 0` so the pill keeps its content width when adjacent
     content (long titles, dense rows) tries to squeeze it. */
  display: inline-flex;
  align-items: center;
  flex-shrink: 0;
  gap: 6px;
  padding: 4px 12px 4px 8px;
  border-radius: 9999px;
  background: var(--bg-1);
  border: 1px solid var(--stroke);
  font-size: 13px;
  font-weight: 500;
  white-space: nowrap;
}

/* The icon already carries the colour signal; the text is a quieter
   "what state am I looking at" reading. */
.status-label {
  line-height: 1;
  color: var(--text-secondary);
}
</style>
