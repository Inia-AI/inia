<script setup lang="ts">
import { ref, watch, computed, nextTick, onUnmounted, useId } from 'vue'
import { useI18n } from 'vue-i18n'
import { Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from '@/components/ui/dialog'
import { Tabs, TabsContent, TabsList, TabsTrigger } from '@/components/ui/tabs'
import { Button } from '@/components/ui/button'
import API from '@/api'
import type { RefinementTurn } from '@/api/contentRefinement/schema'
import { problemDetailsMessage, type ProblemDetails } from '@/api/base/errors'
import { dailyAllowanceMessage } from '@/api/base/dailyAllowance'
import { MarkdownText } from '@/components/atoms'
import { replaceSpecialElements, restoreSpecialElements } from '@/utils/htmlPreservation'
import { vSafeHtml } from '@/lib/safeHtml'
import ChatInput from './ChatInput.vue'
import { appendPairWithWindow } from './historyWindow'
import { useCapabilities } from '@/composables/useCapabilities'
import { CAPABILITIES } from '@/constants/capabilities'

const { t: $t } = useI18n()
const presetHelpId = useId()

// ─── Types ───────────────────────────────────────────────────────────

type RefinementVersion =
  | { isOutOfScope?: false; content: string; instruction: string; explanation: string }
  | { isOutOfScope: true; content: null; instruction: string; explanation: string }

interface UserPreset {
  id: string
  label: string
  instruction: string
}

// ─── Props & Emits ───────────────────────────────────────────────────

const props = defineProps<{
  isOpen: boolean
  initialTab?: 'explore' | 'refine'
  // Explore props
  runId: string
  targetTag: string
  initialQuestion?: string
  // Refine props
  content: string
}>()

const emit = defineEmits<{
  (e: 'update:isOpen', value: boolean): void
  (e: 'accept', refinedContent: string): void
}>()

const has = useCapabilities()
const canExplore = computed(() => has(CAPABILITIES.EXPLORE))
const canRefine = computed(() => has(CAPABILITIES.REFINE))
const openingTab = () => (canRefine.value && (props.initialTab === 'refine' || !canExplore.value) ? 'refine' : 'explore')

const activeTab = ref<string>(openingTab())

// ═══════════════════════════════════════════════════════════════
// EXPLORE TAB STATE
// ═══════════════════════════════════════════════════════════════

const exploreQuestion = ref('')
const exploreAnswer = ref('')
const exploreDisplayedAnswer = ref('')
const exploreLoading = ref(false)
const exploreError = ref('')
const exploreLoadingStatus = ref('')
let exploreTypewriterTimeout: number | null = null
let exploreStatusInterval: number | null = null

const exploreStatusMessages = computed(() => [
  $t('shared.aiPanel.explore.statusAnalyzing'),
  $t('shared.aiPanel.explore.statusSearching'),
  $t('shared.aiPanel.explore.statusTracing'),
  $t('shared.aiPanel.explore.statusFollowing'),
  $t('shared.aiPanel.explore.statusGenerating'),
])

const { mutateAsync: queryProvenance } = API.GraphRag.useQueryProvenance()

const startExploreLoading = () => {
  let index = 0
  exploreLoadingStatus.value = exploreStatusMessages.value[0]!
  exploreStatusInterval = window.setInterval(() => {
    index = (index + 1) % exploreStatusMessages.value.length
    exploreLoadingStatus.value = exploreStatusMessages.value[index]!
  }, 3000)
}

const stopExploreLoading = () => {
  if (exploreStatusInterval) {
    clearInterval(exploreStatusInterval)
    exploreStatusInterval = null
  }
  exploreLoadingStatus.value = ''
}

const exploreTypewriter = (text: string) => {
  if (exploreTypewriterTimeout) {
    clearTimeout(exploreTypewriterTimeout)
    exploreTypewriterTimeout = null
  }
  exploreDisplayedAnswer.value = ''
  let index = 0
  const type = () => {
    if (index < text.length) {
      exploreDisplayedAnswer.value += text.substring(index, index + 5)
      index += 5
      exploreTypewriterTimeout = window.setTimeout(type, 10)
    } else {
      exploreTypewriterTimeout = null
    }
  }
  type()
}

const fetchProvenance = async () => {
  if (!props.runId || !props.targetTag) {
    exploreError.value = $t('shared.aiPanel.explore.errorMissingInfo')
    return
  }
  if (!exploreQuestion.value.trim()) {
    exploreError.value = $t('shared.aiPanel.explore.errorEmptyQuestion')
    return
  }

  exploreLoading.value = true
  exploreError.value = ''
  exploreAnswer.value = ''
  startExploreLoading()

  try {
    const response = await queryProvenance({
      runId: props.runId,
      request: {
        targetTag: props.targetTag,
        question: exploreQuestion.value,
        startingDepth: 4,
        includePreviews: true,
      },
    })
    exploreAnswer.value = response?.answer || ''
    stopExploreLoading()
    exploreTypewriter(exploreAnswer.value)
  } catch (err) {
    stopExploreLoading()
    console.error('Explore query failed:', err)
    const error = err as { response?: { status?: number; data?: { error?: string } }; message?: string }
    const status = error.response?.status
    const allowanceMessage = dailyAllowanceMessage(err, 'explore', $t)
    if (allowanceMessage) {
      exploreError.value = allowanceMessage
    } else if (status === 401) {
      exploreError.value = $t('shared.aiPanel.explore.errorUnauthorized')
    } else if (status === 404) {
      exploreError.value = $t('shared.aiPanel.explore.errorNotFound')
    } else if (status === 422) {
      exploreError.value = $t('shared.aiPanel.explore.errorNoPath')
    } else {
      exploreError.value = error.response?.data?.error || error.message || $t('shared.aiPanel.explore.errorGeneric')
    }
  } finally {
    exploreLoading.value = false
  }
}

// ═══════════════════════════════════════════════════════════════
// REFINE TAB STATE
// ═══════════════════════════════════════════════════════════════

const instruction = ref('')
const conversationHistory = ref<RefinementTurn[]>([])
const versions = ref<RefinementVersion[]>([])
const selectedVersionIndex = ref(-1)
const refineError = ref('')
const refineWarn = ref('')
const refineLoadingStatus = ref('')
const displayedExplanation = ref('')

// Explore -> Refine pipe. Declared after both states because the function reads/writes
// across both, and TypeScript only narrows safely when refs are in scope.
const isExploreAnswerComplete = computed(() =>
  !exploreLoading.value
  && exploreAnswer.value.length > 0
  && exploreDisplayedAnswer.value === exploreAnswer.value)

// Preamble is an instruction TO the LLM, not UI copy — kept English and
// non-localised so model behaviour stays consistent across locales.
// The <supplied_information> fence delimits LLM-generated content (the Explore
// answer) from the user's own refine instruction, so prompt-injection-shaped
// language inside the answer can't masquerade as an instruction to the model.
const EXPLORE_TO_REFINE_PREAMBLE = 'Incorporate the supplied information below into the current text:\n\n<supplied_information>\n'
const EXPLORE_TO_REFINE_SUFFIX = '\n</supplied_information>'

const sendExploreAnswerToRefine = () => {
  if (!isExploreAnswerComplete.value) { return }
  instruction.value = EXPLORE_TO_REFINE_PREAMBLE + exploreAnswer.value + EXPLORE_TO_REFINE_SUFFIX
  activeTab.value = 'refine'
}
let refineTypewriterTimeout: number | null = null
let refineStatusInterval: number | null = null
const conversationArea = ref<HTMLElement | null>(null)
const showSavePreset = ref(false)
const presetName = ref('')
const presetInstruction = ref('')
const deletingPresetId = ref<string | null>(null)
const expandedPreviews = ref<Set<number>>(new Set())
const pendingInstruction = ref('')

const { mutateAsync: refineContent, isPending: isRefining } =
  API.ContentRefinement.useRefineContent()

// Presets (DB-backed)
const { data: presetsData } = API.RefinementPresets.usePresets(canRefine)
const { mutateAsync: createPreset } = API.RefinementPresets.useCreatePreset()
const { mutateAsync: updatePresetApi } = API.RefinementPresets.useUpdatePreset()
const { mutateAsync: deletePresetApi } = API.RefinementPresets.useDeletePreset()
const editingPreset = ref<{ id: string; name: string; instruction: string } | null>(null)

const userPresets = computed(() =>
  (presetsData.value ?? []).map(p => ({
    id: p.id,
    label: p.name,
    instruction: p.instruction,
  }))
)

const onSavePreset = async () => {
  if (!presetName.value.trim() || !presetInstruction.value.trim()) { return }
  try {
    await createPreset({ name: presetName.value.trim(), instruction: presetInstruction.value.trim() })
    presetName.value = ''
    presetInstruction.value = ''
    showSavePreset.value = false
  } catch (err) {
    console.error('Failed to save preset:', err)
  }
}
const onEditPreset = (preset: UserPreset) => {
  editingPreset.value = { id: preset.id, name: preset.label, instruction: preset.instruction }
}

const onUpdatePreset = async () => {
  if (!editingPreset.value || !editingPreset.value.name.trim() || !editingPreset.value.instruction.trim()) { return }
  try {
    await updatePresetApi({ id: editingPreset.value.id, name: editingPreset.value.name.trim(), instruction: editingPreset.value.instruction.trim() })
    editingPreset.value = null
  } catch (err) {
    console.error('Failed to update preset:', err)
  }
}

const onDeletePreset = (id: string) => { deletingPresetId.value = id }
const onConfirmDelete = async () => {
  if (deletingPresetId.value) {
    try {
      await deletePresetApi(deletingPresetId.value)
      deletingPresetId.value = null
    } catch (err) {
      console.error('Failed to delete preset:', err)
    }
  }
}
const onDeleteFromEditDialog = () => {
  if (editingPreset.value?.id) {
    onDeletePreset(editingPreset.value.id)
    editingPreset.value = null
  }
}
const onPresetClick = (preset: UserPreset) => sendRefinement(preset.instruction)

// Computed
const currentContent = computed(() => {
  const selected = versions.value[selectedVersionIndex.value]
  if (selected && !selected.isOutOfScope) {
    return selected.content
  }
  return props.content
})
const hasVersions = computed(() => versions.value.length > 0)
const iterationCount = computed(() => versions.value.length)
const showIterationWarning = computed(() => iterationCount.value >= 10)

// Loading
const refineStatusMessages = computed(() => [
  $t('shared.aiPanel.refine.statusAnalyzing'),
  $t('shared.aiPanel.refine.statusApplying'),
  $t('shared.aiPanel.refine.statusPolishing'),
  $t('shared.aiPanel.refine.statusFinalizing'),
])
const startRefineLoading = () => {
  let index = 0
  refineLoadingStatus.value = refineStatusMessages.value[0]!
  refineStatusInterval = window.setInterval(() => { index = (index + 1) % refineStatusMessages.value.length; refineLoadingStatus.value = refineStatusMessages.value[index]! }, 3000)
}
const stopRefineLoading = () => { if (refineStatusInterval) { clearInterval(refineStatusInterval); refineStatusInterval = null }; refineLoadingStatus.value = '' }

// Typewriter
const refineTypewriter = (text: string) => {
  if (refineTypewriterTimeout) { clearTimeout(refineTypewriterTimeout); refineTypewriterTimeout = null }
  displayedExplanation.value = ''
  let index = 0
  const type = () => {
    if (index < text.length) { displayedExplanation.value += text.substring(index, index + 5); index += 5; refineTypewriterTimeout = window.setTimeout(type, 10) }
    else { refineTypewriterTimeout = null }
  }
  type()
}

// Validation caps mirror the backend (RefineContentCommandValidator). Catching here
// surfaces a localized message before the request so the user isn't blocked by a
// generic 400 from the API.
const MAX_INSTRUCTION_LENGTH = 10_000
const MAX_CONTENT_LENGTH = 100_000
const MAX_COMBINED_LENGTH = 500_000

// Sliding window over conversationHistory. Backend caps total turns at 20; we keep the
// most-recent 8 user+assistant pairs (16 turns) to leave headroom and avoid the model
// pattern-matching a long run of prior rejections.
const MAX_HISTORY_PAIRS = 8

const appendHistoryPair = (userText: string, assistantText: string) => {
  appendPairWithWindow(conversationHistory.value, userText, assistantText, MAX_HISTORY_PAIRS)
}

// Actions
const sendRefinement = async (instructionText: string) => {
  if (!instructionText.trim() || isRefining.value) { return }
  refineError.value = ''
  refineWarn.value = ''

  const trimmedInstruction = instructionText.trim()
  if (trimmedInstruction.length > MAX_INSTRUCTION_LENGTH) {
    refineError.value = $t('shared.aiPanel.refine.errorInstructionTooLong', {
      length: trimmedInstruction.length,
      max: MAX_INSTRUCTION_LENGTH,
    })
    return
  }

  const contentLength = currentContent.value.length
  if (contentLength > MAX_CONTENT_LENGTH) {
    refineError.value = $t('shared.aiPanel.refine.errorContentTooLong', {
      length: contentLength,
      max: MAX_CONTENT_LENGTH,
    })
    return
  }

  const historyLength = conversationHistory.value.reduce((sum, t) => sum + t.content.length, 0)
  const combinedLength = contentLength + trimmedInstruction.length + historyLength
  if (combinedLength > MAX_COMBINED_LENGTH) {
    refineError.value = $t('shared.aiPanel.refine.errorCombinedTooLong', {
      length: combinedLength,
      max: MAX_COMBINED_LENGTH,
    })
    return
  }

  // Show user message immediately (trimmed — matches what's sent to the API
  // and stored in history/versions, so the chat bubble doesn't display
  // whitespace that gets stripped everywhere else).
  pendingInstruction.value = trimmedInstruction
  instruction.value = ''
  startRefineLoading()

  // Scroll to bottom after showing user message
  await nextTick()
  if (conversationArea.value) { conversationArea.value.scrollTop = conversationArea.value.scrollHeight }

  try {
    // Preserve citations, tables, images etc. with placeholders
    const { content: safeContent, replacementMap } = replaceSpecialElements(currentContent.value)

    const response = await refineContent({
      content: safeContent,
      instruction: trimmedInstruction,
      conversationHistory: conversationHistory.value,
    })
    stopRefineLoading()

    if (response.isOutOfScope) {
      // Preserve the rejected user request and the rejection itself in history so the model
      // can stitch a follow-up data-only message ("here you go: ...") to the still-pending
      // instruction from the prior turn.
      const message = $t('shared.aiPanel.refine.outOfScopeMessage')
      appendHistoryPair(trimmedInstruction, message)
      versions.value.push({
        content: null,
        instruction: trimmedInstruction,
        explanation: message,
        isOutOfScope: true,
      })
      pendingInstruction.value = ''
      refineTypewriter(message)
      await nextTick()
      if (conversationArea.value) { conversationArea.value.scrollTop = conversationArea.value.scrollHeight }
      return
    }

    if (!response.refinedContent) {
      pendingInstruction.value = ''
      // Soft warning (recoverable) rather than red error strip.
      refineWarn.value = $t('shared.aiPanel.refine.errorEmptyResponse')
      return
    }

    // Restore original elements in the refined content
    const restoredContent = restoreSpecialElements(response.refinedContent, replacementMap)
    const explanationText = response.explanation ?? $t('shared.aiPanel.refine.defaultExplanation')

    // Push the user instruction and the full refined text. The backend renders each pair as
    // <instruction> + <version> inside <history>, so the model can see every prior version
    // of the text in addition to <current_text>.
    appendHistoryPair(trimmedInstruction, restoredContent)
    versions.value.push({ content: restoredContent, instruction: trimmedInstruction, explanation: explanationText })
    selectedVersionIndex.value = versions.value.length - 1
    pendingInstruction.value = ''
    // Collapse all previous previews, expand the latest
    expandedPreviews.value = new Set([versions.value.length - 1])
    refineTypewriter(explanationText)
    await nextTick()
    if (conversationArea.value) { conversationArea.value.scrollTop = conversationArea.value.scrollHeight }
  } catch (err) {
    stopRefineLoading()
    pendingInstruction.value = ''
    console.error('Refinement failed:', err)
    const error = err as { response?: { data?: ProblemDetails & { error?: string } }; message?: string }
    refineError.value = dailyAllowanceMessage(err, 'refine', $t)
      || error.response?.data?.error
      || problemDetailsMessage(error.response?.data)
      || error.message
      || $t('shared.aiPanel.explore.errorGeneric')
  }
}

const togglePreview = (index: number) => {
  if (expandedPreviews.value.has(index)) {
    expandedPreviews.value.delete(index)
  } else {
    expandedPreviews.value.add(index)
  }
  expandedPreviews.value = new Set(expandedPreviews.value)
}

const onSendInstruction = () => sendRefinement(instruction.value)

const onSelectVersion = (index: number) => {
  const v = versions.value[index]
  if (!v || v.isOutOfScope) { return }
  selectedVersionIndex.value = index
  displayedExplanation.value = v.explanation
}
const onAccept = () => { emit('accept', currentContent.value); emit('update:isOpen', false) }

// ─── Reset & Lifecycle ──────────────────────────────────────────────

watch(() => props.isOpen, (isOpen) => {
  if (isOpen) {
    activeTab.value = openingTab()
    if (props.initialQuestion) { exploreQuestion.value = props.initialQuestion }
  } else {
    // Clear explore
    if (exploreTypewriterTimeout) { clearTimeout(exploreTypewriterTimeout); exploreTypewriterTimeout = null }
    stopExploreLoading()
    exploreAnswer.value = ''
    exploreDisplayedAnswer.value = ''
    exploreError.value = ''
    exploreQuestion.value = ''
    // Clear refine
    if (refineTypewriterTimeout) { clearTimeout(refineTypewriterTimeout); refineTypewriterTimeout = null }
    stopRefineLoading()
    instruction.value = ''
    conversationHistory.value = []
    versions.value = []
    selectedVersionIndex.value = -1
    refineError.value = ''
    refineWarn.value = ''
    displayedExplanation.value = ''
    showSavePreset.value = false
    presetName.value = ''
    presetInstruction.value = ''
    deletingPresetId.value = null
    editingPreset.value = null
    expandedPreviews.value = new Set()
    pendingInstruction.value = ''
  }
})

onUnmounted(() => {
  if (exploreStatusInterval) { clearInterval(exploreStatusInterval) }
  if (exploreTypewriterTimeout) { clearTimeout(exploreTypewriterTimeout) }
  if (refineStatusInterval) { clearInterval(refineStatusInterval) }
  if (refineTypewriterTimeout) { clearTimeout(refineTypewriterTimeout) }
})
</script>

<template>
  <div v-if="isOpen" class="ai-panel">
      <Tabs v-model="activeTab" class="ai-panel-tabs">
        <div class="ai-tabs-header">
          <TabsList class="ai-tabs-list-underline">
            <TabsTrigger v-if="canExplore" value="explore" class="ai-tab-underline">{{ $t('shared.aiPanel.tabs.explore') }}</TabsTrigger>
            <TabsTrigger v-if="canRefine" value="refine" class="ai-tab-underline">{{ $t('shared.aiPanel.tabs.refine') }}</TabsTrigger>
          </TabsList>
          <button type="button" class="ai-close-btn" @click="$emit('update:isOpen', false)" :aria-label="$t('shared.aiPanel.close')" :title="$t('shared.aiPanel.close')">&times;</button>
        </div>

        <!-- ═══════════ EXPLORE TAB ═══════════ -->
        <TabsContent v-if="canExplore" value="explore" class="ai-tab-content">
          <div class="ai-tab-body">
            <p class="ai-tab-desc">{{ $t('shared.aiPanel.explore.description') }}</p>

            <ChatInput
              v-model="exploreQuestion"
              :label="$t('shared.aiPanel.explore.inputLabel')"
              :placeholder="$t('shared.aiPanel.explore.placeholder')"
              :disabled="exploreLoading"
              :loading="exploreLoading"
              @submit="fetchProvenance"
            />

            <!-- Loading -->
            <div v-if="exploreLoading" class="ai-loading">
              <div class="ai-loading-orb">
                <div class="ai-loading-orb-core" />
                <div class="ai-loading-orb-ring" />
              </div>
              <p class="ai-loading-text">{{ exploreLoadingStatus }}</p>
              <div class="ai-skeleton">
                <div class="ai-skeleton-line" style="width: 85%" />
                <div class="ai-skeleton-line" style="width: 95%" />
                <div class="ai-skeleton-line" style="width: 75%" />
                <div class="ai-skeleton-line" style="width: 90%" />
              </div>
            </div>

            <!-- Error -->
            <div v-else-if="exploreError" class="ai-error">{{ exploreError }}</div>

            <!-- Answer -->
            <div v-else-if="exploreAnswer" class="ai-answer">
              <MarkdownText :content="exploreDisplayedAnswer" class="ai-markdown" />
              <div v-if="isExploreAnswerComplete && canRefine" class="ai-answer-actions">
                <button type="button" class="chat-apply-btn" @click="sendExploreAnswerToRefine">
                  <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M5 12h14"/><path d="m12 5 7 7-7 7"/></svg>
                  {{ $t('shared.aiPanel.explore.useInRefine') }}
                </button>
              </div>
            </div>
          </div>
        </TabsContent>

        <!-- ═══════════ REFINE TAB ═══════════ -->
        <TabsContent v-if="canRefine" value="refine" class="ai-tab-content">
          <div class="ai-tab-body">
            <p class="ai-tab-desc">{{ $t('shared.aiPanel.refine.description') }}</p>

            <div class="ai-presets-row">
              <button
                type="button"
                class="ai-toolbar-btn"
                :title="$t('shared.aiPanel.presets.newPreset')"
                :aria-label="$t('shared.aiPanel.presets.newPreset')"
                @click="showSavePreset = true"
              >+</button>
              <template v-for="preset in userPresets" :key="preset.id">
                <div class="ai-preset">
                  <button type="button" class="ai-preset-btn" :disabled="isRefining" :title="preset.instruction" @click="onPresetClick(preset)">{{ preset.label }}</button>
                  <div class="ai-preset-actions">
                    <button type="button" class="ai-preset-action" :title="$t('shared.aiPanel.presets.editPreset', { name: preset.label })" :aria-label="$t('shared.aiPanel.presets.editPreset', { name: preset.label })" @click.stop="onEditPreset(preset)">
                      <svg aria-hidden="true" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M12 20h9"/><path d="M16.5 3.5a2.121 2.121 0 0 1 3 3L7 19l-4 1 1-4L16.5 3.5z"/></svg>
                    </button>
                    <button type="button" class="ai-preset-action ai-preset-action--delete" :title="$t('shared.aiPanel.presets.deletePreset', { name: preset.label })" :aria-label="$t('shared.aiPanel.presets.deletePreset', { name: preset.label })" @click.stop="onDeletePreset(preset.id)">&times;</button>
                  </div>
                </div>
              </template>
              <div class="ai-toolbar-help-wrap">
                <button
                  type="button"
                  class="ai-toolbar-help"
                  :aria-label="$t('shared.aiPanel.presets.aboutPresets')"
                  :aria-describedby="presetHelpId"
                >?</button>
                <div :id="presetHelpId" class="ai-toolbar-tooltip" role="tooltip">
                  {{ $t('shared.aiPanel.presets.helpText') }}
                </div>
              </div>
            </div>

            <!-- Save preset dialog -->
            <Dialog v-model:open="showSavePreset">
              <DialogContent>
                <DialogHeader>
                  <DialogTitle>{{ $t('shared.aiPanel.presets.createTitle') }}</DialogTitle>
                  <DialogDescription>
                    {{ $t('shared.aiPanel.presets.createDescription') }}
                  </DialogDescription>
                </DialogHeader>
                <div class="ai-save-form">
                  <input v-model="presetName" class="ai-save-input" :aria-label="$t('shared.aiPanel.presets.nameLabel')" :placeholder="$t('shared.aiPanel.presets.createNamePlaceholder')" />
                  <textarea v-model="presetInstruction" class="ai-save-input ai-save-textarea" :aria-label="$t('shared.aiPanel.presets.instructionLabel')" :placeholder="$t('shared.aiPanel.presets.createInstructionPlaceholder')" rows="3" />
                </div>
                <DialogFooter>
                  <Button variant="ghost" size="sm" @click="showSavePreset = false">{{ $t('shared.aiPanel.presets.cancel') }}</Button>
                  <Button variant="outline" size="sm" @click="onSavePreset" :disabled="!presetName.trim() || !presetInstruction.trim()">{{ $t('shared.aiPanel.presets.save') }}</Button>
                </DialogFooter>
              </DialogContent>
            </Dialog>

            <!-- Delete confirmation dialog -->
            <Dialog :open="!!deletingPresetId" @update:open="deletingPresetId = null">
              <DialogContent>
                <DialogHeader>
                  <DialogTitle>{{ $t('shared.aiPanel.presets.deleteTitle') }}</DialogTitle>
                  <DialogDescription>
                    {{ $t('shared.aiPanel.presets.deleteDescription') }}
                  </DialogDescription>
                </DialogHeader>
                <DialogFooter>
                  <Button variant="ghost" size="sm" @click="deletingPresetId = null">{{ $t('shared.aiPanel.presets.cancel') }}</Button>
                  <Button variant="destructive" size="sm" @click="onConfirmDelete">{{ $t('shared.aiPanel.presets.delete') }}</Button>
                </DialogFooter>
              </DialogContent>
            </Dialog>

            <!-- Edit preset dialog -->
            <Dialog :open="!!editingPreset" @update:open="editingPreset = null">
              <DialogContent>
                <DialogHeader>
                  <DialogTitle>{{ $t('shared.aiPanel.presets.editTitle') }}</DialogTitle>
                  <DialogDescription>{{ $t('shared.aiPanel.presets.editDescription') }}</DialogDescription>
                </DialogHeader>
                <div class="ai-save-form" v-if="editingPreset">
                  <input v-model="editingPreset.name" class="ai-save-input" :aria-label="$t('shared.aiPanel.presets.nameLabel')" :placeholder="$t('shared.aiPanel.presets.editNamePlaceholder')" />
                  <textarea v-model="editingPreset.instruction" class="ai-save-input ai-save-textarea" :aria-label="$t('shared.aiPanel.presets.instructionLabel')" :placeholder="$t('shared.aiPanel.presets.editInstructionPlaceholder')" rows="3" />
                </div>
                <DialogFooter>
                  <Button variant="ghost" size="sm" @click="editingPreset = null">{{ $t('shared.aiPanel.presets.cancel') }}</Button>
                  <Button variant="destructive" size="sm" @click="onDeleteFromEditDialog">{{ $t('shared.aiPanel.presets.delete') }}</Button>
                  <Button variant="outline" size="sm" @click="onUpdatePreset" :disabled="!editingPreset?.name?.trim() || !editingPreset?.instruction?.trim()">{{ $t('shared.aiPanel.presets.saveChanges') }}</Button>
                </DialogFooter>
              </DialogContent>
            </Dialog>

            <!-- Warning -->
            <div v-if="showIterationWarning" class="ai-warn">
              {{ $t('shared.aiPanel.refine.iterationWarning', { count: iterationCount }) }}
            </div>

            <!-- Chat thread -->
            <div ref="conversationArea" class="chat-thread">
              <!-- Messages -->
              <template v-for="(version, index) in versions" :key="index">
                <!-- User message -->
                <div class="chat-msg chat-msg--user">
                  <div class="chat-bubble chat-bubble--user">{{ version.instruction }}</div>
                </div>

                <!-- Assistant message -->
                <div class="chat-msg chat-msg--assistant">
                  <div class="chat-bubble chat-bubble--assistant" :class="{ 'chat-bubble--oos': version.isOutOfScope }">
                    <template v-if="version.isOutOfScope">
                      <div class="chat-oos-badge">
                        <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M12 9v4"/><path d="M12 17h.01"/><path d="M10.29 3.86 1.82 18a2 2 0 0 0 1.71 3h16.94a2 2 0 0 0 1.71-3L13.71 3.86a2 2 0 0 0-3.42 0z"/></svg>
                        <span>{{ $t('shared.aiPanel.refine.outOfScopeBadge') }}</span>
                      </div>
                      <p class="chat-explanation">{{ index === versions.length - 1 && refineTypewriterTimeout ? displayedExplanation : version.explanation }}</p>
                    </template>
                    <template v-else>
                      <p class="chat-explanation">{{ index === versions.length - 1 && refineTypewriterTimeout ? displayedExplanation : version.explanation }}</p>
                      <div class="chat-preview" :class="{ open: index === versions.length - 1 }">
                        <button type="button" class="chat-preview-toggle" @click.stop="togglePreview(index)">
                          {{ expandedPreviews.has(index) ? $t('shared.aiPanel.refine.hideContent') : $t('shared.aiPanel.refine.showContent') }}
                        </button>
                        <div v-if="expandedPreviews.has(index) && version.content" v-safe-html.model="version.content" class="chat-preview-content" />
                      </div>
                      <div class="chat-actions">
                        <button
                          v-if="index !== selectedVersionIndex"
                          type="button"
                          class="chat-apply"
                          @click.stop="onSelectVersion(index)"
                        >{{ $t('shared.aiPanel.refine.useVersion') }}</button>
                        <button
                          type="button"
                          class="chat-apply-btn"
                          @click.stop="onSelectVersion(index); onAccept()"
                        >
                          <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.5" stroke-linecap="round" stroke-linejoin="round"><polyline points="20 6 9 17 4 12"/></svg>
                          {{ $t('shared.aiPanel.refine.applyToContent') }}
                        </button>
                      </div>
                    </template>
                  </div>
                </div>
              </template>

              <!-- Pending user message (shown immediately before API responds) -->
              <div v-if="pendingInstruction" class="chat-msg chat-msg--user">
                <div class="chat-bubble chat-bubble--user">{{ pendingInstruction }}</div>
              </div>

              <!-- Loading -->
              <div v-if="isRefining" class="chat-msg chat-msg--assistant">
                <div class="chat-bubble chat-bubble--assistant chat-bubble--loading">
                  <div class="ai-loading-orb"><div class="ai-loading-orb-core" /><div class="ai-loading-orb-ring" /></div>
                  <p class="ai-loading-text">{{ refineLoadingStatus }}</p>
                </div>
              </div>
            </div>

            <!-- Hard error (network / 5xx etc.) -->
            <div v-if="refineError" class="ai-error">{{ refineError }}</div>
            <!-- Soft warning (recoverable, e.g. empty response) -->
            <div v-if="refineWarn" class="ai-warn">{{ refineWarn }}</div>

            <!-- Input -->
            <ChatInput
              v-model="instruction"
              :label="$t('shared.aiPanel.refine.inputLabel')"
              :placeholder="hasVersions ? $t('shared.aiPanel.refine.placeholderFollowUp') : $t('shared.aiPanel.refine.placeholder')"
              :disabled="isRefining"
              :loading="isRefining"
              @submit="onSendInstruction"
            />

          </div>
        </TabsContent>
      </Tabs>
  </div>
</template>

<style scoped>
.ai-panel {
  display: flex;
  flex-direction: column;
  margin-top: var(--spacing-sm);
  padding: var(--spacing-sm);
  border: 1px solid var(--stroke);
  border-radius: var(--radius-card);
  background: var(--bg-1);
  animation: fade-in 0.2s ease-out;
  max-height: 1000px;
}

.ai-panel-tabs {
  display: flex;
  flex-direction: column;
  height: 100%;
  min-height: 0;
}

.ai-tabs-header {
  display: flex;
  align-items: center;
  margin-bottom: var(--spacing-sm);
}

.ai-tabs-list-underline {
  display: flex;
  flex: 1;
  gap: 0;
  border-bottom: 1px solid var(--stroke);
  flex-shrink: 0;
  background: none !important;
  padding: 0 !important;
  border-radius: 0 !important;
}

.ai-tab-underline {
  flex: 1;
  text-align: center;
}

.ai-tab-underline {
  padding: 8px 16px !important;
  font-size: 13px !important;
  font-weight: 500 !important;
  color: var(--text-secondary) !important;
  background: none !important;
  border: none !important;
  border-bottom: 2px solid transparent !important;
  border-radius: 0 !important;
  margin-bottom: -1px;
  cursor: pointer;
  box-shadow: none !important;
  transition: all 0.15s ease;
}

.ai-tab-underline:hover {
  color: var(--text-primary) !important;
}

.ai-tab-underline[data-state="active"] {
  color: var(--text-primary) !important;
  border-bottom-color: var(--primary) !important;
  font-weight: 600 !important;
  background: none !important;
}

.ai-close-btn {
  width: 28px;
  height: 28px;
  display: flex;
  align-items: center;
  justify-content: center;
  border: none;
  border-radius: 6px;
  background: transparent;
  color: var(--text-muted);
  font-size: 18px;
  cursor: pointer;
  transition: all 0.15s ease;
}

.ai-close-btn:hover {
  background: var(--bg-2);
  color: var(--text-primary);
}

.ai-tab-content {
  flex: 1;
  min-height: 0;
  display: flex;
  flex-direction: column;
}

.ai-tab-body {
  flex: 1;
  display: flex;
  flex-direction: column;
  min-height: 0;
}

.ai-tab-desc {
  font-size: 13px;
  color: var(--text-secondary);
  margin: 0 0 4px;
}

/* ─── Shared: loading ────────────────────── */

.ai-loading {
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: 8px;
  padding: 24px 0;
}

.ai-loading-orb {
  position: relative;
  width: 60px;
  height: 60px;
  display: flex;
  align-items: center;
  justify-content: center;
}

.ai-loading-orb-core {
  width: 34px;
  height: 34px;
  background: var(--primary);
  border-radius: 50%;
  box-shadow: 0 0 16px color-mix(in srgb, var(--accent-indigo-from) 60%, transparent);
  animation: pulse-glow 2s ease-in-out infinite;
  z-index: 2;
  position: relative;
}

.ai-loading-orb-ring {
  position: absolute;
  width: 60px;
  height: 60px;
  border: 2px solid color-mix(in srgb, var(--accent-indigo-from) 40%, transparent);
  border-radius: 50%;
  animation: pulse-ring 2s ease-out infinite;
}

.ai-loading-text {
  font-size: 12px;
  color: var(--text-secondary);
  font-weight: 500;
  margin: 0;
  animation: fade-pulse 2s ease-in-out infinite;
}

.ai-skeleton {
  display: flex;
  flex-direction: column;
  gap: 6px;
  width: 100%;
  padding: 0 8px;
}

.ai-skeleton-line {
  height: 12px;
  background: linear-gradient(90deg, rgba(200,200,200,0.2) 0%, rgba(200,200,200,0.3) 50%, rgba(200,200,200,0.2) 100%);
  background-size: 200% 100%;
  border-radius: 3px;
  animation: skeleton-loading 1.5s ease-in-out infinite;
}

/* ─── Shared: error ──────────────────────── */

.ai-error {
  padding: 8px;
  background: var(--bg-1);
  border: 1px solid var(--accent-error);
  border-radius: 4px;
  color: var(--accent-error);
  font-size: 13px;
  line-height: 1.4;
}

/* ─── Explore: answer ────────────────────── */

.ai-answer {
  padding: 10px;
  background: var(--bg-1);
  border-radius: 4px;
  line-height: 1.5;
  animation: fade-in 0.3s ease-in;
  flex: 1;
  overflow-y: auto;
  min-height: 0;
}

.ai-markdown {
  font-size: 13px;
}

/* ─── Refine: presets ────────────────────── */

.ai-refine-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  margin-bottom: 4px;
}

