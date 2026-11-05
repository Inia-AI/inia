import type { InjectionKey } from 'vue'

/**
 * Opens the report's feedback panel with the reader's highlighted text quoted. Provided by the
 * report page; report content that is shown anywhere else does not offer feedback.
 */
export const OPEN_REPORT_FEEDBACK_KEY = Symbol('openReportFeedback') as InjectionKey<
  (quotedText: string) => void
>
