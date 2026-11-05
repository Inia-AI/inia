<template>
  <div class="page-view">
    <div class="page-header-with-action">
      <div class="header-left">
        <Button variant="ghost" size="sm" as-child>
          <RouterLink :to="reportRoute(id)">
            <IniaIcon name="chevron-right" classes="back-icon" />
            {{ $t('page.workflowExecutionLogs.backToReport') }}
          </RouterLink>
        </Button>
        <div>
          <h1 class="page-title">{{ $t('page.workflowExecutionLogs.title') }}</h1>
          <p v-if="reportName" class="page-subtitle report-name">{{ reportName }}</p>
        </div>
      </div>
      <DownloadExecutedWorkflowButton
        :execution-id="id"
        variant="default"
      />
    </div>

    <div class="logs-content">
      <div class="logs-main">
        <div v-if="!isExecutionLoading" class="execution-meta">
          <div class="meta-pill">
            <span class="meta-pill-label">{{ $t('page.workflowExecutionLogs.meta.counterparty') }}</span>
            <span class="meta-pill-value">{{ counterpartyName }}</span>
          </div>
          <div v-if="startedAtFormatted" class="meta-pill">
            <IniaIcon name="clock" class="meta-pill-icon" />
            <span class="meta-pill-label">{{ $t('page.workflowExecutionLogs.meta.started') }}</span>
            <span class="meta-pill-value">{{ startedAtFormatted }}</span>
          </div>
          <div v-if="liveDuration" class="meta-pill">
            <IniaIcon name="clock" class="meta-pill-icon" />
            <span class="meta-pill-label">{{ $t('page.workflowExecutionLogs.meta.duration') }}</span>
            <span class="meta-pill-value duration-display">{{ liveDuration }}</span>
          </div>
        </div>

        <!-- Failed Blocks Summary -->
        <div v-if="failedLogs.length" class="alert-failure">
          <div class="alert-header">
            <div class="alert-title">
              <span class="alert-dot" />
              {{ $t('page.workflowExecutionLogs.failedBlocks') }} ({{ failedLogs.length }})
            </div>
            <div class="alert-sub">{{ $t('page.workflowExecutionLogs.clickToJump') }}</div>
          </div>

          <div class="failed-list">
            <button
              v-for="log in failedLogs"
              :key="log.id || `${log.blockName}-${log.createdAt}`"
              class="failure-row minimal"
              @click="scrollToFailed(log)"
              :title="log.blockName"
              type="button"
            >
              <div class="kv-row">
                <span class="failure-label">{{ $t('page.workflowExecutionLogs.section') }}</span>
                <span class="failure-section" v-if="log.sectionName">{{ log.sectionName }}</span>
              </div>
              <div class="kv-row">
                <span class="failure-label">{{ $t('page.workflowExecutionLogs.block') }}</span>
                <span class="failure-row-name truncate">{{ log.blockName }}</span>
              </div>
              <div v-if="log.refusalCategory" class="kv-row failure-refusal">
                {{ $t('shared.modelRefusal.badge') }} {{ refusalReason($t, log.refusalCategory) }}
              </div>
            </button>
          </div>
        </div>

        <div
          v-if="!isExecutionLoading && Object.keys(variables).length"
          class="card-panel variables-card"
        >
          <h2 class="variables-title">{{ $t('page.workflowExecutionLogs.variables') }}</h2>
          <ul class="variables-list">
            <li v-for="(val, key) in variables" :key="key">
              <span class="variable-key">{{ key }}: </span>
              <span v-if="Array.isArray(val)">
                {{ val.join(', ') }}
              </span>
              <span v-else>
                {{ val }}
              </span>
            </li>
          </ul>
        </div>

        <div v-if="isLogsLoading" class="loading-state">{{ $t('page.workflowExecutionLogs.loadingLogs') }}</div>
        <div v-else-if="logs.length === 0" class="empty-state">
          {{ $t('page.workflowExecutionLogs.noLogs') }}
        </div>

        <!-- Logs List - grouped by section -->
        <div v-else class="logs-list">
          <div
            v-for="(sectionLogs, section, sectionIndex) in groupedLogs"
            :key="section"
            class="log-section"
          >
            <!-- Section Header -->
            <div class="section-header" :id="`section-${slugify(section as string)}`">
              {{ sectionIndex + 1 }}. {{ (section as string).toUpperCase() }}
            </div>

            <!-- Log Items in Section -->
            <div class="section-content">
              <div
                v-for="(log, logIndex) in sectionLogs"
                :key="log.id"
                :id="`log-${slugify(section as string)}-${logIndex}`"
                class="log-item-wrapper"
              >
                <WorkflowExecutionLogItem
                  :log="log"
                  :log-detail="logDetails[log.id]"
                  :is-loading-prompt="loadingPrompt.has(log.id)"
                  :is-loading-output="loadingOutput.has(log.id)"
                  :full-placeholders="fullPlaceholders[log.id]"
                  :is-loading-placeholders="loadingPlaceholders.has(log.id)"
                  :is-showing-full-values="showingFullValues.has(log.id)"
                  @load-detail="(logId, contentType) => loadLogDetail(logId, contentType)"
                  @fetch-placeholders="fetchFullPlaceholders"
                  @toggle-full-values="toggleFullValues"
                />
              </div>
            </div>
          </div>
        </div>
      </div>

      <nav class="logs-sidebar" :aria-label="$t('page.workflowExecutionLogs.navigationLabel')">
        <h2 class="sidebar-title">{{ $t('page.workflowExecutionLogs.navigation') }}</h2>
        <div
          v-for="(sectionLogs, section, index) in groupedLogs"
          :key="section"
          class="sidebar-section"
        >
          <button
            type="button"
            class="sidebar-section-title"
            @click="scrollToSection(slugify(section as string))"
          >
            {{ index + 1 }}. {{ (section as string).toUpperCase() }}
          </button>

          <button
            v-for="(log, logIndex) in sectionLogs"
            :key="log.id"
            type="button"
            class="sidebar-log-item"
            @click="scrollToLog(slugify(section as string), logIndex as number)"
          >
            <span class="sidebar-log-index">{{ logIndex + 1 }}.</span>
            <span class="sidebar-log-name">{{ log.blockName }}</span>
            <span class="sr-only">{{ $t('page.workflowExecutionLogs.inSection', { section }) }}</span>
          </button>
        </div>
      </nav>
    </div>
  </div>