.ai-refine-header .ai-tab-desc { margin: 0; }

.ai-tabs-actions {
  display: flex;
  align-items: center;
  gap: 6px;
  flex-wrap: wrap;
}

.ai-presets-row {
  display: flex;
  align-items: center;
  flex-wrap: wrap;
  gap: 6px;
  margin-bottom: 0;
}
.ai-preset { position: relative; }

.ai-preset-btn {
  height: 26px;
  padding: 0 10px;
  font-size: 12px;
  font-weight: 500;
  border: 1px solid var(--stroke);
  border-radius: 6px;
  background: transparent;
  color: var(--text-secondary);
  cursor: pointer;
  transition: all 0.15s ease;
  display: flex;
  align-items: center;
}

.ai-preset-btn:hover:not(:disabled) { border-color: var(--accent-indigo-from); color: var(--text-primary); }
.ai-preset-btn:disabled { opacity: 0.4; cursor: not-allowed; }

.ai-preset-actions {
  position: absolute;
  top: -10px;
  right: -10px;
  display: none;
}

.ai-preset:hover .ai-preset-actions,
.ai-preset:focus-within .ai-preset-actions { display: flex; }

/* The circle is drawn by ::before so the button keeps a 24px target around a 16px mark. */
.ai-preset-action {
  position: relative;
  isolation: isolate;
  border-radius: 50%;
  width: 24px;
  height: 24px;
  border: 0;
  background: none;
  color: var(--text-muted);
  font-size: 10px;
  line-height: 1;
  cursor: pointer;
  display: flex;
  align-items: center;
  justify-content: center;
  padding: 0;
}

