import { ref, computed } from 'vue'
import { useI18n } from 'vue-i18n'
import axios from 'axios'
import { v4 as uuidv4 } from 'uuid'
import API from '@/api'
import { extractApiErrorMessage } from '@/api/base/errors'
import { FILE_UPLOAD_ERROR_CODES } from '@/api/files/schema'
import type { Translate } from '@/api/base/dailyAllowance'
import { uploadLimitMessage } from '@/api/files/uploadLimits'
import type { DocumentInfo } from '@/types'

export type UploadErrorCode =
  | 'generic'
  | 'refused'
  | 'blockedBeforeServer'
  | 'passwordRequired'
  | 'passwordIncorrect'
  | 'passwordUnsupported'
  | 'limited'

/**
 * Whether sending the same file again can clear each failure. A refusal, a protection the
 * platform cannot open and an email that cannot be converted are verdicts on the bytes; a
 * password failure stays retryable because a retry reopens the password prompt.
 */
const CLEARS_ON_RETRY: Record<UploadErrorCode, boolean> = {
  generic: true,
  blockedBeforeServer: true,
  passwordRequired: true,
  passwordIncorrect: true,
  refused: false,
  passwordUnsupported: false,
  limited: true,
}

export interface UploadableDocument {
  id: string | undefined
  ref: string
  class: string | undefined
  file: File
  name: string
  progress: number
  isUploading: boolean
  error: UploadErrorCode | undefined
  errorDetail: string | undefined
  warnings: string[] | undefined
  countedPages: number | null | undefined
  countedAttachmentPages: number | null | undefined
}

export interface UploadError {
  code: UploadErrorCode
  detail: string | undefined
}

export function toUploadError(err: unknown, t: Translate): UploadError {
  if (!axios.isAxiosError(err)) {
    return { code: 'generic', detail: undefined }
  }
  if (!err.response) {
    return { code: 'blockedBeforeServer', detail: undefined }
  }
  const limit = uploadLimitMessage(err, t)
  if (limit) {
    return { code: 'limited', detail: limit }
  }
  if (err.response.status === 400) {
    const errors = err.response.data?.errors
    if (errors && typeof errors === 'object') {
      // A platform older than the verbatim-key change camelCases the first segment of an error key
      // (File.Password.Required arrives as file.Password.Required), so match case-insensitively.
      const entries = Object.entries(errors as Record<string, unknown>)
      const hasCode = (code: string) => entries.some(([key]) => key.toLowerCase() === code.toLowerCase())
      if (hasCode(FILE_UPLOAD_ERROR_CODES.passwordRequired)) {
        return { code: 'passwordRequired', detail: undefined }
      }
      if (hasCode(FILE_UPLOAD_ERROR_CODES.passwordIncorrect)) {
        return { code: 'passwordIncorrect', detail: undefined }
      }
      if (hasCode(FILE_UPLOAD_ERROR_CODES.passwordUnsupported)) {
        return { code: 'passwordUnsupported', detail: undefined }
      }
      const refusal = entries.find(([key]) =>
        key.toLowerCase().startsWith(FILE_UPLOAD_ERROR_CODES.refusedPrefix.toLowerCase()),
      )
      if (refusal) {
        return { code: 'refused', detail: firstMessage(refusal[1]) }
      }
    }
  }
  return { code: 'generic', detail: undefined }
}

function firstMessage(messages: unknown): string | undefined {
  return Array.isArray(messages) && typeof messages[0] === 'string' ? messages[0] : undefined
}

const UPLOAD_ERROR_MESSAGE_PREFIX = 'shared.components.fileControl.'

/** The message for a failed upload: the platform's own detail, a known refusal, or the fallback. */
export function describeUploadError(error: unknown, t: Translate, fallback: string): string {
  const { code, detail } = toUploadError(error, t)
  if (detail) return detail
  return code === 'generic' ? extractApiErrorMessage(error, fallback) : t(`${UPLOAD_ERROR_MESSAGE_PREFIX}${code}`)
}

/**
 * Composable for handling document uploads with proper state management
 * Prevents infinite loops by managing upload state internally
 */
