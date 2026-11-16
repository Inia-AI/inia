<script setup lang="ts">
import { computed, ref, useId, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import type { Row } from '@tanstack/vue-table'

import API from '@/api'
import { IniaLoader, IniaIcon } from '@/components/atoms'
import {
  EditDocumentDialog,
  FilePreviewDialog,
  IniaSelect,
  UploadDocumentsDialog,
} from '@/components/molecules'
import { DataTable } from '@/components/organisms'
import { Button } from '@/components/ui/button'
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from '@/components/ui/dialog'
import { useToast } from '@/components/ui/toast/use-toast'
import { useCurrentUserId } from '@/composables/useCurrentUserId'
import { useCurrentUserRoles } from '@/composables/useCurrentUserRoles'
import { useDebouncedRef } from '@/composables/useDebounce'
import { useDocumentsQueryState } from '@/composables/useDocumentsQueryState'
import { useWorkflowTypeNames } from '@/composables/useWorkflowTypeNames'
import { buildDocumentsQuery } from '@/lib/documentsQuery'
import { documentDetailsRoute } from '@/lib/routes'
import { useDocumentsTable } from '@/tables'
import type {
  AllSelectedState,
  DocumentRow,
} from '@/tables/documents'
import type { DocumentSummary, DocumentsQueryParams } from '@/api/documents/schema'
import { MEDIA_TYPE_GROUPS, previewKindOf } from '@/lib/formatMediaType'
import type { OptionType } from '@/types'
import { ownEmail } from '@/lib/utils'
import { useCanDeleteOwnDocuments, useCanSeeTeam, useCanUploadDocuments } from '@/composables/useCapabilities'

const searchFieldId = useId()

const { t: $t } = useI18n()
const { toast } = useToast()
const { isAdmin, isOperator } = useCurrentUserRoles()
const canDeleteEveryDocument = computed(() => isAdmin.value || isOperator.value)
const canDeleteOwnDocuments = useCanDeleteOwnDocuments()
// The table reads its columns once, so the template keys it on this value: the selection column
// appears when the organization's features arrive after the first render.
const canDeleteDocuments = computed(() => canDeleteEveryDocument.value || canDeleteOwnDocuments.value)
const { userId: currentUserId } = useCurrentUserId()
const canDelete = (doc: DocumentSummary) =>
  canDeleteEveryDocument.value || (canDeleteOwnDocuments.value && doc.userId !== null && doc.userId === currentUserId.value)

const { state, patchState } = useDocumentsQueryState()

// Search input is a local ref for snappy typing; it mirrors state.search and
// its changes write to the URL immediately. The API query picks up the
// debounced projection so we don't hammer the backend on every keystroke.
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
  patchState({ search: value, page: 0 })
}

// IniaSelect maps empty string to a single space internally; keep our sentinel
// here so "All" clears the corresponding URL param.
const ALL = ' '

const mediaTypeModel = computed<string>({
  get: () => state.value.mediaType || ALL,
  set: v => patchState({ mediaType: v === ALL ? '' : v, page: 0 }),
})

const categoryModel = computed<string>({
  get: () => state.value.categoryId || ALL,
  set: v => patchState({ categoryId: v === ALL ? '' : v, page: 0 }),
})

const userModel = computed<string>({
  get: () => state.value.userId || ALL,
  set: v => patchState({ userId: v === ALL ? '' : v, page: 0 }),
})

const mediaTypeOptions = computed<OptionType[]>(() => [
  { label: $t('page.documents.filters.all'), value: ALL },
  ...MEDIA_TYPE_GROUPS.map(g => ({ label: g.label, value: g.id })),
])

const { data: categoriesData } =
  API.DocumentCategories.useGetDocumentCategories()

const categoryOptions = computed<OptionType[]>(() => {
  const list: OptionType[] = [
    { label: $t('page.documents.filters.all'), value: ALL },
  ]
  for (const cat of categoriesData.value ?? []) {
    list.push({ label: cat.name, value: cat.id })
  }
  return list
})

