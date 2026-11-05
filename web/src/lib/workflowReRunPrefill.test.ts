import { describe, expect, it } from 'vitest'
import { buildChangeDocumentsPrefill, buildReRunPrefill } from './workflowReRunPrefill'
import { WorkflowState, WorkflowVariableTypes } from '@/enums'
import type {
  WorkflowExecutionVariableValue,
  WorkflowReport,
} from '@/api/workflowReports/schema'

const baseReport: WorkflowReport = {
  id: 'exec-1',
  workflowId: 'wf-snapshot-1',
  workflowTypeId: 'type-abc',
  workflowState: WorkflowState.Draft,
  counterpartyId: 'cp-9',
  variables: { revenue: 1000, region: 'NA' },
  workflowExecutionDocuments: [
    {
      fileId: 'file-input-1',
      name: 'Form10K',
      extension: '.pdf',
      documentClass: 'Form10K',
      shouldBeDisplayedInOutput: false,
    },
    {
      fileId: 'file-input-2',
      name: 'extra',
      extension: '.pdf',
      documentClass: '   ',
      shouldBeDisplayedInOutput: false,
    },
    {
      fileId: 'file-output-1',
      name: 'report',
      extension: '.docx',
      documentClass: 'GeneratedReport',
      shouldBeDisplayedInOutput: true,
      isOutputDocument: true,
    },
  ],
}

const CANONICAL_ID = 'canonical-wf-abc'

const variableValue = (
  replacementTag: string,
): WorkflowExecutionVariableValue => ({
  replacementTag,
  displayName: replacementTag,
  type: WorkflowVariableTypes.String,
  isConfidential: false,
  isHidden: false,
  isRequired: true,
})