.ai-preset-action::before {
  content: '';
  position: absolute;
  inset: 4px;
  z-index: -1;
  border-radius: 50%;
  border: 1px solid var(--stroke);
  background: var(--bg-1);
}

.ai-preset-action svg { width: 9px; height: 9px; }
.ai-preset-action:hover { color: var(--accent-indigo-from); }
.ai-preset-action:hover::before { border-color: var(--accent-indigo-from); }
.ai-preset-action--delete:hover { color: white; }
.ai-preset-action--delete:hover::before { background: var(--accent-error-fill); border-color: var(--accent-error); }

/* ─── Refine: warning ────────────────────── */

.ai-warn {
  padding: 8px 12px; font-size: 12px; color: var(--accent-warning);
  background: color-mix(in srgb, var(--accent-warning) 8%, transparent);
  border: 1px solid color-mix(in srgb, var(--accent-warning) 25%, transparent);
  border-radius: 6px; margin-bottom: 12px;
}

/* ─── Chat thread ────────────────────────── */

/* ─── Chat thread ────────────────────────── */

.chat-thread {
  flex: 1;
  overflow-y: auto;
  display: flex;
  flex-direction: column;
  min-height: 250px;
  gap: 6px;
  padding: 8px 0;
}

.chat-msg {
  display: flex;
}

