import { describe, expect, it } from 'vitest'
import { submitValidationMessages } from './workflowSubmitErrors'

describe('submitValidationMessages', () => {
  it('splits documentClasses errors on semicolons', () => {
    const messages = submitValidationMessages({
      errors: {
        documentClasses: ["Required document class 'Annual Report' is missing; Required document class 'Financials' is missing"],
      },
    })
    expect(messages).toEqual([
      "Required document class 'Annual Report' is missing",
      "Required document class 'Financials' is missing",
    ])
  })

  it('accepts the PascalCase DocumentClasses key and a bare string value', () => {
    const messages = submitValidationMessages({
      errors: { DocumentClasses: "Required document class 'Annual Report' is missing" },
    })
    expect(messages).toEqual(["Required document class 'Annual Report' is missing"])
  })

  it('surfaces any other server validation map entry, such as an unresolvable input document', () => {
    const messages = submitValidationMessages({
      title: 'One or more validation errors occurred.',
      errors: {
        'workflowExecution.UnresolvableInputDocuments': [
          "The submission names input documents that do not exist in this organization: 'doc-dead'. Remove these documents from the submission, or upload them again, then resubmit.",
        ],
      },
    })
    expect(messages).toEqual([
      "The submission names input documents that do not exist in this organization: 'doc-dead'. Remove these documents from the submission, or upload them again, then resubmit.",
    ])
  })

  it('falls back to title or detail mentioning validation or documents', () => {
    expect(submitValidationMessages({ title: 'Validation failed', detail: undefined })).toEqual(['Validation failed'])
    expect(submitValidationMessages({ detail: 'Document limit exceeded' })).toEqual(['Document limit exceeded'])
  })

  it('falls back to a message mentioning documents or validation', () => {
    expect(submitValidationMessages({ message: 'Document processing failed' })).toEqual(['Document processing failed'])
  })

  it('returns an empty list when nothing matches', () => {
    expect(submitValidationMessages({ title: 'Server exploded' })).toEqual([])
    expect(submitValidationMessages(undefined)).toEqual([])
  })
})
