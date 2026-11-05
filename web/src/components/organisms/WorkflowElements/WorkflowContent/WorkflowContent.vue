<script lang="ts" setup>
import { PropType, ref, computed, inject, nextTick, onMounted, onUnmounted, useTemplateRef, watch } from 'vue'
import WorkflowElementWrapper from '../WorkflowElementWrapper/WorkflowElementWrapper.vue'
import AiPanel from '@/components/organisms/AiPanel/AiPanel.vue'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { Progress } from '@/components/ui/progress'
import {
  Tooltip,
  TooltipContent,
  TooltipProvider,
  TooltipTrigger,
} from '@/components/ui/tooltip'
import { useToast } from '@/components/ui/toast/use-toast'
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from '@/components/ui/dialog'
import type { BlockCitedSources, Citation } from '@/api/workflows/schema'

import TiptapEditor from '@/components/organisms/TiptapEditor/TiptapEditor.vue'
import { OPEN_REPORT_FEEDBACK_KEY } from '@/components/organisms/ReportFeedback/injectionKeys'

import { renderContent, sanitizeHtml } from '@/lib/renderContent'
import { vSafeHtml } from '@/lib/safeHtml'
import { useFactTooltip } from '@/composables/useFactTooltip'

import { useI18n } from 'vue-i18n'
import { useCanAskAi } from '@/composables/useCapabilities'

const { t: $t } = useI18n()

const {
  elementId,
  workflowId,
  content,
  propKey,
  actions,
  showMeta,
  useEditor,
  confidence,
  sources,
  contentClass,
  replacementTag,
  citations,
  citedSourcesByBlock,
} = defineProps({
  elementId: {
    type: String,
  },
  workflowId: {
    type: String,
  },
  content: {
    type: String,
  },
  propKey: {
    type: String,
    default: 'content',
  },
  actions: {
    type: Array as PropType<string[]>,
  },
  showMeta: {
    type: Boolean,
    default: true,
  },
  useEditor: {
    type: Boolean,
    default: true,
  },
  confidence: {
    type: Number,
    default: 0,
  },
  sources: {
    type: Array as PropType<string[]>,
  },
  contentClass: {
    type: String,
    default: 'workflow-content-text',
  },
  replacementTag: {
    type: String,
  },
  citations: {
    type: Array as PropType<Citation[]>,
    default: () => [],
  },
  citedSourcesByBlock: {
    type: Array as PropType<BlockCitedSources[]>,
    default: () => [],
  },
})

const $emits = defineEmits(['save', 'terminal', 'remove'])

const openReportFeedback = inject(OPEN_REPORT_FEEDBACK_KEY, null)

const { toast } = useToast()

const confidenceText = computed(() => $t('shared.components.workflowElement.confidence', { value: (confidence || 0) * 100 }))
const sourcesLabel = computed(() => $t('shared.components.workflowElement.sources', sources?.length ?? 0))

const plainTextOf = (html: string) => {
  const body = new DOMParser().parseFromString(html, 'text/html').body
  body.querySelectorAll('fact_source').forEach(source => source.remove())
  return body.textContent?.trim() ?? ''
}

const contentOrigin = renderContent(content)

const openedBy = ref<'edit' | 'editHeading'>(actions?.includes('editHeading') ? 'editHeading' : 'edit')
const editorLabel = computed(() =>
  $t(openedBy.value === 'editHeading' ? 'shared.components.workflowElement.editor.heading' : 'shared.components.workflowElement.editor.paragraph'),
)
const elementWrapper = useTemplateRef<InstanceType<typeof WorkflowElementWrapper>>('elementWrapper')
const plainEditor = useTemplateRef<InstanceType<typeof Input>>('plainEditor')

const isEditing = ref(false)
const isTerminalDialogOpen = ref(false)
const canAskAi = useCanAskAi()
const isAiPanelOpen = ref(false)
const aiPanelInitialTab = ref<'explore' | 'refine'>('explore')
const aiPanelAnchor = ref<HTMLElement | null>(null)

