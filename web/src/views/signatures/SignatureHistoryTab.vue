<script setup lang="ts">
/**
 * Signature override history tab.
 *
 * Shows every value transition (Created / Updated / Removed / Renamed) on
 * `SignatureFieldOverride` for the current workflow type, newest first.
 * Filters by field (placeholder), actor, and date range; pagination is
 * server-side via skip/take.
 *
 * Filter and pagination state lives in the URL (`?placeholder=...&skip=...`)
 * so reloads and shared links preserve the view. URL writes use `router.replace`
 * to avoid stacking history entries on every keystroke.
 */
import { computed, ref, watch } from 'vue'
import { useRoute, useRouter, type LocationQueryRaw } from 'vue-router'
import { useI18n } from 'vue-i18n'
import API from '@/api'
import { formatDate, formatDateTime, formatTime } from '@/lib/utils'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import {
  Dialog,
  DialogDescription,
  DialogHeader,
  DialogScrollContent,
  DialogTitle,
} from '@/components/ui/dialog'
import { Input } from '@/components/ui/input'
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select'
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from '@/components/ui/table'
import type {
  SignatureHistoryApproverDto,
  SignatureHistoryEntryDto,
  SignatureHistoryFilters,
  WorkflowSignaturesDto,
} from '@/api/signatures/schema'
import { historyFieldLabel } from './helpers'

const props = defineProps<{
  workflowTypeId: string
  workflow: WorkflowSignaturesDto
}>()

const route = useRoute()
const router = useRouter()
const { t } = useI18n()

const ANY = '__any__'
const PAGE_SIZE = 50

const placeholderFilter = ref<string>(readStringQuery('placeholder', ''))
const actorFilter = ref<string>(readStringQuery('actor', ''))
const fromFilter = ref<string>(readStringQuery('fromUtc', ''))
const toFilter = ref<string>(readStringQuery('toUtc', ''))
const skip = ref<number>(clampSkip(route.query.skip))

function clampSkip(value: unknown): number {
  const parsed = Number(value ?? 0)
  if (!Number.isFinite(parsed)) {
    return 0
  }
  return Math.max(0, Math.floor(parsed))
}

// Placeholder filter and table Field column want a user-friendly label
// (`displayName`), but the backend filter still keys on the internal field
// name. We expose the friendly label in the UI and ship the internal value to
// the API. Options come from the workflow-level `fields` list — the single
// source of truth, each field appearing exactly once.
interface PlaceholderOption {
  placeholderName: string
  displayName: string
}

const placeholderOptions = computed<PlaceholderOption[]>(() => {
  return props.workflow.fields
    .map((field) => ({
      placeholderName: field.name,
      displayName: field.displayName || field.name,
    }))
    .sort((a, b) => a.displayName.localeCompare(b.displayName))
})

// Field name → friendly `displayName`, used by the table to render the
// Field column with the same label users see in the filter. Falls back to the
// raw name when the field is no longer in the workflow (e.g. a staff
// rename happened between the audit row being written and now).
const placeholderDisplayByName = computed<Map<string, string>>(() => {
  const map = new Map<string, string>()
  for (const option of placeholderOptions.value) {
    map.set(option.placeholderName, option.displayName)
  }
  return map
})

// Block-id → human label for LEGACY rows (written before overrides moved to
// the workflow level; they carry a non-null blockId). Rows whose block has
// since been renamed fall back to the raw block id.
const blockLabelById = computed<Map<string, string>>(() => {
  const map = new Map<string, string>()
  for (const section of props.workflow.sections) {
    for (const block of section.blocks) {
      map.set(block.blockId, block.blockName)
    }
  }
  return map
})

const filters = computed<SignatureHistoryFilters>(() => ({
  placeholderName: placeholderFilter.value || undefined,
  actorUserId: actorFilter.value || undefined,
  fromUtc: fromFilter.value
    ? new Date(`${fromFilter.value}T00:00:00`).toISOString()
    : undefined,
  toUtc: toFilter.value
    ? new Date(`${toFilter.value}T23:59:59.999`).toISOString()
    : undefined,
  skip: skip.value,
  take: PAGE_SIZE,
}))

