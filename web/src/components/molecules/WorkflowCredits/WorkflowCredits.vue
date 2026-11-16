<script setup lang="ts">
import { computed, useId } from 'vue'
import { useI18n } from 'vue-i18n'
import IniaIcon from '@atoms/IniaIcon/IniaIcon.vue'
import {
  Tooltip,
  TooltipContent,
  TooltipProvider,
  TooltipTrigger,
} from '@/components/ui/tooltip'
import {
  Accordion,
  AccordionContent,
  AccordionItem,
  AccordionTrigger,
} from '@/components/ui/accordion'
import type {
  WorkflowExecutionDocument,
  WorkflowExecutionVariableValue,
} from '@/api/workflowReports/schema'
import { useLiveDuration } from '@/composables/useLiveDuration'
import { formatDate, formatDateTime } from '@/lib/utils'
import { formatBytes } from '@/lib/formatBytes'
import { fullFileName } from '@/lib/fileName'
import { WorkflowVariableTypes } from '@/enums'
import { describeExclusionReason } from '@/utils/documentExclusion'

const props = defineProps({
  users: {
    type: Object,
  },
  executionData: {
    type: Object,
  },
})

const emit = defineEmits(['preview', 'download'])

const { t: $t } = useI18n()

const liveDuration = useLiveDuration(computed(() => props.executionData))

const credits = computed(() => {
  const rows: Array<{ title: string; value: unknown; valueClass?: string }> = [
    {
      title: $t(`page.workflowDetails.credit.counterpartyLabel`),
      value: props.executionData?.counterpartyName,
    },
    {
      title: $t(`page.workflowDetails.credit.creditAnalystLabel`),
      value:
        props.users?.value?.find(
          (u: { username: string; fullName?: string }) =>
            u.username === props.executionData?.analystName,
        )?.fullName || props.executionData?.analystName,
    },
    {
      title: $t(`page.workflowDetails.credit.createdLabel`),
      value: formatDateTime(props.executionData?.createdAt),
    },
  ]

  if (liveDuration.value) {
    rows.push({
      title: $t(`page.workflowDetails.credit.durationLabel`),
      value: liveDuration.value,
      valueClass: 'duration-display',
    })
  }

  return rows
})

const documentClassLabel = (doc: WorkflowExecutionDocument): string =>
  doc.documentClassDisplayName?.trim() || doc.documentClass || ''

const sortByClassThenName = (a: WorkflowExecutionDocument, b: WorkflowExecutionDocument): number => {
  const classCompare = documentClassLabel(a).localeCompare(documentClassLabel(b))
  return classCompare !== 0 ? classCompare : (a.name ?? '').localeCompare(b.name ?? '')
}

const isHiddenFromOutput = (doc: WorkflowExecutionDocument): boolean =>
  doc.shouldBeDisplayedInOutput === false

const documentButtonName = (doc: WorkflowExecutionDocument): string => {
  const name = [documentClassLabel(doc), fullFileName(doc)].filter(Boolean).join(' ')
  return isPdf(doc.extension)
    ? $t('page.workflowDetails.documents.previewFile', { name })
    : $t('page.workflowDetails.documents.downloadFile', { name })
}

const openDocument = (doc: WorkflowExecutionDocument) => {
  emit(isPdf(doc.extension) ? 'preview' : 'download', doc)
}

const documentButtonAttrs = (doc: WorkflowExecutionDocument, section: string, index: number) => ({
  type: 'button',
  class: 'row-main-button',
  'data-testid': `document-row-${doc.fileId}`,
  'aria-label': documentButtonName(doc),
  'aria-describedby': documentDescriptionIds(doc, section, index),
  onClick: () => openDocument(doc),
})

type DocumentDescriptionPart = 'note' | 'hidden'

const panelId = useId()
const documentElementId = (sectionKey: string, index: number, part: DocumentDescriptionPart) =>
  `${panelId}-${sectionKey}-${index}-${part}`

const documentDescriptionIds = (
  doc: WorkflowExecutionDocument,
  sectionKey: string,
  index: number,
): string | undefined => {
  const parts: DocumentDescriptionPart[] = []
  if (sectionKey === 'excluded') {
    parts.push('note')
  }
  if (isHiddenFromOutput(doc)) {
    parts.push('hidden')
  }
  return parts.map(part => documentElementId(sectionKey, index, part)).join(' ') || undefined
}

