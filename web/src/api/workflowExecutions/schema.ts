// Create Types

export interface PostDocumentInfoRequest {
  fileId: string
  documentClass?: string
  documentTags: []
}

export interface StartWorkflowExecutionRequest {
  workflowId: string
  files: PostDocumentInfoRequest[]
  counterpartyId?: string
  peerCompanyIds?: string[]
  variables: Record<string, any>
  expandEmails?: boolean
  attachmentsDocumentClass?: string
}

export type ReRunInputReason = 'NoValue' | 'InvalidValue' | 'Confidential'

export interface ReRunVariableValue {
  replacementTag: string
  value: unknown
}

export interface ReRunVariableNeedingInput {
  replacementTag: string
  displayName: string
  reason: ReRunInputReason
  message: string
}

export interface ReRunVariableChange {
  replacementTag: string
  displayName: string
}

export interface ReRunDocumentClassNeedingInput {
  key: string
  displayName: string
  message: string
}

export interface ReRunUnavailableDocument {
  documentId: string
  fileName?: string | null
}

export interface ReRunInputs {
  workflowId: string
  workflowTypeId: string
  isReady: boolean
  variables: ReRunVariableValue[]
  variablesNeedingInput: ReRunVariableNeedingInput[]
  defaultedVariables: ReRunVariableChange[]
  removedVariables: ReRunVariableChange[]
  documentClassesNeedingInput: ReRunDocumentClassNeedingInput[]
  unavailableDocuments: ReRunUnavailableDocument[]
}

/**
 * Lightweight summary for list view - fast initial load
 */
export interface WorkflowExecutionLogSummary {
  id: string
  blockName: string
  sectionName?: string
  blockType?: string
  replacementTag?: string
  modelUsed?: string
  status: 'NotExecuted' | 'Running' | 'Completed' | 'Failed' | 'Skipped'
  error?: string
  refusalCategory?: string | null
  shouldDisplayInLogs?: boolean
  createdAt?: string
  hasPrompt?: boolean
  hasOutput?: boolean
  hasInputs?: boolean
}

export interface PromptPlaceholderValue {
  value: string
  translated: boolean
  truncated: boolean
}

/**
 * Full log details - loaded on demand when expanding.
 * Only contains content fields (prompt, output, inputs).
 * Summary/metadata fields come from WorkflowExecutionLogSummary.
 */
export interface WorkflowExecutionLogDetail {
  id: string
  inputs?: Record<string, any>
  prompt?: string
  promptPlaceholderValues?: Record<string, PromptPlaceholderValue>
  output?: string
}

/**
 * @deprecated Use WorkflowExecutionLogSummary for list and WorkflowExecutionLogDetail for details
 */
export interface WorkflowExecutionLog {
  id: string
  blockName: string
  sectionName?: string
  blockType?: string
  replacementTag?: string
  modelUsed?: string
  status: 'NotExecuted' | 'Running' | 'Completed' | 'Failed' | 'Skipped'
  inputs?: Record<string, any>
  prompt?: string
  promptPlaceholderValues?: Record<string, string>
  output?: string
  error?: string
  shouldDisplayInLogs?: boolean
  createdAt?: string
  workflowOutputElementId?: string
}

