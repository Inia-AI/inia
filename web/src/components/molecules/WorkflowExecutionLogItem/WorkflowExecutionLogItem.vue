<template>
  <div class="log-item">
    <div class="log-item-header">
      <div class="log-item-title">
        <span class="log-block-name">{{ log.blockName }}</span>
        <span class="log-block-type" :class="blockTypeClass(log.blockType)">
          {{ log.blockType }}
        </span>
      </div>
    </div>

    <div class="log-metadata">
      <div>
        <strong>{{ $t('page.workflowExecutionLogs.item.replacementTag') }}</strong>
        {{ log.replacementTag || '—' }}
      </div>

      <div><strong>{{ $t('page.workflowExecutionLogs.item.model') }}</strong> {{ log.modelUsed ?? '—' }}</div>

      <div class="log-status-container">
        <strong>{{ $t('page.workflowExecutionLogs.item.status') }}</strong>
        <span
          class="log-status"
          :class="{
            'status-success': log.status === 'Completed',
            'status-failed': log.status === 'Failed',
            'status-skipped': log.status === 'Skipped',
            'status-running': log.status === 'Running',
            'status-not-executed': log.status === 'NotExecuted',
          }"
        >
          <IniaIcon v-if="log.status === 'Completed'" name="check" class="text-sm" />
          <IniaIcon v-else-if="log.status === 'Failed'" name="circle-exclamation" class="text-sm" />
          <IniaIcon v-else-if="log.status === 'Skipped'" name="minus" class="text-sm" />
          <IniaIcon v-else-if="log.status === 'Running'" name="spinner" class="text-sm animate-spin" />
          <IniaIcon v-else name="circle-info" class="text-sm" />
          <span>{{ log.status }}</span>
        </span>
        <span v-if="log.refusalCategory" class="log-refusal">
          <IniaIcon name="circle-exclamation" class="text-sm" />
          <span>{{ $t('shared.modelRefusal.badge') }} {{ refusalReason($t, log.refusalCategory) }}</span>
        </span>
      </div>
    </div>

    <!-- Inputs (loaded with details) -->
    <div v-if="logDetail?.inputs && Object.keys(logDetail.inputs).length" class="log-inputs">
      <strong>{{ $t('page.workflowExecutionLogs.item.inputs') }}</strong>
      <ul class="inputs-list">
        <li v-for="(val, key) in logDetail.inputs" :key="key">
          <span class="input-key">{{ key }}:</span> {{ val }}
        </li>
      </ul>
    </div>

    <!-- Prompt Toggle -->
    <div v-if="log.hasPrompt || logDetail?.prompt">
      <button
        type="button"
        class="button-reset toggle-button"
        :aria-label="nameForBlock(promptToggleLabel)"
        :aria-expanded="isPromptExpanded"
        :aria-controls="promptRegionId"
        @click="togglePrompt"
      >
        <span v-if="isLoadingPrompt" class="loading-spinner" aria-hidden="true">⌛</span>
        <span>
          {{ promptToggleLabel }}
          <span aria-hidden="true">{{ isPromptExpanded ? '▲' : '▼' }}</span>
        </span>
      </button>
      <div :id="promptRegionId">
        <div v-if="isPromptExpanded && logDetail?.prompt?.trim()" class="log-detail-content">
          <div v-if="hasTruncatedPlaceholders" class="mb-3">
            <button
              @click="handleTogglePlaceholders"
              :disabled="isLoadingPlaceholders"
              :aria-label="isLoadingPlaceholders ? undefined : nameForBlock(placeholderToggleLabel)"
              class="placeholder-toggle-btn"
              type="button"
            >
              <span v-if="isLoadingPlaceholders" class="animate-spin" aria-hidden="true">⌛</span>
              <span v-else-if="isShowingFullValues" aria-hidden="true">📄</span>
              <span v-else aria-hidden="true">🔍</span>
              {{ isLoadingPlaceholders ? $t('common.loading') : placeholderToggleLabel }}
            </button>
          </div>

          <div class="prompt-display" v-safe-html.model="formattedPrompt" />
        </div>
        <div v-else-if="isPromptExpanded && isLoadingPrompt" class="log-detail-content loading">
          {{ $t('page.workflowExecutionLogs.item.loadingPrompt') }}
        </div>
        <div v-else-if="isPromptExpanded && logDetail?.prompt != null" class="log-detail-content note">
          {{ $t('page.workflowExecutionLogs.item.noPromptPreview') }}
        </div>
      </div>
    </div>

    <!-- Output Toggle -->
    <div v-if="log.hasOutput || logDetail?.output">
      <button
        type="button"
        class="button-reset toggle-button"
        :aria-label="nameForBlock(outputToggleLabel)"
        :aria-expanded="isOutputExpanded"
        :aria-controls="outputRegionId"
        @click="toggleOutput"
      >
        <span v-if="isLoadingOutput" class="loading-spinner" aria-hidden="true">⌛</span>
        <span>
          {{ outputToggleLabel }}
          <span aria-hidden="true">{{ isOutputExpanded ? '▲' : '▼' }}</span>
        </span>
      </button>
      <div :id="outputRegionId">
        <div
          v-if="isOutputExpanded && logDetail?.output?.trim()"
          class="log-detail-content log-output-content"
          v-safe-html.model="logDetail.output"
        />
        <div v-else-if="isOutputExpanded && isLoadingOutput" class="log-detail-content loading">
          {{ $t('page.workflowExecutionLogs.item.loadingOutput') }}
        </div>
        <div v-else-if="isOutputExpanded && logDetail?.output != null" class="log-detail-content note">
          {{ $t('page.workflowExecutionLogs.item.noOutput') }}
        </div>
      </div>
    </div>

    <div v-if="log.error" class="log-error">
      {{ log.error }}
    </div>
  </div>