.chat-msg--user { justify-content: flex-end; }
.chat-msg--assistant { justify-content: flex-start; }

.chat-bubble {
  max-width: 90%;
  padding: 10px 14px;
  font-size: 13px;
  line-height: 1.5;
  white-space: pre-wrap;
  /* break-word (not `anywhere`) so atomic tokens like {{#FACT_1}} stay intact
     when the bubble narrows; long URLs / unbroken strings still break. */
  overflow-wrap: break-word;
}

.chat-bubble--user {
  background: var(--bg-2);
  color: var(--text-primary);
  border-radius: 8px;
  font-weight: 500;
}

.chat-bubble--assistant {
  color: var(--text-primary);
  padding: 10px 0;
}

.chat-bubble--loading {
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: 8px;
  padding: 20px 0;
  min-width: 160px;
}

/* ─── Chat message content ───────────────── */

.chat-explanation {
  margin: 0;
  font-size: 13px;
  line-height: 1.5;
  color: var(--text-secondary);
}

.chat-bubble--oos {
  background: color-mix(in srgb, var(--accent-warning) 8%, transparent);
  border-left: 3px solid var(--accent-warning);
  padding: 10px 14px;
  border-radius: 4px;
}

.chat-oos-badge {
  display: inline-flex;
  align-items: center;
  gap: 5px;
  font-size: 12px;
  font-weight: 600;
  color: var(--accent-warning);
  margin-bottom: 6px;
  letter-spacing: 0.01em;
}