const categoryMap = computed<Record<string, string>>(() => {
  const map: Record<string, string> = {}
  for (const cat of categoriesData.value ?? []) {
    map[cat.id] = cat.name
  }
  return map
})

const usersParams = ref({ $top: 10000 })
const canSeeTeam = useCanSeeTeam()
const canUploadDocuments = useCanUploadDocuments()
const { data: usersData } = API.Users.useGetUsers(usersParams, canSeeTeam)

const userOptions = computed<OptionType[]>(() => {
  const list: OptionType[] = [
    { label: $t('page.documents.filters.all'), value: ALL },
  ]
  for (const user of usersData.value?.value ?? []) {
    list.push({ label: user.email, value: user.id })
  }
  return list
})

const userMap = computed<Record<string, string>>(() => {
  const map: Record<string, string> = {}
  for (const user of usersData.value?.value ?? []) {
    map[user.id] = user.email
  }
  return map
})

// Documents query — consumes debounced search, everything else eager.
const queryParams = computed<DocumentsQueryParams>(() =>
  buildDocumentsQuery({ ...state.value, search: searchDebounced.value }),
)

const { data: documentsData, isLoading } =
  API.Documents.useGetDocuments(queryParams)

const { typeNames: workflowTypeNames } = useWorkflowTypeNames()

const rows = computed<DocumentRow[]>(() => {
  const me = ownEmail()
  return (documentsData.value?.value ?? []).map((doc: DocumentSummary) => ({
    ...doc,
    categoryName: doc.documentCategoryId
      ? (categoryMap.value[doc.documentCategoryId] ?? doc.documentCategoryId)
      : '—',
    userName: doc.userId ? (userMap.value[doc.userId] ?? (me.userId === doc.userId ? me.email : undefined) ?? doc.userId) : '—',
    workflowTypeName: doc.workflowTypeId
      ? (workflowTypeNames.value.get(doc.workflowTypeId) ?? doc.workflowTypeId)
      : null,
  }))
})

const totalCount = computed(() => documentsData.value?.count ?? 0)

// Selection state — plain Set, not persisted, cleared whenever the query
// changes (pagination, sort, filter).
const selection = ref<Set<string>>(new Set())

watch(queryParams, () => {
  if (selection.value.size) selection.value = new Set()
})

const toggleRow = (id: string) => {
  const next = new Set(selection.value)
  if (next.has(id)) next.delete(id)
  else next.add(id)
  selection.value = next
}

const deletableOnPage = computed(() => rows.value.filter(canDelete).map(r => r.id))

const toggleAll = () => {
  const allOnPage = deletableOnPage.value
  const allSelected = allOnPage.every(id => selection.value.has(id))
  const next = new Set(selection.value)
  if (allSelected) {
    allOnPage.forEach(id => next.delete(id))
  } else {
    allOnPage.forEach(id => next.add(id))
  }
  selection.value = next
}

const allSelectedState = computed<AllSelectedState>(() => {
  const onPage = deletableOnPage.value
  if (!onPage.length) return 'off'
  const selectedCount = onPage.filter(id => selection.value.has(id)).length
  if (selectedCount === 0) return 'off'
  if (selectedCount === onPage.length) return 'on'
  return 'mixed'
})

// Delete flow (shared for single + bulk).
const confirmOpen = ref(false)
const pendingDeleteIds = ref<string[]>([])
const pendingDeleteName = ref<string>('')
const bulkDeleteMutation = API.Documents.useBulkDeleteDocuments()

const askDeleteSingle = (row: DocumentRow) => {
  pendingDeleteIds.value = [row.id]
  pendingDeleteName.value = row.name
  confirmOpen.value = true
}

const askDeleteBulk = () => {
  pendingDeleteIds.value = Array.from(selection.value)
  pendingDeleteName.value = ''
  confirmOpen.value = true
}

