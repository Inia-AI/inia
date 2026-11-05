<script setup lang="ts">
import { computed, ref, shallowRef, useId, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import type { Row } from '@tanstack/vue-table'

import { IniaIcon, IniaLoader } from '@/components/atoms'
import { ImportCounterpartiesDialog, MultiSelect } from '@/components/molecules'
import { DataTable, CounterpartyForm } from '@/components/organisms'
import {
  Dialog,
  DialogContent,
  DialogHeader,
  DialogTitle,
} from '@/components/ui/dialog'
import { Button } from '@/components/ui/button'
import { useCounterpartyTable } from '@/tables'
import type { CounterpartyRow } from '@/tables/counterparty'
import { useToast } from '@/components/ui/toast/use-toast'
import { useDebouncedRef } from '@/composables/useDebounce'
import { useCounterpartiesQueryState } from '@/composables/useCounterpartiesQueryState'
import { useCurrentUserRoles } from '@/composables/useCurrentUserRoles'
import { useBundleDownload } from '@/composables/useBundleDownload'
import {
  MATCH_QUERY_CAP,
  buildChildrenOfRootsQuery,
  buildContextParentsQuery,
  buildHasChildrenProbe,
  buildMatchQuery,
  buildRootsQuery,
  isFiltering,
} from '@/lib/counterpartiesQuery'
import {
  countTopLevelGroups,
  groupMatchesIntoTree,
} from '@/lib/counterpartiesTree'
import API from '@/api'
import { extractApiErrorMessage } from '@/api/base/errors'
import type {
  Counterparty,
  CounterpartyBundle,
  CreateCounterpartyRequest,
} from '@/api/schema'
import { OptionType } from '@/types'
import { DIALOG_TYPE } from '@/enums'

const searchFieldId = useId()

const { t: $t } = useI18n()
const { toast } = useToast()
const { isAdmin, isEditor } = useCurrentUserRoles()

const { state, patchState } = useCounterpartiesQueryState()

const searchInput = ref(state.value.search)
watch(
  () => state.value.search,
  v => {
    if (v !== searchInput.value) searchInput.value = v
  },
)
const searchDebounced = useDebouncedRef(searchInput, 300)

const onSearchInput = (event: Event) => {
  const value = (event.target as HTMLInputElement).value
  searchInput.value = value
  void patchState({ search: value, page: 0 })
}

const typeIdsModel = computed<string[]>({
  get: () => state.value.typeIds,
  set: v => {
    void patchState({ typeIds: v, page: 0 })
  },
})

const clearFilters = () => {
  searchInput.value = ''
  void patchState({ search: '', typeIds: [], page: 0 })
}

const effectiveState = computed(() => ({
  ...state.value,
  search: searchDebounced.value,
}))
const filtering = computed(() => isFiltering(effectiveState.value))

const counterpartyTypeParams = ref({ $top: 10000, $orderby: 'name asc' })
const { data: counterpartyTypes } =
  API.CounterpartyTypes.useGetCounterpartyTypes(counterpartyTypeParams)

const typeOptions = computed<OptionType[]>(() =>
  (counterpartyTypes?.value?.value ?? []).map(t => ({
    label: t.name,
    value: t.id,
  })),
)

const typeNameById = computed<Map<string, string>>(() => {
  const map = new Map<string, string>()
  for (const t of counterpartyTypes.value?.value ?? []) map.set(t.id, t.name)
  return map
})

const rootsParams = computed(() => buildRootsQuery(effectiveState.value))
const { data: rootsData, isLoading: rootsLoading } =
  API.Counterparties.useGetCounterparties(rootsParams, {
    enabled: computed(() => !filtering.value),
  })

const visibleRootIds = computed(() =>
  (rootsData.value?.value ?? []).map(r => r.id),
)
const hasChildrenParams = computed(() =>
  buildHasChildrenProbe(visibleRootIds.value),
)
const { data: hasChildrenData } = API.Counterparties.useGetCounterparties(
  hasChildrenParams,
  {
    enabled: computed(
      () => !filtering.value && visibleRootIds.value.length > 0,
    ),
  },
)
const counterpartiesWithChild = computed<string[] | undefined>(() => {
  const rows = hasChildrenData.value?.value ?? []
  const set = new Set<string>()
  for (const r of rows) if (r.parentId) set.add(r.parentId)
  return Array.from(set)
})

const expandedRowIds = ref(new Set<string>())
watch(filtering, on => {
  if (on) expandedRowIds.value = new Set()
})
watch(
  () => state.value.orderBy,
  () => {
    expandedRowIds.value = new Set()
  },
)

const expandedVisibleIds = computed(() =>
  visibleRootIds.value.filter(id => expandedRowIds.value.has(id)),
)
const expandedChildrenParams = computed(() =>
  buildChildrenOfRootsQuery(expandedVisibleIds.value),
)
const { data: expandedChildrenData } = API.Counterparties.useGetCounterparties(
  expandedChildrenParams,
  {
    enabled: computed(
      () => !filtering.value && expandedVisibleIds.value.length > 0,
    ),
  },
)

const matchParams = computed(() => buildMatchQuery(effectiveState.value))
const { data: matchData, isLoading: matchLoading } =
  API.Counterparties.useGetCounterparties(matchParams, {
    enabled: filtering,
  })

const matches = computed<Counterparty[]>(
  () => matchData.value?.value ?? [],
)
const matchedRootIds = computed(() => {
  const ids: string[] = []
  for (const m of matches.value) if (m.parentId == null) ids.push(m.id)
  return ids
})
const contextParentIds = computed(() => {
  const matchedRoots = new Set(matchedRootIds.value)
  const set = new Set<string>()
  for (const m of matches.value) {
    if (m.parentId && !matchedRoots.has(m.parentId)) set.add(m.parentId)
  }
  return Array.from(set)
})

const contextParentsParams = computed(() =>
  buildContextParentsQuery(contextParentIds.value),
)
const { data: contextParentsData } = API.Counterparties.useGetCounterparties(
  contextParentsParams,
  {
    enabled: computed(
      () => filtering.value && contextParentIds.value.length > 0,
    ),
  },
)

const childrenOfMatchedRootsParams = computed(() =>
  buildChildrenOfRootsQuery(matchedRootIds.value),
)
const { data: childrenOfMatchedRootsData } =
  API.Counterparties.useGetCounterparties(childrenOfMatchedRootsParams, {
    enabled: computed(
      () => filtering.value && matchedRootIds.value.length > 0,
    ),
  })

type ResolvedRow = CounterpartyRow

const enrichRow = (
  cp: Counterparty,
  opts: {
    searchQuery?: string
    isContextParent?: boolean
    parentName?: string
  } = {},
): ResolvedRow => {
  const typeMap = typeNameById.value
  const typeNames: string[] = []
  for (const id of cp.counterpartyTypeIds ?? []) {
    const name = typeMap.get(id)
    if (name) typeNames.push(name)
  }
  return {
    ...cp,
    type: typeNames.join(', '),
    searchQuery: opts.searchQuery ?? '',
    isContextParent: opts.isContextParent ?? false,
    parentName: opts.parentName,
  }
}

const displayRows = computed<ResolvedRow[]>(() => {
  if (filtering.value) {
    const groups = groupMatchesIntoTree({
      matches: matches.value,
      contextParents: contextParentsData.value?.value ?? [],
      childrenOfMatchedRoots: childrenOfMatchedRootsData.value?.value ?? [],
      orderBy: effectiveState.value.orderBy,
      page: effectiveState.value.page,
      pageSize: effectiveState.value.pageSize,
    })
    const out: ResolvedRow[] = []
    for (const g of groups) {
      out.push(
        enrichRow(g.root, {
          searchQuery: effectiveState.value.search,
          isContextParent: g.kind === 'context-parent',
        }),
      )
      for (const child of g.children) {
        const highlight =
          g.kind === 'matched-root' ? g.matchedChildIds.has(child.id) : true
        out.push(
          enrichRow(child, {
            searchQuery: highlight ? effectiveState.value.search : '',
            parentName: g.root.name,
          }),
        )
      }
    }
    return out
  }

  const roots = rootsData.value?.value ?? []
  const childRows = expandedChildrenData.value?.value ?? []
  const out: ResolvedRow[] = []
  for (const root of roots) {
    out.push(enrichRow(root))
    if (expandedRowIds.value.has(root.id)) {
      const kids = childRows.filter(c => c.parentId === root.id)
      for (const kid of kids) out.push(enrichRow(kid))
    }
  }
  return out
})

const totalCount = computed(() => {
  if (filtering.value) {
    return countTopLevelGroups({
      matches: matches.value,
      contextParents: contextParentsData.value?.value ?? [],
    })
  }
  return rootsData.value?.count ?? 0
})

const rawMatchCount = computed(() => matchData.value?.count ?? 0)
const showRefineHint = computed(
  () => filtering.value && rawMatchCount.value >= MATCH_QUERY_CAP,
)

const rowClasses = computed(() => {
  const out: Record<string, string> = {}
  for (const row of displayRows.value) {
    const classes: string[] = []
    if (row.parentId) classes.push('child-row')
    if (row.isContextParent) classes.push('context-parent-row')
    if (classes.length) out[row.id] = classes.join(' ')
  }
  return out
})

const isLoading = computed(() =>
  filtering.value ? matchLoading.value : rootsLoading.value,
)

const showDialog = ref(false)
const dialogType = ref<string>('')
const selectedCounterparty = ref<Counterparty | null>(null)

const { mutateAsync: createCounterparty } =
  API.Counterparties.useCreateCounterparty()
const { mutateAsync: getCounterpartyDetails } =
  API.Counterparties.useGetCounterparty()
const { mutateAsync: updateCounterparty } =
  API.Counterparties.updateCounterparty()
const { mutateAsync: deleteCounterparty } =
  API.Counterparties.deleteCounterparty()

const onCreate = () => {
  dialogType.value = DIALOG_TYPE.ADD
  showDialog.value = true
}

const onPageChange = (newPage: number) => {
  void patchState({ page: newPage })
}

const onOrderByChange = (newOrderBy: string) => {
  void patchState({ orderBy: newOrderBy, page: 0 })
}

const counterpartyInitialValues = computed(() => {
  return dialogType.value === DIALOG_TYPE.EDIT
    ? { ...selectedCounterparty.value }
    : null
})

const resetState = () => {
  dialogType.value = ''
  selectedCounterparty.value = null
  showDialog.value = false
}

const notifySuccess = (key: 'addSuccess' | 'editSuccess' | 'deleteSuccess', name: string) => {
  toast({
    class: 'text-sm',
    description: $t(`page.counterpartyManagement.toast.${key}`, { name }),
    duration: 2000,
  })
}

const notifyError = (error: unknown, fallbackKey: 'saveError' | 'deleteError') => {
  toast({
    class: 'text-sm',
    variant: 'destructive',
    description: extractApiErrorMessage(
      error,
      $t(`page.counterpartyManagement.toast.${fallbackKey}`),
    ),
    duration: 3000,
  })
}

const handleSubmit = async (data: CreateCounterpartyRequest) => {
  const selectedId = selectedCounterparty.value?.id
  try {
    switch (dialogType.value) {
      case DIALOG_TYPE.ADD:
        await createCounterparty(data)
        notifySuccess('addSuccess', data?.name ?? '')
        break

      case DIALOG_TYPE.ADD_CHILD:
        if (!selectedId) {
          throw new Error('No parent counterparty selected')
        }
        await createCounterparty({
          ...data,
          parentId: selectedId,
        })
        notifySuccess('addSuccess', data?.name ?? '')
        break

      case DIALOG_TYPE.EDIT:
        if (!selectedId) {
          throw new Error('No counterparty selected')
        }
        await updateCounterparty({
          id: selectedId,
          params: { ...selectedCounterparty.value, ...data },
        })
        notifySuccess('editSuccess', data?.name ?? '')
        break

      default:
        break
    }
  } catch (error) {
    console.error('Failed to save counterparty:', error)
    notifyError(error, 'saveError')
    return
  }

  resetState()
}

const handleDelete = async () => {
  const name = selectedCounterparty.value?.name ?? ''
  try {
    const selectedId = selectedCounterparty.value?.id
    if (!selectedId) {
      throw new Error('No counterparty selected')
    }
    await deleteCounterparty(selectedId)
  } catch (error) {
    console.error('Failed to delete counterparty:', error)
    notifyError(error, 'deleteError')
    return
  }
  resetState()
  notifySuccess('deleteSuccess', name)
}

const { columns } = useCounterpartyTable({
  onEdit: async row => {
    const details = await getCounterpartyDetails(row?.original?.id)
    dialogType.value = DIALOG_TYPE.EDIT
    showDialog.value = true
    selectedCounterparty.value = details
  },
  onAdd: row => {
    dialogType.value = DIALOG_TYPE.ADD_CHILD
    showDialog.value = true
    selectedCounterparty.value = row?.original
  },
  onDelete: row => {
    dialogType.value = DIALOG_TYPE.DELETE
    showDialog.value = true
    selectedCounterparty.value = row?.original
  },
  toggleRow: (row: Row<CounterpartyRow>) => {
    const next = new Set(expandedRowIds.value)
    if (next.has(row.original.id)) next.delete(row.original.id)
    else next.add(row.original.id)
    expandedRowIds.value = next
  },
  expandedRowIds: {
    has: (id: string) => expandedRowIds.value.has(id),
  },
  counterpartiesWithChild,
})

const dialogTitle = computed(() => {
  switch (dialogType.value) {
    case DIALOG_TYPE.ADD:
      return $t('page.counterpartyManagement.dialog.createTitle')
    case DIALOG_TYPE.ADD_CHILD:
      return $t('page.counterpartyManagement.dialog.createChildTitle')
    case DIALOG_TYPE.EDIT:
      return $t('page.counterpartyManagement.dialog.editTitle')
    case DIALOG_TYPE.DELETE:
      return $t('page.counterpartyManagement.dialog.deleteTitle')
    default:
      return ''
  }
})

const showEmptyNoMatches = computed(
  () => !isLoading.value && filtering.value && displayRows.value.length === 0,
)
const showEmptyNoData = computed(
  () => !isLoading.value && !filtering.value && displayRows.value.length === 0,
)

const exportBundle = useBundleDownload(
  API.Counterparties.useExportBundle(),
  'counterparties.json',
  'page.counterpartyManagement.exportError',
)
const exportCounterparties = () => exportBundle.download()

const importInput = ref<HTMLInputElement | null>(null)
const pendingBundle = shallowRef<CounterpartyBundle | null>(null)

const looksLikeBundle = (parsed: unknown): parsed is CounterpartyBundle => {
  const candidate = parsed as Partial<CounterpartyBundle> | null
  return (
    typeof candidate === 'object' &&
    candidate !== null &&
    typeof candidate.bundleFormatVersion === 'number' &&
    Array.isArray(candidate.counterpartyTypes) &&
    Array.isArray(candidate.counterparties)
  )
}

const onImportPicked = async (event: Event) => {
  const file = (event.target as HTMLInputElement).files?.[0]
  if (importInput.value) {
    importInput.value.value = ''
  }
  if (!file) {
    return
  }
  let parsed: unknown
  try {
    parsed = JSON.parse(await file.text())
  } catch {
    parsed = null
  }
  if (!looksLikeBundle(parsed)) {
    toast({
      class: 'text-sm',
      variant: 'destructive',
      description: $t('page.counterpartyManagement.importNotABundle', {
        name: file.name,
      }),
      duration: 3000,
    })
    return
  }
  pendingBundle.value = parsed
}
</script>

<template>
  <div class="page-view">
    <div class="page-header-with-action">
      <div>
        <h1 class="page-title">
          {{ $t('page.counterpartyManagement.title') }}
        </h1>
        <p class="page-subtitle">
          {{ $t('page.counterpartyManagement.count', { count: totalCount }, totalCount) }}
        </p>
      </div>
      <div class="header-actions">
        <template v-if="isAdmin">
          <Button
            variant="outline"
            data-testid="counterparties-export"
            :disabled="exportBundle.isPending.value"
            @click="exportCounterparties"
          >
            <IniaIcon name="download" class="mr-2" />
            {{ $t('page.counterpartyManagement.export') }}
          </Button>
          <Button
            variant="outline"
            data-testid="counterparties-import"
            @click="importInput?.click()"
          >
            <IniaIcon name="upload" class="mr-2" />
            {{ $t('page.counterpartyManagement.import') }}
          </Button>
          <input
            ref="importInput"
            type="file"
            accept=".json,application/json"
            class="hidden"
            :aria-label="$t('page.counterpartyManagement.import')"
            data-testid="counterparties-import-input"
            @change="onImportPicked"
          />
        </template>
        <Button v-if="isEditor" variant="outline" as-child>
          <RouterLink to="/counterparty-types" data-testid="manage-counterparty-types">
            <IniaIcon name="tags" class="mr-2" />
            {{ $t('page.counterpartyManagement.manageTypes') }}
          </RouterLink>
        </Button>
        <Button class="btn-gradient-primary" @click="onCreate">{{
          $t('page.counterpartyManagement.createCounterpartyLabel')
        }}</Button>
      </div>
    </div>

    <div class="filters-section">
      <div class="filter-field filter-field--wide">
        <label :for="searchFieldId" class="filter-label">{{
          $t('page.counterpartyManagement.filters.search')
        }}</label>
        <input
          :id="searchFieldId"
          type="search"
          class="filter-input"
          :value="searchInput"
          :placeholder="
            $t('page.counterpartyManagement.filters.searchPlaceholder')
          "
          @input="onSearchInput"
        />
      </div>
      <div class="filter-field">
        <span class="filter-label">{{
          $t('page.counterpartyManagement.filters.type')
        }}</span>
        <MultiSelect
          v-model="typeIdsModel"
          :options="typeOptions"
          :aria-label="$t('page.counterpartyManagement.filters.type')"
          :placeholder="$t('page.counterpartyManagement.filters.all')"
          :max-badges="1"
        />
      </div>
    </div>

    <div v-if="showRefineHint" class="refine-banner">
      {{
        $t('page.counterpartyManagement.refineHint', { cap: MATCH_QUERY_CAP })
      }}
    </div>

    <div class="card-panel table-container">
      <DataTable
        v-if="displayRows.length"
        :columns="columns"
        :data="displayRows"
        :page-size="state.pageSize"
        :total-count="totalCount"
        :page="state.page"
        :order-by="state.orderBy"
        :row-classes="rowClasses"
        @update:page="onPageChange"
        @update:order-by="onOrderByChange"
      />
      <div v-else-if="showEmptyNoMatches" class="empty-state">
        {{ $t('page.counterpartyManagement.emptyNoMatches') }}
        <Button
          variant="link"
          class="clear-filters-button"
          @click="clearFilters"
          >{{
            $t('page.counterpartyManagement.filters.clearFilters')
          }}</Button
        >
      </div>
      <div v-else-if="showEmptyNoData" class="empty-state">
        {{ $t('page.counterpartyManagement.emptyNoData') }}
      </div>
      <div v-if="isLoading" class="loading-overlay">
        <IniaLoader class="w-8" />
      </div>
    </div>

    <Dialog v-model:open="showDialog as boolean">
      <DialogContent class="flex max-h-[90dvh] flex-col overflow-hidden">
        <DialogHeader>
          <DialogTitle>{{ dialogTitle }}</DialogTitle>
        </DialogHeader>

        <div v-if="dialogType === DIALOG_TYPE.DELETE">
          <p>
            {{
              $t('page.counterpartyManagement.dialog.deleteConfirm', {
                name: selectedCounterparty?.name ?? '',
              })
            }}
          </p>

          <div class="flex justify-end gap-4 mt-4">
            <Button variant="outline" @click="resetState">
              {{ $t('page.counterpartyManagement.form.cancelLabel') }}
            </Button>
            <Button type="submit" variant="soft" @click="handleDelete">
              {{ $t('page.counterpartyManagement.form.submitLabel') }}
            </Button>
          </div>
        </div>

        <CounterpartyForm
          v-else
          :counterparty-types="typeOptions"
          :initial-values="counterpartyInitialValues"
          @submit="handleSubmit"
          @cancel="resetState"
        />
      </DialogContent>
    </Dialog>

    <ImportCounterpartiesDialog
      :bundle="pendingBundle"
      @close="pendingBundle = null"
    />
  </div>
</template>

<style scoped>
@reference "tailwindcss";

.page-subtitle {
  margin-top: var(--spacing-xs);
  font-size: 14px;
  color: var(--text-secondary);
}

.header-actions {
  display: flex;
  gap: var(--spacing-sm);
}

/* Toolbar — mirrors DocumentsList */
.filters-section {
  display: flex;
  gap: var(--spacing-sm);
  margin-bottom: var(--spacing-lg);
  align-items: flex-end;
}

.filter-field {
  display: flex;
  flex-direction: column;
  gap: var(--spacing-xs);
  flex: 0 0 220px;
}

.filter-field--wide {
  flex: 1 1 320px;
}

.filter-field :deep(button) {
  width: 100%;
}

.filter-label {
  font-size: 0.75rem;
  font-weight: 500;
  color: var(--text-secondary);
  text-transform: uppercase;
  letter-spacing: 0.04em;
}

.filter-input {
  width: 100%;
  height: 36px;
  padding: 0 12px;
  background: var(--bg-1);
  border: 1px solid var(--stroke);
  border-radius: var(--radius-button);
  color: var(--text-primary);
  font-size: 14px;
  transition: border-color 0.15s ease-in-out;
}
.filter-input:hover {
  border-color: var(--stroke-hover);
}
.filter-input:focus {
  outline: none;
  border-color: var(--accent-indigo-from);
}

@media (max-width: 768px) {
  .filters-section {
    flex-wrap: wrap;
  }
  .filter-field {
    flex: 1 1 150px;
  }
  .filter-field--wide {
    flex-basis: 100%;
  }
}

/* "Refine your search" banner */
.refine-banner {
  margin-bottom: var(--spacing-md);
  padding: var(--spacing-sm) var(--spacing-md);
  background: var(--accent-warning-light);
  border: 1px solid var(--accent-warning-border);
  border-radius: var(--radius-button);
  color: var(--text-primary);
  font-size: 13px;
}

.clear-filters-button {
  margin-left: var(--spacing-xs);
  color: var(--accent-indigo-from);
}

/* ---------- ID column ---------- */
.table-container :deep(.id-cell) {
  display: inline-flex;
  align-items: center;
  gap: var(--spacing-xs);
  font-family: ui-monospace, SFMono-Regular, Menlo, monospace;
  font-size: 12px;
  color: var(--text-secondary);
}
.table-container :deep(.id-cell-value) {
  white-space: nowrap;
}
.table-container :deep(.expand-chevron) {
  color: var(--text-secondary);
  width: 12px;
}
.table-container :deep(.expand-chevron-spacer) {
  display: inline-block;
  width: 12px;
}
.table-container :deep(.id-copy) {
  color: var(--text-secondary);
  opacity: 0;
  transition: opacity 0.15s ease-in-out, color 0.15s ease-in-out;
}
.table-container :deep(.id-cell:hover .id-copy),
.table-container :deep(.id-copy:focus-visible) {
  opacity: 1;
}
.table-container :deep(.id-copy:hover) {
  color: var(--accent-indigo-to);
}

/* ---------- Name column ---------- */
.table-container :deep(.name-cell) {
  display: flex;
  flex-direction: column;
  gap: 2px;
  min-width: 0;
}
.table-container :deep(.name-cell-parent) {
  font-size: 12px;
  color: var(--text-secondary);
}

/* ---------- Action icon buttons ---------- */
.table-container :deep(.table-action-button) {
  color: var(--text-secondary);
  transition: filter 0.15s ease-in-out, color 0.15s ease-in-out;
}
.table-container :deep(.action-icon-edit) {
  color: var(--accent-indigo-to);
}
.table-container :deep(.action-icon-add) {
  color: var(--accent-success);
}
.table-container :deep(.action-icon-delete) {
  color: var(--accent-error);
}
.table-container :deep(.action-icon-edit:hover),
.table-container :deep(.action-icon-add:hover),
.table-container :deep(.action-icon-delete:hover) {
  filter: brightness(1.15);
}

/* Visually-hidden text for screen readers (e.g. context-parent row label). */
.table-container :deep(.sr-only) {
  position: absolute;
  width: 1px;
  height: 1px;
  padding: 0;
  margin: -1px;
  overflow: hidden;
  clip: rect(0, 0, 0, 0);
  white-space: nowrap;
  border: 0;
}

/* ---------- Child row indent + accent border (replaces bg-sky-900) ---------- */
.table-container :deep(.child-row td:first-child) {
  border-left: 2px solid var(--accent-info-border);
}
.table-container :deep(.child-row td:first-child > *) {
  padding-left: 16px;
}

/* ---------- Context-parent row: dim, parent visible only for context ---------- */
.table-container :deep(.context-parent-row) {
  color: var(--text-muted);
}
.table-container :deep(.context-parent-row:hover) {
  color: inherit;
}
</style>