.chat-oos-badge svg {
  width: 14px;
  height: 14px;
}

.ai-answer-actions {
  margin-top: 12px;
  display: flex;
  justify-content: flex-end;
}

.chat-preview {
  margin-top: 8px;
}

.chat-preview-toggle {
  font-size: 12px;
  color: var(--text-muted);
  font-weight: 500;
  background: none;
  border: none;
  cursor: pointer;
  padding: 0;
}

.chat-preview-toggle:hover {
  color: var(--text-secondary);
}

.chat-preview-content {
  margin-top: 8px;
  padding: 12px;
  background: var(--bg-2);
  border-radius: var(--radius-small);
  font-size: 13px;
  line-height: 1.6;
  color: var(--text-primary);
  /* Rendered HTML, not preformatted text — reset the pre-wrap inherited from
     .chat-bubble so newlines between block tags don't render as blank lines. */
  white-space: normal;
}

.chat-preview-content :deep(p) { margin: 0 0 0.5em; font-size: inherit; }
.chat-preview-content :deep(p:last-child) { margin-bottom: 0; }

/* ─── Chat actions ───────────────────────── */

.chat-actions {
  display: flex;
  gap: 12px;
  margin-top: 8px;
}

.chat-apply {
  font-size: 12px;
  font-weight: 500;
  color: var(--text-muted);
  background: none;
  border: none;
  cursor: pointer;
  padding: 0;
}

