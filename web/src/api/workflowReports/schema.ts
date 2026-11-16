import {
  WorkflowState,
  ReportParagraphKind,
  WorkflowVariableTypes,
} from '@/enums'
import type { BlockCitedSources, Citation } from '@/api/workflows/schema'
import type { AgentBlockActivity } from '@/api/agentActivity/schema'
import type { ChartConfig } from '@/api/dynamicChart/schema'

export type ReportExportFormat = 'word' | 'markdown'

// Document types for workflow execution

export interface WorkflowExecutionDocument {
  fileId: string
  name: string
  extension: string
  sizeBytes?: number
  documentClass?: string
  documentClassDisplayName?: string
  sources?: string[]
  shouldBeDisplayedInOutput: boolean
  isOutputDocument?: boolean
  /** True when the document was deleted after the run read it: it cannot be opened or downloaded. */
  isDeleted?: boolean
  /** When the run took this document out of its input before processing it. Null for a document the run used. */
  excludedAt?: string | null
  /** Why the run took this document out of its input, such as Unreadable, NotExpectedDocument or NotNeeded. Null for a document the run used. */
  exclusionReason?: string | null
}

export interface WorkflowExecutionVariableValue {
  replacementTag: string
  displayName: string
  description?: string | null
  type: WorkflowVariableTypes
  displayValue?: string | null
  isConfidential: boolean
  isHidden: boolean
  isRequired: boolean
}

// List types

export interface WorkflowReportSummary {
  id: string
  workflowId?: string
  createdAt?: string
  workflowState: WorkflowState
  /** Why the run ended Failed: a platform failure kind such as RejectedAtIntake or LifetimeExceeded. Null otherwise. */
  failureKind?: string | null
  /** The platform's explanation of the failure. Null when there is nothing to explain. */
  failureDetail?: string | null
  workflowTypeId?: string
  /** The list endpoint never sends this. Map rows through useWorkflowTypeNames().withTypeName to fill it. */
  workflowTypeName?: string
  counterpartyId?: string
  counterpartyName?: string
  analystName?: string
  reviewDate?: string
  userId?: string
  title?: string
  jobId?: string
  isBaseline?: boolean
  /** Whether every live validation case behind the baseline is Active. Null/absent when not a baseline. */
  baselineVerified?: boolean | null
  /** Set when the execution replayed external fetches from another execution (a test artifact). */
  freezeSourceExecutionId?: string | null
  freezeReplayedCount?: number | null
  freezeEligibleCount?: number | null
  /** UTC ISO timestamp when the worker dequeued the execution. Null while queued. */
  startedAt?: string
  /** UTC ISO timestamp when the execution reached Draft or Failed. Null until terminal. */
  completedAt?: string
  /** .NET TimeSpan string ("hh:mm:ss[.fffffff]"). Null while still queued or running. */
  duration?: string
}

export const WORKFLOW_ACTIVITY_SCHEMA_VERSION = 1

export type WorkflowActivityStepStatus = 'Done' | 'Active' | 'Pending'

export interface WorkflowActivityStep {
  label: string
  status?: WorkflowActivityStepStatus
}

/** Resolved server-side from the workflow version's cached activity script. */
export interface WorkflowActivity {
  schemaVersion: number
  currentStepIndex: number
  steps: WorkflowActivityStep[]
}

export interface NextWorkflowDocument {
  fileId: string
  name: string
  extension: string
  /** The class of the next workflow that the document goes to. */
  documentClass: string
}

export interface NextWorkflow {
  workflowId: string
  workflowTypeId: string
  workflowTypeName: string
  creditCost?: number | null
  documents: NextWorkflowDocument[]
}

