import { Workflow } from '../workflows/schema'

export interface WorkflowCategory {
  id: string
  name: string
  description?: string
  workflows: Workflow[]
}

/**
 * Lightweight workflow info for initial page load.
 * Contains only the minimum fields needed to display workflow names.
 */
export interface WorkflowSummary {
  id: string
  workflowTypeId: string
  workflowTypeName?: string
  isAvailable: boolean
}

/**
 * Lightweight category summary for initial page load.
 * Contains workflow names but not full workflow details (no Variables, DocumentClasses).
 */
export interface WorkflowCategorySummary {
  id: string
  name: string
  description?: string
  displayOrder?: number
  workflows?: WorkflowSummary[]
}