</template>

<script setup lang="ts">
import { ref, computed, watch, useId } from 'vue'
import { useI18n } from 'vue-i18n'
import { escapeHtml } from '@/lib/escapeHtml'
import { vSafeHtml } from '@/lib/safeHtml'
import { refusalReason } from '@/utils/modelRefusal'
import type {
  WorkflowExecutionLogSummary,
  WorkflowExecutionLogDetail,
} from '@/api/workflowExecutions/schema'

const props = defineProps<{
  log: WorkflowExecutionLogSummary
  logDetail?: WorkflowExecutionLogDetail | null
  isLoadingPrompt?: boolean
  isLoadingOutput?: boolean
  fullPlaceholders?: Record<string, string>
  isLoadingPlaceholders?: boolean
  isShowingFullValues?: boolean
}>()

const emit = defineEmits<{
  (e: 'load-detail', logId: string, contentType: 'prompt' | 'output'): void
  (e: 'fetch-placeholders', logId: string, truncatedKeys: string[]): void
  (e: 'toggle-full-values', logId: string): void
}>()

const { t: $t } = useI18n()

// Local expanded state
const isPromptExpanded = ref(false)
const isOutputExpanded = ref(false)

const itemId = useId()
const promptRegionId = `${itemId}-prompt`
const outputRegionId = `${itemId}-output`

const promptToggleLabel = computed(() =>
  isPromptExpanded.value
    ? $t('page.workflowExecutionLogs.toggles.hidePrompt')
    : $t('page.workflowExecutionLogs.toggles.showPrompt'),
)
const outputToggleLabel = computed(() =>
  isOutputExpanded.value
    ? $t('page.workflowExecutionLogs.toggles.hideOutput')
    : $t('page.workflowExecutionLogs.toggles.showOutput'),
)

const placeholderToggleLabel = computed(() =>
  props.isShowingFullValues
    ? $t('page.workflowExecutionLogs.toggles.showTruncatedValues')
    : $t('page.workflowExecutionLogs.toggles.showFullValues'),
)

const nameForBlock = (label: string) => {
  const block = props.log.blockName.trim()
  if (!block) return label
  const section = props.log.sectionName?.trim()
  return section
    ? $t('page.workflowExecutionLogs.toggles.forBlockInSection', { label, block, section })
    : $t('page.workflowExecutionLogs.toggles.forBlock', { label, block })
}

const blockTypeClass = (type?: string) => {
  if (!type) return 'bg-gray-500 text-white'
  return (
    {
      AiQuery: 'bg-purple-600 text-white',
      Agent: 'bg-amber-500 text-black',
      Merge: 'bg-blue-600 text-white',
      CalculateNumber: 'bg-orange-400 text-black',
      SimpleRag: 'bg-teal-500 text-black',
      LoopStart: 'bg-gray-400 text-black',
      LoopEnd: 'bg-gray-500 text-white',
      WebSnippetSearch: 'bg-indigo-500 text-white',
      IntelligentSearch: 'bg-red-600 text-white',
      ExtractFinancialMetric: 'bg-red-600 text-white',
      BuildTopicsForAllDocuments: 'bg-cyan-700 text-white',
      DataApiCall: 'bg-lime-600 text-black',
      WebApiCall: 'bg-green-700 text-white',
      DynamicChart: 'bg-sky-700 text-white',
      Json2Xlsx: 'bg-emerald-700 text-white',
      CreatePeerAnalysis: 'bg-indigo-600 text-white',
      ExchangeRate: 'bg-emerald-500 text-black',
    }[type] || 'bg-gray-500 text-white'
  )
}