describe('buildReRunPrefill', () => {
  it('uses the canonical workflow id (not the snapshot id) for the form type', () => {
    const prefill = buildReRunPrefill(baseReport, CANONICAL_ID)
    expect(prefill.workflowId).toBe(CANONICAL_ID)
    expect(prefill.workflowId).not.toBe(baseReport.workflowId)
  })

  it('carries through workflowTypeId so the on-demand details fetch can fire immediately', () => {
    const prefill = buildReRunPrefill(baseReport, CANONICAL_ID)
    expect(prefill.workflowTypeId).toBe('type-abc')
  })

  it('preserves counterpartyId and variables', () => {
    const prefill = buildReRunPrefill(baseReport, CANONICAL_ID)
    expect(prefill.counterpartyId).toBe('cp-9')
    expect(prefill.variables).toEqual({ revenue: 1000, region: 'NA' })
  })

  it('drops a whitespace-only counterpartyId', () => {
    const prefill = buildReRunPrefill(
      { ...baseReport, counterpartyId: '   ' },
      CANONICAL_ID,
    )
    expect(prefill.counterpartyId).toBeUndefined()
  })

  it('returns an empty variables object when the execution had none', () => {
    const prefill = buildReRunPrefill(
      { ...baseReport, variables: undefined },
      CANONICAL_ID,
    )
    expect(prefill.variables).toEqual({})
  })

  it('excludes output documents from the prefill', () => {
    const prefill = buildReRunPrefill(baseReport, CANONICAL_ID)
    expect(prefill.documents.map(d => d.fileId)).toEqual([
      'file-input-1',
      'file-input-2',
    ])
  })

  it('keeps name + extension so the uploader can render a recognizable row', () => {
    const prefill = buildReRunPrefill(baseReport, CANONICAL_ID)
    const first = prefill.documents[0]
    expect(first?.name).toBe('Form10K')
    expect(first?.extension).toBe('.pdf')
  })

  it('preserves the assigned document class', () => {
    const prefill = buildReRunPrefill(baseReport, CANONICAL_ID)
    expect(prefill.documents[0]?.documentClass).toBe('Form10K')
  })

  it('treats whitespace-only document class as no class', () => {
    const prefill = buildReRunPrefill(baseReport, CANONICAL_ID)
    expect(prefill.documents[1]?.documentClass).toBeUndefined()
  })

  it('returns no documents when the execution had none', () => {
    const prefill = buildReRunPrefill(
      { ...baseReport, workflowExecutionDocuments: undefined },
      CANONICAL_ID,
    )
    expect(prefill.documents).toEqual([])
  })

  it('restores definition-cased variable keys camelCased by the report serializer', () => {
    const prefill = buildReRunPrefill(
      {
        ...baseReport,
        variables: { dscr: 0.8, netLeverage: 'x4.5', ebitdA2: 'high' },
        variableValues: [
          variableValue('dscr'),
          variableValue('NetLeverage'),
          variableValue('EBITDA2'),
        ],
      },
      CANONICAL_ID,
    )
    expect(prefill.variables).toEqual({
      dscr: 0.8,
      NetLeverage: 'x4.5',
      EBITDA2: 'high',
    })
  })

  it('keeps variable keys that match no replacement tag', () => {
    const prefill = buildReRunPrefill(
      {
        ...baseReport,
        variables: { orphan: 1 },
        variableValues: [variableValue('NetLeverage')],
      },
      CANONICAL_ID,
    )
    expect(prefill.variables).toEqual({ orphan: 1 })
  })

  it('prefers an exact tag match over a case-insensitive one', () => {
    const prefill = buildReRunPrefill(
      {
        ...baseReport,
        variables: { RATE: 'upper' },
        variableValues: [variableValue('Rate'), variableValue('RATE')],
      },
      CANONICAL_ID,
    )
    expect(prefill.variables).toEqual({ RATE: 'upper' })
  })

  it('does not remap a key whose lowercase form matches multiple tags', () => {
    const prefill = buildReRunPrefill(
      {
        ...baseReport,
        variables: { rate: 'ambiguous' },
        variableValues: [variableValue('Rate'), variableValue('RATE')],
      },
      CANONICAL_ID,
    )
    expect(prefill.variables).toEqual({ rate: 'ambiguous' })
  })

  it('never lets a case-variant key overwrite an exact-match value', () => {
    const prefill = buildReRunPrefill(
      {
        ...baseReport,
        variables: { Rate: 'exact', rate: 'variant' },
        variableValues: [variableValue('Rate')],
      },
      CANONICAL_ID,
    )
    expect(prefill.variables).toEqual({ Rate: 'exact', rate: 'variant' })
  })

  it('opens an ordinary re-run on the first step', () => {
    expect(buildReRunPrefill(baseReport, CANONICAL_ID).startStep).toBeUndefined()
  })

  it('leaves variable keys untouched when the execution has no variableValues', () => {
    const prefill = buildReRunPrefill(
      { ...baseReport, variables: { NetLeverage: 'x4.5' } },
      CANONICAL_ID,
    )
    expect(prefill.variables).toEqual({ NetLeverage: 'x4.5' })
  })
})

describe('buildChangeDocumentsPrefill', () => {
  const refused: WorkflowReport = {
    ...baseReport,
    workflowState: WorkflowState.Failed,
    failureKind: 'RejectedAtIntake',
    excludedDocuments: [
      {
        fileId: 'file-dropped',
        name: 'holiday-photo',
        extension: '.png',
        shouldBeDisplayedInOutput: true,
        exclusionReason: 'NotExpectedDocument',
      },
    ],
  }

  it('opens the documents step with only the documents the run used', () => {
    const prefill = buildChangeDocumentsPrefill(refused, CANONICAL_ID)

    expect(prefill.startStep).toBe('documents')
    expect(prefill.documents.map(d => d.fileId)).toEqual(['file-input-1', 'file-input-2'])
  })

  it('leaves every class to the platform, so a class it decided is not sent back as a hand-picked one', () => {
    const prefill = buildChangeDocumentsPrefill(refused, CANONICAL_ID)

    expect(prefill.documents.map(d => d.documentClass)).toEqual([undefined, undefined])
  })

  it('keeps the settings the run was started with', () => {
    const prefill = buildChangeDocumentsPrefill(refused, CANONICAL_ID)

    expect(prefill.counterpartyId).toBe('cp-9')
    expect(prefill.variables).toEqual({ revenue: 1000, region: 'NA' })
  })
})
