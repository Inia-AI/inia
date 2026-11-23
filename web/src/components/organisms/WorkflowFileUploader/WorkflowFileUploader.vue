<script lang="ts" setup>
import { PropType, watch, computed, reactive, ref } from 'vue'
import { Field as FormField } from 'vee-validate'
import { useI18n } from 'vue-i18n'

import { useClipboard } from '@vueuse/core'

import { DocumentInfo, OptionType } from '@/types'
import { useDocumentUpload } from '@/composables/useDocumentUpload'
import { buildUploadDiagnostics } from '@/lib/uploadDiagnostics'
import { fileExtension } from '@/lib/fileName'
import API from '@/api'

import { Input } from '@/components/ui/input'
import { Progress } from '@/components/ui/progress'
import {
  Dialog,
  DialogContent,
  DialogHeader,
  DialogTitle,
  DialogDescription,
  DialogFooter,
} from '@/components/ui/dialog'
import { Label } from '@/components/ui/label'
import { IniaIcon } from '@/components/atoms'
import {
  FormItem,
  FormLabel,
  FormControl,
  FormMessage,
} from '@/components/ui/form'
import { IniaSelect } from '@/components/molecules'
import { Button } from '@/components/ui/button'
import { extensionPoints } from '@/modules/extensionPoints'
import { DEFAULT_LOCALE } from '@/assets/locales'

const { t: $t } = useI18n()
const modelValue = defineModel<DocumentInfo[]>()
const requestFields = defineModel<Record<string, unknown>>('requestFields', { default: () => ({}) })

const emit = defineEmits<{ 'update:uploading': [boolean] }>()

const props = defineProps({
  documentClassOptions: {
    type: Array as PropType<OptionType[]>,
    default: () => [],
  },
  name: {
    type: String,
    required: true,
  },
  everyDocumentNeedsAClass: {
    type: Boolean,
    default: false,
  },
})

// Use the composable for upload management
const {
  documents,
  isAnyUploading,
  retryableFailures,
  addFiles,
  removeDocument,
  removeFailed,
  retryFailed,
  uploadWithPassword,
  updateDocumentClass,
  setDocuments,
  getFormDocuments,
} = useDocumentUpload()

// Password prompt: a password-protected upload comes back as a passwordRequired
// error; we prompt for the password and re-upload. Skipped files are remembered
// so the prompt does not immediately re-open for them.
const pendingPasswordRef = ref<string | null>(null)
const passwordInput = ref('')
const isUnlocking = ref(false)
const skippedPasswordRefs = reactive(new Set<string>())

const pendingPasswordDoc = computed(() =>
  documents.value.find(d => d.ref === pendingPasswordRef.value)
)

const nextPasswordRequiredDoc = computed(() =>
  documents.value.find(
    d => d.error === 'passwordRequired' && !skippedPasswordRefs.has(d.ref)
  )
)

const passwordDialogOpen = computed({
  get: () => pendingPasswordDoc.value !== undefined,
  set: (open: boolean) => {
    if (!open) {
      skipPasswordPrompt()
    }
  },
})

watch([nextPasswordRequiredDoc, pendingPasswordRef], ([next, pending]) => {
  if (next && pending === null) {
    passwordInput.value = ''
    pendingPasswordRef.value = next.ref
  }
})

watch(pendingPasswordDoc, doc => {
  if (doc === undefined && pendingPasswordRef.value !== null) {
    pendingPasswordRef.value = null
  }
})

async function retryFailedUploads() {
  skippedPasswordRefs.clear()
  await retryFailed()
}

async function submitPassword() {
  const doc = pendingPasswordDoc.value
  if (!doc || !passwordInput.value || isUnlocking.value) {
    return
  }
  isUnlocking.value = true
  try {
    await uploadWithPassword(doc.ref, passwordInput.value)
  } finally {
    isUnlocking.value = false
  }
  const refreshed = documents.value.find(d => d.ref === doc.ref)
  if (refreshed?.id) {
    pendingPasswordRef.value = null
    passwordInput.value = ''
  } else {
    passwordInput.value = ''
  }
}