export interface WorkflowReport {
  id: string
  workflowId?: string
  createdAt?: string
  workflowState: WorkflowState
  /** Why the run ended Failed: a platform failure kind such as RejectedAtIntake or LifetimeExceeded. Null otherwise. */
  failureKind?: string | null
  /** The platform's explanation of the failure. Null when there is nothing to explain. */
  failureDetail?: string | null
  workflowTypeId?: string
  workflowTypeName?: string
  counterpartyId?: string
  counterpartyName?: string
  analystName?: string
  reviewDate?: string
  userId?: string
  title?: string
  variables?: Record<string, unknown>
  variableValues?: WorkflowExecutionVariableValue[]
  jobId?: string
  isBaseline?: boolean
  /** Whether every live validation case behind the baseline is Active. Null/absent when not a baseline. */
  baselineVerified?: boolean | null
  workflowExecutionDocuments?: WorkflowExecutionDocument[]
  /** The workflows a finished run leads to, each with the documents of the run it takes. Absent until the run finishes. */
  nextWorkflows?: NextWorkflow[] | null
  /** Documents the caller sent that the run did not use, each with its exclusion reason. Not in workflowExecutionDocuments. */
  excludedDocuments?: WorkflowExecutionDocument[]
  /** Set when the execution replayed external fetches from another execution (a test artifact). */
  freezeSourceExecutionId?: string | null
  freezeReplayedCount?: number | null
  freezeEligibleCount?: number | null
  /** UTC ISO timestamp when the worker dequeued the execution. Null while queued. */
  startedAt?: string
  /** UTC ISO timestamp when the execution reached Draft or Failed. Null until terminal. */
  completedAt?: string
  /** .NET TimeSpan string ("hh:mm:ss[.fffffff]"). Null until terminal. */
  duration?: string
  /** Live agent-block activity; present only on the GET endpoint while runs are live. */
  agentActivity?: AgentBlockActivity[] | null
  /** Classic-run activity; present only on the GET endpoint while the run is Running. */
  workflowActivity?: WorkflowActivity | null
}

// Report tree types

export interface GridElementPosition {
  blockOrder: number
  row: number
  column: number
  rowSpan: number
  columnSpan: number
}

export interface GridLayoutOutput {
  outputType?: string
  mode?: string
  columns?: number
  elements?: GridElementPosition[]
}

export interface ChartDataPoint {
  x: string
  y: number
  series: string
  rawData?: Record<string, unknown> | null
}

export interface ChartOutput {
  outputType?: string
  name?: string
  chartData?: ChartDataPoint[]
  chartConfig?: ChartConfig
}

export interface TableConfigColumn {
  header?: string
  format?: string
  groupName?: string | null
}

export interface TableOutput {
  outputType?: string
  name?: string
  tableName?: string
  tableConfig?: TableConfigColumn[]
  tableData?: string[][]
}

export interface ReportParagraph {
  id: string
  kind: ReportParagraphKind
  order?: number
  title?: string | null
  subtitle?: string | null
  confidence?: number | null
  content?: string | null
  replacementTag?: string | null
  chartPayload?: ChartOutput | null
  tablePayload?: TableOutput | null
  sources?: string[]
  citations?: Citation[]
  citedSourcesByBlock?: BlockCitedSources[]
}

export interface ReportSection {
  id: string
  order?: number
  title?: string | null
  layout?: GridLayoutOutput | null
  excludeFromWordExport?: boolean
  paragraphs?: ReportParagraph[]
}

/**
 * A citation of one figure inside a data output's JSON. `path` is the JSON Pointer of the value
 * that holds the figure, and `value` the figure as that value writes it.
 */
export interface DataOutputCitation extends Citation {
  path: string
  value: string
}

/**
 * One JSON a run returns beside its report, as the block produced it.
 */
export interface ReportDataOutput {
  name: string
  data: unknown
  citations: DataOutputCitation[]
  citedSourcesByBlock: BlockCitedSources[]
}

export interface UpdateReportParagraphRequest {
  title?: string | null
  subtitle?: string | null
  confidence?: number | null
  content?: string | null
}

// Report feedback: a reader's comment on a report, kept with the execution. No element or block linkage.

export enum ReportFeedbackCategory {
  WrongFigure = 'WrongFigure',
  MissingInformation = 'MissingInformation',
  WrongClassification = 'WrongClassification',
  WordingOrStyle = 'WordingOrStyle',
  NotRelevant = 'NotRelevant',
}

export enum ReportFeedbackStatus {
  Open = 'Open',
  Addressed = 'Addressed',
  Dismissed = 'Dismissed',
}

export interface ReportFeedbackItem {
  id: string
  workflowExecutionId: string
  workflowTypeId?: string | null
  /** The reader's own text selection, plain text; null for feedback on the report as a whole. */
  quotedText?: string | null
  category: ReportFeedbackCategory
  comment: string
  authorUserId: string
  authorDisplayName: string
  status: ReportFeedbackStatus
  dismissReason?: string | null
  /** The editor who last set `status`; null while the item is as the reader left it. */
  statusChangedByUserId?: string | null
  createdAt: string
  updatedAt?: string | null
  /** Populated only on the editor's cross-organization read; null on the per-report reads. */
  organizationId?: string | null
}

export interface CreateReportFeedbackRequest {
  quotedText?: string | null
  category: ReportFeedbackCategory
  comment: string
}

export interface UpdateReportFeedbackStatusRequest {
  status: ReportFeedbackStatus
  /** Required when `status` is Dismissed, ignored otherwise. */
  dismissReason?: string | null
}
