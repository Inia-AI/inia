<script setup lang="ts">
/**
 * Signatures page — four-column editor:
 *   [ sections ] → [ blocks in section ] → [ fields for selected block ] → [ dependencies ]
 *
 * Dependencies between blocks are surfaced in the right-hand Dependencies
 * column: upstream (what this block reads from) and downstream (what
 * reads from this block) with click-to-jump.
 *
 * Routes handled:
 *   /signatures                  → workflow picker (top of file)
 *   /signatures/:workflowTypeId  → four-column editor (bottom of file)
 *
 * URL keys on workflowTypeId (stable across Save-As-Core). Signature fields
 * are defined once at the workflow level and keyed by name; blocks reference
 * them via `fieldNames`, so a field shared by several blocks shows one value
 * (and one draft / pending state) everywhere. A 409 on submit means the field
 * was renamed or deleted by a concurrent staff edit; the client flips into an
 * "outdated" state and invites a refresh.
 */
import { useItemTitle } from '@/composables/useDocumentTitle'
import axios from 'axios'
import { computed, nextTick, ref, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import API from '@/api'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from '@/components/ui/dialog'
import { Progress } from '@/components/ui/progress'
import { Textarea } from '@/components/ui/textarea'
import { useToast } from '@/components/ui/toast/use-toast'
import SignatureTestDialog from '@/components/signatures/SignatureTestDialog.vue'
import SignatureHistoryTab from '@/views/signatures/SignatureHistoryTab.vue'
import {
  Tabs,
  TabsContent,
  TabsList,
  TabsTrigger,
} from '@/components/ui/tabs'
import {
  Tooltip,
  TooltipContent,
  TooltipProvider,
  TooltipTrigger,
} from '@/components/ui/tooltip'
import { useI18n } from 'vue-i18n'
import type {
  SignatureBlockDto,
  SignatureFieldDto,
  SignatureSectionDto,
} from '@/api/signatures/schema'
import {
  buildBlocksById,
  buildFieldsByName,
  canUserVote,
  computeVoteProgress,
  countAwaitingMyVote,
  countAwaitingProposerAction,
  normaliseDepTag,
  resolveBlockFields,
  resolveConnections as resolveConnectionsPure,
  resolvePendingState,
  summariseSuggestions,
  type Connection,
  type PendingUiState,
} from './signatures/helpers'
import { useCurrentUserId } from '@/composables/useCurrentUserId'
import { useMyReviewCounts } from '@/composables/useMyReviewCounts'
import { hasRole } from '@/lib/utils'
import { ROLES } from '@/constants/roles'
import { useCanSeeTeam } from '@/composables/useCapabilities'

// Floor for the per-tenant review threshold. The server-side minimum is 2; this
// fallback only kicks in if the payload somehow omits the value (shouldn't
// happen with current BE contract).
const FALLBACK_VOTE_THRESHOLD = 2

const route = useRoute()
const router = useRouter()
const { toast } = useToast()
const { t } = useI18n()

// Decoded JWT subject — needed by the review-queue UI to distinguish "my pending"
// from "someone else's pending" and to gate the Approve/Reject buttons. Reactive
// (re-decodes on storage events) so cross-tab logout invalidates immediately.
const { userId: myUserId } = useCurrentUserId()

// Aggregate "fields awaiting my vote" (reviewer-side, red pill) and
// "counter-suggestions awaiting my decision" (proposer-side, amber pill) across
// every workflow type. Nav bar consumes the same composable separately.
const {
  countsByWorkflowType: reviewCountsByWorkflowType,
  proposerCountsByWorkflowType: proposerActionCountsByWorkflowType,
} = useMyReviewCounts()

// Reviewer eligibility for the vote buttons. The router already guards this
// page behind the same role set; this duplicate is the fine-grained check
// `canUserVote()` consults on a per-field basis. Wrapped in `computed` so the
// reactive `tick` inside `useCurrentUserId` flows through.
const hasReviewerRole = computed(() => {
  void myUserId.value
  return (
    hasRole(ROLES.SUPER_ADMIN)
    || hasRole(ROLES.SIGNATURE_CONTRIBUTOR)
  )
})

// ─── Review-queue UI helpers ────────────────────────────────────────────
// Map a `PendingUiState` to a Badge variant. `outline` = default/unchanged,
// `secondary` = override (matches the existing purple-dot indicator's neutral
// "this is non-default" tone), `default` = pending (most prominent — drives
// review attention).
function badgeVariantFor(
  state: PendingUiState,
): 'default' | 'outline' | 'secondary' {
  switch (state) {
    case 'default': {
      return 'outline'
    }
    case 'override': {
      return 'secondary'
    }
    case 'pending-mine':
    case 'pending-other': {
      return 'default'
    }
  }
}

// User-facing badge label. "Pending (yours)" suffix for own pendings is the
// only differentiator beyond the variant — same colour because both states
// carry the same review-queue weight.
function badgeLabelFor(state: PendingUiState): string {
  switch (state) {
    case 'default': {
      return t('page.signatures.editor.badges.default')
    }
    case 'override': {
      return t('page.signatures.editor.badges.override')
    }
    case 'pending-mine': {
      return t('page.signatures.editor.badges.pendingMine')
    }
    case 'pending-other': {
      return t('page.signatures.editor.badges.pendingOther')
    }
  }
}

function fieldUiState(field: SignatureFieldDto): PendingUiState {
  return resolvePendingState(field, myUserId.value)
}

function fieldCanVote(field: SignatureFieldDto): boolean {
  return canUserVote(field, myUserId.value, hasReviewerRole.value)
}

// Counts fields within a block that are waiting for *this* user's vote —
// pending exists, caller isn't the proposer, and caller hasn't voted. Used to
// surface a "needs review" badge on the block + section selectors so a user
// can see outstanding work without drilling into every block. These pills are
// navigation affordances: a shared field deliberately counts once per
// referencing block here, while workflow-level totals (useMyReviewCounts)
// count each field once from the flat `fields` list.
function blockNeedsReviewCount(block: SignatureBlockDto): number {
  return countAwaitingMyVote(
    resolveBlockFields(block, fieldsByName.value),
    myUserId.value,
    hasReviewerRole.value,
  )
}

function sectionNeedsReviewCount(section: SignatureSectionDto): number {
  let total = 0
  for (const block of section.blocks) {
    total += blockNeedsReviewCount(block)
  }
  return total
}

// Parallel counts for the proposer side: a field has an action waiting when the
// caller is the proposer of the pending AND a reviewer has left at least one
// counter-suggestion. Drives the amber pill on section + block selectors,
// alongside the existing red reviewer-side pill.
function blockNeedsProposerActionCount(block: SignatureBlockDto): number {
  return countAwaitingProposerAction(
    resolveBlockFields(block, fieldsByName.value),
    myUserId.value,
  )
}

function sectionNeedsProposerActionCount(section: SignatureSectionDto): number {
  let total = 0
  for (const block of section.blocks) {
    total += blockNeedsProposerActionCount(block)
  }
  return total
}

function voteProgressFor(field: SignatureFieldDto) {
  const pending = field.pending ?? null
  return computeVoteProgress({
    approveCount: pending?.approveCount ?? 0,
    rejectCount: pending?.rejectCount ?? 0,
    threshold: reviewThreshold.value,
  })
}

const reviewThreshold = computed(
  () => data.value?.reviewThreshold ?? FALLBACK_VOTE_THRESHOLD,
)

// The URL keys on WorkflowTypeId (stable across Save-As-Core), not the Core
// workflow GUID. Core ids regenerate on every Save-As-Core; type id is
// staff-authored and preserved.
const workflowTypeId = computed(() => (route.params.workflowTypeId as string) ?? '')
const hasWorkflowTypeId = computed(() => workflowTypeId.value.length > 0)

// Editor / History tab toggle, kept in sync with the `?tab=` URL query so
// reloads and shared links preserve the selection. `router.replace` to avoid
// stacking history entries on every flip.
type EditorTab = 'settings' | 'history'

const activeTab = computed<EditorTab>(() =>
  route.query.tab === 'history' ? 'history' : 'settings',
)

function setActiveTab(tab: EditorTab) {
  const nextQuery = { ...route.query }
  if (tab === 'history') {
    nextQuery.tab = 'history'
  } else {
    delete nextQuery.tab
    // Clearing the tab also clears history-tab-only query params so the URL
    // doesn't carry stale filter state into the Settings view.
    delete nextQuery.placeholder
    delete nextQuery.actor
    delete nextQuery.fromUtc
    delete nextQuery.toUtc
    delete nextQuery.skip
  }
  router.replace({ query: nextQuery })
}

// Tabs emits `AcceptableValue` which includes `null`. Narrow to `EditorTab`
// and ignore anything unrecognised so a future Tabs payload change can't
// silently flip us to an undefined tab.
function onTabUpdate(value: string | number | boolean | null | undefined) {
  if (value !== 'history' && value !== 'settings') {
    return
  }
  setActiveTab(value)
}

// ─── Picker state (shown when no workflowTypeId in URL) ──────────────
const {
  data: categories,
  isLoading: isLoadingCategories,
  isError: isCategoriesError,
} = API.WorkflowCategories.useGetWorkflowCategorySummaries()

function openWorkflow(id: string) {
  router.push({ name: 'SignatureSettings', params: { workflowTypeId: id } })
}

function goBackToPicker() {
  router.push({ name: 'SignaturesPicker' })
}

// Field-editor "Try on a past execution" button. Routes the user to the
// Inline test-dialog state. The field-editor "Try on a past execution"
// button pops a modal scoped to the current block + field; the user picks
// an execution inside the dialog and runs the replay without leaving this
// page. Keeping everything in one route means the user's editor context
// (section/block selection, in-flight draft) survives.
const testDialogOpen = ref(false)
const testDialogReplacementTag = ref<string | null>(null)
const testDialogPlaceholder = ref<string | null>(null)
const testDialogDisplayName = ref<string | null>(null)
// How many blocks reference the field under test. Drives the dialog's
// "applies to all N blocks" note for shared fields.
const testDialogSharedBlockCount = ref(1)

const pendingTestExecutionId = ref<string | null>(
  typeof route.query.testExecutionId === 'string' && route.query.testExecutionId.length > 0
    ? route.query.testExecutionId
    : null,
)

if (pendingTestExecutionId.value) {
  const nextQuery = { ...route.query }
  delete nextQuery.testExecutionId
  void router.replace({ query: nextQuery })
}

function onTryOnPastExecution(field: SignatureFieldDto) {
  if (!selectedBlock.value) {
    return
  }
  testDialogReplacementTag.value = selectedBlock.value.replacementTag
  testDialogPlaceholder.value = field.name
  testDialogDisplayName.value = field.displayName
  testDialogSharedBlockCount.value = field.usedByBlockIds.length
  testDialogOpen.value = true
}

watch(testDialogOpen, (isOpen, wasOpen) => {
  if (wasOpen && !isOpen && pendingTestExecutionId.value) {
    pendingTestExecutionId.value = null
  }
})

// Fired by the test-dialog's "Propose as override" button. Routes through
// the same submit-pending-override mutation the field editor uses directly —
// same validation, same cache invalidation, same outdated-dialog fallback.
async function onTestDialogSubmitProposal(payload: {
  placeholderName: string
  proposedValue: string
}) {
  try {
    await submitPendingMutation.mutateAsync(payload)
    testDialogOpen.value = false
    toast({
      class: 'text-sm',
      description: t('page.signatures.editor.submit.submitSuccess'),
    })
  }
  catch (err) {
    if (axios.isAxiosError(err) && err.response?.status === 409) {
      testDialogOpen.value = false
      isOutdated.value = true
      return
    }
    toast({
      class: 'text-sm',
      variant: 'destructive',
      description: t('page.signatures.editor.submit.submitFailed'),
    })
  }
}

interface FlatWorkflow {
  workflowTypeId: string
  typeName: string
  categoryName: string
}

const flatWorkflows = computed<FlatWorkflow[]>(() => {
  const out: FlatWorkflow[] = []
  for (const category of categories.value ?? []) {
    for (const workflow of category.workflows ?? []) {
      // Navigate by workflowTypeId — survives Save-As-Core. The category summary's
      // `workflow.id` is the Core GUID and would 404 on reload after a save.
      if (!workflow.workflowTypeId) {
        continue
      }
      out.push({
        workflowTypeId: workflow.workflowTypeId,
        typeName: workflow.workflowTypeName ?? workflow.workflowTypeId,
        categoryName: category.name,
      })
    }
  }
  // Sort alphabetically by the display name, case-insensitively. The server
  // returns workflows grouped by category in whatever order the DB hands
  // them back (often insertion / id order) — for a short client-facing
  // picker, alphabetical is more findable.
  out.sort((a, b) => a.typeName.localeCompare(b.typeName, undefined, { sensitivity: 'base' }))
  return out
})

// ─── Editor state (shown when /signatures/:workflowTypeId) ──────────
const { data, isLoading, isError, error, refetch } =
  API.Signatures.useGetWorkflowSignatures(workflowTypeId)

useItemTitle(() => data.value?.workflowName)

const selectedSectionId = ref<string | null>(null)
const selectedBlockId = ref<string | null>(null)

// Preserve selection across refetches (including post-save invalidation). Only
// reset when the current selection can no longer be resolved — either no data,
// no sections, or the previously selected section/block is gone from the new
// payload.
watch(
  data,
  (d) => {
    if (!d) {
      return
    }
    const ordered = [...d.sections].sort((a, b) => a.sectionOrder - b.sectionOrder)
    const currentSection = selectedSectionId.value
      ? ordered.find((s) => s.sectionId === selectedSectionId.value) ?? null
      : null
    if (!currentSection) {
      const firstSection = ordered[0] ?? null
      selectedSectionId.value = firstSection?.sectionId ?? null
      selectedBlockId.value = firstSection?.blocks[0]?.blockId ?? null
      return
    }

    const currentBlockStillThere = selectedBlockId.value
      ? currentSection.blocks.some((b) => b.blockId === selectedBlockId.value)
      : false
    if (!currentBlockStillThere) {
      selectedBlockId.value = currentSection.blocks[0]?.blockId ?? null
    }
  },
  { immediate: true },
)

const sortedSections = computed<SignatureSectionDto[]>(() => {
  return [...(data.value?.sections ?? [])].sort(
    (a, b) => a.sectionOrder - b.sectionOrder,
  )
})

const selectedSection = computed<SignatureSectionDto | null>(() => {
  if (!data.value || !selectedSectionId.value) {
    return null
  }
  return (
    sortedSections.value.find((s) => s.sectionId === selectedSectionId.value) ?? null
  )
})

const selectedBlock = computed<SignatureBlockDto | null>(() => {
  if (!selectedSection.value || !selectedBlockId.value) {
    return null
  }
  return (
    selectedSection.value.blocks.find((b) => b.blockId === selectedBlockId.value) ?? null
  )
})

const fieldsByName = computed(() => buildFieldsByName(data.value?.fields ?? []))

/** Fields for the selected block (column 3), in the block's template order. */
const selectedBlockFields = computed<SignatureFieldDto[]>(() => {
  if (!selectedBlock.value) {
    return []
  }
  return resolveBlockFields(selectedBlock.value, fieldsByName.value)
})

// Block-id lookup for the "Used by N blocks" chips on shared fields — each
// chip navigates to the referencing block, mirroring the dependency-graph
// node navigation.
const blocksById = computed(() => buildBlocksById(data.value?.sections ?? []))

interface UsedByChip {
  blockId: string
  blockName: string
  sectionId: string
  sectionTitle: string
}

function usedByChipsFor(field: SignatureFieldDto): UsedByChip[] {
  const out: UsedByChip[] = []
  for (const blockId of field.usedByBlockIds) {
    const entry = blocksById.value.get(blockId)
    if (!entry) {
      continue
    }
    out.push({
      blockId,
      blockName: entry.block.blockName,
      sectionId: entry.sectionId,
      sectionTitle: entry.sectionTitle,
    })
  }
  return out
}

/** Flat map of every block in the workflow, indexed by ReplacementTag.
 *  Needed for dependency lookups — `dependsOn` holds upstream replacement
 *  tags, not block ids. */
const blocksByTag = computed(() => {
  const map = new Map<string, { block: SignatureBlockDto; sectionId: string }>()
  for (const section of data.value?.sections ?? []) {
    for (const block of section.blocks) {
      map.set(block.replacementTag, { block, sectionId: section.sectionId })
    }
  }
  return map
})

function lookupBlockByDepTag(tag: string) {
  const direct = blocksByTag.value.get(tag)
  if (direct) {
    return direct
  }
  const normalised = normaliseDepTag(tag)
  if (normalised === tag || !normalised) {
    return null
  }
  return blocksByTag.value.get(normalised) ?? null
}

function resolveConnections(tags: string[]): Connection[] {
  return resolveConnectionsPure(tags, blocksByTag.value, data.value?.sections ?? [])
}

/** Blocks the currently selected block reads from. */
const upstreamConnections = computed<Connection[]>(() => {
  if (!selectedBlock.value) {
    return []
  }
  return resolveConnections(selectedBlock.value.dependsOn)
})

function blockDependsOn(block: SignatureBlockDto, targetTag: string): boolean {
  const normalisedTarget = normaliseDepTag(targetTag)
  for (const dep of block.dependsOn) {
    if (dep === targetTag) {
      return true
    }
    if (normaliseDepTag(dep) === normalisedTarget) {
      return true
    }
  }
  return false
}

/** Blocks that read from the currently selected block. */
const downstreamConnections = computed<Connection[]>(() => {
  if (!selectedBlock.value || !data.value) {
    return []
  }
  const selfTag = selectedBlock.value.replacementTag
  const out: Connection[] = []
  for (const section of data.value.sections) {
    for (const block of section.blocks) {
      if (blockDependsOn(block, selfTag)) {
        out.push({
          blockId: block.blockId,
          blockName: block.blockName,
          sectionId: section.sectionId,
          sectionTitle: section.sectionTitle,
          replacementTag: block.replacementTag,
        })
      }
    }
  }
  return out
})

function selectSection(sectionId: string) {
  selectedSectionId.value = sectionId
  const section = data.value?.sections.find((s) => s.sectionId === sectionId)
  selectedBlockId.value = section?.blocks[0]?.blockId ?? null
}

function selectBlock(blockId: string, sectionId?: string) {
  const resolvedSectionId = sectionId ?? selectedSectionId.value
  const section = resolvedSectionId
    ? data.value?.sections.find((s) => s.sectionId === resolvedSectionId)
    : undefined
  const blockExists = section?.blocks.some((b) => b.blockId === blockId) ?? false
  if (!blockExists) {
    return
  }
  if (sectionId && sectionId !== selectedSectionId.value) {
    selectedSectionId.value = sectionId
  }
  selectedBlockId.value = blockId
}

// ─── Per-field draft buffer ─────────────────────────────────────────
// Submission migrated from "stage many edits, click Save once" to
// "edit one field, submit one pending review." That collapses the previous
// three buffers (pendingEdits / pendingRemovals / removedSnapshots) into:
//
//   draftValues  — text the user is typing into a per-field textarea before
//                  they click "Submit for review" on that field. Cleared
//                  after a successful submit (the inline pending row takes
//                  over the visual). Indexed by the field's workflow-level
//                  `name`, so a shared field shows the same draft from every
//                  block that references it — consistent by construction.
//   editingKeys  — fields where the textarea is currently open. Distinguishes
//                  "I'm editing this" from "the field shows the saved value
//                  (default or active override)." Toggled by Propose / Edit /
//                  Cancel; cleared on submit.
//
// `pendingRemovals` and `removedSnapshots` are gone. Removal is now a single
// gesture ("Propose remove override") that fires `submitPendingOverride` with
// `proposedValue: null` immediately — no batched-undo to track because the
// proposal lives server-side as a pending row the proposer can withdraw via
// the normal review flow.
const draftValues = ref<Map<string, string>>(new Map())
const editingKeys = ref<Set<string>>(new Set())

// Textareas can render well below the pending panel (after progress bars and
// counter-suggestions), so clicking "Edit proposal" looks like nothing
// happened. Register each editor's DOM node as it mounts, then scroll the
// requested one into view + focus it.
const draftTextareaRefs = new Map<string, HTMLTextAreaElement>()

function registerDraftTextareaRef(fieldName: string, el: unknown) {
  if (el instanceof HTMLTextAreaElement) {
    draftTextareaRefs.set(fieldName, el)
  } else {
    draftTextareaRefs.delete(fieldName)
  }
}

async function scrollDraftIntoView(fieldName: string) {
  // Two nextTicks: the first lets the v-if textarea render, the second lets
  // the function-ref fire and Vue flush layout so the scroll computes from
  // the final position. One tick is occasionally too early, causing a no-op.
  await nextTick()
  await nextTick()
  const el = draftTextareaRefs.get(fieldName)
  if (!el) {
    return
  }
  el.scrollIntoView({ behavior: 'smooth', block: 'center' })
  el.focus({ preventScroll: true })
}

function getDraftValue(fieldName: string, fallback: string): string {
  return draftValues.value.get(fieldName) ?? fallback
}

function isEditingField(fieldName: string): boolean {
  return editingKeys.value.has(fieldName)
}

function onFieldInput(fieldName: string, event: Event) {
  const target = event.target as HTMLTextAreaElement
  draftValues.value.set(fieldName, target.value)
}

// Seed the textarea with the current value (override if present, else
// default) and reveal the editor. The user types, then either Submit or
// Cancel.
function startOverride(fieldName: string, seedValue: string) {
  if (!draftValues.value.has(fieldName)) {
    draftValues.value.set(fieldName, seedValue)
  }
  editingKeys.value.add(fieldName)
  void scrollDraftIntoView(fieldName)
}

function cancelEdit(fieldName: string) {
  draftValues.value.delete(fieldName)
  editingKeys.value.delete(fieldName)
}

// Per-field review-queue mutations. The composables read `workflowTypeId` at
// call time (not capture time) — the same component instance handles both
// /signatures and /signatures/:workflowTypeId, so a captured snapshot would
// hit the wrong workflow on intra-editor navigation. Cache invalidation is
// handled inside each composable's `onSuccess`.
const submitPendingMutation = API.Signatures.useSubmitPendingMutation(workflowTypeId)
const castVoteMutation = API.Signatures.useCastVoteMutation(workflowTypeId)
const acceptSuggestionMutation = API.Signatures.useAcceptSuggestionMutation(workflowTypeId)
const cancelPendingMutation = API.Signatures.useCancelPendingMutation(workflowTypeId)
const withdrawVoteMutation = API.Signatures.useWithdrawVoteMutation(workflowTypeId)

// Tenant user resolver — used to turn voter/proposer `userId` → name on the
// pending panel + counter-suggestion cards. We fetch only the users actually
// referenced in this workflow's pending reviews (proposers + voters +
// commenters) instead of pulling every tenant user. Bounds the query size
// against arbitrarily-large tenants and avoids the `/users/resolve`
// endpoint, which filters by AnalystName and misses seeded admins.
const referencedUserIds = computed<string[]>(() => {
  const ids = new Set<string>()
  for (const field of data.value?.fields ?? []) {
    const pending = field.pending
    if (!pending) {
      continue
    }
    if (pending.proposerUserId) {
      ids.add(pending.proposerUserId)
    }
    for (const comment of pending.comments) {
      if (comment.voterUserId) {
        ids.add(comment.voterUserId)
      }
    }
    for (const suggestion of pending.suggestions) {
      if (suggestion.voterUserId) {
        ids.add(suggestion.voterUserId)
      }
    }
  }
  return [...ids]
})

// OData `$filter=id eq 'a' or id eq 'b' or ...`. When nothing is
// referenced yet we fall back to a contradiction-filter (`id eq ''`) so
// the request returns an empty list instead of the unfiltered top-N of
// tenant users — useGetUsers doesn't accept an `enabled` option so
// gating via the filter itself is the cleanest path.
const usersParams = computed(() => {
  const ids = referencedUserIds.value
  if (ids.length === 0) {
    return { $filter: "id eq ''", $top: 1 }
  }
  return {
    $filter: ids
      .map(id => `id eq '${id.replace(/'/g, "''")}'`)
      .join(' or '),
    $top: ids.length,
  }
})

const { data: tenantUsersData } = API.Users.useGetUsers(usersParams, useCanSeeTeam())

function voterLabelFor(userId: string): string {
  const user = tenantUsersData.value?.value?.find(u => u.id === userId)
  const fullName = user?.fullName?.trim()
  if (fullName) {
    return fullName
  }
  return user?.email ?? userId
}

// Set when a mutation returns a 409 Conflict — the payload referenced a field
// that has been renamed/deleted (or is no longer referenced by any block)
// since our GET. Surfaces the outdated banner; refresh is the recovery path.
const isOutdated = ref(false)

// Reset draft state and selection when the user navigates to a different
// workflow. Without this, in-progress drafts would persist across route
// changes (same component instance) and stale submits could land on a
// workflow that doesn't own them.
watch(workflowTypeId, () => {
  draftValues.value = new Map()
  editingKeys.value = new Set()
  selectedSectionId.value = null
  selectedBlockId.value = null
  isOutdated.value = false
  submitPendingMutation.reset()
  castVoteMutation.reset()
  acceptSuggestionMutation.reset()
  cancelPendingMutation.reset()
  withdrawVoteMutation.reset()
  closeRejectDialog()
})

// ─── Vote actions ──────────────────────────────────────────────────────
// Approve is a single-click confirmation — there's no comment / suggestion
// payload, so no dialog. The UI surfaces the outcome via toast and the
// pending panel re-renders from the cache invalidation.
async function onApprove(field: SignatureFieldDto) {
  const pending = field.pending
  if (!pending) {
    return
  }
  try {
    await castVoteMutation.mutateAsync({
      pendingOverrideId: pending.id,
      body: {
        voteKind: 'Approve',
        comment: null,
        suggestedValue: null,
      },
    })
    toast({
      class: 'text-sm',
      description: t('page.signatures.editor.pending.approveSuccess'),
    })
  } catch (err) {
    if (axios.isAxiosError(err) && err.response?.status === 409) {
      isOutdated.value = true
      return
    }
    toast({
      class: 'text-sm',
      variant: 'destructive',
      description: t('page.signatures.editor.pending.voteFailed'),
    })
  }
}

// Reject dialog — collects a required comment (max 2000) and an optional
// counter-suggestion (max 4000). State lives at module scope (not per-field)
// so only one dialog is open at a time; closing resets the inputs.
const REJECT_COMMENT_MAX = 2000
const REJECT_SUGGESTION_MAX = 4000

const rejectDialogTarget = ref<{
  pendingOverrideId: string
  fieldName: string
  // Seeded into the suggested-value textarea when the user opts in to making a
  // counter-suggestion. Captured at dialog-open time so the starting point is
  // the value being rejected, not whatever's in state after network refetches.
  proposedValueAtOpen: string
} | null>(null)
const rejectComment = ref('')
const rejectSuggestion = ref('')
// Counter-suggestion is opt-in — checkbox off by default so a plain reject
// doesn't nag the reviewer for a replacement value they may not have.
const rejectWithSuggestion = ref(false)

const isRejectDialogOpen = computed({
  get: () => rejectDialogTarget.value !== null,
  set: (open) => {
    if (!open) {
      closeRejectDialog()
    }
  },
})

const isRejectSubmitDisabled = computed(() => {
  if (!rejectDialogTarget.value) {
    return true
  }
  if (rejectComment.value.trim().length === 0) {
    return true
  }
  if (rejectComment.value.length > REJECT_COMMENT_MAX) {
    return true
  }
  if (rejectWithSuggestion.value && rejectSuggestion.value.length > REJECT_SUGGESTION_MAX) {
    return true
  }
  return castVoteMutation.isPending.value
})

function openRejectDialog(field: SignatureFieldDto) {
  if (!field.pending) {
    return
  }
  rejectDialogTarget.value = {
    pendingOverrideId: field.pending.id,
    fieldName: field.displayName,
    proposedValueAtOpen: field.pending.proposedValue ?? '',
  }
  rejectComment.value = ''
  rejectSuggestion.value = ''
  rejectWithSuggestion.value = false
}

function closeRejectDialog() {
  rejectDialogTarget.value = null
  rejectComment.value = ''
  rejectSuggestion.value = ''
  rejectWithSuggestion.value = false
}

/**
 * Seed the suggestion textarea with the proposal text the moment the reviewer
 * opts in. Gives them something to edit instead of a blank box, and reads
 * intuitively — "I want to counter this value" starts with the value.
 * Only seeds when the field is empty so a reviewer re-toggling the checkbox
 * doesn't clobber draft edits.
 */
function onToggleRejectWithSuggestion(checked: boolean) {
  rejectWithSuggestion.value = checked
  if (checked && rejectSuggestion.value.length === 0) {
    rejectSuggestion.value = rejectDialogTarget.value?.proposedValueAtOpen ?? ''
  }
}

async function onRejectSubmit() {
  const target = rejectDialogTarget.value
  if (!target) {
    return
  }
  try {
    // Only send the counter-suggestion if the reviewer opted in via the
    // checkbox. A whitespace-only value after opt-in still counts as null.
    let suggestedValue: string | null = null
    if (rejectWithSuggestion.value) {
      const trimmed = rejectSuggestion.value.trim()
      if (trimmed.length > 0) {
        suggestedValue = trimmed
      }
    }

    await castVoteMutation.mutateAsync({
      pendingOverrideId: target.pendingOverrideId,
      body: {
        voteKind: 'Reject',
        comment: rejectComment.value.trim(),
        suggestedValue,
      },
    })
    toast({
      class: 'text-sm',
      description: t('page.signatures.editor.pending.approveSuccess'),
    })
    closeRejectDialog()
  } catch (err) {
    if (axios.isAxiosError(err) && err.response?.status === 409) {
      isOutdated.value = true
      closeRejectDialog()
      return
    }
    toast({
      class: 'text-sm',
      variant: 'destructive',
      description: t('page.signatures.editor.pending.voteFailed'),
    })
  }
}

// Accept-suggestion (proposer-only). Replaces the pending value with the
// chosen suggestion; the BE resets votes server-side and the cache
// invalidation surfaces the new pending row.
async function onAcceptSuggestion(
  pendingOverrideId: string,
  voteId: string,
) {
  try {
    await acceptSuggestionMutation.mutateAsync({
      pendingOverrideId,
      body: { voteId },
    })
    toast({
      class: 'text-sm',
      description: t('page.signatures.editor.pending.suggestions.accepted'),
    })
  } catch (err) {
    if (axios.isAxiosError(err) && err.response?.status === 409) {
      isOutdated.value = true
      return
    }
    toast({
      class: 'text-sm',
      variant: 'destructive',
      description: t('page.signatures.editor.pending.suggestions.acceptFailed'),
    })
  }
}

// Withdraw-vote (reviewer-only). Clears the caller's own vote so they can
// re-cast or leave it unvoted for another reviewer to break the tie. Proposer's
// auto-approve is structural and cannot be withdrawn — they use cancel instead.
async function onWithdrawVote(pendingOverrideId: string) {
  try {
    await withdrawVoteMutation.mutateAsync({ pendingOverrideId })
    toast({
      class: 'text-sm',
      description: t('page.signatures.editor.pending.withdrawn'),
    })
  } catch (err) {
    if (axios.isAxiosError(err) && err.response?.status === 409) {
      isOutdated.value = true
      return
    }
    toast({
      class: 'text-sm',
      variant: 'destructive',
      description: t('page.signatures.editor.pending.withdrawFailed'),
    })
  }
}

// Confirmation state for the Cancel-proposal action. Cancelling is destructive
// — it discards the draft + every reviewer vote + any suggestions — so we gate
// behind a small dialog instead of firing on the first click. Proposer sees one
// "Are you sure?" modal before the mutation runs.
const cancelConfirmPendingId = ref<string | null>(null)

const isCancelConfirmOpen = computed({
  get: () => cancelConfirmPendingId.value !== null,
  set: (open) => {
    if (!open) {
      cancelConfirmPendingId.value = null
    }
  },
})

function requestCancelPending(pendingOverrideId: string) {
  cancelConfirmPendingId.value = pendingOverrideId
}

function dismissCancelConfirm() {
  cancelConfirmPendingId.value = null
}

async function confirmCancelPending() {
  const pendingId = cancelConfirmPendingId.value
  if (pendingId === null) {
    return
  }
  cancelConfirmPendingId.value = null
  await onCancelPending(pendingId)
}

// Cancel-pending (proposer-only). Escape hatch when the pending is stuck at
// sub-threshold approvals + rejections in a small tenant. The BE deletes the
// pending; cache invalidation drops the "Pending" badge and restores whatever
// active override / default the field held before the proposal.
async function onCancelPending(pendingOverrideId: string) {
  try {
    await cancelPendingMutation.mutateAsync({ pendingOverrideId })
    toast({
      class: 'text-sm',
      description: t('page.signatures.editor.pending.cancelled'),
    })
  } catch (err) {
    if (axios.isAxiosError(err) && err.response?.status === 409) {
      isOutdated.value = true
      return
    }
    toast({
      class: 'text-sm',
      variant: 'destructive',
      description: t('page.signatures.editor.pending.cancelFailed'),
    })
  }
}

// Refresh from the "outdated" banner — clear in-progress drafts and refetch.
// The client's view is behind server state; re-typing an override after
// refresh is a manual step.
function refreshAfterOutdated() {
  draftValues.value = new Map()
  editingKeys.value = new Set()
  isOutdated.value = false
  void refetch()
}

// ─── Per-field submit-pending ─────────────────────────────────────────
async function onSubmitField(
  fieldName: string,
  proposedValue: string | null,
) {
  try {
    await submitPendingMutation.mutateAsync({
      placeholderName: fieldName,
      proposedValue,
    })
    cancelEdit(fieldName)
    toast({
      class: 'text-sm',
      description: t('page.signatures.editor.submit.submitSuccess'),
    })
  } catch (err) {
    if (axios.isAxiosError(err) && err.response?.status === 409) {
      isOutdated.value = true
      return
    }
    toast({
      class: 'text-sm',
      variant: 'destructive',
      description: t('page.signatures.editor.submit.submitFailed'),
    })
  }
}

function onSubmitDraft(fieldName: string) {
  const draft = draftValues.value.get(fieldName)
  if (draft === undefined) {
    return
  }
  void onSubmitField(fieldName, draft)
}

function onProposeRemoval(fieldName: string) {
  void onSubmitField(fieldName, null)
}

function blockHasDrafts(block: SignatureBlockDto): boolean {
  return block.fieldNames.some((name) => editingKeys.value.has(name))
}

function blockHasOverrideOrPending(block: SignatureBlockDto): boolean {
  return resolveBlockFields(block, fieldsByName.value).some(
    (f) => f.isOverridden || f.pending,
  )
}

/** Set of every blockId that has at least one upstream or downstream connection.
 *  Used to show a small arrow indicator on block rows in the middle column. */
const blocksWithConnections = computed<Set<string>>(() => {
  const ids = new Set<string>()
  if (!data.value) {
    return ids
  }
  // Index every dep tag that resolves to a real block (with fuzzy matching
  // for parameterized loop suffixes like `_{PERIOD_ID}`).
  const referencedBlockIds = new Set<string>()
  for (const section of data.value.sections) {
    for (const block of section.blocks) {
      for (const tag of block.dependsOn) {
        const target = lookupBlockByDepTag(tag)
        if (target) {
          referencedBlockIds.add(target.block.blockId)
        }
      }
    }
  }
  for (const section of data.value.sections) {
    for (const block of section.blocks) {
      const hasUpstream = block.dependsOn.some((t) => lookupBlockByDepTag(t) !== null)
      if (hasUpstream || referencedBlockIds.has(block.blockId)) {
        ids.add(block.blockId)
      }
    }
  }
  return ids
})
</script>

<template>
  <div class="signatures-root">
    <!-- PICKER: /signatures with no workflow type id -->
    <template v-if="!hasWorkflowTypeId">
      <div class="signatures-root__picker-column">
        <header class="signatures-root__header">
          <h1 class="page-title">{{ $t('page.signatures.title') }}</h1>
          <p class="page-subtitle">{{ $t('page.signatures.picker.subtitle') }}</p>
        </header>

        <section v-if="isLoadingCategories" class="signatures-root__state">
          <p>{{ $t('page.signatures.picker.loadingWorkflows') }}</p>
        </section>

        <section
          v-else-if="isCategoriesError"
          class="signatures-root__state signatures-root__state--error"
        >
          <p>{{ $t('page.signatures.picker.loadError') }}</p>
        </section>

        <section v-else class="signatures-root__picker">
          <p v-if="flatWorkflows.length === 0" class="signatures-root__state">
            {{ $t('page.signatures.picker.emptyWorkflows') }}
          </p>
          <ul
            v-else
            class="signatures-root__picker-list"
            :data-count="flatWorkflows.length"
          >
            <li v-for="workflow in flatWorkflows" :key="workflow.workflowTypeId">
              <button
                type="button"
                class="signatures-root__picker-item focus-ring focus-ring--inset"
                @click="openWorkflow(workflow.workflowTypeId)"
              >
                <span class="signatures-root__picker-category">
                  {{ workflow.categoryName }}
                </span>
                <span class="signatures-root__picker-name">
                  {{ workflow.typeName }}
                  <TooltipProvider
                    v-if="(reviewCountsByWorkflowType.get(workflow.workflowTypeId) ?? 0) > 0"
                    :delay-duration="150"
                  >
                    <Tooltip>
                      <TooltipTrigger as-child>
                        <span class="signatures-editor__needs-review">
                          {{ reviewCountsByWorkflowType.get(workflow.workflowTypeId) }}
                        </span>
                      </TooltipTrigger>
                      <TooltipContent>
                        {{
                          $t(
                            'page.signatures.editor.needsReviewTooltip',
                            { count: reviewCountsByWorkflowType.get(workflow.workflowTypeId) ?? 0 },
                            reviewCountsByWorkflowType.get(workflow.workflowTypeId) ?? 0,
                          )
                        }}
                      </TooltipContent>
                    </Tooltip>
                  </TooltipProvider>
                  <TooltipProvider
                    v-if="(proposerActionCountsByWorkflowType.get(workflow.workflowTypeId) ?? 0) > 0"
                    :delay-duration="150"
                  >
                    <Tooltip>
                      <TooltipTrigger as-child>
                        <span class="signatures-editor__needs-review signatures-editor__needs-review--proposer">
                          {{ proposerActionCountsByWorkflowType.get(workflow.workflowTypeId) }}
                        </span>
                      </TooltipTrigger>
                      <TooltipContent>
                        {{
                          $t(
                            'page.signatures.editor.needsProposerActionTooltip',
                            { count: proposerActionCountsByWorkflowType.get(workflow.workflowTypeId) ?? 0 },
                            proposerActionCountsByWorkflowType.get(workflow.workflowTypeId) ?? 0,
                          )
                        }}
                      </TooltipContent>
                    </Tooltip>
                  </TooltipProvider>
                </span>
              </button>
            </li>
          </ul>
        </section>
      </div>
    </template>

    <!-- EDITOR: /signatures/:workflowTypeId -->
    <template v-else>
      <!-- Shared top bar — visible in every editor state (loading, error,
           empty, populated) so there's always an exit from a stale URL. -->
      <div class="signatures-editor__topbar">
        <Button variant="ghost" size="sm" @click="goBackToPicker">
          ← {{ $t('page.signatures.editor.back') }}
        </Button>
      </div>

      <div v-if="isLoading" class="signatures-root__state">
        <h1 class="sr-only">{{ $t('page.signatures.title') }}</h1>
        {{ $t('page.signatures.editor.loading') }}
      </div>

      <div
        v-else-if="isError"
        class="signatures-root__state signatures-root__state--error"
      >
        <h1 class="sr-only">{{ $t('page.signatures.title') }}</h1>
        <p>
          {{
            $t('page.signatures.editor.loadError', {
              message: (error as Error)?.message ?? $t('page.signatures.editor.unknownError'),
            })
          }}
        </p>
        <Button variant="outline" size="sm" type="button" @click="refetch()">
          {{ $t('page.signatures.editor.retry') }}
        </Button>
      </div>

      <div v-else-if="data && data.sections.length === 0" class="signatures-root__state">
        <h1 class="sr-only">{{ data.workflowName }}</h1>
        <p>{{ $t('page.signatures.editor.emptySurface') }}</p>
      </div>

      <div v-else-if="data" class="signatures-editor">
        <header class="signatures-editor__header">
          <h1 class="page-title">{{ data.workflowName }}</h1>
          <p class="page-subtitle">{{ $t('page.signatures.editor.subtitle') }}</p>
        </header>

        <Tabs
          :model-value="activeTab"
          class="signatures-editor__tabs"
          @update:model-value="onTabUpdate"
        >
          <TabsList :aria-label="$t('page.signatures.tabs.ariaLabel')">
            <TabsTrigger value="settings">
              {{ $t('page.signatures.tabs.settings') }}
            </TabsTrigger>
            <TabsTrigger value="history">
              {{ $t('page.signatures.tabs.history') }}
            </TabsTrigger>
          </TabsList>

          <TabsContent value="history">
            <SignatureHistoryTab
              :workflow-type-id="workflowTypeId"
              :workflow="data"
            />
          </TabsContent>

          <TabsContent value="settings">
        <div class="signatures-editor__grid">
          <!-- Column 1: Sections -->
          <aside class="signatures-editor__sections">
            <div class="signatures-editor__column-label">
              {{ $t('page.signatures.editor.columns.sections') }}
            </div>
            <ul>
              <li v-for="section in sortedSections" :key="section.sectionId">
                <button
                  type="button"
                  :class="[
                    'signatures-editor__section-item',
                    'focus-ring',
                    'focus-ring--inset',
                    { 'signatures-editor__section-item--active': section.sectionId === selectedSectionId },
                  ]"
                  :aria-current="section.sectionId === selectedSectionId ? 'true' : undefined"
                  @click="selectSection(section.sectionId)"
                >
                  <span class="signatures-editor__section-title">{{ section.sectionTitle }}</span>
                  <span class="signatures-editor__section-meta">
                    <TooltipProvider
                      v-if="sectionNeedsReviewCount(section) > 0"
                      :delay-duration="150"
                    >
                      <Tooltip>
                        <TooltipTrigger as-child>
                          <span class="signatures-editor__needs-review">
                            {{ sectionNeedsReviewCount(section) }}
                          </span>
                        </TooltipTrigger>
                        <TooltipContent>
                          {{
                            $t(
                              'page.signatures.editor.needsReviewTooltip',
                              { count: sectionNeedsReviewCount(section) },
                              sectionNeedsReviewCount(section),
                            )
                          }}
                        </TooltipContent>
                      </Tooltip>
                    </TooltipProvider>
                    <TooltipProvider
                      v-if="sectionNeedsProposerActionCount(section) > 0"
                      :delay-duration="150"
                    >
                      <Tooltip>
                        <TooltipTrigger as-child>
                          <span class="signatures-editor__needs-review signatures-editor__needs-review--proposer">
                            {{ sectionNeedsProposerActionCount(section) }}
                          </span>
                        </TooltipTrigger>
                        <TooltipContent>
                          {{
                            $t(
                              'page.signatures.editor.needsProposerActionTooltip',
                              { count: sectionNeedsProposerActionCount(section) },
                              sectionNeedsProposerActionCount(section),
                            )
                          }}
                        </TooltipContent>
                      </Tooltip>
                    </TooltipProvider>
                    <span class="signatures-editor__section-count">{{ section.blocks.length }}</span>
                  </span>
                </button>
              </li>
            </ul>
          </aside>

          <!-- Column 2: Blocks in selected section -->
          <aside class="signatures-editor__blocks">
            <div class="signatures-editor__column-label">
              {{ $t('page.signatures.editor.columns.blocks') }}
            </div>
            <ul v-if="selectedSection">
              <li v-for="block in selectedSection.blocks" :key="block.blockId">
                <button
                  type="button"
                  :class="[
                    'signatures-editor__block-item',
                    'focus-ring',
                    'focus-ring--inset',
                    { 'signatures-editor__block-item--active': block.blockId === selectedBlockId },
                  ]"
                  :aria-current="block.blockId === selectedBlockId ? 'true' : undefined"
                  @click="selectBlock(block.blockId)"
                >
                  <span class="signatures-editor__block-name">
                    {{ block.blockName }}
                    <span
                      v-if="blocksWithConnections.has(block.blockId)"
                      class="signatures-editor__block-link-icon"
                      :title="$t('page.signatures.editor.connectionsTooltip')"
                    >
                      ↔
                    </span>
                  </span>
                  <span class="signatures-editor__block-meta">
                    <span class="signatures-editor__block-field-count">
                      {{ $t('page.signatures.editor.fieldCount', { count: block.fieldNames.length }, block.fieldNames.length) }}
                    </span>
                    <!-- Red pill with the count of fields awaiting a vote from
                         the current user. Sits alongside the purple override dot
                         so distinct states stay visually distinct. -->
                    <TooltipProvider
                      v-if="blockNeedsReviewCount(block) > 0"
                      :delay-duration="150"
                    >
                      <Tooltip>
                        <TooltipTrigger as-child>
                          <span class="signatures-editor__needs-review">
                            {{ blockNeedsReviewCount(block) }}
                          </span>
                        </TooltipTrigger>
                        <TooltipContent>
                          {{
                            $t(
                              'page.signatures.editor.needsReviewTooltip',
                              { count: blockNeedsReviewCount(block) },
                              blockNeedsReviewCount(block),
                            )
                          }}
                        </TooltipContent>
                      </Tooltip>
                    </TooltipProvider>
                    <TooltipProvider
                      v-if="blockNeedsProposerActionCount(block) > 0"
                      :delay-duration="150"
                    >
                      <Tooltip>
                        <TooltipTrigger as-child>
                          <span class="signatures-editor__needs-review signatures-editor__needs-review--proposer">
                            {{ blockNeedsProposerActionCount(block) }}
                          </span>
                        </TooltipTrigger>
                        <TooltipContent>
                          {{
                            $t(
                              'page.signatures.editor.needsProposerActionTooltip',
                              { count: blockNeedsProposerActionCount(block) },
                              blockNeedsProposerActionCount(block),
                            )
                          }}
                        </TooltipContent>
                      </Tooltip>
                    </TooltipProvider>
                    <!-- Purple dot — surfaces "this block has at least one
                         field that's been overridden, has an in-flight pending,
                         or is being edited right now." -->
                    <span
                      v-if="blockHasOverrideOrPending(block) || blockHasDrafts(block)"
                      class="signatures-editor__block-override"
                      :title="$t('page.signatures.editor.overriddenTooltip')"
                    />
                  </span>
                </button>
              </li>
            </ul>
          </aside>

          <!-- Column 3: Fields editor + connections panel -->
          <div class="signatures-editor__fields">
            <template v-if="selectedBlock">
              <div class="signatures-editor__block-heading">
                <span class="signatures-editor__block-eyebrow">
                  {{ selectedSection?.sectionTitle }}
                </span>
                <h2>{{ selectedBlock.blockName }}</h2>
              </div>


              <!-- Fields form -->
              <section
                v-if="selectedBlockFields.length > 0"
                class="signatures-editor__fields-list"
              >
                <div
                  v-for="field in selectedBlockFields"
                  :key="field.name"
                  class="signatures-editor__field"
                >
                  <div class="signatures-editor__field-header">
                    <span class="signatures-editor__field-name">{{ field.displayName }}</span>
                    <Badge
                      :variant="badgeVariantFor(fieldUiState(field))"
                      class="signatures-editor__field-badge"
                    >
                      {{ badgeLabelFor(fieldUiState(field)) }}
                    </Badge>
                  </div>
                  <p class="signatures-editor__field-description">
                    {{ field.description }}
                  </p>

                  <!-- Shared-field chips — a field referenced by more than one
                       block has ONE value everywhere; the chips make that
                       visible and jump to the referencing blocks (same
                       navigation as the dependency-graph nodes). -->
                  <div
                    v-if="field.usedByBlockIds.length > 1"
                    class="signatures-editor__used-by"
                  >
                    <span
                      class="signatures-editor__used-by-label"
                      :title="$t('page.signatures.editor.usedBy.tooltip')"
                    >
                      {{
                        $t(
                          'page.signatures.editor.usedBy.label',
                          { count: field.usedByBlockIds.length },
                          field.usedByBlockIds.length,
                        )
                      }}
                    </span>
                    <button
                      v-for="chip in usedByChipsFor(field)"
                      :key="chip.blockId"
                      type="button"
                      class="signatures-editor__used-by-chip"
                      :class="{
                        'signatures-editor__used-by-chip--current':
                          chip.blockId === selectedBlockId,
                      }"
                      :title="chip.sectionTitle"
                      @click="selectBlock(chip.blockId, chip.sectionId)"
                    >
                      {{ chip.blockName }}
                    </button>
                  </div>

                  <!-- Pending review panel — visible whenever the field has an
                       in-flight pending. Shows progress, the proposer's
                       proposed value, and the user's vote status (read-only). -->
                  <section
                    v-if="field.pending"
                    class="signatures-editor__pending"
                  >
                    <div class="signatures-editor__pending-meta">
                      <span class="signatures-editor__pending-label">
                        {{
                          field.pending.proposedValue === null
                            ? $t('page.signatures.editor.pending.proposedRemovalLabel')
                            : $t('page.signatures.editor.pending.proposed')
                        }}
                      </span>
                      <span
                        class="signatures-editor__pending-value"
                        :class="{
                          'signatures-editor__pending-value--removal':
                            field.pending.proposedValue === null,
                        }"
                      >
                        {{
                          field.pending.proposedValue === null
                            ? $t('page.signatures.editor.pending.proposedRemoval')
                            : field.pending.proposedValue
                        }}
                      </span>
                      <span class="signatures-editor__pending-proposer">
                        {{
                          $t('page.signatures.editor.pending.proposedBy', {
                            name: voterLabelFor(field.pending.proposerUserId),
                          })
                        }}
                      </span>
                    </div>

                    <div class="signatures-editor__pending-progress">
                      <Progress
                        :model-value="voteProgressFor(field).approvePct"
                        class="signatures-editor__pending-progress-bar"
                        :aria-label="
                          $t('page.signatures.editor.pending.approvalCount', {
                            count: field.pending.approveCount,
                            total: reviewThreshold,
                          })
                        "
                      />
                      <span class="signatures-editor__pending-progress-label">
                        {{
                          $t('page.signatures.editor.pending.approvalCount', {
                            count: field.pending.approveCount,
                            total: reviewThreshold,
                          })
                        }}
                      </span>
                    </div>

                    <div
                      v-if="field.pending.rejectCount > 0"
                      class="signatures-editor__pending-progress"
                    >
                      <Progress
                        :model-value="voteProgressFor(field).rejectPct"
                        class="signatures-editor__pending-progress-bar signatures-editor__pending-progress-bar--reject"
                        :aria-label="
                          $t('page.signatures.editor.pending.rejectionCount', {
                            count: field.pending.rejectCount,
                            total: reviewThreshold,
                          })
                        "
                      />
                      <span class="signatures-editor__pending-progress-label">
                        {{
                          $t('page.signatures.editor.pending.rejectionCount', {
                            count: field.pending.rejectCount,
                            total: reviewThreshold,
                          })
                        }}
                      </span>
                    </div>

                    <!-- Caller-is-proposer actions: Edit opens the textarea
                         prefilled with the current proposed value (submit
                         replaces the pending BE-side). Cancel withdraws the
                         pending entirely — the escape hatch for small-tenant
                         stalemates. Both buttons are outline/sm so they read
                         as one set; Cancel uses `--danger` text for intent. -->
                    <div
                      v-if="field.pending.proposerUserId === myUserId"
                      class="signatures-editor__pending-owner-actions"
                    >
                      <Button
                        variant="outline"
                        size="sm"
                        :disabled="
                          cancelPendingMutation.isPending.value
                            || submitPendingMutation.isPending.value
                        "
                        @click="startOverride(field.name, field.pending.proposedValue ?? '')"
                      >
                        {{ $t('page.signatures.editor.pending.edit') }}
                      </Button>
                      <Button
                        variant="outline"
                        size="sm"
                        class="signatures-editor__pending-owner-action--danger"
                        :disabled="cancelPendingMutation.isPending.value"
                        @click="requestCancelPending(field.pending.id)"
                      >
                        <template v-if="cancelPendingMutation.isPending.value">
                          {{ $t('page.signatures.editor.pending.cancelling') }}
                        </template>
                        <template v-else>
                          {{ $t('page.signatures.editor.pending.cancel') }}
                        </template>
                      </Button>
                    </div>

                    <!-- Caller-has-already-voted (not proposer) strip:
                         their prior vote outcome + a Change-vote button that
                         withdraws so they can re-cast. Proposer's structural
                         auto-approve isn't withdrawable — handled above. -->
                    <div
                      v-else-if="field.pending.myVote"
                      class="signatures-editor__pending-mine"
                    >
                      <Badge variant="outline">
                        {{
                          field.pending.myVote === 'Approve'
                            ? $t('page.signatures.editor.pending.youApproved')
                            : $t('page.signatures.editor.pending.youRejected')
                        }}
                      </Badge>
                      <Button
                        variant="outline"
                        size="sm"
                        :disabled="withdrawVoteMutation.isPending.value"
                        @click="onWithdrawVote(field.pending.id)"
                      >
                        <template v-if="withdrawVoteMutation.isPending.value">
                          {{ $t('page.signatures.editor.pending.withdrawing') }}
                        </template>
                        <template v-else>
                          {{ $t('page.signatures.editor.pending.withdraw') }}
                        </template>
                      </Button>
                    </div>

                    <!-- Inline vote actions — only when the user is eligible
                         (role + non-proposer + no prior vote). Approve fires
                         immediately; Reject opens the dialog so the user can
                         attach a reason / counter-suggestion. -->
                    <div
                      v-else-if="fieldCanVote(field)"
                      class="signatures-editor__pending-vote-actions"
                    >
                      <Button
                        variant="outline"
                        size="sm"
                        :disabled="castVoteMutation.isPending.value"
                        @click="onApprove(field)"
                      >
                        {{ $t('page.signatures.editor.pending.approve') }}
                      </Button>
                      <Button
                        variant="destructive"
                        size="sm"
                        :disabled="castVoteMutation.isPending.value"
                        @click="openRejectDialog(field)"
                      >
                        {{ $t('page.signatures.editor.pending.reject') }}
                      </Button>
                    </div>

                    <!-- Comments — collapsible above 3 entries to keep the field
                         card compact for the common low-comment case. -->
                    <div
                      v-if="field.pending.comments.length > 0"
                      class="signatures-editor__pending-comments"
                    >
                      <details
                        v-if="field.pending.comments.length > 3"
                        class="signatures-editor__pending-comments-details"
                      >
                        <summary>
                          {{
                            $t('page.signatures.editor.pending.commentsToggle', {
                              count: field.pending.comments.length,
                            })
                          }}
                        </summary>
                        <ul class="signatures-editor__pending-comments-list">
                          <li
                            v-for="comment in field.pending.comments"
                            :key="comment.voteId"
                            class="signatures-editor__pending-comment"
                          >
                            <span class="signatures-editor__pending-comment-kind">
                              {{ comment.voteKind }}
                            </span>
                            <span class="signatures-editor__pending-comment-author">
                              {{ voterLabelFor(comment.voterUserId) }}
                            </span>
                            <span class="signatures-editor__pending-comment-text">
                              {{ comment.comment }}
                            </span>
                          </li>
                        </ul>
                      </details>
                      <ul
                        v-else
                        class="signatures-editor__pending-comments-list"
                      >
                        <li
                          v-for="comment in field.pending.comments"
                          :key="comment.voteId"
                          class="signatures-editor__pending-comment"
                        >
                          <span class="signatures-editor__pending-comment-kind">
                            {{ comment.voteKind }}
                          </span>
                          <span class="signatures-editor__pending-comment-text">
                            {{ comment.comment }}
                          </span>
                        </li>
                      </ul>
                    </div>
                  </section>

                  <!-- Suggestions card — visible to anyone on the pending so
                       the suggester can confirm their own submission, reviewers
                       see what peers proposed, and the proposer has somewhere
                       to act. Only the proposer gets the Accept button; a
                       "by you" chip marks the caller's own suggestions. -->
                  <section
                    v-if="
                      field.pending
                      && summariseSuggestions(field.pending).length > 0
                    "
                    class="signatures-editor__suggestions"
                  >
                    <span class="signatures-editor__suggestions-title">
                      {{ $t('page.signatures.editor.pending.suggestions.title') }}
                    </span>
                    <div
                      v-for="suggestion in summariseSuggestions(field.pending)"
                      :key="suggestion.voteId"
                      class="signatures-editor__suggestion"
                    >
                      <Badge
                        v-if="suggestion.voterUserId === myUserId"
                        variant="outline"
                        class="signatures-editor__suggestion-mine"
                      >
                        {{ $t('page.signatures.editor.pending.suggestions.mine') }}
                      </Badge>
                      <div class="signatures-editor__suggestion-row">
                        <span class="signatures-editor__suggestion-field-label">
                          {{ $t('page.signatures.editor.pending.suggestions.byLabel') }}
                        </span>
                        <span class="signatures-editor__suggestion-value">
                          {{ voterLabelFor(suggestion.voterUserId) }}
                        </span>
                      </div>
                      <div class="signatures-editor__suggestion-row">
                        <span class="signatures-editor__suggestion-field-label">
                          {{ $t('page.signatures.editor.pending.suggestions.valueLabel') }}
                        </span>
                        <span class="signatures-editor__suggestion-value">
                          {{ suggestion.suggestedValue }}
                        </span>
                      </div>
                      <div
                        v-if="suggestion.comment"
                        class="signatures-editor__suggestion-row"
                      >
                        <span class="signatures-editor__suggestion-field-label">
                          {{ $t('page.signatures.editor.pending.suggestions.reasonLabel') }}
                        </span>
                        <span class="signatures-editor__suggestion-comment">
                          {{ suggestion.comment }}
                        </span>
                      </div>
                      <div
                        v-if="field.pending.proposerUserId === myUserId"
                        class="signatures-editor__suggestion-actions"
                      >
                        <Button
                          variant="outline"
                          size="sm"
                          :disabled="acceptSuggestionMutation.isPending.value"
                          @click="onAcceptSuggestion(field.pending!.id, suggestion.voteId)"
                        >
                          {{ $t('page.signatures.editor.pending.suggestions.accept') }}
                        </Button>
                      </div>
                    </div>
                  </section>

                  <!-- Draft editor — visible when the user has clicked
                       Propose / Edit. Submitting POSTs a single pending; the
                       BE replaces any prior pending for the same field so the
                       proposer can iterate on their own value without having
                       to cancel first. -->
                  <textarea
                    v-if="isEditingField(field.name)"
                    :ref="(el) => registerDraftTextareaRef(field.name, el)"
                    class="signatures-editor__field-input"
                    :aria-label="field.displayName"
                    :value="getDraftValue(field.name, field.value)"
                    rows="5"
                    @input="onFieldInput(field.name, $event)"
                  />

                  <div
                    v-if="
                      field.isOverridden
                      && !field.pending
                      && !isEditingField(field.name)
                    "
                    class="signatures-editor__field-default signatures-editor__field-current"
                  >
                    <span class="signatures-editor__field-default-label">
                      {{ $t('page.signatures.editor.overrideValueLabel') }}
                    </span>
                    <span class="signatures-editor__field-default-value">
                      {{ field.value }}
                    </span>
                  </div>

                  <div
                    v-if="field.defaultValue"
                    class="signatures-editor__field-default"
                  >
                    <span class="signatures-editor__field-default-label">
                      {{ $t('page.signatures.editor.defaultLabel') }}
                    </span>
                    <span class="signatures-editor__field-default-value">
                      {{ field.defaultValue }}
                    </span>
                  </div>

                  <!-- Action row. The buttons surfaced depend on three things:
                       - whether a pending already exists (block edits)
                       - whether the user has the textarea open (Submit / Cancel)
                       - whether the field already has an override (Edit + Remove)
                       "Test this signature" is always-on — testing is useful
                       whether or not there's a pending or an override. -->
                  <div class="signatures-editor__field-actions">
                    <template v-if="isEditingField(field.name)">
                      <Button
                        size="sm"
                        :disabled="submitPendingMutation.isPending.value"
                        @click="onSubmitDraft(field.name)"
                      >
                        <template v-if="submitPendingMutation.isPending.value">
                          {{ $t('page.signatures.editor.submit.submitting') }}
                        </template>
                        <template v-else>
                          {{ $t('page.signatures.editor.submit.submitForReview') }}
                        </template>
                      </Button>
                      <Button
                        variant="outline"
                        size="sm"
                        :disabled="submitPendingMutation.isPending.value"
                        @click="cancelEdit(field.name)"
                      >
                        {{ $t('page.signatures.editor.saveConfirm.cancel') }}
                      </Button>
                    </template>
                    <template v-else-if="!field.pending">
                      <Button
                        variant="outline"
                        size="sm"
                        @click="startOverride(field.name, field.value)"
                      >
                        {{ $t('page.signatures.editor.override') }}
                      </Button>
                      <Button
                        v-if="field.isOverridden"
                        variant="outline"
                        size="sm"
                        :disabled="submitPendingMutation.isPending.value"
                        @click="onProposeRemoval(field.name)"
                      >
                        {{ $t('page.signatures.editor.removeOverride') }}
                      </Button>
                    </template>
                    <!-- Always-on for AI Query blocks: tests a proposed value against
                         a past run. Hidden for Merge blocks because they have no
                         prompt to replay (the prompt-replay surface is
                         AI-Query-specific). -->
                    <Button
                      v-if="selectedBlock.blockType === 'AiQuery'"
                      variant="outline"
                      size="sm"
                      @click="onTryOnPastExecution(field)"
                    >
                      {{ $t('page.signatures.editor.tryOnPastExecution') }}
                    </Button>
                  </div>
                </div>
              </section>

              <section v-else class="signatures-editor__state">
                <p>{{ $t('page.signatures.editor.noFields') }}</p>
              </section>
            </template>

            <section v-else class="signatures-editor__state">
              <p>{{ $t('page.signatures.editor.pickBlock') }}</p>
            </section>
          </div>

          <!-- Column 4: Dependency graph — upstream above, current in middle, downstream below -->
          <aside class="signatures-editor__graph">
            <div class="signatures-editor__column-label">
              {{ $t('page.signatures.editor.columns.dependencies') }}
            </div>

            <template v-if="selectedBlock">
              <div class="signatures-editor__graph-column">
                <!-- Upstream -->
                <div
                  v-if="upstreamConnections.length > 0"
                  class="signatures-editor__graph-group signatures-editor__graph-group--upstream"
                >
                  <div class="signatures-editor__graph-group-label">
                    {{ $t('page.signatures.editor.graph.readsFrom') }}
                  </div>
                  <button
                    v-for="conn in upstreamConnections"
                    :key="conn.blockId"
                    type="button"
                    class="signatures-editor__graph-node signatures-editor__graph-node--upstream"
                    @click="selectBlock(conn.blockId, conn.sectionId)"
                  >
                    <span class="signatures-editor__graph-node-name">{{ conn.blockName }}</span>
                    <span
                      v-if="conn.sectionId !== selectedSectionId"
                      class="signatures-editor__graph-node-section"
                    >
                      {{ conn.sectionTitle }}
                    </span>
                  </button>
                  <div class="signatures-editor__graph-connector" aria-hidden="true">↓</div>
                </div>

                <!-- Current -->
                <div class="signatures-editor__graph-node signatures-editor__graph-node--current">
                  <span class="signatures-editor__graph-node-eyebrow">
                    {{ selectedSection?.sectionTitle }}
                  </span>
                  <span class="signatures-editor__graph-node-name">
                    {{ selectedBlock.blockName }}
                  </span>
                </div>

                <!-- Downstream -->
                <div
                  v-if="downstreamConnections.length > 0"
                  class="signatures-editor__graph-group signatures-editor__graph-group--downstream"
                >
                  <div class="signatures-editor__graph-connector" aria-hidden="true">↓</div>
                  <div class="signatures-editor__graph-group-label">
                    {{ $t('page.signatures.editor.graph.feedsInto') }}
                  </div>
                  <button
                    v-for="conn in downstreamConnections"
                    :key="conn.blockId"
                    type="button"
                    class="signatures-editor__graph-node signatures-editor__graph-node--downstream"
                    @click="selectBlock(conn.blockId, conn.sectionId)"
                  >
                    <span class="signatures-editor__graph-node-name">{{ conn.blockName }}</span>
                    <span
                      v-if="conn.sectionId !== selectedSectionId"
                      class="signatures-editor__graph-node-section"
                    >
                      {{ conn.sectionTitle }}
                    </span>
                  </button>
                </div>

                <p
                  v-if="upstreamConnections.length === 0 && downstreamConnections.length === 0"
                  class="signatures-editor__graph-empty"
                >
                  {{ $t('page.signatures.editor.graph.noConnections') }}
                </p>
              </div>
            </template>
          </aside>
        </div>
          </TabsContent>
        </Tabs>

        <!-- No global save footer — submission is per-field via the
             "Submit for review" button on each field card. -->

        <!-- Dialogs live outside the tab `v-else` so a half-typed reject
             comment, an active test-dialog run, or an open outdated banner
             survives a tab switch and back. The dialogs gate themselves on
             their `open` refs so being mounted while History is active is a
             cost-free no-op. -->

        <!-- Reject dialog — collects a required reason and an optional
             counter-suggestion before firing the Reject vote. Comment is
             trimmed before submit; an empty comment disables submit. -->
        <Dialog v-model:open="isRejectDialogOpen">
          <DialogContent class="pt-8 pr-8">
            <DialogHeader>
              <DialogTitle>
                {{ $t('page.signatures.editor.pending.rejectDialog.title') }}
              </DialogTitle>
              <DialogDescription>
                {{ $t('page.signatures.editor.pending.rejectDialog.description') }}
              </DialogDescription>
            </DialogHeader>

            <div class="signatures-editor__reject-field">
              <label for="signature-reject-comment" class="signatures-editor__reject-label">
                {{ $t('page.signatures.editor.pending.rejectDialog.commentLabel') }}
              </label>
              <Textarea
                id="signature-reject-comment"
                v-model="rejectComment"
                :placeholder="$t('page.signatures.editor.pending.rejectDialog.commentPlaceholder')"
                :maxlength="REJECT_COMMENT_MAX"
                rows="4"
              />
              <span class="signatures-editor__reject-counter">
                {{
                  $t('page.signatures.editor.pending.rejectDialog.counter', {
                    count: rejectComment.length,
                    max: REJECT_COMMENT_MAX,
                  })
                }}
              </span>
            </div>

            <div class="signatures-editor__reject-field">
              <label class="signatures-editor__reject-suggestion-toggle">
                <input
                  type="checkbox"
                  :checked="rejectWithSuggestion"
                  @change="onToggleRejectWithSuggestion(($event.target as HTMLInputElement).checked)"
                />
                <span>{{ $t('page.signatures.editor.pending.rejectDialog.suggestionToggle') }}</span>
              </label>
              <template v-if="rejectWithSuggestion">
                <Textarea
                  v-model="rejectSuggestion"
                  :aria-label="$t('page.signatures.editor.pending.rejectDialog.suggestionLabel')"
                  :placeholder="$t('page.signatures.editor.pending.rejectDialog.suggestionPlaceholder')"
                  :maxlength="REJECT_SUGGESTION_MAX"
                  rows="4"
                />
                <span class="signatures-editor__reject-counter">
                  {{
                    $t('page.signatures.editor.pending.rejectDialog.counter', {
                      count: rejectSuggestion.length,
                      max: REJECT_SUGGESTION_MAX,
                    })
                  }}
                </span>
              </template>
            </div>

            <DialogFooter>
              <Button variant="outline" @click="closeRejectDialog">
                {{ $t('page.signatures.editor.pending.rejectDialog.cancel') }}
              </Button>
              <Button
                variant="destructive"
                :disabled="isRejectSubmitDisabled"
                @click="onRejectSubmit"
              >
                {{ $t('page.signatures.editor.pending.rejectDialog.submit') }}
              </Button>
            </DialogFooter>
          </DialogContent>
        </Dialog>

        <!-- Cancel-proposal confirmation. Destructive — wipes the pending, every
             vote on it, and any counter-suggestions — so we require an explicit
             second click instead of firing on the toolbar button. -->
        <Dialog v-model:open="isCancelConfirmOpen">
          <DialogContent class="pt-8 pr-8">
            <DialogHeader>
              <DialogTitle>
                {{ $t('page.signatures.editor.pending.cancelConfirm.title') }}
              </DialogTitle>
              <DialogDescription>
                {{ $t('page.signatures.editor.pending.cancelConfirm.description') }}
              </DialogDescription>
            </DialogHeader>

            <DialogFooter>
              <Button variant="outline" @click="dismissCancelConfirm">
                {{ $t('page.signatures.editor.pending.cancelConfirm.keep') }}
              </Button>
              <Button
                variant="destructive"
                :disabled="cancelPendingMutation.isPending.value"
                @click="confirmCancelPending"
              >
                {{ $t('page.signatures.editor.pending.cancelConfirm.confirm') }}
              </Button>
            </DialogFooter>
          </DialogContent>
        </Dialog>

        <!-- Modal surfaced when a PUT returned 409 — the client's in-memory block
             ids are stale because Save-As-Core regenerated them. Refresh drops
             pending edits and refetches. `interact-outside` / `escape-key-down`
             are prevented so the user engages with the dialog rather than
             bypassing it; the only path forward is Refresh. -->
        <Dialog v-model:open="isOutdated">
          <DialogContent
            class="pt-10 pr-10 border-t-[3px] border-t-warning"
            :hide-close-button="true"
            @interact-outside.prevent
            @escape-key-down.prevent
          >
            <DialogHeader>
              <DialogTitle class="flex items-center gap-2 pr-8">
                <font-awesome-icon
                  icon="exclamation-triangle"
                  class="text-warning"
                />
                {{ $t('page.signatures.editor.outdatedTitle') }}
              </DialogTitle>
              <DialogDescription>
                {{ $t('page.signatures.editor.outdatedDescription') }}
              </DialogDescription>
            </DialogHeader>

            <DialogFooter>
              <Button @click="refreshAfterOutdated">
                {{ $t('page.signatures.editor.refresh') }}
              </Button>
            </DialogFooter>
          </DialogContent>
        </Dialog>

        <SignatureTestDialog
          v-model:open="testDialogOpen"
          :workflow-type-id="workflowTypeId"
          :replacement-tag="testDialogReplacementTag"
          :placeholder-name="testDialogPlaceholder"
          :field-display-name="testDialogDisplayName"
          :shared-block-count="testDialogSharedBlockCount"
          :initial-execution-id="pendingTestExecutionId"
          @submit-proposal="onTestDialogSubmitProposal"
        />
      </div>

      <h1 v-else class="sr-only">{{ $t('page.signatures.title') }}</h1>
    </template>
  </div>