function skipPasswordPrompt() {
  if (pendingPasswordRef.value !== null) {
    skippedPasswordRefs.add(pendingPasswordRef.value)
  }
  pendingPasswordRef.value = null
  passwordInput.value = ''
}

// Sync external model value to internal state (for initial values).
// Errored rows never leave this component (getFormDocuments filters them),
// so they are invisible to the external model: exclude them from the
// comparisons and carry them across every sync, otherwise the parent's
// echo of the filtered payload would wipe the failed rows and their
// retry/dismiss UI within a tick of an upload failing.
watch(
  () => modelValue.value,
  (newVal) => {
    // Only sync if we're not currently uploading (to prevent loops)
    if (isAnyUploading.value) {
      return
    }

    const erroredDocs = documents.value.filter(d => d.error)
    const syncableDocs = documents.value.filter(d => !d.error)

    const applyExternal = (docs: DocumentInfo[]) => {
      setDocuments(docs)
      if (erroredDocs.length > 0) {
        documents.value = [...documents.value, ...erroredDocs]
      }
    }

    // Handle explicit clear from parent
    if (!newVal || newVal.length === 0) {
      if (syncableDocs.length > 0) {
        applyExternal([])
      }
      return
    }

    // Check if the external value is different from our internal state
    const internalIds = new Set(syncableDocs.map(d => d.id).filter(Boolean))
    const internalRefs = new Set(syncableDocs.map(d => d.ref))
    const externalIds = new Set(newVal.map(d => d.id).filter(Boolean))
    const externalRefs = new Set(newVal.map(d => d.ref))

    // Only sync if external has documents we don't have internally (by id or ref)
    const hasNewExternal = newVal.some(d =>
      (d.id && !internalIds.has(d.id)) || (d.ref && !internalRefs.has(d.ref))
    )
    const hasRemovedInternal = syncableDocs.some(d =>
      (d.id && !externalIds.has(d.id)) || (!d.id && d.ref && !externalRefs.has(d.ref))
    )

    if (hasNewExternal || hasRemovedInternal) {
      applyExternal(newVal)
    }
  },
  { immediate: true }
)

// Emit completed documents when internal state changes
// Only emit when not uploading to prevent loops
watch(
  documents,
  () => {
    // Only emit if we have completed documents or if we're done uploading
    if (!isAnyUploading.value) {
      modelValue.value = getFormDocuments()
    }
  },
  { deep: true }
)

watch(isAnyUploading, uploading => emit('update:uploading', uploading), {
  immediate: true,
})

const displayDocuments = computed(() => documents.value)

const OPEN_EXTENSIONS = ['pdf', 'xlsx', 'xlsm', 'docx', 'csv', 'jpg', 'jpeg', 'png']

const acceptedExtensions = [...OPEN_EXTENSIONS, ...extensionPoints.uploads.flatMap(upload => upload.extensions)]

// Without a module that accepts them, the platform still refuses these with a reason that says what to do instead.
const FULL_EDITION_EXTENSIONS = ['xls', 'msg', 'eml']

const supportedFormats = new Intl.ListFormat(DEFAULT_LOCALE, { type: 'conjunction' }).format(
  acceptedExtensions.map(extension => extension.toUpperCase()),
)

const uploadPanels = extensionPoints.uploads.flatMap(upload => (upload.panel ? [upload.panel] : []))

const fileNames = computed(() => documents.value.map(document => document.name))

const acceptAttribute = acceptedExtensions.map(ext => `.${ext}`).join(', ')

const isAcceptedFile = (file: File) =>
  [...acceptedExtensions, ...FULL_EDITION_EXTENSIONS].includes(fileExtension(file.name))

const rejectedFileNames = ref<string[]>([])

const addAcceptedFiles = async (files: File[]) => {
  const accepted = files.filter(isAcceptedFile)
  rejectedFileNames.value = files.filter(file => !isAcceptedFile(file)).map(file => file.name)
  if (accepted.length > 0) {
    await addFiles(accepted)
  }
}

