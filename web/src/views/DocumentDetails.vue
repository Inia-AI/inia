<script setup lang="ts">
import { computed, ref } from 'vue'
import { useItemTitle } from '@/composables/useDocumentTitle'
import { useI18n } from 'vue-i18n'
import { useRoute, useRouter } from 'vue-router'
import API from '@/api'
import { IniaIcon, IniaLoader } from '@/components/atoms'
import { FilePreviewDialog } from '@/components/molecules'
import DocumentPageReader from '@/components/organisms/DocumentPageReader/DocumentPageReader.vue'
import { Button } from '@/components/ui/button'
import { useToast } from '@/components/ui/toast/use-toast'
import { useBundleDownload } from '@/composables/useBundleDownload'
import { downloadBlob } from '@/lib/downloadFile'
import { formatDateTime, formatDate, ownEmail } from '@/lib/utils'
import { formatBytes } from '@/lib/formatBytes'
import { formatMediaType, mediaTypeIconName, previewKindOf } from '@/lib/formatMediaType'
import { useCanSeeTeam } from '@/composables/useCapabilities'

const TEXT_FALLBACK_NAME = 'document.txt'
const DETAILS_ID = 'document-details'

const route = useRoute()
const router = useRouter()
const { t: $t } = useI18n()
const { toast } = useToast()

const documentId = computed(() => String(route.params.id ?? ''))

const {
  data: documentData,
  isLoading,
  isError,
  error,
} = API.Documents.useGetDocumentById(documentId)

useItemTitle(() => documentData.value?.name)

const notFound = computed(() => {
  if (!isError.value) return false
  const status = (error.value as { response?: { status?: number } } | null)?.response?.status
  return status === 404 || status === 403
})

// Categories + users for friendly labels.
const { data: categoriesData } = API.DocumentCategories.useGetDocumentCategories()
const usersParams = ref({ $top: 10000 })
const { data: usersData } = API.Users.useGetUsers(usersParams, useCanSeeTeam())

const categoryName = computed(() => {
  const id = documentData.value?.documentCategoryId
  if (!id) return null
  return categoriesData.value?.find(cat => cat.id === id)?.name ?? id
})

const userName = computed(() => {
  const id = documentData.value?.userId
  if (!id) return null
  const me = ownEmail()
  return usersData.value?.value?.find(user => user.id === id)?.email ?? (me.userId === id ? me.email : undefined) ?? id
})

const subtitle = computed(() => {
  const doc = documentData.value
  if (!doc) return ''
  return [
    formatMediaType(doc.mediaType),
    formatBytes(doc.sizeBytes),
    formatDate(doc.createdAt),
    categoryName.value,
    userName.value ? $t('page.documents.detail.uploadedBy', { user: userName.value }) : null,
  ]
    .filter(Boolean)
    .join(' · ')
})

const detailsOpen = ref(false)

const selectedPage = computed<number>({
  get: () => {
    const requested = Number(route.query.page)
    const last = Math.max(documentData.value?.pageCount ?? 1, 1)
    return Number.isInteger(requested) && requested >= 1 ? Math.min(requested, last) : 1
  },
  set: number => {
    void router.replace({ query: { ...route.query, page: String(number) } })
  },
})

// Download (raw file)
const { mutateAsync: getFile } = API.Files.useGetFile()

const onDownload = async () => {
  const doc = documentData.value
  if (!doc) return
  try {
    const response = await getFile({
      fileId: doc.id,
      disposition: 'download',
    })
    downloadBlob(new Blob([response.data]), `${doc.name}${doc.extension ?? ''}`)
  } catch {
    toast({
      class: 'text-sm',
      description: $t('page.documents.toast.downloadError'),
      variant: 'destructive',
      duration: 3000,
    })
  }
}

const { download: downloadText, isPending: isDownloadingText } = useBundleDownload(
  API.Documents.useDownloadDocumentText(),
  TEXT_FALLBACK_NAME,
  'page.documents.toast.downloadError',
)

