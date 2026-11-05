import { describe, expect, it } from 'vitest'
import {
  isVariableRefusal,
  planEdit,
  planRestart,
  reRunFailureMessage,
  loadReRunInputs,
  reRunStartedMessage,
  variableRefusalPrefill,
} from './workflowReRunPlan'
import { WorkflowState } from '@/enums'
import type { WorkflowReport } from '@/api/workflowReports/schema'
import type { ReRunInputs } from '@/api/workflowExecutions/schema'

const CANONICAL_ID = 'canonical-wf'

const report: WorkflowReport = {
  id: 'exec-1',
  workflowId: 'snapshot-1',
  workflowTypeId: 'bank-account-analysis',
  workflowState: WorkflowState.Draft,
  counterpartyId: 'cp-1',
  variables: { companyName: 'Acme Ltd', region: 'EMEA' },
  workflowExecutionDocuments: [
    {
      fileId: 'file-1',
      name: 'statement',
      extension: '.pdf',
      documentClass: 'BankStatement',
      shouldBeDisplayedInOutput: false,
    },
  ],
}

const inputs = (patch: Partial<ReRunInputs> = {}): ReRunInputs => ({
  workflowId: CANONICAL_ID,
  workflowTypeId: 'bank-account-analysis',
  isReady: true,
  variables: [{ replacementTag: 'CompanyName', value: 'Acme Ltd' }],
  variablesNeedingInput: [],
  defaultedVariables: [],
  removedVariables: [],
  documentClassesNeedingInput: [],
  unavailableDocuments: [],
  ...patch,
})

const accountHolderNeeded = {
  replacementTag: 'account_holder_type',
  displayName: 'Account Holder',
  reason: 'NoValue' as const,
  message: "Required variable 'Account Holder' (account_holder_type) is missing.",
}

const t = (key: string, values?: Record<string, unknown>) =>
  values ? `${key} ${JSON.stringify(values)}` : key

describe('planRestart', () => {
  it('submits the run as before when the check is not available', () => {
    const plan = planRestart(report, CANONICAL_ID, null)

    expect(plan.kind).toBe('start')
    if (plan.kind !== 'start') return
    expect(plan.payload.variables).toEqual({ companyName: 'Acme Ltd', region: 'EMEA' })
    expect(plan.changes).toBeUndefined()
  })

  it('submits the checked values, without dropped variables and with defaults, when ready', () => {
    const plan = planRestart(
      report,
      CANONICAL_ID,
      inputs({
        variables: [
          { replacementTag: 'CompanyName', value: 'Acme Ltd' },
          { replacementTag: 'account_holder_type', value: 'Company' },
        ],
        defaultedVariables: [{ replacementTag: 'account_holder_type', displayName: 'Account Holder' }],
        removedVariables: [{ replacementTag: 'Region', displayName: 'Region' }],
      }),
    )

    expect(plan.kind).toBe('start')
    if (plan.kind !== 'start') return
    expect(plan.payload.workflowId).toBe(CANONICAL_ID)
    expect(plan.payload.variables).toEqual({ CompanyName: 'Acme Ltd', account_holder_type: 'Company' })
    expect(plan.payload.files).toEqual([
      { fileId: 'file-1', documentClass: 'BankStatement', documentTags: [] },
    ])
    expect(plan.changes?.removedVariables.map(v => v.displayName)).toEqual(['Region'])
  })

  it('opens the form on the details step when the workflow gained a required variable', () => {
    const plan = planRestart(
      report,
      CANONICAL_ID,
      inputs({ isReady: false, variablesNeedingInput: [accountHolderNeeded] }),
    )

    expect(plan.kind).toBe('form')
    if (plan.kind !== 'form') return
    expect(plan.prefill.startStep).toBe('details')
    expect(plan.prefill.variables).toEqual({ CompanyName: 'Acme Ltd' })
    expect(plan.prefill.documents.map(d => d.fileId)).toEqual(['file-1'])
    expect(plan.prefill.counterpartyId).toBe('cp-1')
    expect(plan.prefill.reRunChanges?.variablesNeedingInput).toEqual([accountHolderNeeded])
  })

  it('opens the form on the documents step when a document class needs input', () => {
    const plan = planRestart(
      report,
      CANONICAL_ID,
      inputs({
        isReady: false,
        variablesNeedingInput: [accountHolderNeeded],
        documentClassesNeedingInput: [
          { key: 'IdDocument', displayName: 'ID document', message: "Required document class 'ID document' is missing" },
        ],
      }),
    )

    expect(plan.kind).toBe('form')
    if (plan.kind !== 'form') return
    expect(plan.prefill.startStep).toBe('documents')
  })
})