.chat-apply:hover { color: var(--text-primary); }

.chat-apply-btn {
  display: inline-flex;
  align-items: center;
  gap: 5px;
  height: 26px;
  padding: 0 10px;
  font-size: 12px;
  font-weight: 600;
  color: var(--accent-indigo-from);
  background: color-mix(in srgb, var(--accent-indigo-from) 8%, transparent);
  border: 1px solid color-mix(in srgb, var(--accent-indigo-from) 20%, transparent);
  border-radius: 6px;
  cursor: pointer;
  transition: all 0.15s ease;
}

.chat-apply-btn:hover {
  background: color-mix(in srgb, var(--accent-indigo-from) 15%, transparent);
  border-color: var(--accent-indigo-from);
}

.chat-apply-btn svg {
  width: 12px;
  height: 12px;
}

/* ─── Toolbar (+ and ?) ──────────────────── */

.ai-toolbar-btn {
  height: 26px;
  padding: 0 8px;
  display: flex;
  align-items: center;
  justify-content: center;
  border: 1px solid var(--stroke);
  border-radius: 6px;
  background: transparent;
  color: var(--text-secondary);
  font-size: 14px;
  font-weight: 500;
  line-height: 0;
  cursor: pointer;
  transition: all 0.15s ease;
}

.ai-toolbar-btn:hover {
  border-color: var(--accent-indigo-from);
  color: var(--accent-indigo-from);
}