const workflowTypeIdRef = computed(() => props.workflowTypeId)
const { data, isLoading, isError, refetch } = API.Signatures.useGetSignatureHistory(
  workflowTypeIdRef,
  filters,
)

const rows = computed<SignatureHistoryEntryDto[]>(() => data.value?.items ?? [])
const totalCount = computed<number>(() => data.value?.totalCount ?? 0)

const seenActors = ref<Map<string, string | null>>(new Map())

watch(
  rows,
  (items) => {
    let changed = false
    const next = new Map(seenActors.value)
    for (const row of items) {
      if (!row.actorUserId) {
        continue
      }
      const existing = next.get(row.actorUserId)
      if (existing === undefined || existing !== row.actorUserEmail) {
        next.set(row.actorUserId, row.actorUserEmail)
        changed = true
      }
    }
    if (changed) {
      seenActors.value = next
    }
  },
  { immediate: true },
)

interface ActorOption {
  userId: string
  label: string
}

const actorOptions = computed<ActorOption[]>(() => {
  const options = Array.from(seenActors.value, ([userId, email]) => ({
    userId,
    label: email ?? userId,
  }))
  // If a URL-loaded `?actor=<id>` filter is active but that id isn't in the
  // loaded rows yet (or ever), synthesise a placeholder option so the Select
  // trigger reflects the active filter instead of showing the "(Any)" empty
  // state — without this the user sees an apparently-off filter actively
  // narrowing the table, which is deeply confusing.
  if (actorFilter.value && !seenActors.value.has(actorFilter.value)) {
    options.push({
      userId: actorFilter.value,
      label: t('page.signatures.history.unknownActor'),
    })
  }
  return options.sort((a, b) => a.label.localeCompare(b.label))
})

const pageFrom = computed(() => (totalCount.value === 0 ? 0 : skip.value + 1))
const pageTo = computed(() =>
  Math.min(skip.value + PAGE_SIZE, totalCount.value),
)
const canPrev = computed(() => skip.value > 0)
const canNext = computed(() => skip.value + PAGE_SIZE < totalCount.value)

const hasActiveFilters = computed(
  () =>
    placeholderFilter.value !== ''
    || actorFilter.value !== ''
    || fromFilter.value !== ''
    || toFilter.value !== '',
)

function readStringQuery(key: string, fallback: string): string {
  const value = route.query[key]
  return typeof value === 'string' ? value : fallback
}

// Browser back/forward navigations replace `route.query` without reloading
// the component, so re-seed the local refs when that happens. Without this,
// the inputs and table would stay frozen on stale state after a Back button.
watch(
  () => route.query,
  (q) => {
    const placeholder = typeof q.placeholder === 'string' ? q.placeholder : ''
    const actor = typeof q.actor === 'string' ? q.actor : ''
    const from = typeof q.fromUtc === 'string' ? q.fromUtc : ''
    const to = typeof q.toUtc === 'string' ? q.toUtc : ''
    const nextSkip = clampSkip(q.skip)
    if (placeholderFilter.value !== placeholder) {
      placeholderFilter.value = placeholder
    }
    if (actorFilter.value !== actor) {
      actorFilter.value = actor
    }
    if (fromFilter.value !== from) {
      fromFilter.value = from
    }
    if (toFilter.value !== to) {
      toFilter.value = to
    }
    if (skip.value !== nextSkip) {
      skip.value = nextSkip
    }
  },
)