</template>

<script setup lang="ts">
import { ref, computed, watch } from 'vue'
import { useRoute } from 'vue-router'
import { useI18n } from 'vue-i18n'
import { useTitle } from '@vueuse/core'
import API from '@/api'
import { WorkflowExecutionLogItem, DownloadExecutedWorkflowButton } from '@/components/molecules'
import { IniaIcon } from '@/components/atoms'
import { Button } from '@/components/ui/button'
import { formatDateTime } from '@/lib/utils'
import { reportTitle } from '@/lib/reportTitle'
import { reportRoute } from '@/lib/routes'
import { inFlightRefetchInterval } from '@/lib/workflowRefetch'
import { useLiveDuration } from '@/composables/useLiveDuration'
import { refusalReason } from '@/utils/modelRefusal'
import type {
  WorkflowExecutionLogSummary,
  WorkflowExecutionLogDetail,
} from '@/api/workflowExecutions/schema'

const { t: $t } = useI18n()

const route = useRoute()
const id = computed(() => route.params.id as string)

const { data: logsData, isLoading: isLogsLoading, refetch: refetchLogs } =
  API.WorkflowExecutions.useGetWorkflowExecutionLogs(id.value)
const { data: executionData, isLoading: isExecutionLoading } =
  API.WorkflowReports.useGetWorkflowReportById(id.value, {
    refetchInterval: inFlightRefetchInterval(),
  })
watch(
  () => executionData.value?.workflowState,
  (state, previous) => {
    if (state && previous && state !== previous) {
      void refetchLogs()
    }
  },
)

const logDetails = ref<{ [logId: string]: WorkflowExecutionLogDetail }>({})
const loadingPrompt = ref<Set<string>>(new Set())
const loadingOutput = ref<Set<string>>(new Set())

const slugify = (text: string): string => {
  const slug = text
    .toLowerCase()
    .replace(/[^a-z0-9]+/g, '-')
    .replace(/(^-|-$)/g, '')
  return slug || 'unnamed'
}