const onFileUpload = async (event: Event) => {
  const input = event.target as HTMLInputElement
  const files = input.files

  if (files && files.length > 0) {
    await addAcceptedFiles(Array.from(files))
    // Clear the input so the same file can be re-selected if needed
    input.value = ''
  }
}

const onRemove = (docRef: string) => {
  removeDocument(docRef)
}

const onUpdateClass = (docRef: string, newClass: string | undefined) => {
  updateDocumentClass(docRef, newClass)
}

const isDragging = ref(false)
const fileInput = ref<HTMLInputElement | null>(null)

const openFilePicker = () => {
  fileInput.value?.click()
}

const onDragOver = () => {
  isDragging.value = true
}

const onDragLeave = (event: DragEvent) => {
  const next = event.relatedTarget as Node | null
  if (next && (event.currentTarget as Node).contains(next)) {
    return
  }
  isDragging.value = false
}

const onDrop = async (event: DragEvent) => {
  isDragging.value = false
  const files = event.dataTransfer?.files
  if (files && files.length > 0) {
    await addAcceptedFiles(Array.from(files))
  }
}

const { copy: copyDiagnosticsText, copied: diagnosticsCopied } = useClipboard({
  legacy: true,
  copiedDuring: 2000,
})

const onCopyDiagnostics = async () => {
  const failed = documents.value.flatMap(d =>
    d.error ? [{ name: d.name, sizeBytes: d.file.size, error: d.error }] : [],
  )
  await copyDiagnosticsText(
    buildUploadDiagnostics(failed, {
      pageUrl: window.location.href,
      uploadEndpoint: API.Files.service.uploadEndpoint(window.location.origin),
      userAgent: navigator.userAgent,
      timestamp: new Date().toISOString(),
    }),
  )
}

const fileIcon = (fileName: string | undefined) => {
  switch (fileExtension(fileName)) {
    case 'pdf':
      return 'file-pdf'
    case 'csv':
      return 'file-csv'
    case 'xls':
    case 'xlsx':
    case 'xlsm':
      return 'file-excel'
    case 'jpg':
    case 'jpeg':
    case 'png':
      return 'file-image'
    case 'doc':
    case 'docx':
      return 'file-word'
    case 'msg':
    case 'eml':
      return 'envelope'
    default:
      return 'file-lines'
  }
}
</script>