.ai-toolbar-help {
  width: 24px;
  height: 24px;
  border-radius: 50%;
  display: flex;
  align-items: center;
  justify-content: center;
  background: transparent;
  border: 0;
  color: var(--text-muted);
  font-size: 11px;
  font-weight: 600;
  cursor: help;
  position: relative;
  isolation: isolate;
}

.ai-toolbar-help::before {
  content: '';
  position: absolute;
  inset: 2px;
  z-index: -1;
  border-radius: 50%;
  border: 1px solid var(--stroke);
}

.ai-toolbar-help-wrap {
  position: relative;
}

.ai-toolbar-help:focus-visible {
  outline: 2px solid var(--ring);
  outline-offset: 2px;
}

.ai-toolbar-help:hover + .ai-toolbar-tooltip,
.ai-toolbar-help:focus-visible + .ai-toolbar-tooltip,
.ai-toolbar-tooltip:hover {
  opacity: 1;
  visibility: visible;
  transform: translateY(0);
}

.ai-toolbar-tooltip {
  position: absolute;
  top: calc(100% + 8px);
  left: -8px;
  width: 260px;
  padding: 10px 12px;
  font-size: 12px;
  line-height: 1.5;
  color: var(--text-primary);
  background: var(--bg-1);
  border: 1px solid var(--stroke);
  border-radius: 8px;
  box-shadow: var(--shadow-card);
  z-index: 10;
  opacity: 0;
  visibility: hidden;
  transform: translateY(4px);
  transition: all 0.15s ease;
  pointer-events: auto;
}