const fullPlaceholders = ref<{ [logId: string]: Record<string, string> }>({})
const loadingPlaceholders = ref<Set<string>>(new Set())
const showingFullValues = ref<Set<string>>(new Set())

const logs = computed(() => logsData.value || [])

// Preserve the backend ordering (section order, then block order). A client-side
// createdAt sort would reintroduce parallel completion order, not authored order.
const groupedLogs = computed(() => {
  const grouped: Record<string, WorkflowExecutionLogSummary[]> = {}

  for (const log of logs.value) {
    const section = log.sectionName || $t('page.workflowExecutionLogs.unknownSection')
    if (!grouped[section]) {
      grouped[section] = []
    }
    grouped[section].push(log)
  }
  return grouped
})

const failedLogs = computed(() => logs.value.filter(log => log.status === 'Failed'))

const variables = computed(() => executionData.value?.variables ?? {})

const counterpartyName = computed(() => executionData.value?.counterpartyName ?? '—')

const reportName = computed(() => (executionData.value ? reportTitle(executionData.value) : undefined))

const appTitle = document.title

useTitle(
  () =>
    reportName.value
      ? $t('page.workflowExecutionLogs.documentTitle', { report: reportName.value })
      : $t('page.workflowExecutionLogs.title'),
  { titleTemplate: `%s · ${appTitle}` },
)

const startedAtFormatted = computed(() => {
  const startedAt = executionData.value?.startedAt
  if (!startedAt) return null
  const formatted = formatDateTime(startedAt)
  return formatted === '-' ? null : formatted
})

const liveDuration = useLiveDuration(executionData)

const scrollToSection = (section: string) => {
  const el = document.getElementById(`section-${section}`)
  if (el) {
    el.scrollIntoView({ behavior: 'smooth', block: 'start' })
  }
}

const scrollToLog = (section: string, logIndex: number) => {
  const el = document.getElementById(`log-${section}-${logIndex}`)
  if (el) {
    el.scrollIntoView({ behavior: 'smooth', block: 'start' })
  }
}

const scrollToFailed = (failedLog: WorkflowExecutionLogSummary) => {
  const section = failedLog.sectionName || $t('page.workflowExecutionLogs.unknownSection')
  const sectionLogs = groupedLogs.value[section] || []
  const logIndex = sectionLogs.findIndex(
    (l) =>
      l.id === failedLog.id ||
      (l.blockName === failedLog.blockName && l.createdAt === failedLog.createdAt),
  )

  const sectionSlug = slugify(section)
  if (logIndex >= 0) {
    scrollToLog(sectionSlug, logIndex)
  } else {
    scrollToSection(sectionSlug)
  }
}

const loadLogDetail = async (logId: string, contentType: 'prompt' | 'output') => {
  const loadingSet = contentType === 'prompt' ? loadingPrompt : loadingOutput

  if (loadingSet.value.has(logId)) return

  // Use null check (not falsy) to distinguish "not loaded" from "loaded but empty"
  const existingDetail = logDetails.value[logId]
  const needsPrompt = contentType === 'prompt' && existingDetail?.prompt == null
  const needsOutput = contentType === 'output' && existingDetail?.output == null

  if (!needsPrompt && !needsOutput) return

  try {
    loadingSet.value.add(logId)
    const response = await API.WorkflowExecutions.getLogDetail(id.value, logId, {
      // Only load what was specifically requested
      includePrompt: needsPrompt,
      includeOutput: needsOutput,
    })
    const detail = needsPrompt ? { ...response, prompt: response.prompt ?? '' } : response

    // Merge with existing detail if present (re-read to avoid race conditions)
    const currentDetail = logDetails.value[logId]
    if (currentDetail) {
      logDetails.value[logId] = {
        ...currentDetail,
        ...detail,
        // Preserve existing values if new detail didn't load them
        prompt: needsPrompt ? detail.prompt : currentDetail.prompt,
        promptPlaceholderValues: needsPrompt ? detail.promptPlaceholderValues : currentDetail.promptPlaceholderValues,
        output: needsOutput ? detail.output : currentDetail.output,
        inputs: detail.inputs ?? currentDetail.inputs,
      }
    } else {
      logDetails.value[logId] = detail
    }
  } catch (error) {
    console.error('Failed to load log detail:', error)
  } finally {
    loadingSet.value.delete(logId)
  }
}

