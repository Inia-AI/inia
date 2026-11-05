export enum WorkflowState {
  Undefined = 'Undefined',
  Running = 'Running',
  Draft = 'Draft',
  Confirmed = 'Confirmed',
  Failed = 'Failed',
  ProcessingDocuments = 'ProcessingDocuments',
  Queued = 'Queued',
  Cancelled = 'Cancelled',
}

export enum WorkflowOutputType {
  None = 'None',
  Workflow = 'Workflow',
  Section = 'Section',
  Paragraph = 'Paragraph',
  Divider = 'Divider',
  Table = 'Table',
  DynamicChart = 'DynamicChart',
}

export enum ReportParagraphKind {
  Paragraph = 'Paragraph',
  DynamicChart = 'DynamicChart',
  Divider = 'Divider',
  Table = 'Table',
}

export enum BackgroundJobState {
  None = 'None',
  Processing = 'Processing',
  Done = 'Done',
  Error = 'Error',
}

export enum BackgroundJobType {
  None = 'None',
  PdfGeneration = 'PdfGeneration',
}
