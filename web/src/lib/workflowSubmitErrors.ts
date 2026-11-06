export interface SubmitErrorResponseData {
  title?: string
  detail?: string
  message?: string
  errors?: Record<string, string[] | string>
}

const firstOf = (value: string[] | string | undefined): string | undefined =>
  Array.isArray(value) ? value[0] : value

const splitOnSemicolons = (value: string[] | string): string[] => {
  const parts = Array.isArray(value) ? value : [value]
  return parts.flatMap(part =>
    part.split(';').map(piece => piece.trim()).filter(piece => piece.length > 0),
  )
}

/**
 * The user-facing messages for a failed workflow submission, in display order.
 * Document-class failures keep their semicolon splitting; any other server
 * validation map entry (an unresolvable input document, a variable failure)
 * surfaces its first message instead of vanishing behind a generic toast.
 */
export function submitValidationMessages(responseData: SubmitErrorResponseData | undefined): string[] {
  const errors = responseData?.errors

  const docClassErrors = errors?.documentClasses ?? errors?.DocumentClasses
  if (docClassErrors) {
    return splitOnSemicolons(docClassErrors)
  }

  if (errors) {
    const firstKey = Object.keys(errors)[0]
    const firstError = firstKey ? firstOf(errors[firstKey]) : undefined
    if (firstError) {
      return [firstError]
    }
  }

  if (responseData?.title?.includes('Validation') || responseData?.detail?.includes('Document')) {
    return [responseData.detail || responseData.title || 'Document validation failed']
  }

  if (responseData?.message?.includes('Document') || responseData?.message?.includes('validation')) {
    return [responseData.message]
  }

  return []
}

const DOCUMENT_PROBLEM = /document/i

/**
 * Whether a refused submission is about the attached documents, so the user fixes it on the
 * Documents step. Any other refusal, such as one about the account, stays on the step the user
 * submitted from.
 */
export function isDocumentProblem(responseData: SubmitErrorResponseData | undefined): boolean {
  const errors = responseData?.errors
  if (errors && Object.keys(errors).length > 0) {
    return Object.keys(errors).some(key => DOCUMENT_PROBLEM.test(key))
  }
  return submitValidationMessages(responseData).length > 0
}