const partitionedDocuments = computed(() => {
  const docs = props.executionData?.workflowExecutionDocuments as
    | WorkflowExecutionDocument[]
    | undefined
  const inputs: WorkflowExecutionDocument[] = []
  const outputs: WorkflowExecutionDocument[] = []
  if (!docs) {
    return { inputs, outputs }
  }
  for (const doc of docs) {
    if (doc.isOutputDocument === true) {
      if (isHiddenFromOutput(doc)) continue
      outputs.push(doc)
    } else {
      inputs.push(doc)
    }
  }
  inputs.sort(sortByClassThenName)
  outputs.sort(sortByClassThenName)
  return { inputs, outputs }
})

const excludedDocuments = computed(() =>
  [...((props.executionData?.excludedDocuments as WorkflowExecutionDocument[] | undefined) ?? [])]
    .sort(sortByClassThenName),
)

const documentSections = computed(() =>
  [
    {
      value: 'excluded',
      label: $t('page.workflowDetails.documents.excludedLabel'),
      documents: excludedDocuments.value,
    },
    {
      value: 'source',
      label: $t('page.workflowDetails.documents.inputLabel'),
      documents: partitionedDocuments.value.inputs,
    },
    {
      value: 'generated',
      label: $t('page.workflowDetails.documents.outputLabel'),
      documents: partitionedDocuments.value.outputs,
    },
  ].filter(section => section.documents.length > 0),
)

const formatVariableValue = (v: WorkflowExecutionVariableValue): string | null => {
  if (v.displayValue === undefined || v.displayValue === null) {
    return null
  }
  if (v.type === WorkflowVariableTypes.DateTime) {
    const hasTime = v.displayValue.includes('T') || /\d{2}:\d{2}/.test(v.displayValue)
    const formatted = hasTime ? formatDateTime(v.displayValue) : formatDate(v.displayValue)
    return formatted === '-' ? v.displayValue : formatted
  }
  return v.displayValue
}

const visibleVariableValues = computed<WorkflowExecutionVariableValue[]>(() => {
  const vars = props.executionData?.variableValues as
    | WorkflowExecutionVariableValue[]
    | undefined
  if (!vars) return []
  return vars.filter(v => !v.isHidden)
})

const isPdf = (extension: string | undefined): boolean =>
  (extension ?? '').toLowerCase().replace('.', '') === 'pdf'

const getFileIcon = (extension: string | undefined) => {
  const ext = (extension ?? '').toLowerCase().replace('.', '')
  switch (ext) {
    case 'pdf':
      return 'file-pdf'
    case 'doc':
    case 'docx':
      return 'file-word'
    case 'xls':
    case 'xlsx':
    case 'xlsm':
      return 'file-excel'
    case 'jpg':
    case 'jpeg':
    case 'png':
    case 'gif':
    case 'svg':
      return 'file-image'
    case 'txt':
      return 'file-alt'
    case 'csv':
      return 'file-csv'
    case 'ppt':
    case 'pptx':
      return 'file-powerpoint'
    case 'zip':
    case 'rar':
      return 'file-archive'
    default:
      return 'file'
  }
}

</script>

