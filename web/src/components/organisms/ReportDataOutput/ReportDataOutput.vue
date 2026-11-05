<script setup lang="ts">
import { computed, ref, useId } from 'vue'
import { useI18n } from 'vue-i18n'
import { Button } from '@/components/ui/button'
import IniaIcon from '@/components/atoms/IniaIcon/IniaIcon.vue'
import { useToast } from '@/components/ui/toast/use-toast'
import { useFactTooltip } from '@/composables/useFactTooltip'
import { downloadBlob } from '@/lib/downloadFile'
import { renderDataOutputJson } from '@/lib/dataOutputJson'
import { vSafeHtml } from '@/lib/safeHtml'
import type { ReportDataOutput } from '@/api/workflowReports/schema'

const props = defineProps<{
  output: ReportDataOutput
}>()

const { t: $t } = useI18n()
const { toast } = useToast()
const headingId = useId()

const jsonRef = ref<HTMLElement | null>(null)
useFactTooltip(jsonRef)

const jsonText = computed(() => JSON.stringify(props.output.data, null, 2))
const html = computed(() => renderDataOutputJson(props.output.data, props.output.citations))
const citationsAttr = computed(() =>
  props.output.citations.length > 0 ? JSON.stringify(props.output.citations) : undefined)
const citedSourcesByBlockAttr = computed(() =>
  props.output.citedSourcesByBlock.length > 0 ? JSON.stringify(props.output.citedSourcesByBlock) : undefined)

const onCopy = async () => {
  try {
    await navigator.clipboard.writeText(jsonText.value)
    toast({ class: 'text-sm', description: $t('page.workflowDetails.dataOutputs.copied') })
  } catch {
    toast({ class: 'text-sm', variant: 'destructive', description: $t('page.workflowDetails.dataOutputs.copyFailed') })
  }
}

const onDownload = () => {
  downloadBlob(new Blob([jsonText.value], { type: 'application/json' }), `${props.output.name}.json`)
}
</script>

<template>
  <section class="data-output" :aria-labelledby="headingId">
    <div class="data-output__head">
      <h3 :id="headingId" class="data-output__name">{{ output.name }}</h3>
      <div class="data-output__actions">
        <Button variant="soft" size="sm" @click="onCopy">
          <IniaIcon name="copy" classes="w-4 h-4 mr-2" />
          {{ $t('page.workflowDetails.dataOutputs.copy') }}
        </Button>
        <Button variant="soft" size="sm" @click="onDownload">
          <IniaIcon name="download" classes="w-4 h-4 mr-2" />
          {{ $t('page.workflowDetails.dataOutputs.download') }}
        </Button>
      </div>
    </div>
    <pre
      ref="jsonRef"
      class="data-output__json"
      tabindex="0"
      :aria-label="$t('page.workflowDetails.dataOutputs.jsonLabel', { name: output.name })"
      :data-citations="citationsAttr"
      :data-cited-sources-by-block="citedSourcesByBlockAttr"
      v-safe-html="html"
    />
  </section>
</template>

<style scoped>
.data-output {
  border: 1px solid var(--stroke);
  border-radius: var(--radius-md);
}

.data-output__head {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: var(--spacing-sm);
  padding: var(--spacing-sm) var(--spacing-md);
  border-bottom: 1px solid var(--stroke);
}

.data-output__name {
  flex: 1;
  min-width: 0;
  margin: 0;
  font-family: var(--font-mono, ui-monospace, monospace);
  font-size: 15px;
  font-weight: 600;
  color: var(--text-primary);
  overflow-wrap: anywhere;
}

.data-output__actions {
  display: flex;
  gap: var(--spacing-xs);
}

.data-output__json {
  margin: 0;
  padding: var(--spacing-md);
  max-height: 70vh;
  overflow: auto;
  font-family: var(--font-mono, ui-monospace, monospace);
  font-size: 13px;
  line-height: 1.6;
  white-space: pre;
  color: var(--text-primary);
}

.data-output__json:focus-visible {
  outline: 2px solid var(--ring);
  outline-offset: -2px;
}

.data-output__json :deep(.json-key) {
  color: var(--text-secondary);
}
</style>