function syncQuery() {
  // Preserve unrelated query params (deep-link state, feature flags, etc.).
  // Only the keys this tab owns are deleted before being re-set so a typo or
  // stale value can't leak between filter updates.
  const next: LocationQueryRaw = { ...route.query, tab: 'history' }
  delete next.placeholder
  delete next.actor
  delete next.fromUtc
  delete next.toUtc
  delete next.skip
  if (placeholderFilter.value) {
    next.placeholder = placeholderFilter.value
  }
  if (actorFilter.value) {
    next.actor = actorFilter.value
  }
  if (fromFilter.value) {
    next.fromUtc = fromFilter.value
  }
  if (toFilter.value) {
    next.toUtc = toFilter.value
  }
  if (skip.value > 0) {
    next.skip = String(skip.value)
  }
  router.replace({ query: next })
}

// All filter mutations route through `applyFilter` so skip resets to 0
// alongside the filter change. Doing the resets here (rather than via a
// watcher on the filter refs) means `skip` and the filter ref both update in
// the same synchronous step, so the `filters` computed only re-evaluates
// once and the query fetch fires once.
function applyFilter(mutate: () => void) {
  mutate()
  skip.value = 0
  syncQuery()
}

function clearFilters() {
  applyFilter(() => {
    placeholderFilter.value = ''
    actorFilter.value = ''
    fromFilter.value = ''
    toFilter.value = ''
  })
}

function onPlaceholderChange(value: unknown) {
  const stringValue = typeof value === 'string' ? value : ''
  applyFilter(() => {
    placeholderFilter.value = stringValue === ANY ? '' : stringValue
  })
}

function onActorChange(value: unknown) {
  const stringValue = typeof value === 'string' ? value : ''
  applyFilter(() => {
    actorFilter.value = stringValue === ANY ? '' : stringValue
  })
}

function onFromChange(value: Event) {
  const next = (value.target as HTMLInputElement).value
  applyFilter(() => {
    fromFilter.value = next
  })
}

function onToChange(value: Event) {
  const next = (value.target as HTMLInputElement).value
  applyFilter(() => {
    toFilter.value = next
  })
}

// Each action gets a distinct, readable colour so the table is scannable at
// a glance. Painted via design-token classes (success / warning / info)
// rather than the shadcn Badge variants — `secondary` was too muted to
// distinguish from `outline` and `destructive` was the wrong semantic for an
// intentional Removed action. The token classes live in this component's
// styles and pick up dark-mode automatically through the global token vars.
function actionBadgeClass(action: string): string {
  const base = 'signature-history__action-badge'
  if (action === 'Created') {
    return `${base} ${base}--created`
  }
  if (action === 'Updated') {
    return `${base} ${base}--updated`
  }
  if (action === 'Removed') {
    return `${base} ${base}--removed`
  }
  if (action === 'Renamed') {
    return `${base} ${base}--renamed`
  }
  return base
}

function actionLabel(action: string): string {
  const key = `page.signatures.history.action.${action}`
  const translated = t(key)
  if (translated !== key) {
    return translated
  }
  return t('page.signatures.history.action.unknown')
}

function actorLabel(row: SignatureHistoryEntryDto): string {
  if (row.actorUserEmail) {
    return row.actorUserEmail
  }
  // Backfilled rows synthesised from existing overrides carry an actor id but
  // no email (the migration couldn't cross the UserData DB boundary to
  // resolve it). Rendering a 36-char GUID alone is opaque, so we surface a
  // friendlier label instead. Same for rows where the user has been deleted.
  return t('page.signatures.history.unknownActor')
}

// Approver-list preview for the table cell. Showing every approver inline
// blows out the row height once you get past two or three; the detail dialog
// renders the full list anyway, so a "first 2 + +N more" preview gives a
// useful hint without making the table jagged.
const APPROVER_PREVIEW_COUNT = 2

interface ApproverPreview {
  visible: SignatureHistoryApproverDto[]
  overflow: number
}

function approverPreview(row: SignatureHistoryEntryDto): ApproverPreview {
  if (row.approvers.length <= APPROVER_PREVIEW_COUNT) {
    return { visible: [...row.approvers], overflow: 0 }
  }
  return {
    visible: row.approvers.slice(0, APPROVER_PREVIEW_COUNT),
    overflow: row.approvers.length - APPROVER_PREVIEW_COUNT,
  }
}

