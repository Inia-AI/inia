import { describe, expect, it } from 'vitest'
import { buildReRunPayload } from './workflowReRun'
import { WorkflowState, WorkflowVariableTypes } from '@/enums'
import type {
  WorkflowExecutionVariableValue,
  WorkflowReport,
} from '@/api/workflowReports/schema'

const baseReport: WorkflowReport = {
  id: 'exec-1',
  workflowId: 'wf-123',
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

describe('buildReRunPayload', () => {
  it('uses the canonical workflow id passed in, not the snapshot id on the execution', () => {
    const payload = buildReRunPayload(baseReport, CANONICAL_ID)
    expect(payload.workflowId).toBe(CANONICAL_ID)
    expect(payload.workflowId).not.toBe(baseReport.workflowId)
  })

  it('preserves counterpartyId and variables', () => {
    const payload = buildReRunPayload(baseReport, CANONICAL_ID)
    expect(payload.counterpartyId).toBe('cp-9')
    expect(payload.variables).toEqual({ revenue: 1000, region: 'NA' })
  })

  it('excludes output documents from files', () => {
    const payload = buildReRunPayload(baseReport, CANONICAL_ID)
    expect(payload.files.map(f => f.fileId)).toEqual([
      'file-input-1',
      'file-input-2',
    ])
  })

  it('keeps the assigned document class for input documents', () => {
    const payload = buildReRunPayload(baseReport, CANONICAL_ID)
    expect(payload.files[0].documentClass).toBe('Form10K')
  })

  it('treats whitespace-only document class as no class', () => {
    const payload = buildReRunPayload(baseReport, CANONICAL_ID)
    expect(payload.files[1].documentClass).toBeUndefined()
  })

  it('drops a whitespace-only counterpartyId', () => {
    const payload = buildReRunPayload(
      { ...baseReport, counterpartyId: '   ' },
      CANONICAL_ID,
    )
    expect(payload.counterpartyId).toBeUndefined()
  })

  it('defaults peerCompanyIds to an empty array', () => {
    const payload = buildReRunPayload(baseReport, CANONICAL_ID)
    expect(payload.peerCompanyIds).toEqual([])
  })

  it('returns an empty variables object when none stored', () => {
    const payload = buildReRunPayload(
      { ...baseReport, variables: undefined },
      CANONICAL_ID,
    )
    expect(payload.variables).toEqual({})
  })

  it('returns no files when execution has no documents', () => {
    const payload = buildReRunPayload(
      { ...baseReport, workflowExecutionDocuments: undefined },
      CANONICAL_ID,
    )
    expect(payload.files).toEqual([])
  })

  it('restores definition-cased variable keys camelCased by the report serializer', () => {
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
    const payload = buildReRunPayload(
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
    expect(payload.variables).toEqual({
      dscr: 0.8,
      NetLeverage: 'x4.5',
      EBITDA2: 'high',
    })
  })

  it('sends a stored date with a time part as its calendar day', () => {
    const payload = buildReRunPayload(
      {
        ...baseReport,
        variables: { asOfDate: '2026-09-25T17:45', note: '2026-09-25T17:45' },
        variableValues: [
          {
            replacementTag: 'AsOfDate',
            displayName: 'As of date',
            type: WorkflowVariableTypes.DateTime,
            isConfidential: false,
            isHidden: false,
            isRequired: true,
          },
          {
            replacementTag: 'note',
            displayName: 'Note',
            type: WorkflowVariableTypes.String,
            isConfidential: false,
            isHidden: false,
            isRequired: false,
          },
        ],
      },
      CANONICAL_ID,
    )
    expect(payload.variables).toEqual({ AsOfDate: '2026-09-25', note: '2026-09-25T17:45' })
  })

  it('passes a stored date it cannot read through for the server to judge', () => {
    const payload = buildReRunPayload(
      {
        ...baseReport,
        variables: { AsOfDate: 'end of quarter' },
        variableValues: [
          {
            replacementTag: 'AsOfDate',
            displayName: 'As of date',
            type: WorkflowVariableTypes.DateTime,
            isConfidential: false,
            isHidden: false,
            isRequired: true,
          },
        ],
      },
      CANONICAL_ID,
    )
    expect(payload.variables).toEqual({ AsOfDate: 'end of quarter' })
  })
})