const onDownloadText = () => {
  if (documentData.value) void downloadText(documentData.value.id)
}

// Preview — opens on demand in <FilePreviewDialog>. The button is only shown
// for PDF and image docs; other kinds have no usable inline viewer.
const previewKind = computed(() => previewKindOf(documentData.value?.mediaType))
const canPreview = computed(() => previewKind.value !== null)
const showPreview = ref(false)

const goBack = () => {
  const cameFromAnAppPage = Boolean(router.options.history.state.back)
  if (cameFromAnAppPage) {
    router.back()
  } else {
    router.push({ name: 'Documents' })
  }
}

const sourcesList = computed(() => documentData.value?.sources ?? [])
</script>

<template>
  <div class="page-view">
    <div v-if="isLoading" class="loading-container">
      <h1 class="sr-only">{{ $t('page.documents.detail.title') }}</h1>
      <IniaLoader class="w-8" />
    </div>

    <div v-else-if="notFound" class="error-container">
      <div class="error-message">
        <h1>{{ $t('page.documents.detail.notFoundTitle') }}</h1>
        <p>{{ $t('page.documents.detail.notFoundBody') }}</p>
        <Button variant="outline" @click="goBack">
          {{ $t('page.documents.detail.back') }}
        </Button>
      </div>
    </div>

    <template v-else-if="documentData">
      <div class="page-header-with-action">
        <div class="header-left">
          <Button variant="ghost" size="sm" class="back-button" @click="goBack">
            <IniaIcon name="chevron-right" classes="back-icon" />
            {{ $t('page.documents.detail.back') }}
          </Button>
          <div class="title-block">
            <div class="title-row">
              <IniaIcon
                :name="mediaTypeIconName(documentData.mediaType)"
                classes="title-icon"
              />
              <h1 class="page-title">{{ documentData.name }}</h1>
            </div>
            <p class="page-subtitle">
              <span>{{ subtitle }}</span>
              <button
                type="button"
                class="details-toggle"
                data-testid="details-toggle"
                :aria-expanded="detailsOpen"
                :aria-controls="detailsOpen ? DETAILS_ID : undefined"
                @click="detailsOpen = !detailsOpen"
              >
                {{ $t('page.documents.detail.details') }}
                <IniaIcon name="chevron-down" :classes="detailsOpen ? 'details-chevron details-chevron--open' : 'details-chevron'" />
              </button>
            </p>
          </div>
        </div>
        <div class="action-buttons">
          <Button
            v-if="canPreview"
            variant="outline"
            @click="showPreview = true"
          >
            <IniaIcon name="eye" classes="w-4 h-4 mr-2" />
            {{ $t('page.documents.detail.preview.open') }}
          </Button>
          <Button variant="outline" class="btn-utility" @click="onDownload">
            <IniaIcon name="download" classes="w-4 h-4 mr-2" />
            {{ $t('page.documents.detail.preview.download') }}
          </Button>
          <Button
            variant="outline"
            class="btn-utility"
            data-testid="download-text"
            :disabled="isDownloadingText"
            @click="onDownloadText"
          >
            <IniaIcon name="file-lines" classes="w-4 h-4 mr-2" />
            {{ $t('page.documents.detail.downloadText') }}
          </Button>
        </div>
      </div>

      <section v-if="detailsOpen" :id="DETAILS_ID" class="card-panel document-details">
        <div class="info-grid">
          <div v-if="documentData.class" class="info-item">
            <span class="info-label">{{ $t('page.documents.detail.fields.class') }}</span>
            <span class="info-value">{{ documentData.class }}</span>
          </div>
          <div v-if="documentData.workflowTypeId" class="info-item">
            <span class="info-label">{{ $t('page.documents.detail.fields.workflowType') }}</span>
            <span class="info-value">{{ documentData.workflowTypeId }}</span>
          </div>
          <div class="info-item">
            <span class="info-label">{{ $t('page.documents.detail.fields.extension') }}</span>
            <span class="info-value">{{ documentData.extension || '—' }}</span>
          </div>
          <div v-if="documentData.updatedAt" class="info-item">
            <span class="info-label">{{ $t('page.documents.detail.fields.updatedAt') }}</span>
            <span class="info-value">{{ formatDateTime(documentData.updatedAt) }}</span>
          </div>
          <div v-if="documentData.description" class="info-item">
            <span class="info-label">{{ $t('page.documents.detail.fields.description') }}</span>
            <span class="info-value info-text">{{ documentData.description }}</span>
          </div>
          <div v-if="documentData.topics" class="info-item">
            <span class="info-label">{{ $t('page.documents.detail.fields.topics') }}</span>
            <span class="info-value info-text">{{ documentData.topics }}</span>
          </div>
          <div v-if="documentData.tableOfContents" class="info-item">
            <span class="info-label">{{ $t('page.documents.detail.fields.tableOfContents') }}</span>
            <span class="info-value info-text">{{ documentData.tableOfContents }}</span>
          </div>
          <div v-if="sourcesList.length" class="info-item">
            <span class="info-label">{{ $t('page.documents.detail.fields.sources') }}</span>
            <div class="source-chips">
              <span v-for="source in sourcesList" :key="source" class="source-chip">{{ source }}</span>
            </div>
          </div>
        </div>
      </section>

      <DocumentPageReader
        v-if="documentData.pageCount > 0"
        v-model:page-number="selectedPage"
        :document-id="documentData.id"
        :page-count="documentData.pageCount"
      />
      <p v-else class="empty-state">{{ $t('page.documents.detail.pages.empty') }}</p>

      <FilePreviewDialog
        v-if="previewKind"
        v-model:open="showPreview"
        :file-id="documentData.id"
        :filename="documentData.name"
        :kind="previewKind"
        :unavailable-text="$t('page.documents.detail.preview.unavailable')"
      />
    </template>

    <div v-else class="error-container">
      <div class="error-message">
        <h1>{{ $t('page.documents.detail.loadErrorTitle') }}</h1>
        <p>{{ $t('page.documents.detail.loadErrorBody') }}</p>
        <Button variant="outline" @click="goBack">
          {{ $t('page.documents.detail.back') }}
        </Button>
      </div>
    </div>
  </div>