function fieldLabel(row: SignatureHistoryEntryDto): string {
  return historyFieldLabel(
    row,
    blockLabelById.value,
    placeholderDisplayByName.value,
    (blockNames) => t('page.signatures.history.usedBy', { blocks: blockNames }),
  )
}

function nextPage() {
  if (canNext.value) {
    skip.value = skip.value + PAGE_SIZE
    syncQuery()
  }
}

function prevPage() {
  if (canPrev.value) {
    skip.value = Math.max(0, skip.value - PAGE_SIZE)
    syncQuery()
  }
}

// Row-detail dialog: long override values (prompts can run hundreds of lines)
// would blow out the table layout if rendered inline. Cells show a single-line
// preview and the row opens a dialog with the full Previous/New text on
// click. The dialog is intentionally lightweight — no edit affordance, just
// readable formatting + close.
const PREVIEW_CHARS = 80

const detailRow = ref<SignatureHistoryEntryDto | null>(null)
const detailOpen = ref(false)

function openDetail(row: SignatureHistoryEntryDto) {
  detailRow.value = row
  detailOpen.value = true
}

function previewValue(value: string | null, side: 'previous' | 'new'): string {
  if (value === null) {
    return nullValueLabel(side)
  }
  const oneLine = value.replace(/\s+/g, ' ').trim()
  if (oneLine.length <= PREVIEW_CHARS) {
    return oneLine
  }
  return `${oneLine.slice(0, PREVIEW_CHARS)}…`
}

// The meaning of a null value depends on which side it's on:
// - null `previousValue` (Created row): there was no override before, the
//   field resolved to its staff default.
// - null `newValue` (Removed row): the client cleared the override, the
//   field again resolves to its staff default.
function nullValueLabel(side: 'previous' | 'new'): string {
  if (side === 'previous') {
    return t('page.signatures.history.noPreviousValue')
  }
  return t('page.signatures.history.removedMarker')
}
</script>