const fetchFullPlaceholders = async (logId: string, truncatedKeys: string[]) => {
  if (loadingPlaceholders.value.has(logId)) return

  try {
    loadingPlaceholders.value.add(logId)
    const fullValues = await API.WorkflowExecutions.getLogPlaceholders(
      id.value,
      logId,
      truncatedKeys,
    )
    fullPlaceholders.value[logId] = fullValues
    // Auto-toggle to show full values after fetch completes
    showingFullValues.value.add(logId)
  } catch (error) {
    console.error('Failed to fetch full placeholders:', error)
  } finally {
    loadingPlaceholders.value.delete(logId)
  }
}

const toggleFullValues = (logId: string) => {
  if (showingFullValues.value.has(logId)) {
    showingFullValues.value.delete(logId)
  } else {
    showingFullValues.value.add(logId)
  }
}
</script>

<style scoped>
/* ========================================
   WORKFLOW EXECUTION LOGS - COMPONENT-SPECIFIC STYLES
   Only unique styles for this complex logs view
   ======================================== */

.header-left {
  display: flex;
  align-items: flex-start;
  gap: var(--spacing-sm);
}

.back-icon {
  transform: rotate(180deg);
}

.report-name {
  overflow-wrap: anywhere;
}

/* Content Layout - main + sidebar */
.logs-content {
  display: flex;
  gap: var(--spacing-md);
}

.logs-main {
  flex: 1;
  display: flex;
  flex-direction: column;
  gap: var(--spacing-lg);
}

/* Counterparty info display */
.counterparty-info {
  font-size: 14px;
  color: var(--text-secondary);
  margin-top: 4px;
}

.info-label {
  font-weight: 600;
  color: var(--text-primary);
}

/* Execution metadata pills (counterparty, started, duration) */
.execution-meta {
  display: flex;
  flex-wrap: wrap;
  gap: var(--spacing-xs);
  margin-top: 4px;
}

.meta-pill {
  display: inline-flex;
  align-items: center;
  gap: 8px;
  padding: 6px 12px;
  background: var(--bg-1);
  border: 1px solid var(--stroke);
  border-radius: 999px;
  font-size: 13px;
  color: var(--text-secondary);
}

.meta-pill-icon {
  font-size: 11px;
  color: var(--text-secondary);
}

.meta-pill-label {
  font-size: 10px;
  font-weight: 700;
  letter-spacing: 0.06em;
  text-transform: uppercase;
  color: var(--text-muted);
}

.meta-pill-value {
  font-weight: 600;
  color: var(--text-primary);
}


/* Variables card - extends card-panel */
.variables-card {
  font-size: 14px;
  color: var(--text-secondary);
}

.variables-title {
  font-size: 18px;
  color: var(--text-primary);
  font-weight: 600;
  margin-bottom: var(--spacing-xs);
}

/* Failure Alert */
.alert-failure {
  background: linear-gradient(180deg, var(--accent-error-light), rgba(220, 38, 38, 0.08));
  border: 1px solid var(--accent-error-border);
  border-radius: var(--radius-card);
  padding: var(--spacing-md);
}

.alert-header {
  display: flex;
  align-items: baseline;
  justify-content: space-between;
  margin-bottom: var(--spacing-sm);
}

.alert-title {
  display: inline-flex;
  align-items: center;
  gap: 8px;
  font-size: 18px;
  font-weight: 600;
  color: var(--text-primary);
}

.alert-dot {
  width: 10px;
  height: 10px;
  border-radius: 999px;
  background: #dc2626;
  box-shadow: 0 0 0 4px rgba(220, 38, 38, 0.2);
}

.alert-sub {
  font-size: 12px;
  color: var(--text-primary);
  font-weight: 600;
}

/* Vertical failed items layout and stronger styling */
.failed-list {
  display: flex;
  flex-direction: column;
  gap: 8px;
}

.failure-row {
  background: var(--accent-error-light);
  color: var(--text-primary);
  border: 1px solid var(--accent-error-border);
  border-radius: var(--radius-card);
  padding: 10px 12px;
  text-align: left;
  cursor: pointer;
  display: flex;
  flex-direction: column;
  gap: 6px;
  transition:
    background 0.15s ease-in-out,
    transform 0.15s ease-in-out;
}

