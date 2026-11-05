export interface DownloadExecutedWorkflowParams {
  executionId?: string
  workflowId?: string
  jobId?: string
}

export interface DownloadExecutedWorkflowResult {
  blob: Blob
  filename: string
}