<template>
  <div class="meta-panel">
    <div class="meta-list">
      <div
        v-for="(credit, creditIdx) in credits"
        :key="creditIdx"
        class="meta-row"
      >
        <span class="meta-label">{{ credit.title }}</span>
        <span class="meta-value" :class="credit.valueClass">{{ credit.value || '-' }}</span>
      </div>
    </div>

    <Accordion
      v-if="documentSections.length > 0"
      type="multiple"
      :default-value="['excluded', 'generated']"
    >
      <AccordionItem
        v-for="section in documentSections"
        :key="section.value"
        :value="section.value"
        class="meta-section-accordion-item meta-section"
      >
        <AccordionTrigger
          :data-testid="`documents-trigger-${section.value}`"
          :heading-level="2"
          class="meta-section-accordion-trigger"
        >
          <span class="meta-section-header">
            <span>{{ section.label }}</span>
            <span class="meta-section-count">{{ section.documents.length }}</span>
          </span>
        </AccordionTrigger>
        <AccordionContent class="meta-section-accordion-content">
          <TooltipProvider>
            <ul class="meta-list">
              <li
                v-for="(doc, index) in section.documents"
                :key="doc.fileId"
                class="meta-row"
                :class="{ 'meta-row--clickable': !doc.isDeleted, 'meta-row--hidden-from-output': isHiddenFromOutput(doc) }"
              >
                <Tooltip>
                  <TooltipTrigger as-child>
                    <component
                      :is="doc.isDeleted ? 'div' : 'button'"
                      class="meta-doc-main"
                      v-bind="doc.isDeleted ? { 'data-testid': `document-deleted-${doc.fileId}` } : documentButtonAttrs(doc, section.value, index)"
                    >
                      <span class="meta-label meta-doc-label">
                        <IniaIcon
                          :name="getFileIcon(doc.extension)"
                          type="solid"
                          class="meta-doc-icon"
                        />
                        <span class="meta-label-text">{{ documentClassLabel(doc) }}</span>
                      </span>
                      <span class="meta-value meta-doc-value">
                        <span class="meta-doc-text">
                          <span class="meta-doc-name">{{ fullFileName(doc) }}</span>
                          <span v-if="doc.isDeleted" class="meta-doc-note">{{ $t('page.workflowDetails.documents.deleted') }}</span>
                          <span v-if="section.value === 'excluded'" :id="documentElementId(section.value, index, 'note')" class="meta-doc-note">{{ describeExclusionReason($t, doc.exclusionReason) }}</span>
                        </span>
                        <span
                          v-if="isHiddenFromOutput(doc)"
                          :id="documentElementId(section.value, index, 'hidden')"
                          :data-testid="`document-hidden-${doc.fileId}`"
                          class="meta-doc-hidden"
                        >
                          {{ $t('page.workflowDetails.documents.hiddenFromOutput') }}
                        </span>
                      </span>
                    </component>
                  </TooltipTrigger>
                  <TooltipContent>
                    <div class="text-sm">
                      <div class="font-semibold">{{ fullFileName(doc) }}</div>
                      <div v-if="formatBytes(doc.sizeBytes) !== '-'" class="text-xs text-muted-foreground">
                        {{ formatBytes(doc.sizeBytes) }}
                      </div>
                    </div>
                  </TooltipContent>
                </Tooltip>
                <button
                  v-if="!doc.isDeleted"
                  type="button"
                  :data-testid="`document-download-${doc.fileId}`"
                  class="meta-doc-download hit-target row-main-button-raised"
                  :aria-label="$t('page.workflowDetails.documents.downloadFile', { name: fullFileName(doc) })"
                  @click="emit('download', doc)"
                >
                  <IniaIcon
                    name="download"
                    type="solid"
                  />
                </button>
              </li>
            </ul>
          </TooltipProvider>
        </AccordionContent>
      </AccordionItem>
    </Accordion>

    <Accordion
      v-if="visibleVariableValues.length > 0"
      type="single"
      collapsible
      data-testid="variables-section"
      class="meta-section meta-section--accordion"
    >
      <AccordionItem value="variables" class="meta-section-accordion-item">
        <AccordionTrigger
          data-testid="variables-trigger"
          :heading-level="2"
          class="meta-section-accordion-trigger"
        >
          <span class="meta-section-header">
            <span>{{ $t('page.workflowDetails.variables.label') }}</span>
            <span class="meta-section-count">{{ visibleVariableValues.length }}</span>
          </span>
        </AccordionTrigger>
        <AccordionContent class="meta-section-accordion-content">
          <TooltipProvider>
            <ul class="meta-list">
              <li
                v-for="v in visibleVariableValues"
                :key="v.replacementTag"
                class="meta-row"
              >
                <span class="meta-label">
                  <span class="meta-label-text">{{ v.displayName }}</span>
                  <Tooltip v-if="v.description">
                    <TooltipTrigger as-child>
                      <span
                        :data-testid="`variable-info-${v.replacementTag}`"
                        class="meta-info hit-target"
                        role="button"
                        tabindex="0"
                        :aria-label="v.description ?? ''"
                      >
                        <IniaIcon
                          name="circle-info"
                          type="solid"
                        />
                      </span>
                    </TooltipTrigger>
                    <TooltipContent>
                      <div class="text-xs max-w-xs">
                        {{ v.description }}
                      </div>
                    </TooltipContent>
                  </Tooltip>
                </span>
                <span class="meta-value">
                  {{ formatVariableValue(v) ?? $t('page.workflowDetails.variables.noValuePlaceholder') }}
                </span>
              </li>
            </ul>
          </TooltipProvider>
        </AccordionContent>
      </AccordionItem>
    </Accordion>
  </div>
