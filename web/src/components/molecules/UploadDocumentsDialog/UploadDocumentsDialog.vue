<script setup lang="ts">
import { computed, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { useQueryClient } from '@tanstack/vue-query'
import API from '@/api'
import { extractApiErrorMessage } from '@/api/base/errors'
import type { DocumentDetail } from '@/api/documents/schema'
import type { FileDto } from '@/api/files/schema'
import IniaSelect from '@/components/molecules/IniaSelect/IniaSelect.vue'
import { IniaIcon } from '@/components/atoms'
import { Button } from '@/components/ui/button'
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from '@/components/ui/dialog'
import { describeUploadError } from '@/composables/useDocumentUpload'
import type { OptionType } from '@/types'
import DocumentReadWatch from './DocumentReadWatch.vue'

type UploadState = 'pending' | 'uploading' | 'processing' | 'processed' | 'stored' | 'failed'

interface UploadRow {
  key: number
  file: File
  categoryId: string
  state: UploadState
  percent: number
  error: string | null
  documentId: string | null
}

defineProps<{ open: boolean }>()
const emit = defineEmits<{ (e: 'update:open', value: boolean): void }>()

const { t: $t } = useI18n()
const queryClient = useQueryClient()

const { mutateAsync: uploadDocument } = API.Files.useUploadDocument()
const { mutateAsync: processDocument } = API.Documents.useProcessDocument()

const { data: categories } = API.DocumentCategories.useGetDocumentCategories()
const categoryOptions = computed<OptionType[]>(() => [
  { label: $t('page.documents.upload.noCategory'), value: '' },
  ...[...(categories.value ?? [])]
    .sort((a, b) => a.displayOrder - b.displayOrder)
    .map(category => ({ label: category.name, value: category.id })),
])

const rows = ref<UploadRow[]>([])
const fileInput = ref<HTMLInputElement | null>(null)
const running = ref(false)
let nextKey = 0

const pendingRows = computed(() => rows.value.filter(row => row.state === 'pending'))
const canStart = computed(() => !running.value && pendingRows.value.length > 0)

const onFilesPicked = (event: Event) => {
  const input = event.target as HTMLInputElement
  for (const file of Array.from(input.files ?? [])) {
    rows.value.push({ key: nextKey++, file, categoryId: '', state: 'pending', percent: 0, error: null, documentId: null })
  }
  input.value = ''
}

const processError = (error: unknown) => extractApiErrorMessage(error, $t('page.documents.upload.processError'))

const uploadAndQueue = async (row: UploadRow) => {
  row.state = 'uploading'
  let uploaded: FileDto
  try {
    const formData = new FormData()
    formData.append('file', row.file, row.file.name)
    if (row.categoryId) formData.append('documentCategoryId', row.categoryId)
    uploaded = await uploadDocument({
      formData,
      onUploadProcessChange: percent => {
        row.percent = percent
      },
    })
  } catch (error) {
    row.state = 'failed'
    row.error = describeUploadError(error, $t, $t('shared.components.fileControl.generic'))
    return
  }

  if (!uploaded.producesPages) {
    row.state = 'stored'
    return
  }

  try {
    await processDocument(uploaded.id)
    row.documentId = uploaded.id
    row.state = 'processing'
  } catch (error) {
    row.state = 'failed'
    row.error = processError(error)
  }
}

const onRead = (row: UploadRow, document: DocumentDetail) => {
  row.state = document.processingState === 'Processed' ? 'processed' : 'failed'
  row.error = document.processingError
}

const onLost = (row: UploadRow, error: unknown) => {
  row.state = 'failed'
  row.error = processError(error)
}

const start = async () => {
  if (!canStart.value) return
  running.value = true
  try {
    for (const row of [...pendingRows.value]) {
      await uploadAndQueue(row)
    }
  } finally {
    running.value = false
    await queryClient.invalidateQueries({ queryKey: ['documents', 'list'] })
  }
}

const stateLabel = (row: UploadRow) =>
  row.state === 'uploading'
    ? $t('page.documents.upload.state.uploading', { percent: row.percent })
    : $t(`page.documents.upload.state.${row.state}`)
</script>

<template>
  <Dialog :open="open" @update:open="emit('update:open', $event)">
    <DialogContent class="sm:max-w-[720px]">
      <DialogHeader>
        <DialogTitle>{{ $t('page.documents.upload.title') }}</DialogTitle>
        <DialogDescription>{{ $t('page.documents.upload.description') }}</DialogDescription>
      </DialogHeader>

      <div>
        <Button variant="outline" :disabled="running" @click="fileInput?.click()">
          <IniaIcon name="upload" class="mr-2" />
          {{ $t('page.documents.upload.chooseFiles') }}
        </Button>
        <input
          ref="fileInput"
          type="file"
          multiple
          class="hidden"
          :aria-label="$t('page.documents.upload.chooseFiles')"
          data-testid="upload-input"
          @change="onFilesPicked"
        />
      </div>

      <ul v-if="rows.length" class="upload-rows">
        <li v-for="row in rows" :key="row.key" class="upload-row" data-testid="upload-row">
          <span class="file-name" data-testid="file-name" :title="row.file.name">{{ row.file.name }}</span>
          <IniaSelect
            v-model="row.categoryId"
            class="file-category"
            :options="categoryOptions"
            :disabled="row.state !== 'pending'"
            :aria-label="$t('page.documents.upload.categoryFor', { name: row.file.name })"
          />
          <div class="file-status">
            <span :class="['file-state', `file-state--${row.state}`]" data-testid="file-state">{{ stateLabel(row) }}</span>
            <span v-if="row.error" class="file-error" data-testid="file-error">{{ row.error }}</span>
          </div>
          <DocumentReadWatch
            v-if="row.state === 'processing' && row.documentId"
            :document-id="row.documentId"
            @read="onRead(row, $event)"
            @lost="onLost(row, $event)"
          />
        </li>
      </ul>
      <p v-else class="upload-empty">{{ $t('page.documents.upload.empty') }}</p>

      <DialogFooter>
        <Button variant="outline" @click="emit('update:open', false)">{{ $t('page.documents.upload.close') }}</Button>
        <Button class="btn-gradient-primary" data-testid="start-upload" :disabled="!canStart" @click="start">
          {{ $t('page.documents.upload.start') }}
        </Button>
      </DialogFooter>
    </DialogContent>
  </Dialog>
</template>

<style scoped>
.upload-rows {
  display: flex;
  flex-direction: column;
  gap: var(--spacing-sm);
  max-height: 50vh;
  overflow-y: auto;
}

.upload-row {
  display: grid;
  grid-template-columns: minmax(0, 1fr) 200px 180px;
  align-items: center;
  gap: var(--spacing-sm);
}

.file-name {
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.file-status {
  display: flex;
  flex-direction: column;
  gap: 2px;
}

.file-state {
  font-size: 13px;
  color: var(--text-secondary);
}

.file-state--processed {
  color: var(--accent-success);
}

.file-state--failed {
  color: var(--accent-error);
}

.file-error {
  font-size: 12px;
  color: var(--accent-error);
}

.upload-empty {
  font-size: 14px;
  color: var(--text-secondary);
}
</style>