const scrollToAiPanel = async () => {
  await nextTick()
  aiPanelAnchor.value?.scrollIntoView({ behavior: 'smooth', block: 'center' })
}
const targetTag = ref('')
const text = ref(contentOrigin)
const terminalForm = ref({
  systemMessage: '',
  humanMessage: '',
})

// Content ref for text display
const contentRef = ref<HTMLElement | null>(null)
useFactTooltip(contentRef)

// Text selection for highlight feature
const selectedText = ref('')
const showSelectionButton = ref(false)
const selectionButtonPosition = ref({ top: 0, left: 0 })

const selectionInContent = (selection = window.getSelection()) => {
  const anchorNode = selection?.anchorNode
  return anchorNode && contentRef.value?.contains(anchorNode) ? selection.toString().trim() : ''
}

const handleTextSelection = () => {
  const selection = window.getSelection()
  const selectedTextValue = selectionInContent(selection)

  if (selectedTextValue && ((replacementTag && canAskAi.value) || openReportFeedback)) {
    selectedText.value = selectedTextValue

    // Get selection position for floating button
    const range = selection?.getRangeAt(0)
    const rect = range?.getBoundingClientRect()
    if (rect) {
      selectionButtonPosition.value = {
        top: rect.top - 45,
        left: rect.left + rect.width / 2,
      }
      showSelectionButton.value = true
    }
  } else {
    showSelectionButton.value = false
    selectedText.value = ''
  }
}

const onTraceSelection = () => {
  if (!workflowId || !replacementTag) {
    toast({
      class: 'text-sm',
      description: $t('shared.tracer.errorMissingInfo'),
      variant: 'destructive',
    })
    return
  }
  targetTag.value = replacementTag
  aiPanelInitialTab.value = 'explore'
  isAiPanelOpen.value = true
  showSelectionButton.value = false
  scrollToAiPanel()
}

const onFeedbackSelection = () => {
  openReportFeedback?.(selectedText.value)
  showSelectionButton.value = false
}

const hideSelectionButton = () => {
  // Small delay to allow click on button before hiding
  setTimeout(() => {
    if (!isAiPanelOpen.value) {
      showSelectionButton.value = false
    }
  }, 200)
}

const formattedQuestion = computed(() => {
  if (!selectedText.value) return ''
  return $t('shared.tracer.selectionQuestion', { text: selectedText.value })
})

onMounted(() => {
  document.addEventListener('mouseup', handleTextSelection)
  document.addEventListener('mousedown', hideSelectionButton)
})

onUnmounted(() => {
  document.removeEventListener('mouseup', handleTextSelection)
  document.removeEventListener('mousedown', hideSelectionButton)
})

/* Event handlers */
const onEdit = (action: 'edit' | 'editHeading') => {
  openedBy.value = action
  if (!useEditor) text.value = plainTextOf(text.value)
  isEditing.value = true
}

const onCopy = async () => {
  try {
    const tempDiv = document.createElement('div')
    tempDiv.innerHTML = sanitizeHtml(text.value)
    tempDiv.style.position = 'absolute'
    tempDiv.style.left = '-9999px'
    document.body.appendChild(tempDiv)
    let textToCopy = ''
    try {
      textToCopy = tempDiv.innerText || ''
    } finally {
      document.body.removeChild(tempDiv)
    }

    if (navigator.clipboard?.writeText) {
      await navigator.clipboard.writeText(textToCopy)
    } else {
      const textarea = document.createElement('textarea')
      textarea.value = textToCopy
      textarea.style.position = 'fixed'
      textarea.style.opacity = '0'
      document.body.appendChild(textarea)
      try {
        textarea.select()
        const success = document.execCommand('copy')
        if (!success) throw new Error('execCommand failed')
      } finally {
        document.body.removeChild(textarea)
      }
    }

    // TODO: move to i18n
    toast({
      class: 'text-sm',
      description: 'Copied to clipboard',
    })
  } catch (err) {
    console.error('Failed to copy: ', err)
    toast({
      class: 'text-sm',
      description: 'Failed to copy to clipboard',
      variant: 'destructive',
    })
  }
}

