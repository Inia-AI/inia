import type { ODataQueryParams } from '@/api/base/schema'

/**
 * Server-side MediaType enum (PascalCase, e.g. `ApplicationPdf`). Left as a
 * plain `string` on the FE because the enum is long and backend may add
 * entries. Use `mediaTypeGroup`/`formatMediaType` from `@/lib/formatMediaType`
 * to bucket raw values into friendly UI labels.
 */
export type MediaType = string

export type PageStatus =
  | 'NotProcessed'
  | 'ProcessedSuccessfully'
  | 'ExtractedWithErrors'
  | 'OCRFallback'
  | 'OCRSuccessful'
  | 'OCRFailed'
  | (string & {})

export type DocumentProcessingState =
  | 'NotRequested'
  | 'Queued'
  | 'Processed'
  | 'Failed'
  | (string & {})

export const isDocumentProcessing = (state: DocumentProcessingState | undefined): boolean =>
  state !== undefined && state !== 'Processed' && state !== 'Failed'

export interface DocumentSummary {
  id: string
  name: string
  mediaType: MediaType
  sizeBytes: number
  extension: string
  userId: string | null
  documentCategoryId: string | null
  workflowTypeId: string | null
  class: string | null
  processingState: DocumentProcessingState
  processingError: string | null
  createdAt: string
  updatedAt: string | null
}

export interface DocumentDetail extends DocumentSummary {
  description: string | null
  sources: string[]
  topics: string | null
  tableOfContents: string | null
  pageCount: number
  fileMetadataId: string
}

export interface PageSummary {
  id: string
  documentId: string
  pageNumber: number
  filePageNumber: number
  textPreview: string | null
  overview: string | null
  location: string
  status: PageStatus
  hasEmbedding: boolean
  embeddingModel: string | null
  sourceUrl: string | null
}

export interface PageDetail extends PageSummary {
  text: string | null
  readableText: string | null
  embeddingDimensions: number | null
  sectionTitles: string | null
}

export interface DocumentPagesQueryParams extends ODataQueryParams {
  term?: string
}

export interface UpdateDocumentRequest {
  documentCategoryId?: string
  workflowTypeId?: string
  class?: string
  isStatic?: boolean
}

export interface DocumentTextFile {
  blob: Blob
  filename: string | null
}

export interface DocumentsQueryParams extends ODataQueryParams {
  jobId?: string
}

export interface BulkDeleteRequest {
  ids: string[]
}

export interface BulkDeleteResult {
  deleted: string[]
  failed: Record<string, string>
}