<template>
  <div class="w-full">
    <label
      for="workflow-file-uploader-input"
      class="dropzone"
      :class="{ 'is-dragging': isDragging }"
      role="button"
      tabindex="0"
      @keydown.enter.prevent="openFilePicker"
      @keydown.space.prevent="openFilePicker"
      @dragover.prevent="onDragOver"
      @dragenter.prevent="onDragOver"
      @dragleave.prevent="onDragLeave"
      @drop.prevent="onDrop"
    >
      <span class="dropzone-icon">
        <IniaIcon name="file-lines" />
      </span>
      <span class="dropzone-title">
        {{ $t(everyDocumentNeedsAClass ? 'shared.components.fileControl.stepOneDragOrBrowse' : 'shared.components.fileControl.dragOrBrowse') }}
        <span class="dropzone-link">{{ $t('shared.components.fileControl.browse') }}</span>
      </span>
      <span class="dropzone-hint">
        {{ $t('shared.components.fileControl.supportedFiles', { formats: supportedFormats }) }}
      </span>
    </label>

    <input
      ref="fileInput"
      class="hidden"
      id="workflow-file-uploader-input"
      type="file"
      :accept="acceptAttribute"
      multiple
      @change="onFileUpload"
    />

    <div v-if="rejectedFileNames.length" class="rejected-drop-note" role="status">
      <IniaIcon name="circle-exclamation" class="text-warning" />
      <span>
        {{ $t('shared.components.fileControl.unsupportedDropped', { names: rejectedFileNames.join(', ') }, rejectedFileNames.length) }}
      </span>
    </div>

    <div v-if="retryableFailures.length" class="upload-error-summary">
      <p class="text-sm text-error">
        {{ $t('page.createWorkflow.form.errors.uploadFailed', { count: retryableFailures.length }, retryableFailures.length) }}
      </p>
      <div class="flex gap-2">
        <Button type="button" variant="outline" size="sm" @click="retryFailedUploads">
          {{ $t('shared.components.fileControl.retry') }}
        </Button>
        <Button type="button" variant="ghost" size="sm" @click="removeFailed">
          {{ $t('shared.components.fileControl.dismiss') }}
        </Button>
        <Button type="button" variant="ghost" size="sm" @click="onCopyDiagnostics">
          {{ diagnosticsCopied ? $t('shared.components.fileControl.copied') : $t('shared.components.fileControl.copyDetails') }}
        </Button>
      </div>
    </div>

    <p v-if="everyDocumentNeedsAClass && displayDocuments?.length" class="file-list-step" data-testid="choose-class-step">
      {{ $t('shared.components.fileControl.stepTwoChooseClass') }}
    </p>

    <div v-if="displayDocuments?.length" class="file-list">
      <div
        v-for="(file, index) in displayDocuments"
        :key="file.ref"
        class="file-card"
        :class="{ 'has-error': file.error }"
      >
        <span class="file-icon" :class="{ 'is-error': file.error }">
          <IniaIcon :name="file.error ? 'circle-exclamation' : fileIcon(file.name)" />
        </span>

        <div class="file-main">
          <div class="file-name-row">
            <FormLabel class="sr-only">
              {{ $t('page.createWorkflow.form.labels.file.name') }}
            </FormLabel>
            <p class="file-name">{{ file.name }}</p>
            <span v-if="typeof file.countedPages === 'number'" class="file-pages">
              {{
                file.countedAttachmentPages
                  ? $t('shared.components.fileControl.pagesWithAttachments', { n: file.countedPages, m: file.countedAttachmentPages }, file.countedPages)
                  : $t('shared.components.fileControl.pages', { n: file.countedPages }, file.countedPages)
              }}
            </span>
          </div>

          <ul v-if="file.warnings?.length" class="file-warnings">
            <li v-for="(warning, i) in file.warnings" :key="i" class="file-warning">
              <IniaIcon name="circle-exclamation" type="solid" class="file-warning-icon" />
              <span>{{ warning }}</span>
            </li>
          </ul>

          <div v-if="file.isUploading" class="file-progress">
            <Progress
              :model-value="file.progress"
              class="flex-1"
              :aria-label="$t('shared.components.fileControl.uploadingFile', { name: file.name })"
            />
            <span class="file-progress-label">
              <IniaIcon name="spinner" class="text-warning animate-spin" />
              {{ $t('shared.components.fileControl.uploading') }}
            </span>
          </div>

          <p v-else-if="file.error" class="file-error">
            {{ file.errorDetail ?? $t(`shared.components.fileControl.${file.error}`) }}
          </p>
        </div>

        <div
          v-if="file.progress === 100 && !file.isUploading && !file.error"
          class="file-actions"
        >
          <FormField :name="`${name}[${index}].class`">
            <FormItem
              class="file-class-field"
              :class="{ 'is-unchosen': everyDocumentNeedsAClass && !file.class?.trim() }"
            >
              <FormControl>
                <IniaSelect
                  :aria-label="$t('shared.components.fileControl.classOf', { name: file.name })"
                  :model-value="file.class ?? ''"
                  @update:model-value="(val) => onUpdateClass(file.ref, val)"
                  :options="props.documentClassOptions"
                  :placeholder="everyDocumentNeedsAClass ? $t('shared.components.fileControl.chooseClass') : undefined"
                />
              </FormControl>
              <FormMessage />
            </FormItem>
          </FormField>

          <button
            type="button"
            class="file-remove"
            :aria-label="$t('shared.components.fileControl.removeFile', { name: file.name })"
            @click="onRemove(file.ref)"
          >
            <IniaIcon name="trash" />
          </button>
        </div>

        <button
          v-else-if="file.error"
          type="button"
          class="file-remove"
          :aria-label="$t('shared.components.fileControl.removeFile', { name: file.name })"
          @click="onRemove(file.ref)"
        >
          <IniaIcon name="trash" />
        </button>
      </div>
    </div>

    <component
      :is="panel"
      v-for="(panel, index) in uploadPanels"
      :key="index"
      v-model:request-fields="requestFields"
      :file-names="fileNames"
      :document-class-options="props.documentClassOptions"
    />

    <Dialog v-model:open="passwordDialogOpen">
      <DialogContent>
        <DialogHeader>
          <DialogTitle>
            {{ $t('shared.components.fileControl.passwordPrompt.title', { name: pendingPasswordDoc?.name }) }}
          </DialogTitle>
          <DialogDescription>
            {{ $t('shared.components.fileControl.passwordPrompt.description', { name: pendingPasswordDoc?.name }) }}
          </DialogDescription>
        </DialogHeader>

        <div class="flex flex-col gap-3 mt-2">
          <p class="text-xs text-muted-foreground">
            {{ $t('shared.components.fileControl.passwordPrompt.consent') }}
          </p>

          <div
            v-if="pendingPasswordDoc?.error === 'passwordIncorrect'"
            class="password-incorrect"
            role="alert"
          >
            <IniaIcon name="circle-exclamation" />
            <span>{{ $t('shared.components.fileControl.passwordPrompt.incorrect') }}</span>
          </div>

          <div class="flex flex-col gap-1">
            <Label for="workflow-file-password">
              {{ $t('shared.components.fileControl.passwordPrompt.passwordLabel') }}
            </Label>
            <Input
              id="workflow-file-password"
              v-model="passwordInput"
              type="password"
              autocomplete="off"
              @keydown.enter.prevent="submitPassword"
            />
          </div>
        </div>

        <DialogFooter>
          <Button type="button" variant="ghost" @click="skipPasswordPrompt">
            {{ $t('shared.components.fileControl.passwordPrompt.skip') }}
          </Button>
          <Button
            type="button"
            :disabled="!passwordInput || isUnlocking"
            @click="submitPassword"
          >
            {{ $t('shared.components.fileControl.passwordPrompt.unlock') }}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  </div>
