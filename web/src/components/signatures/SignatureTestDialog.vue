<script setup lang="ts">
/**
 * Signature test-replay — inline dialog opened from the SignatureSettings field editor.
 *
 * The user has already picked a (block, field) in the editor; this dialog lets them
 * test a new value for that signature against a past workflow execution without
 * leaving the page. Captured prompt + resolved value come back from the BE; the
 * modified prompt stays server-side (Inia IP).
 *
 * Two actions live here:
 *   - Run test  → fire the replay via the SignaturePlayground API, render the new
 *                 block output next to the original for side-by-side comparison.
 *   - Propose as override → emit `submit-proposal` so the parent can open its
 *                 review queue with the tested value as the proposed value.
 */
import { computed, ref, useId, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import axios from 'axios'
import API from '@/api'
import { stripXmlTags, parseServerDate } from '@/lib/utils'
import { Button } from '@/components/ui/button'
import { Badge } from '@/components/ui/badge'
import { Textarea } from '@/components/ui/textarea'
import { Dialog, DialogContent, DialogHeader, DialogTitle, DialogDescription } from '@/components/ui/dialog'
import IniaLoader from '@/components/atoms/IniaLoader/IniaLoader.vue'
import IniaIcon from '@/components/atoms/IniaIcon/IniaIcon.vue'
import CounterpartySearchableSelect from '@/components/molecules/CounterpartySearchableSelect/CounterpartySearchableSelect.vue'
import SearchableSelect from '@/components/molecules/SearchableSelect/SearchableSelect.vue'
import IniaSelect from '@/components/molecules/IniaSelect/IniaSelect.vue'
import type {
  ExecutionBlock,
  ReplayableSignatureField,
  SignaturePromptTestResult,
} from '@/api/signaturePlayground/types'
import type { WorkflowReportSummary } from '@/api/workflowReports/schema'
import type { OptionType } from '@/types'
import {
  buildCounterpartyOptions,
  buildExecutionOptions,
  pickExecutionPlaceholder,
} from '@/lib/signatureTestPickers'

const capturedValueId = useId()
const counterpartyPickerId = useId()
const executionPickerId = useId()
const blockPickerId = useId()
const testValueId = useId()

const props = defineProps<{
  open: boolean
  workflowTypeId: string
  // Stable match key across editor → clone → execution snapshot; block ids
  // regenerate on each execution but ReplacementTag is carried through.
  replacementTag: string | null
  placeholderName: string | null
  fieldDisplayName: string | null
  sharedBlockCount?: number
  initialExecutionId?: string | null
}>()

const emit = defineEmits<{
  (e: 'update:open', value: boolean): void
  (e: 'submit-proposal', payload: { placeholderName: string; proposedValue: string }): void
}>()

const { t } = useI18n()

// ─── Execution picker ────────────────────────────────────────────────────
// Counterparty filter drives a server-side OData query so we're not paging
// through hundreds of executions client-side. Top 100 is a balance — enough
// that a narrowed counterparty tends to resolve inside the window, few
// enough that the payload stays snappy.
const EXECUTIONS_TOP = 100

const counterpartyFilter = ref('')

// Tenant counterparties for the filter dropdown. Bounded `$top` matching
// the WorkflowReports page's convention.
const counterpartyParams = ref({ $top: 10000 })
const { data: counterpartiesResponse } =
  API.Counterparties.useGetCounterparties(counterpartyParams)
const counterparties = computed(
  () => counterpartiesResponse.value?.value ?? [],
)

const counterpartyNameById = computed(() => {
  const map = new Map<string, string>()
  for (const cp of counterparties.value) map.set(cp.id, cp.name)
  return map
})

const counterpartyOptions = computed<OptionType[]>(() =>
  buildCounterpartyOptions(
    counterparties.value,
    t('page.signatures.test.filters.anyCounterparty'),
  ),
)

function escapeODataLiteral(value: string): string {
  return value.replace(/'/g, "''")
}

const executionPickerParams = computed(() => {
  const parts: string[] = []
  if (props.workflowTypeId) {
    parts.push(`workflowTypeId eq '${escapeODataLiteral(props.workflowTypeId)}'`)
  }
  if (counterpartyFilter.value) {
    parts.push(`counterpartyId eq '${escapeODataLiteral(counterpartyFilter.value)}'`)
  }
  return {
    $filter: parts.join(' and '),
    $orderby: 'createdAt desc',
    $top: EXECUTIONS_TOP,
  }
})

const reportsQuery = API.WorkflowReports.useGetWorkflowReports(
  executionPickerParams,
  { enabled: computed(() => props.open && !!props.workflowTypeId) },
)

const executionsList = computed<WorkflowReportSummary[]>(
  () => (reportsQuery.data.value?.value as WorkflowReportSummary[] | undefined) ?? [],
)

const executionOptions = computed<OptionType[]>(() =>
  buildExecutionOptions(executionsList.value, {
    fmtDate,
    counterpartyLabel,
  }),
)

const executionPickerPlaceholder = computed(() =>
  pickExecutionPlaceholder(
    {
      isLoading: reportsQuery.isLoading.value,
      isEmpty: executionsList.value.length === 0,
    },
    {
      loading: t('page.signatures.test.executionPicker.loading'),
      empty: t('page.signatures.test.executionPicker.empty'),
      ready: t('page.signatures.test.executionPicker.label'),
    },
  ),
)

function counterpartyLabel(id: string | undefined | null): string {
  if (!id) return ''
  return counterpartyNameById.value.get(id) ?? id
}

const selectedExecutionId = ref<string>('')

// Set when a deep-link `initialExecutionId` was provided but wasn't in the
// $top:25 execution list the picker currently shows — the user landed here
// expecting to see that specific run, but the picker silently fell back to
// most-recent. We surface this as a banner so they don't unknowingly test
// against the wrong execution.
const deepLinkedExecutionMissing = ref(false)

// Auto-pick an execution each time the dialog opens, or whenever the fetched
// list resolves. Preference order:
//   1. initialExecutionId prop (set when the user landed here from a specific
//      workflow-report page) — pick only if that execution is actually in the
//      list for this workflow type.
//   2. Most recent run (first element of the desc-ordered list).
// The watch also re-runs when the user changes field mid-dialog and the list
// shape changes, but only assigns if nothing's been picked yet.
watch(
  [() => props.open, executionsList, () => props.initialExecutionId],
  ([isOpen, list, initial]) => {
    if (!isOpen || list.length === 0) {
      return
    }
    if (selectedExecutionId.value) {
      return
    }
    const preferred = initial ? list.find(e => e.id === initial) : undefined
    if (initial && !preferred) {
      // Deep-link pointed at a run older than the picker's recent window.
      deepLinkedExecutionMissing.value = true
    }
    selectedExecutionId.value = preferred ? initial! : list[0].id
  },
  { immediate: true },
)

// Clear the banner when the user manually picks an execution from the
// dropdown — they've acknowledged the situation either way.
watch(selectedExecutionId, () => {
  deepLinkedExecutionMissing.value = false
})

// When the user narrows via the counterparty filter, drop any current
// selection so the picker auto-selects the most-recent match from the
// filtered list on the next tick. Otherwise the dropdown would show a
// stale `selectedExecutionId` that isn't in the newly filtered results.
watch(counterpartyFilter, () => {
  selectedExecutionId.value = ''
})

// ─── Block / field resolution ────────────────────────────────────────────
const executionBlocksQuery = API.SignaturePlayground.useGetExecutionBlocks(selectedExecutionId)
const executionBlocks = computed<ExecutionBlock[]>(
  () => executionBlocksQuery.data.value?.blocks ?? [],
)

// Blocks that declare the placeholder in the picked execution. We do NOT
// filter on `canReplay` here — a pre-capture run still has the placeholder
// listed with `canReplay: false`, and filtering it out would make the
// picker empty and the pre-capture banner unreachable. The Run button is
// gated separately on `canReplay` so pre-capture cases surface with a
// clear explanation rather than a confusing "block didn't run" state.
const candidateBlocks = computed<ExecutionBlock[]>(() => {
  if (!props.placeholderName) {
    return []
  }
  return executionBlocks.value.filter(b =>
    b.signatureFields.some(f => f.placeholderName === props.placeholderName),
  )
})

const blockOptions = computed<OptionType[]>(() =>
  candidateBlocks.value.map(block => ({
    label: stepLabel(block),
    value: block.blockId,
  })),
)

const selectedBlockId = ref<string>('')

watch(
  [candidateBlocks, () => props.replacementTag],
  ([blocks, tag]) => {
    if (blocks.length === 0) {
      selectedBlockId.value = ''
      return
    }
    const stillThere = selectedBlockId.value
      && blocks.some(b => b.blockId === selectedBlockId.value)
    if (stillThere) {
      return
    }
    const byTag = tag ? blocks.find(b => b.replacementTag === tag) : undefined
    selectedBlockId.value = (byTag ?? blocks[0]).blockId
  },
  { immediate: true },
)

const matchedBlock = computed<ExecutionBlock | null>(
  () => candidateBlocks.value.find(b => b.blockId === selectedBlockId.value) ?? null,
)

const matchedField = computed<ReplayableSignatureField | null>(() => {
  if (!matchedBlock.value || !props.placeholderName) {
    return null
  }
  return (
    matchedBlock.value.signatureFields.find(f => f.placeholderName === props.placeholderName)
      ?? null
  )
})

const capturedValue = computed(() => matchedField.value?.capturedResolvedValue ?? '')
const canReplay = computed(() => matchedField.value?.canReplay ?? false)

// ─── Test value + replay ──────────────────────────────────────────────────
const testValue = ref<string>('')
const testResult = ref<SignaturePromptTestResult | null>(null)
const runError = ref<string | null>(null)

// Seed the test-value textarea only when the dialog *opens* fresh. Earlier
// this reset on every matchedField change, which silently wiped the user's
// draft when they switched step/execution mid-edit. Now they keep their
// draft across picker changes — the only thing that resets it is a
// genuine open-after-close cycle.
watch(
  () => props.open,
  (isOpen, wasOpen) => {
    if (isOpen && !wasOpen) {
      testResult.value = null
      runError.value = null
      testValue.value = capturedValue.value
    }
  },
  { immediate: true },
)

// Dialog state must also be torn down when the parent flips `open` to false
// directly (e.g. after a successful Propose-as-override). `onClose` only
// fires on the child's own close events, so rely on the open-prop watch
// instead of the close callback for cleanup.
watch(
  () => props.open,
  (isOpen) => {
    if (!isOpen) {
      selectedExecutionId.value = ''
      selectedBlockId.value = ''
      testResult.value = null
      runError.value = null
    }
  },
)

const testMutation = API.SignaturePlayground.useTestSignaturePromptMutation(selectedExecutionId)

const runDisabled = computed(() => {
  if (!canReplay.value || !matchedBlock.value) {
    return true
  }
  if (testMutation.isPending.value) {
    return true
  }
  // Allow any non-identical test value, including empty, so the user can
  // legitimately test "what happens if this signature goes away". Same-
  // value re-runs are allowed too (useful for checking model determinism).
  return false
})

async function onRun() {
  if (!matchedBlock.value || !props.placeholderName) {
    return
  }
  runError.value = null
  try {
    const result = await testMutation.mutateAsync({
      blockId: matchedBlock.value.blockId,
      placeholderName: props.placeholderName,
      testValue: testValue.value,
    })
    testResult.value = result
  }
  catch (err: unknown) {
    if (axios.isAxiosError(err)) {
      runError.value
        = (err.response?.data as { detail?: string } | undefined)?.detail
        ?? t('page.signatures.test.errors.runFailed')
      return
    }
    runError.value = t('page.signatures.test.errors.runFailed')
  }
}

// True when the user has typed a value that differs from what was
// captured. Drives the "Edited" chip on the test pane + gates the
// Propose-as-override button (which needs a real change, unlike Run
// which we allow to re-execute the same or empty value for exploration).
const hasDiff = computed(() => {
  const trimmed = testValue.value.trim()
  return !!trimmed && trimmed !== capturedValue.value
})

// ─── Submit as pending override ──────────────────────────────────────────
const submitDisabled = computed(() => {
  if (!props.placeholderName) {
    return true
  }
  return !hasDiff.value
})

// The field is shared when more than one block references it — the proposed
// override then applies to all of them, which the footer note calls out.
const isSharedField = computed(() => (props.sharedBlockCount ?? 1) > 1)

function onSubmitProposal() {
  if (!props.placeholderName) {
    return
  }
  emit('submit-proposal', {
    placeholderName: props.placeholderName,
    proposedValue: testValue.value,
  })
}

// Cleanup lives in the open-prop watcher above — it fires whether the
// close was user-initiated or parent-initiated (e.g. after a successful
// Propose-as-override).
function onClose(value: boolean) {
  emit('update:open', value)
}

function fmtDate(value: string | undefined) {
  if (!value) {
    return ''
  }
  try {
    return parseServerDate(value).toLocaleString()
  }
  catch {
    return value
  }
}

function stepBlockName(block: ExecutionBlock): string {
  return block.blockName || block.replacementTag || block.blockId
}

// Used in the dropdown where we can't render an icon between the two parts.
// Falls back to a " › " (right-pointing angle quote) because section titles
// and block names in this app frequently contain em dashes themselves, so a
// plain dash would be visually ambiguous.
function stepLabel(block: ExecutionBlock): string {
  const name = stepBlockName(block)
  return block.sectionTitle ? `${block.sectionTitle} › ${name}` : name
}

// Show the outputs as plain text with every \n preserved. We strip XML-ish
// wrapper tags the models emit (<answer>, <thinking>, <reasoning>, etc.)
// so analysts see the content, not the structural markers.
const originalOutputText = computed(() => stripXmlTags(matchedBlock.value?.rawResponse ?? ''))
const testOutputText = computed(() => stripXmlTags(testResult.value?.responseText ?? ''))
</script>

<template>
  <Dialog :open="props.open" @update:open="onClose">
    <DialogContent class="sig-test-dialog__content">
      <DialogHeader class="sig-test-dialog__header">
        <div class="sig-test-dialog__heading-row">
          <div class="sig-test-dialog__heading-icon">
            <IniaIcon name="play" class="w-5 h-5" />
          </div>
          <div class="sig-test-dialog__heading-text">
            <DialogTitle class="sig-test-dialog__title">
              {{ t('page.signatures.test.title') }}
            </DialogTitle>
            <DialogDescription v-if="props.fieldDisplayName" class="sig-test-dialog__subtitle">
              {{ t('page.signatures.test.subtitle', { field: props.fieldDisplayName }) }}
            </DialogDescription>
          </div>
        </div>
      </DialogHeader>

      <div class="sig-test-dialog__body">
        <!-- Step 1: Pick a past run. Counterparty narrows server-side via
             OData. Execution + step pickers sit in the same row underneath. -->
        <section class="sig-test-dialog__phase">
          <header class="sig-test-dialog__phase-head">
            <span class="sig-test-dialog__phase-num">1</span>
            <h3 class="sig-test-dialog__phase-title">
              {{ t('page.signatures.test.phases.pickRun') }}
            </h3>
          </header>

          <div class="sig-test-dialog__pickers">
            <div class="sig-test-dialog__picker">
              <label :for="counterpartyPickerId" class="sig-test-dialog__picker-label">
                {{ t('page.signatures.test.filters.counterpartyLabel') }}
              </label>
              <CounterpartySearchableSelect
                :id="counterpartyPickerId"
                v-model="counterpartyFilter"
                :options="counterpartyOptions"
                :placeholder="t('page.signatures.test.filters.anyCounterparty')"
              />
            </div>

            <div class="sig-test-dialog__picker sig-test-dialog__picker--wide">
              <label :for="executionPickerId" class="sig-test-dialog__picker-label">
                {{ t('page.signatures.test.executionPicker.label') }}
              </label>
              <SearchableSelect
                :id="executionPickerId"
                v-model="selectedExecutionId"
                :options="executionOptions"
                :placeholder="executionPickerPlaceholder"
                :disabled="executionsList.length === 0"
              />
            </div>

            <div
              v-if="candidateBlocks.length > 1"
              class="sig-test-dialog__picker"
            >
              <label :for="blockPickerId" class="sig-test-dialog__picker-label">
                {{ t('page.signatures.test.blockPicker.label') }}
              </label>
              <IniaSelect
                :id="blockPickerId"
                v-model="selectedBlockId"
                :options="blockOptions"
              />
            </div>
          </div>
        </section>

        <!-- Warn when the user deep-linked from a report the picker
             doesn't currently have in its recent window — otherwise
             they'd silently test against the wrong execution. -->
        <div
          v-if="deepLinkedExecutionMissing"
          class="sig-test-dialog__state sig-test-dialog__state--warn"
        >
          <IniaIcon name="exclamation-triangle" class="w-4 h-4" />
          <span>{{ t('page.signatures.test.deepLinkNotInList') }}</span>
        </div>

        <!-- State: loading, fetch error, empty-candidates, pre-capture, editor. -->
        <div
          v-if="selectedExecutionId && executionBlocksQuery.isLoading.value"
          class="sig-test-dialog__state"
        >
          <IniaLoader />
        </div>

        <div
          v-else-if="selectedExecutionId && executionBlocksQuery.isError.value"
          class="sig-test-dialog__state sig-test-dialog__state--warn"
        >
          <IniaIcon name="exclamation-triangle" class="w-4 h-4" />
          <span>{{ t('page.signatures.test.loadError') }}</span>
        </div>

        <div
          v-else-if="selectedExecutionId && candidateBlocks.length === 0"
          class="sig-test-dialog__state sig-test-dialog__state--warn"
        >
          <IniaIcon name="circle-info" class="w-4 h-4" />
          <span>{{ t('page.signatures.test.blockNotFoundInExecution') }}</span>
        </div>

        <div
          v-else-if="selectedExecutionId && matchedBlock && !canReplay"
          class="sig-test-dialog__state sig-test-dialog__state--warn"
        >
          <IniaIcon name="circle-info" class="w-4 h-4" />
          <span>{{ t('page.signatures.test.fieldEditor.preCaptureRunNote') }}</span>
        </div>

        <!-- Step 2: Test your override. Inner sub-sections (edit value /
             compare output) are unnumbered — the outer phase numbers
             carry the flow, nesting numbers within numbers gets noisy. -->
        <section
          v-if="matchedBlock && canReplay"
          class="sig-test-dialog__phase"
        >
          <header class="sig-test-dialog__phase-head">
            <span class="sig-test-dialog__phase-num">2</span>
            <h3 class="sig-test-dialog__phase-title">
              {{ t('page.signatures.test.phases.testOverride') }}
            </h3>
          </header>

          <!-- Step info strip — section › block breadcrumb. Separator is a
               chevron icon so block/section names containing em-dashes can't
               be mistaken for the separator. -->
          <div class="sig-test-dialog__step-info">
            <span
              v-if="matchedBlock.sectionTitle"
              class="sig-test-dialog__crumb sig-test-dialog__crumb--section"
            >
              {{ matchedBlock.sectionTitle }}
            </span>
            <IniaIcon
              v-if="matchedBlock.sectionTitle"
              name="chevron-right"
              class="w-3.5 h-3.5 sig-test-dialog__crumb-sep"
            />
            <span class="sig-test-dialog__crumb sig-test-dialog__crumb--step">
              {{ stepBlockName(matchedBlock) }}
            </span>
          </div>

          <!-- Values: captured vs test, side-by-side. -->
          <div class="sig-test-dialog__sub">
            <h4 class="sig-test-dialog__sub-title">
              {{ t('page.signatures.test.section.editValue') }}
            </h4>
            <div class="sig-test-dialog__values">
              <div class="sig-test-dialog__value-col">
                <label :for="capturedValueId" class="sig-test-dialog__pane-label">
                  <span class="sig-test-dialog__pane-label-text">
                    {{ t('page.signatures.test.fieldEditor.currentValue') }}
                  </span>
                  <Badge variant="secondary" class="sig-test-dialog__pane-chip">
                    {{ t('page.signatures.test.fieldEditor.asUsed') }}
                  </Badge>
                </label>
                <Textarea
                  :id="capturedValueId"
                  :model-value="capturedValue"
                  readonly
                  rows="12"
                  class="sig-test-dialog__textarea sig-test-dialog__textarea--readonly"
                />
              </div>
              <div class="sig-test-dialog__value-col">
                <label :for="testValueId" class="sig-test-dialog__pane-label">
                  <span class="sig-test-dialog__pane-label-text">
                    {{ t('page.signatures.test.fieldEditor.testValue') }}
                  </span>
                  <Badge
                    v-if="hasDiff"
                    variant="default"
                    class="sig-test-dialog__pane-chip sig-test-dialog__pane-chip--diff"
                  >
                    {{ t('page.signatures.test.fieldEditor.edited') }}
                  </Badge>
                </label>
                <Textarea
                  :id="testValueId"
                  v-model="testValue"
                  :placeholder="t('page.signatures.test.fieldEditor.testValuePlaceholder')"
                  rows="12"
                  class="sig-test-dialog__textarea"
                />
              </div>
            </div>
          </div>

          <!-- Outputs: original vs test -->
          <div class="sig-test-dialog__sub">
            <h4 class="sig-test-dialog__sub-title">
              {{ t('page.signatures.test.section.compareOutput') }}
            </h4>
            <div class="sig-test-dialog__outputs">
              <div class="sig-test-dialog__output">
                <div class="sig-test-dialog__output-head">
                  <span>{{ t('page.signatures.test.output.original') }}</span>
                </div>
                <pre
                  v-if="matchedBlock.rawResponse"
                  class="sig-test-dialog__output-body"
                >{{ originalOutputText }}</pre>
                <div v-else class="sig-test-dialog__output-empty">
                  {{ t('page.signatures.test.output.noOriginal') }}
                </div>
              </div>
              <div class="sig-test-dialog__output sig-test-dialog__output--test">
                <div class="sig-test-dialog__output-head">
                  <span>{{ t('page.signatures.test.output.test') }}</span>
                </div>
                <div
                  v-if="testMutation.isPending.value"
                  class="sig-test-dialog__output-loading"
                >
                  <IniaLoader />
                </div>
                <pre
                  v-else-if="testResult"
                  class="sig-test-dialog__output-body"
                >{{ testOutputText }}</pre>
                <div v-else class="sig-test-dialog__output-empty">
                  {{ t('page.signatures.test.output.noTestYet') }}
                </div>
              </div>
            </div>
          </div>
        </section>
      </div>

      <!-- Sticky footer — always visible regardless of body scroll position.
           Keeps Run / Propose next to the main close action, which is the
           standard dialog convention and avoids the "where did my buttons
           go" scroll-chasing problem. -->
      <div
        v-if="matchedBlock && canReplay"
        class="sig-test-dialog__footer"
      >
        <div class="sig-test-dialog__footer-messages">
          <span v-if="isSharedField" class="sig-test-dialog__note">
            <IniaIcon name="circle-info" class="w-3.5 h-3.5" />
            {{
              t('page.signatures.test.fieldEditor.sharedFieldNote', {
                count: props.sharedBlockCount,
              })
            }}
          </span>
          <span
            v-if="testResult && testResult.occurrencesReplaced > 1"
            class="sig-test-dialog__note"
          >
            <IniaIcon name="circle-info" class="w-3.5 h-3.5" />
            {{
              t('page.signatures.test.fieldEditor.multipleOccurrencesReplaced', {
                count: testResult.occurrencesReplaced,
              })
            }}
          </span>
          <span v-if="runError" class="sig-test-dialog__error">
            <IniaIcon name="exclamation-triangle" class="w-3.5 h-3.5" />
            {{ runError }}
          </span>
        </div>
        <div class="sig-test-dialog__footer-actions">
          <Button
            variant="outline"
            :disabled="submitDisabled"
            @click="onSubmitProposal"
          >
            <IniaIcon name="upload" class="w-4 h-4" />
            {{ t('page.signatures.test.fieldEditor.submitAsProposal') }}
          </Button>
          <Button :disabled="runDisabled" @click="onRun">
            <IniaIcon name="play" class="w-4 h-4" />
            <template v-if="testMutation.isPending.value">
              {{ t('page.signatures.test.fieldEditor.running') }}
            </template>
            <template v-else>
              {{ t('page.signatures.test.fieldEditor.run') }}
            </template>
          </Button>
        </div>
      </div>
    </DialogContent>
  </Dialog>
</template>

<style scoped>
.sig-test-dialog__header {
  padding-bottom: var(--spacing-sm, 0.5rem);
}

.sig-test-dialog__heading-row {
  display: flex;
  align-items: center;
  gap: 0.75rem;
}

.sig-test-dialog__heading-icon {
  width: 2.25rem;
  height: 2.25rem;
  display: flex;
  align-items: center;
  justify-content: center;
  background: var(--bg-2, rgba(26, 56, 80, 0.12));
  color: var(--accent-indigo-to, #8aa3ff);
  border-radius: 10px;
  flex-shrink: 0;
}

.sig-test-dialog__heading-text {
  display: flex;
  flex-direction: column;
  gap: 0.125rem;
}

.sig-test-dialog__title {
  font-size: 1.15rem;
  font-weight: 600;
  margin: 0;
}

.sig-test-dialog__subtitle {
  font-size: 13px;
  color: var(--text-secondary);
  margin: 0;
}

.sig-test-dialog__body {
  display: flex;
  flex-direction: column;
  gap: 1.25rem;
  padding-top: 0.5rem;
}

.sig-test-dialog__pickers,
.sig-test-dialog__filters {
  display: flex;
  gap: 1rem;
  flex-wrap: wrap;
}

.sig-test-dialog__picker {
  display: flex;
  flex-direction: column;
  gap: 0.375rem;
  min-width: 260px;
  flex: 1 1 260px;
}

.sig-test-dialog__picker--wide {
  flex: 2 1 320px;
}

.sig-test-dialog__picker-label {
  font-size: 11px;
  color: var(--text-secondary);
  text-transform: uppercase;
  letter-spacing: 0.08em;
  font-weight: 600;
}

.sig-test-dialog__state {
  padding: 2rem 1rem;
  color: var(--text-secondary);
  font-size: 14px;
  text-align: center;
  display: flex;
  justify-content: center;
  align-items: center;
  gap: 0.5rem;
}

.sig-test-dialog__state--warn {
  padding: 0.9rem 1rem;
  background: rgba(250, 204, 21, 0.08);
  border: 1px solid rgba(250, 204, 21, 0.25);
  border-radius: 8px;
  color: var(--text-primary);
  text-align: left;
  justify-content: flex-start;
}

.sig-test-dialog__step-info {
  display: flex;
  align-items: center;
  gap: 0.375rem;
  padding: 0.5rem 0.75rem;
  background: var(--bg-0);
  border: 1px solid var(--stroke);
  border-radius: 8px;
  font-size: 13px;
  flex-wrap: wrap;
}

.sig-test-dialog__crumb {
  color: var(--text-primary);
  font-weight: 500;
}

.sig-test-dialog__crumb--section {
  color: var(--text-secondary);
  font-weight: 500;
}

.sig-test-dialog__crumb-sep {
  color: var(--text-secondary);
  flex-shrink: 0;
}

/* Numbered phases — make the boundary between "pick a run" and "test the
   override" visually unmistakable. The dialog hosts two distinct mental
   modes (search/select vs edit/compare); without this scaffold the form
   reads as one undifferentiated wall of inputs. */
.sig-test-dialog__phase {
  display: flex;
  flex-direction: column;
  gap: 0.875rem;
  padding: 1rem 1.125rem 1.125rem;
  border: 1px solid var(--stroke);
  border-radius: 12px;
  background: var(--bg-0);
}

.sig-test-dialog__phase-head {
  display: flex;
  align-items: center;
  gap: 0.625rem;
  margin-bottom: 0.125rem;
}

.sig-test-dialog__phase-num {
  width: 1.625rem;
  height: 1.625rem;
  display: flex;
  align-items: center;
  justify-content: center;
  background: var(--primary);
  color: var(--primary-foreground);
  border-radius: 50%;
  font-size: 12px;
  font-weight: 700;
  flex-shrink: 0;
}

.sig-test-dialog__phase-title {
  font-size: 15px;
  font-weight: 600;
  margin: 0;
  color: var(--text-primary);
}

/* Sub-blocks inside a phase — labelled but unnumbered, since the phase
   number is the load-bearing waypoint and nesting numbers would muddle
   the hierarchy. */
.sig-test-dialog__sub {
  display: flex;
  flex-direction: column;
  gap: 0.625rem;
}

.sig-test-dialog__sub-title {
  font-size: 12px;
  font-weight: 600;
  margin: 0;
  color: var(--text-secondary);
  text-transform: uppercase;
  letter-spacing: 0.08em;
}

.sig-test-dialog__values,
.sig-test-dialog__outputs {
  display: grid;
  grid-template-columns: 1fr 1fr;
  gap: 1rem;
}

.sig-test-dialog__value-col {
  display: flex;
  flex-direction: column;
  gap: 0.375rem;
  min-width: 0;
}

.sig-test-dialog__pane-label {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 0.5rem;
  font-size: 11px;
  color: var(--text-secondary);
  text-transform: uppercase;
  letter-spacing: 0.08em;
  font-weight: 600;
}

.sig-test-dialog__pane-label-text {
  flex: 1;
}

.sig-test-dialog__pane-chip {
  font-size: 10px;
  text-transform: none;
  letter-spacing: 0;
  font-weight: 500;
}

.sig-test-dialog__pane-chip--diff {
  background: var(--primary);
  color: var(--primary-foreground);
}

.sig-test-dialog__textarea {
  font-size: 14px;
  line-height: 1.5;
  resize: vertical;
  font-family: inherit;
}

.sig-test-dialog__textarea--readonly {
  background: var(--bg-0);
  cursor: default;
}

.sig-test-dialog__footer {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 1rem;
  flex-wrap: wrap;
  padding: 0.875rem 0 0;
  margin-top: 0.5rem;
  border-top: 1px solid var(--stroke);
  position: sticky;
  bottom: 0;
  background: var(--bg-1);
  z-index: 1;
}

.sig-test-dialog__footer-messages {
  display: flex;
  align-items: center;
  gap: 0.75rem;
  flex-wrap: wrap;
  flex: 1 1 auto;
  min-width: 0;
}

.sig-test-dialog__footer-actions {
  display: flex;
  gap: 0.5rem;
  flex-shrink: 0;
}

.sig-test-dialog__note {
  display: inline-flex;
  align-items: center;
  gap: 0.375rem;
  font-size: 13px;
  color: var(--text-secondary);
}

.sig-test-dialog__error {
  display: inline-flex;
  align-items: center;
  gap: 0.375rem;
  font-size: 13px;
  color: var(--accent-error);
}

/* Output panels — matched height so the eye can compare. */
.sig-test-dialog__output {
  display: flex;
  flex-direction: column;
  min-height: 0;
  border: 1px solid var(--stroke);
  border-radius: 8px;
  overflow: hidden;
  background: var(--bg-0);
}

.sig-test-dialog__output--test {
  border-color: var(--accent-indigo-to, #8aa3ff);
  box-shadow: 0 0 0 1px rgba(138, 163, 255, 0.2);
}

.sig-test-dialog__output-head {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 0.5rem;
  padding: 0.625rem 0.875rem;
  background: var(--bg-2, rgba(255, 255, 255, 0.03));
  border-bottom: 1px solid var(--stroke);
  font-size: 11px;
  color: var(--text-secondary);
  text-transform: uppercase;
  letter-spacing: 0.08em;
  font-weight: 700;
  flex-wrap: wrap;
}

.sig-test-dialog__output-meta-chips {
  display: flex;
  gap: 0.375rem;
  flex-wrap: wrap;
  text-transform: none;
  letter-spacing: 0;
  font-weight: 500;
}

.sig-test-dialog__output-body {
  padding: 1rem 1.125rem;
  overflow: auto;
  min-height: 18rem;
  max-height: 32rem;
  font-size: 14px;
  line-height: 1.55;
  color: var(--text-primary);
  /* <pre> defaults to the UA monospace stack; keep the dialog's body font
     so the output reads like normal prose, and preserve every \n + run of
     whitespace verbatim via pre-wrap. */
  font-family: inherit;
  white-space: pre-wrap;
  word-wrap: break-word;
  overflow-wrap: anywhere;
  margin: 0;
}

.sig-test-dialog__output-empty,
.sig-test-dialog__output-loading {
  padding: 2.5rem 1rem;
  text-align: center;
  color: var(--text-secondary);
  font-size: 13px;
  display: flex;
  align-items: center;
  justify-content: center;
  min-height: 18rem;
}

@media (max-width: 900px) {
  .sig-test-dialog__values,
  .sig-test-dialog__outputs {
    grid-template-columns: 1fr;
  }
}
</style>

<!-- Unscoped block — targets the portaled DialogContent element which lives
     outside this component's scoped-style tree. !important wins over the
     Tailwind utility classes baked into DialogContent.vue. -->
<style>
.sig-test-dialog__content {
  max-width: min(1600px, 96vw) !important;
  width: min(1600px, 96vw) !important;
  max-height: 92vh !important;
  overflow: auto;
}
</style>