const onTerminal = () => {
  isTerminalDialogOpen.value = true
}

const onRefinementAccept = (refinedContent: string) => {
  text.value = refinedContent
  isEditing.value = true
}

const onRemove = () => {
  $emits('remove', elementId)
}

const onTrace = async () => {
  if (!workflowId || !replacementTag) {
    toast({
      class: 'text-sm',
      description: $t('shared.tracer.errorMissingInfo'),
      variant: 'destructive',
    })
    return
  }

  targetTag.value = replacementTag
  selectedText.value = selectionInContent()
  aiPanelInitialTab.value = 'explore'
  isAiPanelOpen.value = true
  scrollToAiPanel()
}

/* Action handlers */

watch(isEditing, async editing => {
  await nextTick()
  if (editing) {
    ;(plainEditor.value?.$el as HTMLInputElement | undefined)?.focus()
  } else {
    elementWrapper.value?.focusAction(openedBy.value)
  }
})

const onEditCancel = () => {
  text.value = contentOrigin
  isEditing.value = false
}

const onEditSave = () => {
  $emits('save', {
    id: elementId,
    [propKey]: text.value,
  })
  isEditing.value = false
}

const onTerminalClose = () => {
  isTerminalDialogOpen.value = false
}

const onTerminalSubmit = () => {
  $emits('terminal', {
    id: elementId,
    terminalForm,
  })
  isTerminalDialogOpen.value = false
}

</script>

<template>
  <WorkflowElementWrapper
    ref="elementWrapper"
    @edit="onEdit"
    @trace="onTrace"
    @copy="onCopy"
    @terminal="onTerminal"
    @remove="onRemove"
    :actions="actions"
    :showActions="!isEditing"
  >
    <template v-if="isEditing">
      <!-- eslint-disable-next-line vuejs-accessibility/no-autofocus -- the editor replaces the paragraph whose Edit the user pressed, so focus moves into it -->
      <TiptapEditor v-if="useEditor" v-model="text" :label="editorLabel" autofocus />
      <Input v-else ref="plainEditor" v-model="text" :aria-label="editorLabel" />
      <div class="flex justify-end">
        <Button class="mt-4" variant="link" @click="onEditCancel">{{
          $t('shared.components.workflowElement.actions.cancel')
        }}</Button>
        <Button class="mt-4" variant="soft" @click="onEditSave">{{
          $t('shared.components.workflowElement.actions.save')
        }}</Button>
      </div>
    </template>
    <template v-else>
      <div
        ref="contentRef"
        class="group overflow-x-auto max-w-full workflow-content-text"
        :class="contentClass"
        :data-citations="citations && citations.length > 0 ? JSON.stringify(citations) : undefined"
        :data-cited-sources-by-block="citedSourcesByBlock.length > 0 ? JSON.stringify(citedSourcesByBlock) : undefined"
        v-safe-html="text"
      />
      <template v-if="showMeta">
        <div class="flex items-center">
          <TooltipProvider v-if="confidence > 0">
            <Tooltip>
              <TooltipTrigger as-child>
                <Progress
                  :aria-label="confidenceText"
                  :model-value="confidence ? confidence * 100 : 0"
                  class="rounded-none w-0 h-0 group-hover:mr-2 group-hover:h-4 group-hover:w-20 group-hover:mt-2 group-focus-within:mr-2 group-focus-within:h-4 group-focus-within:w-20 group-focus-within:mt-2 transition-all duration-300 ease-in-out"
                />
              </TooltipTrigger>
              <TooltipContent>
                {{ confidenceText }}
              </TooltipContent>
            </Tooltip>
          </TooltipProvider>

          <TooltipProvider v-if="sources && sources.length > 0">
            <Tooltip>
              <TooltipTrigger as-child>
                <button type="button" class="source-counter-target button-reset hit-target" :aria-label="sourcesLabel">
                  <span class="source-counter">{{ sources.length }}</span>
                </button>
              </TooltipTrigger>
              <TooltipContent>
                <ul class="m-0 pl-5 list-disc">
                  <li
                    v-for="(source, idx) in sources"
                    :key="idx"
                  >
                    {{ source }}
                  </li>
                </ul>
              </TooltipContent>
            </Tooltip>
          </TooltipProvider>
        </div>
      </template>
    </template>

    <Dialog v-model:open="isTerminalDialogOpen as boolean">
      <DialogContent>
        <DialogHeader>
          <DialogTitle>Run terminal</DialogTitle>
          // TODO: move to i18n
          <DialogDescription>
            Retrigger terminal to be able to change the content
          </DialogDescription>
        </DialogHeader>

        <DialogFooter>
          <Button class="mt-4" variant="link" @click="onTerminalClose">
            Cancel
          </Button>
          <Button class="mt-4" variant="soft" @click="onTerminalSubmit">
            Submit
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>

    <!-- Floating Explore Button on Text Selection -->
    <Teleport to="body">
      <div
        v-if="showSelectionButton"
        class="selection-explore-button"
        :style="{
          top: `${selectionButtonPosition.top}px`,
          left: `${selectionButtonPosition.left}px`,
        }"
      >
        <Button
          v-if="replacementTag && canAskAi"
          variant="outline"
          size="sm"
          class="explore-button"
          @click="onTraceSelection"
        >
          {{ $t('shared.tracer.traceSource') }}
        </Button>
        <Button
          v-if="openReportFeedback"
          variant="outline"
          size="sm"
          class="explore-button"
          data-testid="selection-feedback-button"
          @click="onFeedbackSelection"
        >
          {{ $t('page.workflowDetails.feedback.action') }}
        </Button>
      </div>
    </Teleport>

    <!-- Combined AI Panel (Explore + Refine) -->
    <div ref="aiPanelAnchor" />
    <AiPanel
      :is-open="isAiPanelOpen"
      :initial-tab="aiPanelInitialTab"
      :run-id="workflowId || ''"
      :target-tag="targetTag || ''"
      :initial-question="formattedQuestion"
      :content="text"
      @update:is-open="isAiPanelOpen = $event"
      @accept="onRefinementAccept"
    />
  </WorkflowElementWrapper>