</template>

<style scoped>
@reference "tailwindcss";

.meta-panel {
  background: var(--bg-1);
  border: 1px solid var(--stroke);
  border-radius: var(--radius-panel);
  padding: var(--spacing-md);
}

.meta-list {
  list-style: none;
  padding: 0;
  margin: 0;
  display: flex;
  flex-direction: column;
  gap: 4px;
}

.meta-row {
  display: flex;
  align-items: center;
  gap: 8px;
  font-size: 13px;
  line-height: 1.5;
  min-height: 24px;
  padding: 2px 0;
  margin: 0;
}

.meta-label {
  flex-shrink: 0;
  width: 150px;
  color: var(--text-secondary);
  display: flex;
  align-items: center;
  gap: 6px;
  overflow: hidden;
  white-space: nowrap;
}

.meta-label-text {
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.meta-info {
  margin-left: auto;
  flex-shrink: 0;
  color: var(--text-secondary);
  cursor: help;
  font-size: 11px;
}

.meta-info:hover {
  color: var(--text-primary);
}

.meta-value {
  flex: 1;
  min-width: 0;
  color: var(--text-primary);
  font-weight: 500;
  overflow-wrap: anywhere;
}

.meta-section {
  margin-top: 12px;
  padding-top: 12px;
  border-top: 1px solid var(--stroke);
}

.meta-section-header {
  display: flex;
  align-items: center;
  gap: 6px;
  font-size: 13px;
  font-weight: 500;
  color: var(--text-secondary);
}

.meta-section > .meta-section-header {
  margin-bottom: 6px;
}

.meta-section-count {
  color: var(--text-secondary);
  font-weight: 400;
}

.meta-section--accordion {
  padding-top: 12px;
}

.meta-section-accordion-item {
  border-bottom: none;
}

:deep(.meta-section-accordion-trigger) {
  font-size: 13px;
  font-weight: 500;
  color: var(--text-secondary);
  padding: 0;
}

:deep(.meta-section-accordion-trigger:hover) {
  text-decoration: none;
  color: var(--text-primary);
}

:deep(.meta-section-accordion-content) {
  padding-top: 8px !important;
  padding-bottom: 0 !important;
}

.meta-row--clickable {
  position: relative;
  cursor: pointer;
  border-radius: var(--radius-button, 4px);
  margin: 0 -6px;
  padding: 2px 6px;
  transition: background-color 0.12s ease;
}

.meta-row--clickable:hover {
  background: var(--bg-2);
}

.meta-doc-main {
  flex: 1;
  min-width: 0;
  display: flex;
  align-items: center;
  gap: 8px;
}

.meta-doc-icon {
  font-size: 14px;
  color: var(--text-secondary);
  flex-shrink: 0;
}

.meta-doc-value {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 8px;
}

/* Stepped down to the secondary ink rather than dimmed with opacity: an opacity
   multiplier washes the name below AA on the light theme, where the ink starts
   dark on white and has no headroom to give away. */
.meta-row--hidden-from-output .meta-doc-name {
  color: var(--text-secondary);
}

.meta-doc-hidden {
  flex-shrink: 0;
  font-size: 11px;
  color: var(--text-secondary);
  white-space: nowrap;
}

.meta-doc-text {
  flex: 1;
  min-width: 0;
  display: flex;
  flex-direction: column;
  gap: 2px;
}

.meta-doc-note {
  font-size: 11px;
  color: var(--text-secondary);
}

.meta-doc-name {
  min-width: 0;
  overflow-wrap: anywhere;
}

.meta-doc-download {
  padding: 2px 4px;
  border: none;
  background: transparent;
  border-radius: var(--radius-button, 4px);
  color: var(--text-secondary);
  cursor: pointer;
  flex-shrink: 0;
  font-size: 13px;
  transition: color 0.12s ease, background-color 0.12s ease;
}

.meta-doc-download:hover {
  color: var(--text-primary);
  background: var(--bg-1);
}
</style>
