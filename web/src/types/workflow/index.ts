export interface DocumentInfo {
  id: string | undefined
  ref: string
  class: string | undefined
  file: File
  name: string
  progress: number
  warnings?: string[]
  countedPages?: number | null
  countedAttachmentPages?: number | null
  onProcessChange: (progress: number) => void
}

export interface WorkflowFormData {
  type: string | undefined
  documents: DocumentInfo[] | undefined
  counterpartyId: string | undefined
  peerCompanyIds: string[] | undefined
  variables: Record<string, any> | undefined
}