const togglePrompt = () => {
  const wasExpanded = isPromptExpanded.value
  isPromptExpanded.value = !wasExpanded

  // Load detail if expanding and prompt not already loaded
  // Use null check (not falsy) to distinguish "not loaded" from "loaded but empty"
  if (!wasExpanded && props.logDetail?.prompt == null) {
    emit('load-detail', props.log.id, 'prompt')
  }
}

const toggleOutput = () => {
  const wasExpanded = isOutputExpanded.value
  isOutputExpanded.value = !wasExpanded

  // Load detail if expanding and output not already loaded
  // Use null check (not falsy) to distinguish "not loaded" from "loaded but empty"
  if (!wasExpanded && props.logDetail?.output == null) {
    emit('load-detail', props.log.id, 'output')
  }
}

const truncatedKeys = computed(() =>
  Object.entries(props.logDetail?.promptPlaceholderValues ?? {})
    .filter(([, { truncated }]) => truncated)
    .map(([key]) => key),
)

const hasTruncatedPlaceholders = computed(() => truncatedKeys.value.length > 0)

const handleTogglePlaceholders = () => {
  if (!hasTruncatedPlaceholders.value) return

  if (props.isShowingFullValues) {
    // Currently showing full values: collapse back to truncated
    emit('toggle-full-values', props.log.id)
  } else {
    // Want to show full values
    if (!props.fullPlaceholders) {
      // Full placeholders not yet loaded: request them but do NOT toggle view yet
      // The parent will toggle after fetch completes
      emit('fetch-placeholders', props.log.id, truncatedKeys.value)
      return
    }
    // Full placeholders already available: safe to toggle to full view
    emit('toggle-full-values', props.log.id)
  }
}

// Format prompt with placeholder tooltips
const formattedPrompt = computed(() => {
  const prompt = props.logDetail?.prompt
  const promptPlaceholderValues = props.logDetail?.promptPlaceholderValues ?? {}

  if (!prompt) return ''

  if (Object.keys(promptPlaceholderValues).length === 0) {
    return prompt
  }

  let formattedPrompt = prompt
  const isShowingFull = props.isShowingFullValues

  // Process each placeholder
  for (const [key, { value, truncated, translated }] of Object.entries(promptPlaceholderValues)) {
    // Determine which value to display
    let displayValue = value
    let isDisplayingTruncated = truncated

    if (isShowingFull && props.fullPlaceholders?.[key]) {
      displayValue = props.fullPlaceholders[key]
      isDisplayingTruncated = false
    }

    const escapedValue = escapeHtml(displayValue)

    // Build tooltip text and escape it for use in HTML attribute
    let tooltipText = $t('page.workflowExecutionLogs.placeholder.tooltip', { key })
    if (isDisplayingTruncated) tooltipText += ` ${$t('page.workflowExecutionLogs.placeholder.truncated')}`
    if (translated) tooltipText += ` ${$t('page.workflowExecutionLogs.placeholder.translated')}`
    const escapedTooltip = escapeHtml(tooltipText)
    const escapedTruncatedNote = escapeHtml($t('page.workflowExecutionLogs.placeholder.valueTruncated'))

    // Create tooltip HTML for this placeholder
    const tooltipHtml = `<span
      class="px-1 py-0.5 rounded-sm text-info bg-info/10 border border-info/30 cursor-help inline-flex items-center gap-1 text-sm"
      title="${escapedTooltip}"
    >
      ${escapedValue}${isDisplayingTruncated ? `<span class="text-xs ml-1" title="${escapedTruncatedNote}">⚠️</span>` : ''}
    </span>`

    // Escape special regex characters in the key
    const escapedKey = key.replace(/[.*+?^${}()|[\]\\]/g, '\\$&')

    // Replace all occurrences of this placeholder (case-insensitive)
    formattedPrompt = formattedPrompt.replace(
      new RegExp(`\\{${escapedKey}\\}`, 'gi'),
      () => tooltipHtml,
    )
  }

  return formattedPrompt
})

