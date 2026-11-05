<script setup lang="ts">
import { computed } from 'vue'
import { useI18n } from 'vue-i18n'
import IniaIcon from '@/components/atoms/IniaIcon/IniaIcon.vue'
import { Badge } from '@/components/ui/badge'
import { isCancellableState } from '@/composables/useCancelExecution'
import type { WorkflowState } from '@/enums'

const props = withDefaults(
  defineProps<{
    freezeSourceExecutionId?: string | null
    freezeReplayedCount?: number | null
    freezeEligibleCount?: number | null
    workflowState?: WorkflowState
  }>(),
  {
    freezeSourceExecutionId: null,
    freezeReplayedCount: null,
    freezeEligibleCount: null,
    workflowState: undefined,
  },
)

const { t: $t } = useI18n()

const tooltip = computed(() => {
  if (props.freezeEligibleCount !== null) {
    return $t('shared.replayProvenance.tooltip', {
      source: props.freezeSourceExecutionId,
      replayed: props.freezeReplayedCount ?? 0,
      eligible: props.freezeEligibleCount,
    })
  }
  const key = isCancellableState(props.workflowState) ? 'tooltipPending' : 'tooltipUnrecorded'
  return $t(`shared.replayProvenance.${key}`, { source: props.freezeSourceExecutionId })
})
</script>

<template>
  <Badge
    v-if="freezeSourceExecutionId"
    variant="outline"
    class="replay-provenance-badge gap-1.5 whitespace-nowrap text-[var(--text-secondary)] cursor-help"
    :title="tooltip"
    :aria-label="tooltip"
  >
    <IniaIcon name="clock-rotate-left" classes="text-xs" />
    <span>{{ $t('shared.replayProvenance.label') }}</span>
  </Badge>
</template>