</template>

<style scoped>
@reference "tailwindcss";

/* ========================================
   TYPOGRAPHY & CONTENT STYLING
   ======================================== */

/* Base Content Container */
.workflow-content-text {
  display: block;
  user-select: text;
  line-height: 1.7;
  font-size: 16px;
  color: var(--text-primary);
  min-height: 40px;
}

/* Element Subtitle Styling */
.element-subtitle {
  display: block;
  font-size: 22px !important;
  font-weight: 600 !important;
  color: var(--text-primary) !important;
  letter-spacing: -0.02em !important;
  margin-bottom: var(--spacing-sm);
  line-height: 1.4 !important;
  user-select: none;
}

.element-subtitle :deep(p),
.element-subtitle :deep(div),
.element-subtitle :deep(span) {
  font-size: 22px !important;
  font-weight: 600 !important;
  color: var(--text-primary) !important;
  letter-spacing: -0.02em !important;
  line-height: 1.4 !important;
  margin: 0;
}

/* Headings */
:deep(h1), :deep(h2), :deep(h3), :deep(h4), :deep(h5), :deep(h6) {
  font-weight: 600;
  color: var(--text-primary);
  letter-spacing: -0.02em;
  line-height: 1.3;
  margin-top: 1.5em;
  margin-bottom: 0.75em;
}

:deep(h1) {
  font-size: 28px;
  font-weight: 700;
}

:deep(h2) {
  font-size: 24px;
  font-weight: 700;
}

:deep(h3) {
  font-size: 20px;
}

:deep(h4) {
  font-size: 18px;
}

:deep(h5), :deep(h6) {
  font-size: 16px;
}

/* Paragraphs */
:deep(p) {
  margin-bottom: 1em;
  line-height: 1.7;
  font-size: 16px;
  color: var(--text-primary);
}

:deep(p:last-child) {
  margin-bottom: 0;
}

/* Lists */
:deep(ul), :deep(ol) {
  margin: 1em 0;
  padding-left: 1.5em;
  line-height: 1.7;
}