</template>

<style scoped>
.header-left {
  display: flex;
  align-items: flex-start;
  gap: var(--spacing-sm);
  min-width: 0;
}

.back-button {
  flex-shrink: 0;
}

.back-icon {
  transform: rotate(180deg);
  margin-right: var(--spacing-xs);
}

.title-block {
  min-width: 0;
}

.title-row {
  display: flex;
  align-items: center;
  gap: var(--spacing-xs);
}

.title-icon {
  color: var(--text-secondary);
  font-size: 20px;
}

.page-title {
  overflow-wrap: break-word;
  word-break: break-word;
  margin: 0;
}

.page-subtitle {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: var(--spacing-sm);
  margin-top: var(--spacing-xs);
}

.details-toggle {
  display: inline-flex;
  align-items: center;
  gap: 0.25rem;
  font-size: 0.8125rem;
  color: var(--text-secondary);
  text-decoration: underline;
  text-underline-offset: 2px;
}

.details-toggle:hover {
  color: var(--text-primary);
}

.details-chevron {
  width: 0.625rem;
  height: 0.625rem;
  transition: transform 0.15s ease;
}

.details-chevron--open {
  transform: rotate(180deg);
}

.document-details {
  margin-bottom: var(--spacing-lg);
}

.info-text {
  white-space: pre-wrap;
  overflow-wrap: break-word;
  word-break: break-word;
  font-size: 13px;
  color: var(--text-primary);
}

.source-chips {
  display: flex;
  flex-wrap: wrap;
  gap: var(--spacing-xs);
}

.source-chip {
  padding: 4px 10px;
  background: var(--bg-2);
  border: 1px solid var(--stroke);
  border-radius: var(--radius-button);
  font-size: 12px;
  color: var(--text-secondary);
}
</style>