/* ─── Save preset dialog ─────────────────── */

.ai-save-form {
  display: flex;
  flex-direction: column;
  gap: 8px;
  padding: 4px 0;
}

.ai-save-input {
  width: 100%;
  padding: 8px 10px;
  border: 1px solid var(--stroke);
  border-radius: 6px;
  font-size: 13px;
  background: transparent;
  color: var(--text-primary);
  outline: none;
}

.ai-save-input:focus { border-color: var(--accent-indigo-from); }
.ai-save-input::placeholder { color: var(--text-muted); }

.ai-save-textarea {
  resize: vertical;
  font-family: inherit;
}

/* ─── Chat footer ────────────────────────── */



/* ─── Animations ─────────────────────────── */

@keyframes pulse-glow {
  0%, 100% { transform: scale(1); box-shadow: 0 0 20px color-mix(in srgb, var(--accent-indigo-from) 60%, transparent); }
  50% { transform: scale(1.1); box-shadow: 0 0 30px color-mix(in srgb, var(--accent-indigo-from) 80%, transparent), 0 0 60px color-mix(in srgb, var(--accent-indigo-to) 40%, transparent); }
}
@keyframes pulse-ring { 0% { transform: scale(0.5); opacity: 1; } 100% { transform: scale(1.2); opacity: 0; } }
@keyframes fade-pulse { 0%, 100% { opacity: 0.6; } 50% { opacity: 1; } }
@keyframes skeleton-loading { 0% { background-position: 200% 0; } 100% { background-position: -200% 0; } }
@keyframes fade-in { from { opacity: 0; transform: translateY(8px); } to { opacity: 1; transform: translateY(0); } }
</style>