<template>
  <section class="signature-history">
    <header class="signature-history__header">
      <h2 class="page-title">{{ t('page.signatures.history.title') }}</h2>
      <p class="page-subtitle">{{ t('page.signatures.history.subtitle') }}</p>
    </header>

    <div class="signature-history__filters">
      <div class="signature-history__filter">
        <label for="sig-history-placeholder">
          {{ t('page.signatures.history.filter.placeholder') }}
        </label>
        <!-- eslint-disable-next-line vuejs-accessibility/form-control-has-label -- the Select root renders no element; the label above names its SelectTrigger by id -->
        <Select
          :model-value="placeholderFilter || ANY"
          @update:model-value="onPlaceholderChange"
        >
          <SelectTrigger id="sig-history-placeholder">
            <SelectValue :placeholder="t('page.signatures.history.filter.any')" />
          </SelectTrigger>
          <SelectContent>
            <SelectItem :value="ANY">
              {{ t('page.signatures.history.filter.any') }}
            </SelectItem>
            <SelectItem
              v-for="option in placeholderOptions"
              :key="option.placeholderName"
              :value="option.placeholderName"
            >
              {{ option.displayName }}
            </SelectItem>
          </SelectContent>
        </Select>
      </div>

      <div class="signature-history__filter">
        <label for="sig-history-actor">
          {{ t('page.signatures.history.filter.actor') }}
        </label>
        <!-- eslint-disable-next-line vuejs-accessibility/form-control-has-label -- the Select root renders no element; the label above names its SelectTrigger by id -->
        <Select
          :model-value="actorFilter || ANY"
          @update:model-value="onActorChange"
        >
          <SelectTrigger id="sig-history-actor">
            <SelectValue :placeholder="t('page.signatures.history.filter.any')" />
          </SelectTrigger>
          <SelectContent>
            <SelectItem :value="ANY">
              {{ t('page.signatures.history.filter.any') }}
            </SelectItem>
            <SelectItem
              v-for="option in actorOptions"
              :key="option.userId"
              :value="option.userId"
            >
              {{ option.label }}
            </SelectItem>
          </SelectContent>
        </Select>
      </div>

      <div class="signature-history__filter">
        <label for="sig-history-from">
          {{ t('page.signatures.history.filter.from') }}
        </label>
        <Input
          id="sig-history-from"
          :model-value="fromFilter"
          type="date"
          :max="toFilter || undefined"
          @change="onFromChange"
        />
      </div>

      <div class="signature-history__filter">
        <label for="sig-history-to">
          {{ t('page.signatures.history.filter.to') }}
        </label>
        <Input
          id="sig-history-to"
          :model-value="toFilter"
          type="date"
          :min="fromFilter || undefined"
          @change="onToChange"
        />
      </div>

      <Button variant="ghost" size="sm" type="button" @click="clearFilters">
        {{ t('page.signatures.history.filter.clear') }}
      </Button>
    </div>

    <div
      v-if="isLoading"
      class="signature-history__state"
      role="status"
      aria-live="polite"
      aria-busy="true"
    >
      {{ t('page.signatures.history.loading') }}
    </div>

    <div
      v-else-if="isError"
      class="signature-history__state signature-history__state--error"
      role="alert"
    >
      <p>{{ t('page.signatures.history.loadError') }}</p>
      <Button variant="outline" size="sm" type="button" @click="refetch()">
        {{ t('page.signatures.history.retry') }}
      </Button>
    </div>

    <div v-else-if="rows.length === 0" class="signature-history__state">
      <p v-if="hasActiveFilters">{{ t('page.signatures.history.emptyFiltered') }}</p>
      <p v-else>{{ t('page.signatures.history.empty') }}</p>
      <Button
        v-if="hasActiveFilters"
        variant="outline"
        size="sm"
        type="button"
        @click="clearFilters"
      >
        {{ t('page.signatures.history.filter.clear') }}
      </Button>
    </div>

    <template v-else>
      <div class="signature-history__table-wrapper">
        <Table
          class="min-w-[80rem]"
          :aria-label="t('page.signatures.history.title')"
        >
          <TableHeader>
            <TableRow>
              <TableHead>{{ t('page.signatures.history.columns.date') }}</TableHead>
              <TableHead>{{ t('page.signatures.history.columns.time') }}</TableHead>
              <TableHead>{{ t('page.signatures.history.columns.field') }}</TableHead>
              <TableHead>{{ t('page.signatures.history.columns.action') }}</TableHead>
              <TableHead>{{ t('page.signatures.history.columns.previous') }}</TableHead>
              <TableHead>{{ t('page.signatures.history.columns.new') }}</TableHead>
              <TableHead>{{ t('page.signatures.history.columns.actor') }}</TableHead>
              <TableHead>{{ t('page.signatures.history.columns.approvers') }}</TableHead>
              <TableHead>
                <span class="sr-only">{{ t('page.signatures.history.columns.actions') }}</span>
              </TableHead>
            </TableRow>
          </TableHeader>
          <TableBody>
            <!-- eslint-disable-next-line vue/no-restricted-syntax -- the View button in the row opens the same detail -->
            <TableRow
              v-for="row in rows"
              :key="row.id"
              class="signature-history__row"
              @click="openDetail(row)"
            >
              <TableCell class="signature-history__cell--nowrap">
                {{ formatDate(row.occurredAt) }}
              </TableCell>
              <TableCell class="signature-history__cell--nowrap signature-history__cell--muted">
                {{ formatTime(row.occurredAt) }}
              </TableCell>
              <TableCell class="signature-history__cell--nowrap">
                {{ fieldLabel(row) }}
              </TableCell>
              <TableCell>
                <Badge variant="outline" :class="actionBadgeClass(row.action)">
                  {{ actionLabel(row.action) }}
                </Badge>
              </TableCell>
              <TableCell class="signature-history__value">
                {{ previewValue(row.previousValue, 'previous') }}
              </TableCell>
              <TableCell class="signature-history__value">
                {{ previewValue(row.newValue, 'new') }}
              </TableCell>
              <TableCell class="signature-history__cell--nowrap">
                {{ actorLabel(row) }}
              </TableCell>
              <TableCell>
                <template v-if="row.approvers.length === 0">
                  {{ t('page.signatures.history.noApprovers') }}
                </template>
                <div v-else class="signature-history__approver-list">
                  <Badge
                    v-for="approver in approverPreview(row).visible"
                    :key="approver.userId"
                    variant="outline"
                  >
                    {{ approver.email ?? t('page.signatures.history.unknownActor') }}
                  </Badge>
                  <Badge
                    v-if="approverPreview(row).overflow > 0"
                    variant="outline"
                    class="signature-history__approver-overflow"
                    :aria-label="t('page.signatures.history.moreApprovers', { count: approverPreview(row).overflow }, approverPreview(row).overflow)"
                  >
                    +{{ approverPreview(row).overflow }}
                  </Badge>
                </div>
              </TableCell>
              <TableCell class="signature-history__cell--nowrap">
                <Button
                  variant="outline"
                  size="sm"
                  type="button"
                  :aria-label="t('page.signatures.history.row.openDetail', { field: fieldLabel(row), date: formatDate(row.occurredAt) })"
                  @click.stop="openDetail(row)"
                >
                  {{ t('page.signatures.history.row.view') }}
                </Button>
              </TableCell>
            </TableRow>
          </TableBody>
        </Table>
      </div>

      <footer class="signature-history__pagination">
        <span>
          {{
            t('page.signatures.history.pagination.showing', {
              from: pageFrom,
              to: pageTo,
              total: totalCount,
            })
          }}
        </span>
        <div class="signature-history__pagination-controls">
          <Button
            variant="outline"
            size="sm"
            type="button"
            :disabled="!canPrev"
            @click="prevPage"
          >
            {{ t('page.signatures.history.pagination.previous') }}
          </Button>
          <Button
            variant="outline"
            size="sm"
            type="button"
            :disabled="!canNext"
            @click="nextPage"
          >
            {{ t('page.signatures.history.pagination.next') }}
          </Button>
        </div>
      </footer>
    </template>

    <Dialog v-model:open="detailOpen">
      <!-- Tailwind utilities (not scoped CSS) because DialogScrollContent
           teleports to document.body — the component's scoped data-attribute
           never reaches the rendered element. -->
      <DialogScrollContent class="!max-w-5xl !w-[90vw]">
        <template v-if="detailRow">
          <DialogHeader>
            <DialogTitle>{{ fieldLabel(detailRow) }}</DialogTitle>
            <DialogDescription>
              {{ formatDateTime(detailRow.occurredAt) }} ·
              <Badge variant="outline" :class="actionBadgeClass(detailRow.action)">
                {{ actionLabel(detailRow.action) }}
              </Badge>
            </DialogDescription>
          </DialogHeader>

          <div class="signature-history__detail-meta">
            <div class="signature-history__detail-meta-item">
              <strong>{{ t('page.signatures.history.columns.actor') }}:</strong>
              <Badge variant="outline">{{ actorLabel(detailRow) }}</Badge>
            </div>
            <div class="signature-history__detail-meta-item">
              <strong>{{ t('page.signatures.history.columns.approvers') }}:</strong>
              <span v-if="detailRow.approvers.length === 0">
                {{ t('page.signatures.history.noApprovers') }}
              </span>
              <div v-else class="signature-history__approver-list">
                <Badge
                  v-for="approver in detailRow.approvers"
                  :key="approver.userId"
                  variant="outline"
                >
                  {{ approver.email ?? t('page.signatures.history.unknownActor') }}
                </Badge>
              </div>
            </div>
          </div>

          <div class="signature-history__detail-body">
            <section>
              <h3>{{ t('page.signatures.history.columns.previous') }}</h3>
              <pre v-if="detailRow.previousValue !== null">{{ detailRow.previousValue }}</pre>
              <p v-else class="signature-history__detail-empty">
                {{ nullValueLabel('previous') }}
              </p>
            </section>
            <section>
              <h3>{{ t('page.signatures.history.columns.new') }}</h3>
              <pre v-if="detailRow.newValue !== null">{{ detailRow.newValue }}</pre>
              <p v-else class="signature-history__detail-empty">
                {{ nullValueLabel('new') }}
              </p>
            </section>
          </div>
        </template>
      </DialogScrollContent>
    </Dialog>
  </section>