:deep(ul) {
  list-style-type: disc;
}

:deep(ol) {
  list-style-type: decimal;
}

:deep(li) {
  margin-bottom: 0.5em;
  color: var(--text-primary);
}

:deep(li:last-child) {
  margin-bottom: 0;
}

:deep(ul ul), :deep(ol ul) {
  list-style-type: circle;
  margin-top: 0.5em;
}

:deep(ul ol), :deep(ol ol) {
  margin-top: 0.5em;
}

/* Links */
:deep(a) {
  color: var(--accent-indigo-from);
  text-decoration: underline;
  transition: color 0.2s ease;
}

:deep(a:hover) {
  color: var(--accent-indigo-to);
}

/* Emphasis */
:deep(strong), :deep(b) {
  font-weight: 600;
  color: var(--text-primary);
}

/* Ensure bold text in tables is dark */
:deep(table strong), :deep(table b) {
  font-weight: 700;
  color: var(--text-primary) !important;
}

:deep(em), :deep(i) {
  font-style: italic;
}

/* Code */
:deep(code) {
  background: var(--bg-2);
  padding: 2px 6px;
  border-radius: 4px;
  font-size: 0.9em;
  font-family: 'Monaco', 'Menlo', 'Consolas', monospace;
  color: var(--accent-indigo-from);
}

:deep(pre) {
  background: var(--bg-2);
  padding: var(--spacing-sm);
  border-radius: var(--radius-small);
  overflow-x: auto;
  margin: 1em 0;
}

:deep(pre code) {
  background: none;
  padding: 0;
  font-size: 13px;
}

/* Blockquote */
:deep(blockquote) {
  border-left: 3px solid var(--accent-indigo-from);
  padding-left: var(--spacing-sm);
  margin: 1.5em 0;
  color: var(--text-secondary);
  font-style: italic;
}

/* Horizontal Rule */
:deep(hr) {
  border: none;
  border-top: 1px solid var(--stroke);
  margin: var(--spacing-md) 0;
}

/* ========================================
   TABLE STYLES
   ======================================== */

/* table generic styles */
:deep(table) {
  @apply w-full text-sm text-left;
  background: var(--bg-1);
  border: 2px solid #6b7280;
  color: var(--text-primary);
}

.dark :deep(table) {
  background: var(--bg-1);
  border-color: var(--text-secondary);
  color: var(--text-primary);
}

:deep(thead) {
  background: var(--bg-2);
}

.dark :deep(thead) {
  background: var(--bg-2);
}

:deep(thead th) {
  @apply p-4 font-bold;
  border-bottom: 2px solid #6b7280;
  color: var(--text-primary);
}

.dark :deep(thead th) {
  border-bottom-color: var(--text-secondary);
  color: var(--text-primary);
}

:deep(tbody td) {
  @apply p-4;
  border-bottom: 1px solid #9ca3af;
}

.dark :deep(tbody td) {
  border-bottom-color: var(--text-muted);
}

:deep(table .alignRight) {
  @apply text-right;
}

:deep(table .alignLeft) {
  @apply text-left;
}

:deep(table .alignCenter) {
  @apply text-center;
}

:deep(table .bold) {
  @apply font-bold;
  color: var(--text-primary) !important;
}

:deep(table .italics) {
  @apply italic;
}

/* Add top border to cells in bold rows except first column */
:deep(.table-solo tr.bold td:not(:first-child)) {
  @apply border-t-2! border-t-gray-400!;
}

/* Add spacing below bold rows */
:deep(.table-solo tr.bold td) {
  @apply pb-4!;
}

/* Add right padding to first column in bold rows */
:deep(.table-solo tr.bold td:first-child) {
  @apply pl-4!;
}

/* solo table styles */
:deep(.table-solo) {
  @apply table-auto;
}

:deep(.table-solo thead th) {
  @apply p-1;
}

:deep(.table-solo th) {
  border: 1px solid #6b7280;
}

.dark :deep(.table-solo th) {
  border-color: var(--text-secondary);
}