// Watch for log changes to reset expanded state if needed
watch(
  () => props.log.id,
  () => {
    isPromptExpanded.value = false
    isOutputExpanded.value = false
  },
)
</script>

<style scoped>
/* Log Item */
.log-item {
  background: var(--bg-2);
  border: 1px solid var(--stroke);
  border-radius: var(--radius-card);
  padding: var(--spacing-md);
  box-shadow: 0 1px 3px rgba(0, 0, 0, 0.1);
}

.log-item-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  margin-bottom: var(--spacing-xs);
}

.log-item-title {
  display: flex;
  align-items: center;
  gap: var(--spacing-xs);
}

.log-block-name {
  font-size: 18px;
  font-weight: 600;
  color: var(--text-primary);
}

.log-block-type {
  font-size: 12px;
  font-weight: 500;
  padding: 4px 8px;
  border-radius: var(--radius-small);
}

/* Metadata */
.log-metadata {
  display: flex;
  flex-direction: column;
  gap: var(--spacing-xs);
  margin-bottom: var(--spacing-sm);
  font-size: 14px;
  color: var(--text-secondary);
}

.log-status-container {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: var(--spacing-xs);
}

.log-status {
  display: inline-flex;
  align-items: center;
  gap: var(--spacing-xs);
  padding: 4px 8px;
  border-radius: var(--radius-small);
  font-size: 12px;
  font-weight: 500;
}

.status-success {
  background: #15803d;
  color: white;
}

.status-failed {
  background: #dc2626;
  color: white;
}

.status-skipped {
  background: #eab308;
  color: black;
}

.status-running {
  background: var(--accent-warning-fill);
  color: black;
}

.log-refusal {
  display: inline-flex;
  align-items: center;
  gap: var(--spacing-xs);
  padding: 4px 8px;
  border-radius: var(--radius-small);
  border: 1px solid var(--accent-warning-border);
  background: var(--accent-warning-light);
  color: var(--accent-warning);
  font-size: 12px;
  font-weight: 500;
}

.status-not-executed {
  background: var(--bg-1);
  color: var(--text-secondary);
  border: 1px solid var(--stroke);
}

/* Inputs */
.log-inputs {
  margin-bottom: var(--spacing-xs);
  color: var(--text-secondary);
  font-size: 14px;
}

.inputs-list {
  margin-left: var(--spacing-md);
  margin-top: 4px;
  list-style: disc;
  font-size: 14px;
}

.input-key {
  font-weight: 500;
}

/* Toggle and Detail Content */
.toggle-button {
  font-size: 14px;
  color: var(--accent-indigo-from);
  cursor: pointer;
  margin-top: var(--spacing-xs);
  transition: opacity 0.15s ease-in-out;
  display: flex;
  align-items: center;
  gap: var(--spacing-xs);
}

.toggle-button:hover {
  opacity: 0.8;
}

.loading-spinner {
  animation: spin 1s linear infinite;
}

@keyframes spin {
  from {
    transform: rotate(0deg);
  }
  to {
    transform: rotate(360deg);
  }
}

.log-detail-content {
  background: var(--bg-0);
  font-size: 14px;
  color: var(--text-primary);
  border-radius: var(--radius-small);
  padding: var(--spacing-sm) var(--spacing-md);
  white-space: pre-wrap;
  margin-top: 4px;
  overflow-x: auto;
}

.log-detail-content.loading,
.log-detail-content.note {
  color: var(--text-secondary);
  font-style: italic;
}

.placeholder-toggle-btn {
  background: var(--primary);
  color: var(--primary-foreground);
  border: none;
  border-radius: var(--radius-button);
  padding: 6px 12px;
  font-size: 12px;
  font-weight: 600;
  cursor: pointer;
  display: inline-flex;
  align-items: center;
  gap: var(--spacing-xs);
  transition: all 0.2s ease-in-out;
}

.placeholder-toggle-btn:hover:not(:disabled) {
  transform: translateY(-1px);
  box-shadow: 0 4px 12px rgba(26, 56, 80, 0.3);
}

.placeholder-toggle-btn:disabled {
  opacity: 0.5;
  cursor: not-allowed;
}

.prompt-display {
  line-height: 1.6;
}

/* Output content - preserve whitespace like pre but with valid HTML structure */
.log-output-content {
  font-family: monospace;
  white-space: pre-wrap;
  word-break: break-word;
}

/* Error */
.log-error {
  font-size: 14px;
  color: var(--accent-error);
  margin-top: var(--spacing-xs);
}
</style>