export function useDocumentUpload() {
  const { t } = useI18n()
  const documents = ref<UploadableDocument[]>([])
  const { mutateAsync: uploadDocument } = API.Files.useUploadDocument()

  // Track which documents are currently uploading by their ref
  const uploadingRefs = new Set<string>()

  const isAnyUploading = computed(() =>
    documents.value.some(doc => doc.isUploading)
  )

  const completedDocuments = computed(() =>
    documents.value.filter(doc => doc.id && !doc.isUploading)
  )

  const retryableFailures = computed(() =>
    documents.value.filter(doc => doc.error !== undefined && CLEARS_ON_RETRY[doc.error])
  )

  /**
   * Add files to the upload queue and start uploading
   */
  async function addFiles(files: FileList | File[]) {
    const fileArray = Array.from(files)

    // Filter out files that are already in the list (by name, for duplicates)
    const existingNames = new Set(documents.value.map(d => d.name))
    const newFiles = fileArray.filter(f => !existingNames.has(f.name))

    if (newFiles.length === 0) return

    // Create document entries for new files
    const newDocuments: UploadableDocument[] = newFiles.map(file => ({
      id: undefined,
      ref: uuidv4(),
      class: undefined,
      file,
      name: file.name,
      progress: 0,
      isUploading: true,
      error: undefined,
      errorDetail: undefined,
      warnings: undefined,
      countedPages: undefined,
      countedAttachmentPages: undefined,
    }))

    // Add to documents list
    documents.value = [...documents.value, ...newDocuments]

    // Upload each new document
    await Promise.all(
      newDocuments.map(doc => uploadSingleDocument(doc))
    )
  }

  /**
   * Upload a single document. When a password is supplied the file is
   * re-sent with it so the backend can decrypt a password-protected file at ingest.
   */
  async function uploadSingleDocument(doc: UploadableDocument, password?: string) {
    // Skip if already uploading or already has an ID
    if (uploadingRefs.has(doc.ref) || doc.id) {
      return
    }

    uploadingRefs.add(doc.ref)
    doc.isUploading = true
    doc.error = undefined
    doc.errorDetail = undefined

    try {
      const formData = new FormData()
      formData.append('file', doc.file, doc.name)
      if (password) {
        formData.append('password', password)
      }

      const response = await uploadDocument({
        formData,
        onUploadProcessChange: (progressVal: number) => {
          // Update progress on the document directly
          const targetDoc = documents.value.find(d => d.ref === doc.ref)
          if (targetDoc) {
            targetDoc.progress = progressVal
          }
        },
      })

      // Update document with response
      const targetDoc = documents.value.find(d => d.ref === doc.ref)
      if (targetDoc) {
        targetDoc.id = response?.id
        targetDoc.progress = 100
        targetDoc.isUploading = false
        targetDoc.warnings = response?.warnings
        targetDoc.countedPages = response?.countedPages
        targetDoc.countedAttachmentPages = response?.countedAttachmentPages
      }
    } catch (err) {
      const { code: errorCode, detail } = toUploadError(err, t)
      // Never log the raw error: its request config carries the FormData, password included.
      console.error(
        '[useDocumentUpload] Upload failed:',
        doc.name,
        errorCode,
        axios.isAxiosError(err) ? `HTTP ${err.response?.status ?? 'no-response'}` : String(err),
      )
      const targetDoc = documents.value.find(d => d.ref === doc.ref)
      if (targetDoc) {
        targetDoc.error = errorCode
        targetDoc.errorDetail = detail
        targetDoc.isUploading = false
        targetDoc.progress = 0
      }
    } finally {
      uploadingRefs.delete(doc.ref)
    }
  }

  /**
   * Re-upload a single document with a password so the backend can decrypt it.
   * The row's error is reset before the attempt and re-flagged (e.g. passwordIncorrect)
   * if the password is wrong, so the caller can keep prompting.
   */
  async function uploadWithPassword(docRef: string, password: string) {
    const targetDoc = documents.value.find(d => d.ref === docRef)
    if (!targetDoc) {
      return
    }
    targetDoc.error = undefined
    targetDoc.errorDetail = undefined
    await uploadSingleDocument(targetDoc, password)
  }

  /**
   * Retry failed uploads
   */
  async function retryFailed() {
    const failedDocs = retryableFailures.value.filter(doc => !doc.id)
    await Promise.all(failedDocs.map(doc => {
      doc.error = undefined
      doc.errorDetail = undefined
      return uploadSingleDocument(doc)
    }))
  }

  /**
   * Remove a document from the list
   */
  function removeDocument(docRef: string) {
    documents.value = documents.value.filter(d => d.ref !== docRef)
  }

  /**
   * Remove the failures a resend could clear: the ones the retry banner counts. A refused file
   * keeps its row and its reason until it is removed on its own.
   */
  function removeFailed() {
    const dismissed = new Set(retryableFailures.value)
    documents.value = documents.value.filter(d => !dismissed.has(d))
  }

  /**
   * Update a document's class
   */
  function updateDocumentClass(docRef: string, newClass: string | undefined) {
    const targetDoc = documents.value.find(d => d.ref === docRef)
    if (targetDoc) {
      targetDoc.class = newClass
      // Trigger reactivity by reassigning
      documents.value = [...documents.value]
    }
  }

  /**
   * Set documents from external source (e.g., initial values)
   */
  function setDocuments(docs: DocumentInfo[] | undefined) {
    if (!docs) {
      documents.value = []
      return
    }

    documents.value = docs.map(doc => ({
      id: doc.id,
      ref: doc.ref || uuidv4(),
      class: doc.class,
      file: doc.file,
      name: doc.name,
      progress: doc.id ? 100 : doc.progress || 0,
      isUploading: false,
      error: undefined,
      errorDetail: undefined,
      warnings: doc.warnings,
      countedPages: doc.countedPages,
      countedAttachmentPages: doc.countedAttachmentPages,
    }))
  }

  /**
   * Get documents in the format expected by the form
   */
  function getFormDocuments(): DocumentInfo[] {
    return documents.value.filter(doc => !doc.error).map(doc => ({
      id: doc.id,
      ref: doc.ref,
      class: doc.class,
      file: doc.file,
      name: doc.name,
      progress: doc.progress,
      warnings: doc.warnings,
      countedPages: doc.countedPages,
      countedAttachmentPages: doc.countedAttachmentPages,
      onProcessChange: () => {}, // Not used when upload is handled internally
    }))
  }

  /**
   * Clear all documents
   */
  function clear() {
    documents.value = []
    uploadingRefs.clear()
  }

  return {
    documents,
    isAnyUploading,
    completedDocuments,
    retryableFailures,
    addFiles,
    removeDocument,
    removeFailed,
    retryFailed,
    uploadWithPassword,
    updateDocumentClass,
    setDocuments,
    getFormDocuments,
    clear,
  }
}