</template>

<style scoped>
.dropzone {
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  gap: var(--spacing-xs);
  width: 100%;
  padding: var(--spacing-lg);
  border: 2px dashed var(--stroke);
  border-radius: var(--radius-card);
  background: var(--bg-2);
  cursor: pointer;
  text-align: center;
  transition: all 0.18s ease;
}

.dropzone:hover,
.dropzone:focus-visible {
  border-color: var(--accent-indigo-from);
  background: color-mix(in srgb, var(--accent-indigo-from) 5%, var(--bg-2));
}

.dropzone:focus-visible {
  outline: 2px solid var(--accent-indigo-from);
  outline-offset: 2px;
}

.dropzone.is-dragging {
  border-color: var(--accent-indigo-from);
  background: color-mix(in srgb, var(--accent-indigo-from) 10%, var(--bg-2));
  transform: scale(1.005);
}

.dropzone-icon {
  display: flex;
  align-items: center;
  justify-content: center;
  width: 44px;
  height: 44px;
  border-radius: 50%;
  background: color-mix(in srgb, var(--accent-indigo-from) 12%, transparent);
  color: var(--accent-indigo-from);
  font-size: 18px;
}

.dropzone-title {
  font-size: 14px;
  font-weight: 600;
  color: var(--text-primary);
}

.dropzone-link {
  color: var(--accent-indigo-from);
  text-decoration: underline;
  text-underline-offset: 2px;
}

.dropzone-hint {
  font-size: 12px;
  color: var(--text-muted);
}

.rejected-drop-note {
  display: flex;
  align-items: flex-start;
  gap: var(--spacing-xs);
  margin-top: var(--spacing-sm);
  padding: var(--spacing-sm);
  border: 1px solid var(--accent-warning-border);
  border-radius: var(--radius-card);
  background: var(--accent-warning-light);
  font-size: 13px;
  color: var(--text-secondary);
}