const confirmDelete = async () => {
  const ids = pendingDeleteIds.value
  if (!ids.length) return
  try {
    const result = await bulkDeleteMutation.mutateAsync(ids)
    const failedCount = Object.keys(result.failed).length
    if (failedCount === 0) {
      toast({
        class: 'text-sm',
        description: $t('page.documents.toast.deleteSuccess', { count: ids.length }, ids.length),
        duration: 2000,
      })
    } else {
      console.warn('Documents bulk-delete partial failure', result.failed)
      const namesByReason = new Map<string, string[]>()
      for (const [id, reason] of Object.entries(result.failed)) {
        const name = rows.value.find(row => row.id === id)?.name ?? id
        namesByReason.set(reason, [...(namesByReason.get(reason) ?? []), name])
      }
      const reasons = [...namesByReason].map(([reason, names]) => `${names.join(', ')}: ${reason}`).join(' ')
      toast({
        class: 'text-sm',
        description:
          ids.length === 1
            ? Object.values(result.failed)[0]
            : `${$t('page.documents.toast.deletePartial', {
                deleted: result.deleted.length,
                total: ids.length,
                failed: failedCount,
              })} ${reasons}`,
        variant: 'destructive',
        duration: 8000,
      })
    }
    selection.value = new Set()
  } catch {
    toast({
      class: 'text-sm',
      description: $t('page.documents.toast.deleteError'),
      variant: 'destructive',
      duration: 3000,
    })
  } finally {
    confirmOpen.value = false
    pendingDeleteIds.value = []
    pendingDeleteName.value = ''
  }
}

const cancelDelete = () => {
  confirmOpen.value = false
  pendingDeleteIds.value = []
  pendingDeleteName.value = ''
}

// Preview (row action) — opens the same on-demand viewer as the detail page.
const previewDoc = ref<DocumentRow | null>(null)
const previewOpen = ref(false)
const previewKind = computed(() => previewKindOf(previewDoc.value?.mediaType))

const onPreview = (doc: DocumentRow) => {
  previewDoc.value = doc
  previewOpen.value = true
}

// Download (row action). The Files endpoint resolves by document id, so the
// summary DTO's `id` is passed directly — no metadata round-trip.
const { mutateAsync: getFile } = API.Files.useGetFile()

const onDownload = async (doc: DocumentRow) => {
  try {
    const response = await getFile({
      fileId: doc.id,
      disposition: 'download',
    })
    const blob = new Blob([response.data])
    const url = URL.createObjectURL(blob)
    try {
      const link = document.createElement('a')
      link.href = url
      link.download = `${doc.name}${doc.extension ?? ''}`
      document.body.appendChild(link)
      link.click()
      link.remove()
    } finally {
      URL.revokeObjectURL(url)
    }
  } catch {
    toast({
      class: 'text-sm',
      description: $t('page.documents.toast.downloadError'),
      variant: 'destructive',
      duration: 3000,
    })
  }
}

const editDoc = ref<DocumentRow | null>(null)
const editOpen = ref(false)
const onEdit = (doc: DocumentRow) => {
  editDoc.value = doc
  editOpen.value = true
}

const uploadOpen = ref(false)

// Columns
const { columns } = useDocumentsTable({
  selection,
  allSelected: allSelectedState,
  onToggleRow: toggleRow,
  onToggleAll: toggleAll,
  onPreview,
  onDownload: row => onDownload(row),
  onEdit,
  onDelete: row => askDeleteSingle(row),
  canDeleteDocuments,
  canSelectAny: computed(() => deletableOnPage.value.length > 0),
  canDelete,
})

// Pagination + sort wiring
const onPageChange = (newPage: number) => patchState({ page: newPage })
const onOrderByChange = (newOrderBy: string) =>
  patchState({ orderBy: newOrderBy, page: 0 })

const clearJobFilter = () => patchState({ jobId: '', page: 0 })