describe('planRestart with a document that no longer exists', () => {
  it('drops the document from the form and names it', () => {
    const plan = planRestart(
      report,
      CANONICAL_ID,
      inputs({ isReady: false, unavailableDocuments: [{ documentId: 'file-1', fileName: 'deleted statement.pdf' }] }),
    )

    expect(plan.kind).toBe('form')
    if (plan.kind !== 'form') return
    expect(plan.prefill.documents).toEqual([])
    expect(plan.prefill.startStep).toBe('documents')
    expect(plan.prefill.reRunChanges?.unavailableDocuments).toEqual(['deleted statement.pdf'])
  })
})

describe('planEdit', () => {
  it('keeps the plain prefill when the check is not available', () => {
    const prefill = planEdit(report, CANONICAL_ID, null)

    expect(prefill.variables).toEqual({ companyName: 'Acme Ltd', region: 'EMEA' })
    expect(prefill.reRunChanges).toBeUndefined()
    expect(prefill.startStep).toBeUndefined()
  })

  it('drops removed variables and keeps the first step when nothing needs input', () => {
    const prefill = planEdit(
      report,
      CANONICAL_ID,
      inputs({ removedVariables: [{ replacementTag: 'Region', displayName: 'Region' }] }),
    )

    expect(prefill.variables).toEqual({ CompanyName: 'Acme Ltd' })
    expect(prefill.startStep).toBeUndefined()
    expect(prefill.reRunChanges?.removedVariables).toHaveLength(1)
  })

  it('reports no changes when the workflow is unchanged', () => {
    const prefill = planEdit(
      { ...report, variables: { companyName: 'Acme Ltd' } },
      CANONICAL_ID,
      inputs(),
    )

    expect(prefill.reRunChanges).toBeUndefined()
  })
})

describe('re-run messages', () => {
  it('says the run started, and nothing more, when nothing changed', () => {
    expect(reRunStartedMessage(undefined, t)).toBe('page.workflowDetails.actions.reRunStarted')
  })

  it('names dropped and defaulted variables when the run started with changes', () => {
    const message = reRunStartedMessage(
      {
        variablesNeedingInput: [],
        documentClassesNeedingInput: [],
        removedVariables: [{ replacementTag: 'Region', displayName: 'Region' }],
        defaultedVariables: [{ replacementTag: 'account_holder_type', displayName: 'Account Holder' }],
      },
      t,
    )

    expect(message).toContain('reRunStartedChanged')
    expect(message).toContain('reRunRemoved {"names":"Region"}')
    expect(message).toContain('reRunDefaulted {"names":"Account Holder"}')
  })

  it('recognises a variable refusal, and opens the form on the details step for it', () => {
    const refusal = {
      response: {
        data: {
          errors: { 'WorkflowVariable.Required': ["Required variable 'Account Holder' (account_holder_type) is missing."] },
        },
      },
    }

    expect(isVariableRefusal(refusal)).toBe(true)
    const prefill = variableRefusalPrefill(report, CANONICAL_ID)
    expect(prefill.startStep).toBe('details')
    expect(prefill.documents.map(d => d.fileId)).toEqual(['file-1'])
  })

  it('keeps the server message for other refusals', () => {
    const refusal = { response: { data: { errors: { DocumentClasses: ["Required document class 'ID' is missing"] } } } }

    expect(isVariableRefusal(refusal)).toBe(false)
    expect(reRunFailureMessage(refusal, t)).toBe("Required document class 'ID' is missing")
  })
})

describe('loadReRunInputs', () => {
  it('answers null when the API has no check, so the re-run starts as before', async () => {
    const missingRoute = { response: { status: 404, data: '' } }

    await expect(loadReRunInputs(() => Promise.reject(missingRoute))).resolves.toBeNull()
  })

  it('passes on a 404 that explains itself, such as a workflow no longer available', async () => {
    const notAvailable = {
      response: {
        status: 404,
        data: { title: 'The workflow this report ran is no longer available to your organization, so the report cannot be re-run.' },
      },
    }

    await expect(loadReRunInputs(() => Promise.reject(notAvailable))).rejects.toBe(notAvailable)
  })

  it('passes on a server error', async () => {
    const serverError = { response: { status: 500, data: { title: 'Error' } } }

    await expect(loadReRunInputs(() => Promise.reject(serverError))).rejects.toBe(serverError)
  })
})