</template>

<style scoped>
/* Layout uses the same design tokens (`--spacing-*`, `--bg-*`, `--stroke`,
   `--text-*`, `--radius-*`) as SignatureSettings and the other list views
   in the app so dark mode and theme tweaks flow through automatically. */
.signature-history {
  display: flex;
  flex-direction: column;
  gap: var(--spacing-md);
  /* Match the editor header's horizontal padding so the title, filters, and
     table align with the workflow title above rather than hugging the
     viewport edge. */
  padding: var(--spacing-md) var(--spacing-xl) var(--spacing-lg);
}

.signature-history__header {
  display: flex;
  flex-direction: column;
  gap: var(--spacing-xs);
}

.signature-history__filters {
  display: flex;
  flex-wrap: wrap;
  gap: var(--spacing-sm);
  align-items: end;
}

.signature-history__filter {
  display: flex;
  flex-direction: column;
  gap: var(--spacing-xs);
  min-width: 12rem;
}

/* Filter labels match the `.filter-label` pattern used in WorkflowExecutionLogs:
   regular-case body text in the muted-foreground colour, not the all-caps
   micro-label style. Keeps the page visually consistent with the rest of the
   product. */
.signature-history__filter label {
  font-size: 14px;
  color: var(--text-secondary);
}

.signature-history__state {
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: var(--spacing-sm);
  padding: var(--spacing-lg);
  text-align: center;
  color: var(--text-secondary);
}

