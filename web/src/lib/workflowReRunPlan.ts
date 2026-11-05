import type { WorkflowReport } from '@/api/workflowReports/schema'
import type {
  ReRunInputs,
  StartWorkflowExecutionRequest,
} from '@/api/workflowExecutions/schema'
import { buildReRunPayload, reRunErrorMessage } from './workflowReRun'
import {
  buildReRunPrefill,
  reRunChangesOf,
  withReRunInputs,
  type ReRunChanges,
  type WorkflowReRunPrefill,
} from './workflowReRunPrefill'

export type ReRunPlan =
  | { kind: 'start'; payload: StartWorkflowExecutionRequest; changes?: ReRunChanges }
  | { kind: 'form'; prefill: WorkflowReRunPrefill }

const isMissingEndpoint = (err: unknown) => {
  const response = (err as { response?: { status?: number; data?: { title?: string } } })?.response
  return response?.status === 404 && !response.data?.title
}

export async function loadReRunInputs(
  load: () => Promise<ReRunInputs>,
): Promise<ReRunInputs | null> {
  try {
    return await load()
  } catch (err) {
    if (isMissingEndpoint(err)) {
      return null
    }
    throw err
  }
}

export function planRestart(
  execution: WorkflowReport,
  canonicalWorkflowId: string,
  inputs: ReRunInputs | null,
): ReRunPlan {
  const payload = buildReRunPayload(execution, canonicalWorkflowId)
  if (!inputs) {
    return { kind: 'start', payload }
  }

  if (inputs.isReady) {
    const variables = Object.fromEntries(
      (inputs.variables ?? []).map(v => [v.replacementTag, v.value]),
    )
    return { kind: 'start', payload: { ...payload, variables }, changes: reRunChangesOf(inputs) }
  }

  return {
    kind: 'form',
    prefill: withReRunInputs(buildReRunPrefill(execution, canonicalWorkflowId), inputs),
  }
}

export function planEdit(
  execution: WorkflowReport,
  canonicalWorkflowId: string,
  inputs: ReRunInputs | null,
): WorkflowReRunPrefill {
  const prefill = buildReRunPrefill(execution, canonicalWorkflowId)
  return inputs ? withReRunInputs(prefill, inputs) : prefill
}

export function variableRefusalPrefill(
  execution: WorkflowReport,
  canonicalWorkflowId: string,
): WorkflowReRunPrefill {
  return { ...buildReRunPrefill(execution, canonicalWorkflowId), startStep: 'details' }
}

type Translate = (key: string, values?: Record<string, unknown>) => string

const ACTIONS = 'page.workflowDetails.actions'

const namesOf = (changes: { displayName: string }[]) =>
  changes.map(change => change.displayName).join(', ')

export function reRunStartedMessage(changes: ReRunChanges | undefined, t: Translate): string {
  if (!changes?.removedVariables.length && !changes?.defaultedVariables.length) {
    return t(`${ACTIONS}.reRunStarted`)
  }

  const parts = [t(`${ACTIONS}.reRunStartedChanged`)]
  if (changes.removedVariables.length) {
    parts.push(t(`${ACTIONS}.reRunRemoved`, { names: namesOf(changes.removedVariables) }))
  }
  if (changes.defaultedVariables.length) {
    parts.push(t(`${ACTIONS}.reRunDefaulted`, { names: namesOf(changes.defaultedVariables) }))
  }
  return parts.join(' ')
}

export function reRunFailureMessage(err: unknown, t: Translate): string {
  return reRunErrorMessage(err, t(`${ACTIONS}.reRunFailed`))
}

export function isVariableRefusal(err: unknown): boolean {
  const errors = (err as { response?: { data?: { errors?: Record<string, unknown> } } })
    ?.response?.data?.errors
  return !!errors && Object.keys(errors).some(key => key.toLowerCase().startsWith('workflowvariable.'))
}