:deep(.table-solo tbody td) {
  @apply p-1;
  border-bottom: 0;
  border-left: 1px solid #6b7280;
}

.dark :deep(.table-solo tbody td) {
  border-left-color: var(--text-secondary);
}

:deep(.table-solo tbody td:first-child) {
  @apply w-full;
}

:deep(.table-solo tbody td:not(:first-child)) {
  @apply min-w-[150px];
}

/* Enhanced row distinction styles */
:deep(.table-solo tbody tr:nth-child(odd):not(:has(.metricHeader))) {
  background: var(--bg-1);
}

.dark :deep(.table-solo tbody tr:nth-child(odd):not(:has(.metricHeader))) {
  background: var(--bg-1);
}

:deep(.table-solo tbody tr:nth-child(even):not(:has(.metricHeader))) {
  background: var(--bg-2);
}

.dark :deep(.table-solo tbody tr:nth-child(even):not(:has(.metricHeader))) {
  background: var(--bg-2);
}

:deep(.table-solo tbody tr:hover:not(:has(.metricHeader))) {
  background: var(--divider);
  transition: background-color 0.15s;
}

.dark :deep(.table-solo tbody tr:hover:not(:has(.metricHeader))) {
  background: var(--divider);
}

:deep(.table-solo .metricHeader) {
  @apply font-bold! uppercase! text-xs! tracking-wider! py-2!;
  background: var(--divider);
  border: 1px solid #9ca3af;
  color: var(--text-primary);
}

.dark :deep(.table-solo .metricHeader) {
  background: var(--divider);
  border-color: var(--text-muted);
  color: var(--text-primary);
}

/* Ensure metric header row doesn't get zebra striping */
:deep(.table-solo tbody tr:has(.metricHeader)) {
  @apply bg-transparent!;
}

/* peer table styles */
:deep(.table-peer tbody td:first-child) {
  @apply min-w-[220px];
}

:deep(.table-peer tbody td) {
  @apply p-1;
}

:deep(.table-peer thead th) {
  border: 1px solid #6b7280;
}

.dark :deep(.table-peer thead th) {
  border-color: var(--text-secondary);
}

:deep(.table-peer tbody td) {
  border-bottom: 0;
  border-left: 1px solid #6b7280;
}

.dark :deep(.table-peer tbody td) {
  border-left-color: var(--text-secondary);
}

:deep(.table-peer .group-end) {
  border-bottom: 1px solid #6b7280;
}

.dark :deep(.table-peer .group-end) {
  border-bottom-color: var(--text-secondary);
}

/* ========================================
   SOURCE COUNTER BADGE
   ======================================== */

.source-counter-target {
  margin-top: 0.375rem;
}

.source-counter {
  display: flex;
  justify-content: center;
  align-items: center;
  width: 1.25rem;
  height: 1.25rem;
  border-radius: 50%;
  background: var(--primary);
  color: var(--primary-foreground);
  font-size: 0.75rem;
  font-weight: 600;
  transition: all 0.15s ease-in-out;
}

.source-counter-target:hover .source-counter {
  transform: scale(1.1);
  box-shadow: 0 2px 8px rgba(26, 56, 80, 0.3);
}
</style>

<style>
/* Selection Explore Button - Global styles for Teleport */
.selection-explore-button {
  display: flex;
  gap: 6px;
  position: fixed;
  z-index: 9999;
  transform: translateX(-50%);
  animation: fadeInScale 0.15s ease-out;
}

.selection-explore-button .explore-button {
  background: var(--primary);
  color: var(--primary-foreground);
  border: none;
  font-weight: 500;
  font-size: 13px;
  padding: 6px 12px;
  height: auto;
  box-shadow: 0 2px 8px rgba(26, 56, 80, 0.4);
}

.selection-explore-button .explore-button:hover {
  box-shadow: 0 4px 12px rgba(26, 56, 80, 0.5);
}

@keyframes fadeInScale {
  from {
    opacity: 0;
    transform: translateX(-50%) scale(0.9);
  }
  to {
    opacity: 1;
    transform: translateX(-50%) scale(1);
  }
}
</style>