.upload-error-summary {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: var(--spacing-sm);
  margin-top: var(--spacing-sm);
  padding: var(--spacing-sm);
  border: 1px solid var(--accent-error-border);
  border-radius: var(--radius-card);
  background: var(--accent-error-light);
}

.file-list {
  display: flex;
  flex-direction: column;
  gap: var(--spacing-xs);
  margin-top: var(--spacing-md);
}

.file-card {
  display: flex;
  align-items: center;
  gap: var(--spacing-sm);
  padding: var(--spacing-sm);
  border: 1px solid var(--stroke);
  border-radius: var(--radius-card);
  background: var(--bg-1);
  transition: border-color 0.15s ease;
}

.file-card:hover {
  border-color: var(--stroke-hover);
}

.file-card.has-error {
  border-color: var(--accent-error-border);
  background: var(--accent-error-light);
}

.file-icon {
  display: flex;
  align-items: center;
  justify-content: center;
  width: 36px;
  height: 36px;
  flex-shrink: 0;
  border-radius: var(--radius-small);
  background: var(--bg-2);
  color: var(--text-secondary);
  font-size: 16px;
}

.file-icon.is-error {
  background: var(--accent-error-light);
  color: var(--accent-error);
}

.file-main {
  flex: 1;
  min-width: 0;
  display: flex;
  flex-direction: column;
  gap: var(--spacing-xs);
}

.file-name-row {
  display: flex;
  align-items: center;
  gap: var(--spacing-xs);
  min-width: 0;
}

.file-name {
  font-size: 14px;
  color: var(--text-primary);
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.file-pages {
  flex-shrink: 0;
  font-size: 13px;
  color: var(--text-secondary);
  font-variant-numeric: tabular-nums;
}

.file-warnings {
  display: flex;
  flex-direction: column;
  gap: var(--spacing-xs);
  margin: 0;
  padding: 0;
  list-style: none;
}

.file-warning {
  display: flex;
  align-items: flex-start;
  gap: var(--spacing-xs);
  font-size: 12px;
  color: var(--accent-warning);
}

.file-warning-icon {
  flex-shrink: 0;
  margin-top: 2px;
}

.file-progress {
  display: flex;
  align-items: center;
  gap: var(--spacing-xs);
}

.file-progress-label {
  display: flex;
  align-items: center;
  gap: 4px;
  flex-shrink: 0;
  font-size: 12px;
  color: var(--text-secondary);
}

.file-error {
  font-size: 12px;
  color: var(--accent-error);
}

.password-incorrect {
  display: flex;
  align-items: flex-start;
  gap: var(--spacing-xs);
  padding: var(--spacing-sm);
  border: 1px solid var(--accent-error-border);
  border-radius: var(--radius-card);
  background: var(--accent-error-light);
  font-size: 13px;
  color: var(--accent-error);
}

.file-actions {
  display: flex;
  align-items: center;
  gap: var(--spacing-sm);
  flex-shrink: 0;
}

.file-class-field {
  width: 16rem;
  min-width: 0;
}

.file-class-field.is-unchosen :deep(button) {
  border-color: var(--accent-indigo-from);
  box-shadow: 0 0 0 1px var(--accent-indigo-from);
}

.file-list-step {
  margin-top: var(--spacing-md);
  font-size: 14px;
  font-weight: 600;
  color: var(--text-primary);
}

.file-remove {
  display: flex;
  align-items: center;
  justify-content: center;
  width: 32px;
  height: 32px;
  flex-shrink: 0;
  border-radius: var(--radius-small);
  color: var(--text-muted);
  background: transparent;
  cursor: pointer;
  transition: all 0.15s ease;
}

.file-remove:hover {
  color: var(--accent-error);
  background: var(--accent-error-light);
}

@media (max-width: 640px) {
  .file-card {
    flex-wrap: wrap;
  }

  .file-actions {
    width: 100%;
  }

  .file-class-field {
    flex: 1;
    width: auto;
  }
}
</style>
