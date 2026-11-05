<script setup lang="ts">
import { computed } from 'vue'
import { useI18n } from 'vue-i18n'
import { Button } from '@/components/ui/button'
import IniaIcon from '@/components/atoms/IniaIcon/IniaIcon.vue'
import { useCurrentUserRoles } from '@/composables/useCurrentUserRoles'
import { useCapabilities } from '@/composables/useCapabilities'
import { CAPABILITIES } from '@/constants/capabilities'
import { useDownloadExecutedWorkflow } from '@/composables/useDownloadExecutedWorkflow'
import type { DownloadExecutedWorkflowParams } from '@/api/executedWorkflows/schema'

interface Props {
  executionId?: string | null
  workflowId?: string | null
  jobId?: string | null
  variant?: 'icon' | 'default'
  subject?: string
}

const props = withDefaults(defineProps<Props>(), {
  executionId: null,
  workflowId: null,
  jobId: null,
  variant: 'icon',
})

const { t: $t } = useI18n()
const { isOperator } = useCurrentUserRoles()
const has = useCapabilities()

const title = computed(() =>
  props.subject
    ? $t('shared.workflowDownload.downloadFor', { subject: props.subject })
    : $t('shared.workflowDownload.downloadTitle'),
)
const { download, isPending } = useDownloadExecutedWorkflow()

const downloadParams = computed<DownloadExecutedWorkflowParams | null>(() => {
  if (typeof props.executionId === 'string' && props.executionId.length > 0) {
    return { executionId: props.executionId }
  }
  if (typeof props.workflowId === 'string' && props.workflowId.length > 0) {
    return { workflowId: props.workflowId }
  }
  if (typeof props.jobId === 'string' && props.jobId.length > 0) {
    return { jobId: props.jobId }
  }
  return null
})

const canRender = computed(
  () => isOperator.value && has(CAPABILITIES.RUN_EVIDENCE) && downloadParams.value !== null,
)

const onClick = (event: MouseEvent) => {
  event.stopPropagation()
  const params = downloadParams.value
  if (!params) return
  void download(params)
}
</script>

<template>
  <Button
    v-if="canRender"
    variant="outline"
    size="sm"
    :is-loading="isPending"
    :title="title"
    :aria-label="props.variant === 'icon' ? title : undefined"
    :class="[
      'download-executed-workflow-btn',
      'btn-utility',
      props.variant === 'icon' ? 'download-executed-workflow-btn--icon' : null,
    ]"
    @click="onClick"
  >
    <IniaIcon name="download" classes="w-4 h-4" />
    <span v-if="props.variant === 'default'">
      {{ $t('shared.workflowDownload.downloadLabel') }}
    </span>
  </Button>
</template>

<style scoped>
@reference "tailwindcss";

.download-executed-workflow-btn {
  transition: color 150ms ease, background-color 150ms ease;
}

/* Compact square-ish button used inside dense table rows.
   Matches the h-8 sm size of sibling buttons but trims horizontal padding
   so the icon sits in a visually square target. */
.download-executed-workflow-btn--icon {
  padding-left: 0.5rem;
  padding-right: 0.5rem;
  width: 2rem;
  flex-shrink: 0;
}
</style>