const deleteDialogBody = computed(() => {
  if (pendingDeleteIds.value.length <= 1) {
    return $t('page.documents.delete.bodySingle', {
      name: pendingDeleteName.value || pendingDeleteIds.value[0] || '',
    })
  }
  return $t('page.documents.delete.bodyBulk', {
    count: pendingDeleteIds.value.length,
  })
})

const deleteDialogTitle = computed(() =>
  pendingDeleteIds.value.length <= 1
    ? $t('page.documents.delete.title')
    : $t('page.documents.delete.bulkTitle', { count: pendingDeleteIds.value.length }),
)
</script>

<template>
  <div class="page-view">
    <div class="page-header-with-action">
      <div>
        <h1 class="page-title">{{ $t('page.documents.title') }}</h1>
        <p class="page-subtitle">
          {{ $t('page.documents.subtitle', { count: totalCount }, totalCount) }}
        </p>
      </div>
      <Button
        v-if="canUploadDocuments"
        class="btn-gradient-primary"
        data-testid="upload-documents"
        @click="uploadOpen = true"
      >
        <IniaIcon name="upload" class="mr-2" />
        {{ $t('page.documents.upload.open') }}
      </Button>
    </div>

    <div v-if="state.jobId" class="job-chip-row">
      <button
        type="button"
        class="job-chip"
        :title="$t('page.documents.clearJobFilter')"
        @click="clearJobFilter"
      >
        <span>{{ $t('page.documents.jobChip', { jobId: state.jobId }) }}</span>
        <IniaIcon name="xmark" classes="job-chip-close" />
      </button>
    </div>

    <div class="filters-section">
      <div class="filter-field filter-field--wide">
        <label :for="searchFieldId" class="filter-label">{{ $t('page.documents.filters.search') }}</label>
        <input
          :id="searchFieldId"
          type="search"
          class="filter-input"
          :value="searchInput"
          :placeholder="$t('page.documents.filters.searchPlaceholder')"
          @input="onSearchInput"
        />
      </div>
      <div class="filter-field">
        <span class="filter-label">{{ $t('page.documents.filters.mediaType') }}</span>
        <IniaSelect
          v-model="mediaTypeModel"
          :options="mediaTypeOptions"
          :aria-label="$t('page.documents.filters.mediaType')"
          :placeholder="$t('page.documents.filters.all')"
        />
      </div>
      <div class="filter-field">
        <span class="filter-label">{{ $t('page.documents.filters.category') }}</span>
        <IniaSelect
          v-model="categoryModel"
          :options="categoryOptions"
          :aria-label="$t('page.documents.filters.category')"
          :placeholder="$t('page.documents.filters.all')"
        />
      </div>
      <div v-if="canSeeTeam" class="filter-field">
        <span class="filter-label">{{ $t('page.documents.filters.user') }}</span>
        <IniaSelect
          v-model="userModel"
          :options="userOptions"
          :aria-label="$t('page.documents.filters.user')"
          :placeholder="$t('page.documents.filters.all')"
        />
      </div>
    </div>

    <div v-if="canDeleteDocuments && selection.size" class="bulk-action-row">
      <span class="bulk-selection-count">{{
        $t('page.documents.selection.count', { count: selection.size })
      }}</span>
      <Button variant="destructive" @click="askDeleteBulk">
        {{ $t('page.documents.actions.deleteSelected', { count: selection.size }) }}
      </Button>
    </div>

    <div class="card-panel table-container">
      <DataTable
        v-if="rows.length"
        :key="String(canDeleteDocuments)"
        :columns="columns"
        :data="rows"
        :page-size="state.pageSize"
        :total-count="totalCount"
        :page="state.page"
        :order-by="state.orderBy"
        :row-to="(row: Row<DocumentRow>) => documentDetailsRoute(row.original.id)"
        @update:page="onPageChange"
        @update:order-by="onOrderByChange"
      />
      <div v-else-if="!isLoading" class="empty-state">
        {{ $t('page.documents.emptyText') }}
      </div>
      <div v-if="isLoading" class="loading-overlay">
        <IniaLoader class="w-8" />
      </div>
    </div>

    <FilePreviewDialog
      v-if="previewDoc && previewKind"
      v-model:open="previewOpen"
      :file-id="previewDoc.id"
      :filename="previewDoc.name"
      :kind="previewKind"
      :unavailable-text="$t('page.documents.detail.preview.unavailable')"
    />

    <EditDocumentDialog v-model:open="editOpen" :document="editDoc" />

    <UploadDocumentsDialog v-model:open="uploadOpen" />

    <Dialog v-model:open="confirmOpen">
      <DialogContent>
        <DialogHeader>
          <DialogTitle>{{ deleteDialogTitle }}</DialogTitle>
          <DialogDescription>{{ deleteDialogBody }}</DialogDescription>
        </DialogHeader>
        <DialogFooter>
          <Button variant="outline" @click="cancelDelete">
            {{ $t('page.documents.delete.cancel') }}
          </Button>
          <Button
            variant="destructive"
            :disabled="bulkDeleteMutation.isPending.value"
            @click="confirmDelete"
          >
            <IniaLoader
              v-if="bulkDeleteMutation.isPending.value"
              class="w-4 mr-2"
            />
            {{ $t('page.documents.delete.confirm') }}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  </div>
