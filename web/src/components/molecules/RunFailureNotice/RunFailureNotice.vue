<script setup lang="ts">
import { computed } from 'vue'
import { useI18n } from 'vue-i18n'
import IniaIcon from '@/components/atoms/IniaIcon/IniaIcon.vue'
import { Button } from '@/components/ui/button'
import { describeRunFailure } from '@/utils/runFailure'

const props = defineProps<{
  failureKind?: string | null
  failureDetail?: string | null
  /** Whether the report can be started again from this page; without it the notice offers no action. */
  canAct?: boolean
}>()

const emit = defineEmits<{
  changeDocuments: []
  runAgain: []
}>()

const { t: $t } = useI18n()

const failure = computed(() => describeRunFailure($t, props.failureKind, props.failureDetail))

const action = computed(() => (props.canAct ? failure.value?.action ?? null : null))

const onAction = () => {
  if (action.value === 'changeDocuments') {
    emit('changeDocuments')
  } else if (action.value === 'runAgain') {
    emit('runAgain')
  }
}
</script>

<template>
  <div v-if="failure" class="banner banner-error run-failure-notice">
    <IniaIcon name="circle-exclamation" class="run-failure-icon" />
    <div class="run-failure-text">
      <p class="run-failure-label">{{ failure.label }}</p>
      <p v-if="failure.body" class="run-failure-body">{{ failure.body }}</p>
      <div v-if="action" class="run-failure-actions">
        <Button type="button" variant="outline" size="sm" class="run-failure-action" @click="onAction">
          {{ $t(`shared.runFailure.actions.${action}`) }}
        </Button>
      </div>
      <details v-if="failure.detail" class="run-failure-detail">
        <summary>{{ $t('shared.runFailure.technicalDetails') }}</summary>
        <p>{{ failure.detail }}</p>
      </details>
    </div>
  </div>
</template>

<style scoped>
.run-failure-notice {
  display: flex;
  align-items: flex-start;
  gap: var(--spacing-sm);
  border: 1px solid var(--accent-error-border);
}

.run-failure-icon {
  flex-shrink: 0;
  margin-top: 2px;
}

.run-failure-text {
  display: flex;
  flex-direction: column;
  gap: 2px;
  min-width: 0;
}

.run-failure-label {
  font-size: 14px;
  font-weight: 600;
}

.run-failure-body {
  overflow-wrap: anywhere;
}

.run-failure-actions {
  margin-top: var(--spacing-xs);
}

.run-failure-detail {
  margin-top: 4px;
  font-size: 12px;
  overflow-wrap: anywhere;
}

.run-failure-detail summary {
  cursor: pointer;
}
</style>