.failure-row:hover {
  transform: translateY(-1px);
  background: rgba(220, 38, 38, 0.18);
}

.kv-row {
  display: grid;
  grid-template-columns: 84px 1fr;
  align-items: center;
  gap: 8px;
}

.failure-label {
  font-size: 10px;
  font-weight: 700;
  letter-spacing: 0.04em;
  text-transform: uppercase;
  padding: 2px 6px;
  border-radius: 6px;
  background: rgba(0, 0, 0, 0.15);
  color: var(--text-primary);
  display: inline-flex;
  justify-content: center;
}

.failure-section {
  background: rgba(220, 38, 38, 0.25);
  border: 1px solid rgba(220, 38, 38, 0.5);
  color: var(--text-primary);
  font-size: 11px;
  padding: 2px 8px;
  border-radius: 999px;
  letter-spacing: 0.02em;
  text-transform: uppercase;
}

.truncate {
  display: inline-block;
  max-width: 60vw;
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
  vertical-align: bottom;
}

.failure-row-name {
  color: var(--text-primary);
  font-weight: 700;
  font-size: 14px;
}

.failure-refusal {
  color: var(--accent-warning);
  font-size: 12px;
  font-weight: 600;
}

/* Explicit theme overrides using the `.dark` class on <html> */
.dark .alert-failure {
  background: linear-gradient(
    180deg,
    var(--accent-error-light),
    rgba(255, 107, 107, 0.1)
  );
}

.dark .failure-row:hover {
  background: rgba(255, 107, 107, 0.2);
}

.variables-list {
  display: flex;
  flex-direction: column;
  gap: 4px;
}

.variable-key {
  font-weight: 500;
  color: var(--text-primary);
}

/* Loading and empty states */
.loading-state,
.empty-state {
  color: var(--text-secondary);
  font-size: 14px;
}

/* Logs List */
.logs-list {
  display: flex;
  flex-direction: column;
  gap: var(--spacing-lg);
}

/* Section container with border (matches original) */
.log-section {
  background: var(--bg-1);
  border: 1px solid var(--stroke);
  border-radius: var(--radius-card);
  box-shadow: var(--shadow-card);
}

.section-header {
  background: var(--bg-2);
  color: var(--accent-indigo-from);
  font-size: 16px;
  font-weight: 600;
  padding: var(--spacing-sm) var(--spacing-md);
  border-radius: var(--radius-card) var(--radius-card) 0 0;
}

/* Section content area (matches original) */
.section-content {
  padding: var(--spacing-md);
  display: flex;
  flex-direction: column;
  gap: var(--spacing-md);
}

/* Sidebar */
.logs-sidebar {
  width: 300px;
  height: calc(100vh - 200px);
  overflow-y: auto;
  position: sticky;
  top: var(--spacing-lg);
  background: var(--bg-1);
  border: 1px solid var(--stroke);
  border-radius: var(--radius-card);
  padding: var(--spacing-md);
}

.sidebar-title {
  font-size: 18px;
  font-weight: 600;
  color: var(--text-primary);
  margin-bottom: var(--spacing-md);
}

.sidebar-section {
  margin-bottom: var(--spacing-lg);
}

.sidebar-section-title {
  font-size: 14px;
  font-weight: 600;
  color: var(--accent-indigo-from);
  margin-bottom: var(--spacing-xs);
  cursor: pointer;
  transition: color 0.15s ease-in-out;
}

.sidebar-section-title:hover {
  color: var(--accent-indigo-to);
}

.sidebar-log-item {
  display: flex;
  align-items: center;
  gap: var(--spacing-xs);
  padding: var(--spacing-xs) var(--spacing-sm);
  border-radius: var(--radius-small);
  cursor: pointer;
  transition: background 0.15s ease-in-out;
  margin-bottom: 4px;
}

.sidebar-log-item:hover {
  background: var(--bg-2);
}

.sidebar-log-index {
  font-size: 14px;
  color: var(--text-primary);
  flex-shrink: 0;
}

.sidebar-log-name {
  font-size: 14px;
  color: var(--text-primary);
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
}

/* Responsive design */
@media (max-width: 768px) {
  .logs-content {
    flex-direction: column;
  }

  .logs-sidebar {
    width: 100%;
    height: auto;
    position: static;
    margin-top: var(--spacing-md);
  }
}
</style>