</template>

<style scoped>
.page-subtitle {
  margin-top: var(--spacing-xs);
}

.job-chip-row {
  margin-bottom: var(--spacing-md);
}

.job-chip {
  display: inline-flex;
  align-items: center;
  gap: var(--spacing-xs);
  padding: 6px 12px;
  background: var(--bg-2);
  border: 1px solid var(--stroke);
  border-radius: var(--radius-button);
  color: var(--text-primary);
  font-size: 13px;
  cursor: pointer;
  transition: all 0.15s ease-in-out;
}

.job-chip:hover {
  border-color: var(--accent-error-border);
  color: var(--accent-error);
}

.job-chip-close {
  font-size: 12px;
}

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

.bulk-action-row {
  display: flex;
  align-items: center;
  gap: var(--spacing-md);
  margin-bottom: var(--spacing-md);
  padding: var(--spacing-sm) var(--spacing-md);
  background: var(--bg-1);
  border: 1px solid var(--stroke);
  border-radius: var(--radius-button);
}

.bulk-selection-count {
  font-size: 14px;
  color: var(--text-secondary);
}

.table-container :deep(.document-name-cell) {
  display: flex;
  align-items: center;
  gap: var(--spacing-xs);
  min-width: 0;
}

.table-container :deep(.data-table-row-link):focus-visible {
  outline: 2px solid var(--accent-indigo-to);
  outline-offset: 2px;
  border-radius: var(--radius-small);
}

/* ----------------------------------------------------------------------
   Recolour the reka-ui Checkbox from the global `--primary` (pink) to the
   indigo/purple accents used elsewhere (btn-gradient-primary, progress bars,
   etc.). Scoped to this view so the brand pink stays for components that want
   it globally.
   ---------------------------------------------------------------------- */
.page-view :deep(button[role='checkbox']) {
  border-color: var(--accent-indigo-to);
}

.page-view :deep(button[role='checkbox'][data-state='checked']) {
  background-color: var(--primary);
  border-color: var(--accent-indigo-to);
  color: var(--primary-foreground);
}

.page-view :deep(button[role='checkbox'][data-state='indeterminate']) {
  border-color: var(--accent-indigo-to);
}

.table-container :deep(.document-name-icon) {
  flex-shrink: 0;
  color: var(--text-secondary);
}

.table-container :deep(.document-name-text) {
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
  max-width: 360px;
}

@media (max-width: 768px) {
  .filters-section {
    flex-wrap: wrap;
  }

  .filter-field {
    flex: 1 1 150px;
  }

  .filter-field--wide {
    flex: 1 1 100%;
  }
}
</style>