.signature-history__state--error {
  color: var(--accent-error);
}

.signature-history__state p {
  margin: 0;
}

/* Flex+gap so wrapped approver badges get real vertical breathing room.
   The previous adjacent-sibling `margin-left` rule only spaced badges on
   the same line and left wrapped lines effectively overlapping because
   line-height equalled the badge height. */
.signature-history__approver-list {
  display: flex;
  flex-wrap: wrap;
  gap: var(--spacing-xs);
}

/* "+N more" overflow badge — slightly muted so it visually subordinates to
   the real approver chips next to it. Click the row to see the full list in
   the detail dialog. */
.signature-history__approver-overflow {
  background: var(--bg-1);
  color: var(--text-secondary);
}

.signature-history__row {
  cursor: pointer;
}

.signature-history__cell--nowrap {
  white-space: nowrap;
}

/* The time-of-day column reads as secondary metadata next to the date —
   muted colour de-emphasises it without losing the information entirely. */
.signature-history__cell--muted {
  color: var(--text-secondary);
}

.signature-history__value {
  /* One-line preview in the table — the full text lives in the row-detail
     dialog. Constrains column width so a long prompt can't push the rest of
     the table off-screen, while keeping every row the same height for a
     scannable layout. */
  max-width: 24rem;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.signature-history__detail-meta {
  display: flex;
  flex-wrap: wrap;
  gap: var(--spacing-sm) var(--spacing-md);
  padding: var(--spacing-sm) 0;
  border-bottom: 1px solid var(--stroke);
  font-size: 14px;
  color: var(--text-primary);
}

/* Each meta item is its own flex row so the label → value gap is identical
   for both "Proposed by" (text value) and "Approved by" (chip list). Aligning
   on the first baseline keeps badges visually flush with the colon above. */
.signature-history__detail-meta-item {
  display: flex;
  align-items: baseline;
  gap: var(--spacing-xs);
}

.signature-history__detail-meta-item strong {
  font-weight: 600;
  color: var(--text-secondary);
}

.signature-history__detail-body {
  display: grid;
  grid-template-columns: 1fr 1fr;
  gap: var(--spacing-md);
  padding-top: var(--spacing-sm);
}

/* Side-by-side Previous/New stacks on narrow viewports — at 360px wide each
   <pre> would otherwise compress to ~150px and triple-nest scroll inside
   the already-scrollable dialog. Single column keeps the comparison
   linear-readable on phones. */
@media (max-width: 640px) {
  .signature-history__detail-body {
    grid-template-columns: 1fr;
  }
}

.signature-history__detail-body h3 {
  font-size: 12px;
  text-transform: uppercase;
  letter-spacing: 0.04em;
  color: var(--text-muted);
  margin: 0 0 var(--spacing-xs);
}

.signature-history__detail-body pre {
  margin: 0;
  padding: var(--spacing-sm);
  background: var(--bg-1);
  border: 1px solid var(--stroke);
  border-radius: var(--radius-small);
  /* `--font-mono` isn't defined globally — keep an explicit stack so the
     code block stays readable on every platform. */
  font-family: ui-monospace, SFMono-Regular, Menlo, Monaco, Consolas, monospace;
  font-size: 13px;
  line-height: 1.5;
  color: var(--text-primary);
  white-space: pre-wrap;
  word-break: break-word;
  max-height: 24rem;
  overflow-y: auto;
}

.signature-history__detail-empty {
  margin: 0;
  padding: var(--spacing-sm);
  color: var(--text-muted);
  font-style: italic;
}

/* Border-top + larger padding match the section-separation pattern other
   list views (WorkflowExecutionLogs, DocumentsList) use under their tables,
   so the History page reads as one of the family. */
.signature-history__pagination {
  display: flex;
  justify-content: space-between;
  align-items: center;
  padding: var(--spacing-sm) 0 0;
  margin-top: var(--spacing-xs);
  border-top: 1px solid var(--stroke);
  font-size: 14px;
  color: var(--text-secondary);
}

.signature-history__pagination-controls {
  display: flex;
  gap: var(--spacing-xs);
}

/* Action-state badges — paint with the app's accent tokens (success/warning/
   info) so the colours line up with Toasts and other status surfaces. The
   shadcn Badge default variants were either too muted (`secondary`) or too
   loud (`destructive`) for an audit log. The `outline` variant the Badge is
   used with already includes a 1px solid border, so we only need to set the
   colours here. */
.signature-history__action-badge--created {
  background: var(--accent-success-light);
  border-color: var(--accent-success-border);
  color: var(--accent-success);
}

.signature-history__action-badge--updated {
  background: var(--accent-warning-light);
  border-color: var(--accent-warning-border);
  color: var(--accent-warning);
}

.signature-history__action-badge--removed {
  background: var(--accent-info-light);
  border-color: var(--accent-info-border);
  color: var(--accent-info);
}

/* Renamed = a staff rename carried the override to a new name; the value is
   unchanged (previous === new), so paint it neutrally rather than with a
   value-transition accent. */
.signature-history__action-badge--renamed {
  background: var(--bg-2);
  border-color: var(--stroke);
  color: var(--text-secondary);
}

.signature-history__table-wrapper :deep(.overflow-auto) {
  scrollbar-gutter: stable;
  scrollbar-color: var(--stroke-hover) transparent;
  scrollbar-width: thin;
}
.signature-history__table-wrapper :deep(.overflow-auto)::-webkit-scrollbar {
  height: 10px;
  -webkit-appearance: none;
}
.signature-history__table-wrapper :deep(.overflow-auto)::-webkit-scrollbar-thumb {
  background: var(--stroke-hover);
  border-radius: var(--radius-small);
}
.signature-history__table-wrapper :deep(.overflow-auto)::-webkit-scrollbar-thumb:hover {
  background: var(--text-muted);
}
.signature-history__table-wrapper :deep(.overflow-auto)::-webkit-scrollbar-track {
  background: transparent;
}
</style>