</template>

<style scoped>
/* Signature settings — dark theme, platform-aligned. All colours pulled
   from the existing CSS variables in assets/main.css (--bg-0, --bg-1,
   --stroke, --text-primary, etc.) so this page matches the rest of the
   app out of the box. No custom brand tokens. */

.signatures-root {
  background: var(--bg-0);
  color: var(--text-primary);
  min-height: 100%;
}

.signatures-root__state {
  color: var(--text-secondary);
  padding: 6rem 2rem;
  text-align: center;
  font-size: 15px;
}
.signatures-root__state--error {
  color: var(--accent-error);
}

/* ─── Picker page ────────────────────────────────────────── */
.signatures-root__picker-column {
  max-width: 1280px;
  margin: 0 auto;
  padding: var(--spacing-xl, 2rem) var(--spacing-xl, 2rem) 3rem;
}
.signatures-root__header {
  margin-bottom: 2rem;
}
.signatures-root__picker-list {
  list-style: none;
  margin: 0;
  padding: 0;
  width: 100%;
  display: grid;
  gap: 1rem;
}
.signatures-root__picker-list[data-count='1'] {
  grid-template-columns: minmax(420px, 640px);
  justify-content: center;
}
.signatures-root__picker-list[data-count='2'],
.signatures-root__picker-list[data-count='3'],
.signatures-root__picker-list[data-count='4'] {
  grid-template-columns: repeat(2, minmax(360px, 1fr));
}
.signatures-root__picker-list:not([data-count='1']):not([data-count='2']):not([data-count='3']):not([data-count='4']) {
  grid-template-columns: repeat(auto-fill, minmax(320px, 1fr));
}
.signatures-root__picker-item {
  display: flex;
  flex-direction: column;
  gap: 0.5rem;
  padding: 1.25rem 1.5rem;
  border: 1px solid var(--stroke);
  background: var(--bg-1);
  border-radius: 8px;
  cursor: pointer;
  transition:
    border-color 160ms ease,
    background 160ms ease,
    transform 160ms cubic-bezier(0.16, 1, 0.3, 1);
}
.signatures-root__picker-item:hover {
  border-color: var(--accent-indigo-from, #6c8cff);
  background: var(--bg-2);
  transform: translateY(-1px);
}
.signatures-root__picker-item,
.signatures-editor__section-item,
.signatures-editor__block-item {
  width: 100%;
  text-align: left;
}
.signatures-root__picker-item {
  height: 100%;
}
.signatures-root__picker-category {
  font-size: 11px;
  font-weight: 600;
  letter-spacing: 0.1em;
  text-transform: uppercase;
  color: var(--text-secondary);
}
.signatures-root__picker-name {
  display: inline-flex;
  align-items: center;
  gap: 0.5rem;
  font-size: 18px;
  font-weight: 600;
  color: var(--text-primary);
  line-height: 1.3;
}

/* ─── Four-column editor ─────────────────────────────────── */
.signatures-editor {
  display: flex;
  flex-direction: column;
  height: 100%;
  min-height: calc(100vh - 64px);
}

.signatures-editor__header {
  /* Keep the side + bottom padding generous, but tighten the top so the
     workflow title sits close to the "Back to workflows" button — the
     button is the previous-page anchor and the title is the next-page
     header; they read as a single unit, not two separate sections. */
  padding: var(--spacing-xs, 0.5rem) var(--spacing-xl, 2rem) var(--spacing-lg, 1.5rem);
  border-bottom: 1px solid var(--stroke);
  background: var(--bg-0);
}

.signatures-editor__tabs {
  /* Match the header's horizontal padding so the Settings/History pills sit
     under the workflow title rather than flush against the viewport edge. */
  padding: var(--spacing-sm, 0.75rem) var(--spacing-xl, 2rem) 0;
  background: var(--bg-0);
}

.signatures-editor__grid {
  flex: 1;
  display: grid;
  grid-template-columns: 280px 280px 1fr 280px;
  min-height: 0;
}

.signatures-editor__sections,
.signatures-editor__blocks,
.signatures-editor__fields,
.signatures-editor__graph {
  overflow-y: auto;
  scroll-behavior: smooth;
  padding: var(--spacing-lg, 1.5rem) 0;
  background: var(--bg-0);
}
.signatures-editor__sections,
.signatures-editor__blocks,
.signatures-editor__fields {
  border-right: 1px solid var(--stroke);
}
.signatures-editor__fields {
  padding: var(--spacing-xl, 2rem);
}
.signatures-editor__graph {
  padding: var(--spacing-lg, 1.5rem) var(--spacing-md, 1rem) 3rem;
}

.signatures-editor__column-label {
  font-size: 12px;
  font-weight: 600;
  letter-spacing: 0.08em;
  text-transform: uppercase;
  color: var(--text-secondary);
  padding: 0 var(--spacing-lg, 1.5rem) var(--spacing-sm, 0.75rem);
  margin-bottom: var(--spacing-sm, 0.75rem);
  border-bottom: 1px solid var(--stroke);
}

.signatures-editor__sections ul,
.signatures-editor__blocks ul {
  list-style: none;
  margin: 0;
  padding: 0;
}

/* Section items */
.signatures-editor__section-item {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 0.75rem;
  padding: 0.75rem var(--spacing-lg, 1.5rem);
  cursor: pointer;
  border-left: 2px solid transparent;
  transition: background 160ms ease, border-color 160ms ease;
}
.signatures-editor__section-item:hover {
  background: var(--bg-1);
}
.signatures-editor__section-item--active {
  background: var(--bg-1);
  border-left-color: var(--accent-indigo-from, #6c8cff);
}
.signatures-editor__section-title {
  font-size: 14px;
  font-weight: 500;
  color: var(--text-primary);
}
.signatures-editor__section-count {
  font-size: 12px;
  color: var(--text-secondary);
  font-variant-numeric: tabular-nums;
  padding: 2px 8px;
  background: var(--bg-2);
  border-radius: 10px;
}

/* Block items */
.signatures-editor__block-item {
  display: flex;
  flex-direction: column;
  gap: 0.25rem;
  padding: 0.65rem var(--spacing-lg, 1.5rem);
  cursor: pointer;
  border-left: 2px solid transparent;
  transition: background 160ms ease, border-color 160ms ease;
}
.signatures-editor__block-item:hover {
  background: var(--bg-1);
}
.signatures-editor__block-item--active {
  background: var(--bg-1);
  border-left-color: var(--accent-indigo-from, #6c8cff);
}
.signatures-editor__block-name {
  font-size: 14px;
  color: var(--text-primary);
  display: flex;
  align-items: center;
  gap: 0.4rem;
}
.signatures-editor__block-link-icon {
  font-size: 11px;
  color: var(--accent-indigo-to, #9a6cff);
  font-weight: 600;
}
.signatures-editor__block-meta {
  display: flex;
  align-items: center;
  gap: 0.5rem;
  font-size: 11px;
  color: var(--text-secondary);
}
.signatures-editor__block-field-count {
  font-variant-numeric: tabular-nums;
}
.signatures-editor__block-override {
  width: 6px;
  height: 6px;
  border-radius: 50%;
  background: var(--accent-indigo-to, #9a6cff);
}

/* Rendered next to the field-count / section-count to flag pendings that need
   the current user's vote. Small, high-contrast pill — deliberately louder
   than the override dot so it reads as an action rather than a passive state. */
.signatures-editor__needs-review {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  min-width: 18px;
  height: 18px;
  padding: 0 0.4rem;
  border-radius: 999px;
  background: var(--accent-error-fill);
  color: #fff;
  font-size: 10px;
  font-weight: 700;
  letter-spacing: 0.03em;
  line-height: 1;
  font-variant-numeric: tabular-nums;
}
/* Proposer-side variant: amber so the two flavours of "action needed" stay
   visually distinct — red means you need to vote, amber means you need to
   react to a counter-suggestion on your own proposal. */
.signatures-editor__needs-review--proposer {
  background: var(--accent-warning-fill);
}

.signatures-editor__section-meta {
  display: inline-flex;
  align-items: center;
  gap: 0.4rem;
}

/* Fields column */
.signatures-editor__block-heading {
  margin-bottom: var(--spacing-xl, 2rem);
}
.signatures-editor__block-eyebrow {
  font-size: 11px;
  font-weight: 600;
  letter-spacing: 0.1em;
  text-transform: uppercase;
  color: var(--text-secondary);
}
.signatures-editor__block-heading h2 {
  font-size: 24px;
  font-weight: 600;
  line-height: 1.2;
  margin: 0.25rem 0 0;
  color: var(--text-primary);
}

.signatures-editor__fields-list {
  display: flex;
  flex-direction: column;
  gap: var(--spacing-xl, 2rem);
  max-width: 840px;
}
.signatures-editor__field {
  display: flex;
  flex-direction: column;
  gap: 0.5rem;
  padding: var(--spacing-lg, 1.5rem);
  background: var(--bg-1);
  border: 1px solid var(--stroke);
  border-radius: 8px;
}
.signatures-editor__field-header {
  display: flex;
  align-items: center;
  gap: 0.75rem;
}
.signatures-editor__field-name {
  font-size: 15px;
  font-weight: 600;
  color: var(--text-primary);
}
.signatures-editor__field-override {
  width: 8px;
  height: 8px;
  border-radius: 50%;
  background: var(--accent-indigo-to, #9a6cff);
}
.signatures-editor__field-description {
  font-size: 13px;
  color: var(--text-secondary);
  max-width: 60ch;
  margin: 0 0 0.5rem;
  line-height: 1.5;
}

/* Shared-field "Used by N blocks" row — label + one chip per referencing
   block. Chips reuse the dependency-graph node hover language (indigo border
   on hover, click to jump) at pill scale. */
.signatures-editor__used-by {
  display: flex;
  align-items: center;
  flex-wrap: wrap;
  gap: 0.4rem;
  margin-bottom: 0.25rem;
}
.signatures-editor__used-by-label {
  font-size: 11px;
  font-weight: 600;
  letter-spacing: 0.05em;
  text-transform: uppercase;
  color: var(--text-secondary);
  cursor: help;
}
.signatures-editor__used-by-chip {
  font-size: 12px;
  color: var(--text-primary);
  padding: 0.2rem 0.6rem;
  background: var(--bg-2);
  border: 1px solid var(--stroke);
  border-radius: 999px;
  cursor: pointer;
  transition:
    border-color 160ms ease,
    background 160ms ease;
}
.signatures-editor__used-by-chip:hover {
  border-color: var(--accent-indigo-from, #6c8cff);
  background: var(--bg-1);
}
.signatures-editor__used-by-chip--current {
  border-color: var(--accent-indigo-to, #9a6cff);
  cursor: default;
}
.signatures-editor__field-input {
  width: 100%;
  min-height: 120px;
  padding: 0.75rem 1rem;
  background: var(--bg-2);
  border: 1px solid var(--stroke);
  border-radius: 6px;
  color: var(--text-primary);
  /* Beat the global `textarea { font-size: 12px !important }` rule from main.css. */
  font-family: system-ui, -apple-system, sans-serif !important;
  font-size: 14px !important;
  line-height: 1.6 !important;
  resize: vertical;
  outline: none;
  transition: border-color 160ms ease;
}
.signatures-editor__field-input:focus {
  border-color: var(--accent-indigo-from, #6c8cff);
}

.signatures-editor__field-default {
  display: flex;
  gap: 0.75rem;
  padding: 0.6rem 0.85rem;
  border-radius: 6px;
  background: rgba(255, 255, 255, 0.03);
  border: 1px solid var(--stroke);
  font-size: 13px;
  color: var(--text-secondary);
  line-height: 1.55;
}
.signatures-editor__field-current {
  background: rgba(26, 56, 80, 0.08);
  border-color: var(--accent-indigo-to, #9a6cff);
  color: var(--text-primary);
  margin-bottom: 0.5rem;
}
.signatures-editor__field-default-label {
  flex-shrink: 0;
  font-size: 11px;
  font-weight: 600;
  letter-spacing: 0.05em;
  text-transform: uppercase;
  color: var(--text-secondary);
  padding-top: 2px;
}
.signatures-editor__field-default-value {
  white-space: pre-wrap;
  overflow-wrap: anywhere;
}

.signatures-editor__field-action {
  align-self: flex-start;
}

.signatures-editor__field-actions {
  display: flex;
  flex-wrap: wrap;
  gap: 0.5rem;
  align-items: center;
}

.signatures-editor__field-badge {
  margin-left: auto;
}

/* Pending review panel — sits inside a field card, surfaces the in-flight
   proposal, vote progress, the user's prior vote, and any reviewer
   comments. Visually subdued (uses bg-2 + 1px stroke) so it doesn't
   compete with the override editor above it. */
.signatures-editor__pending {
  display: flex;
  flex-direction: column;
  gap: 0.6rem;
  padding: 0.85rem 1rem;
  background: var(--bg-2);
  border: 1px solid var(--stroke);
  border-radius: 6px;
}
.signatures-editor__pending-meta {
  display: flex;
  flex-direction: column;
  gap: 0.2rem;
}
.signatures-editor__pending-label {
  font-size: 10px;
  font-weight: 600;
  letter-spacing: 0.1em;
  text-transform: uppercase;
  color: var(--text-secondary);
}
.signatures-editor__pending-value {
  font-size: 13px;
  color: var(--text-primary);
  white-space: pre-wrap;
  overflow-wrap: anywhere;
}
.signatures-editor__pending-value--removal {
  font-style: italic;
  color: var(--text-secondary);
}
.signatures-editor__pending-progress {
  display: flex;
  align-items: center;
  gap: 0.6rem;
}
.signatures-editor__pending-progress-bar {
  flex: 1;
  height: 6px;
}
.signatures-editor__pending-progress-bar--reject {
  --progress-indicator: var(--destructive, #ef4444);
}
.signatures-editor__pending-progress-label {
  font-size: 11px;
  color: var(--text-secondary);
  font-variant-numeric: tabular-nums;
  white-space: nowrap;
}
.signatures-editor__pending-mine {
  display: flex;
  align-items: center;
  gap: 0.5rem;
  flex-wrap: wrap;
  align-self: flex-start;
}
.signatures-editor__pending-vote-actions {
  display: flex;
  gap: 0.5rem;
  flex-wrap: wrap;
}
.signatures-editor__pending-owner-actions {
  display: flex;
  gap: 0.5rem;
  flex-wrap: wrap;
}
.signatures-editor__pending-owner-action--danger {
  color: var(--accent-error);
  border-color: var(--accent-error);
}
.signatures-editor__pending-owner-action--danger:hover {
  background: rgba(239, 68, 68, 0.08);
}
.signatures-editor__pending-comments {
  font-size: 13px;
  color: var(--text-secondary);
}
.signatures-editor__pending-comments-details summary {
  cursor: pointer;
  font-size: 12px;
  color: var(--text-secondary);
  list-style: none;
  user-select: none;
}
.signatures-editor__pending-comments-details summary::-webkit-details-marker {
  display: none;
}
.signatures-editor__pending-comments-details summary::before {
  content: '▸ ';
  display: inline-block;
  transition: transform 120ms ease;
}
.signatures-editor__pending-comments-details[open] summary::before {
  content: '▾ ';
}
.signatures-editor__pending-comments-list {
  list-style: none;
  margin: 0.4rem 0 0;
  padding: 0;
  display: flex;
  flex-direction: column;
  gap: 0.35rem;
}
.signatures-editor__pending-comment {
  display: flex;
  gap: 0.5rem;
  align-items: baseline;
}
.signatures-editor__pending-comment-kind {
  font-size: 10px;
  font-weight: 600;
  letter-spacing: 0.08em;
  text-transform: uppercase;
  color: var(--text-secondary);
  flex-shrink: 0;
  min-width: 56px;
}
.signatures-editor__pending-comment-author {
  font-size: 12px;
  color: var(--text-secondary);
  font-weight: 500;
  flex-shrink: 0;
}
.signatures-editor__pending-comment-text {
  color: var(--text-primary);
  white-space: pre-wrap;
  overflow-wrap: anywhere;
}

.signatures-editor__pending-proposer {
  font-size: 12px;
  color: var(--text-secondary);
  margin-top: 0.25rem;
}

/* Suggestions block — shown only to the proposer when reviewers have
   counter-suggested. Lives below the pending panel as its own card to
   give Accept buttons their own decision space. */
.signatures-editor__suggestions {
  display: flex;
  flex-direction: column;
  gap: 0.6rem;
  padding: 0.85rem 1rem;
  background: var(--bg-2);
  border: 1px solid var(--stroke);
  border-radius: 6px;
}
.signatures-editor__suggestions-title {
  font-size: 10px;
  font-weight: 600;
  letter-spacing: 0.1em;
  text-transform: uppercase;
  color: var(--text-secondary);
}
.signatures-editor__suggestion {
  position: relative;
  display: flex;
  flex-direction: column;
  gap: 0.4rem;
  padding: 0.6rem 0.7rem;
  background: var(--bg-1);
  border: 1px solid var(--stroke);
  border-radius: 6px;
}
.signatures-editor__suggestion-row {
  display: grid;
  grid-template-columns: 6rem 1fr;
  gap: 0.75rem;
  align-items: baseline;
}
.signatures-editor__suggestion-field-label {
  font-size: 11px;
  font-weight: 600;
  letter-spacing: 0.05em;
  text-transform: uppercase;
  color: var(--text-secondary);
}
.signatures-editor__suggestion-value {
  font-size: 13px;
  color: var(--text-primary);
  white-space: pre-wrap;
  overflow-wrap: anywhere;
}
.signatures-editor__suggestion-mine {
  position: absolute;
  top: 0.5rem;
  right: 0.5rem;
  font-size: 10px;
  font-weight: 600;
  line-height: 1;
  letter-spacing: 0.06em;
  text-transform: uppercase;
  padding: 0.2rem 0.45rem;
  border-radius: 999px;
  background: rgba(26, 56, 80, 0.12);
  border-color: transparent;
  color: var(--accent-indigo-to, #9a6cff);
}
.signatures-editor__suggestion-comment {
  font-size: 12px;
  color: var(--text-secondary);
  font-style: italic;
}
.signatures-editor__suggestion-actions {
  display: flex;
  gap: 0.5rem;
  align-items: center;
  justify-content: flex-end;
  margin-top: 0.25rem;
}

.signatures-editor__reject-counter {
  font-size: 11px;
  color: var(--text-secondary);
  text-align: right;
  font-variant-numeric: tabular-nums;
}
.signatures-editor__reject-field {
  display: flex;
  flex-direction: column;
  gap: 0.35rem;
}
.signatures-editor__reject-label {
  font-size: 12px;
  font-weight: 600;
  color: var(--text-primary);
}
.signatures-editor__reject-suggestion-toggle {
  display: inline-flex;
  align-items: center;
  gap: 0.5rem;
  font-size: 13px;
  color: var(--text-primary);
  cursor: pointer;
  user-select: none;
}
.signatures-editor__reject-suggestion-toggle input[type='checkbox'] {
  width: 14px;
  height: 14px;
  margin: 0;
  cursor: pointer;
}

.signatures-editor__state {
  color: var(--text-secondary);
  padding: 2rem 0;
  font-size: 14px;
}

/* Column 4: dependency graph */
.signatures-editor__graph-column {
  display: flex;
  flex-direction: column;
  align-items: stretch;
  gap: 0.5rem;
}
.signatures-editor__graph-group {
  display: flex;
  flex-direction: column;
  align-items: stretch;
  gap: 0.4rem;
}
.signatures-editor__graph-group-label {
  font-size: 10px;
  font-weight: 600;
  letter-spacing: 0.1em;
  text-transform: uppercase;
  color: var(--text-secondary);
  text-align: center;
  margin-bottom: 0.25rem;
}
.signatures-editor__graph-node {
  display: flex;
  flex-direction: column;
  gap: 0.2rem;
  padding: 0.7rem 0.9rem;
  border: 1px solid var(--stroke);
  background: var(--bg-1);
  border-radius: 6px;
  text-align: left;
  cursor: pointer;
  transition:
    border-color 160ms ease,
    background 160ms ease;
}
.signatures-editor__graph-node:hover {
  border-color: var(--accent-indigo-from, #6c8cff);
  background: var(--bg-2);
}
.signatures-editor__graph-node-name {
  font-size: 13px;
  font-weight: 500;
  color: var(--text-primary);
  line-height: 1.3;
}
.signatures-editor__graph-node-section {
  font-size: 10px;
  color: var(--text-secondary);
  letter-spacing: 0.04em;
}
.signatures-editor__graph-node-eyebrow {
  font-size: 10px;
  font-weight: 600;
  letter-spacing: 0.1em;
  text-transform: uppercase;
  color: var(--text-secondary);
}
.signatures-editor__graph-node--current {
  background: var(--primary);
  border-color: transparent;
  padding: 0.9rem 1rem;
  cursor: default;
  pointer-events: none;
  margin: 0.4rem 0;
  box-shadow: 0 6px 16px -10px rgba(26, 56, 80, 0.6);
}
.signatures-editor__graph-node--current .signatures-editor__graph-node-name {
  color: var(--primary-foreground);
  font-size: 14px;
  font-weight: 600;
}
.signatures-editor__graph-node--current .signatures-editor__graph-node-eyebrow {
  color: var(--primary-foreground);
}
.signatures-editor__graph-connector {
  text-align: center;
  color: var(--text-secondary);
  font-size: 14px;
  line-height: 1;
}
.signatures-editor__graph-empty {
  font-size: 12px;
  color: var(--text-secondary);
  text-align: center;
  padding: 2rem 0;
  margin: 0;
}

.signatures-editor__topbar {
  padding: var(--spacing-md, 1rem) var(--spacing-xl, 2rem) 0;
}

</style>
