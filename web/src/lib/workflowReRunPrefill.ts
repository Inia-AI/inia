import type { WorkflowReport } from '@/api/workflowReports/schema'
import type {
  ReRunDocumentClassNeedingInput,
  ReRunInputs,
  ReRunVariableChange,
  ReRunVariableNeedingInput,
} from '@/api/workflowExecutions/schema'
import { extractReRunInputs } from './workflowReRun'

export interface ReRunChanges {
  variablesNeedingInput: ReRunVariableNeedingInput[]
  documentClassesNeedingInput: ReRunDocumentClassNeedingInput[]
  removedVariables: ReRunVariableChange[]
  defaultedVariables: ReRunVariableChange[]
  unavailableDocuments: string[]
}

export interface WorkflowReRunPrefillDocument {
  fileId: string
  documentClass?: string
  name: string
  extension: string
}

/**
 * Snapshot of an existing execution shaped for the `/workflows/create` form.
 * `workflowId` is the canonical workflow definition id (the value the form
 * submits as `type`); `workflowTypeId` is what gates the on-demand full-detail
 * fetch in CreateWorkflow.vue. A re-run leaves `peerCompanyIds` out, because
 * `WorkflowReport` does not store them; a form kept across a visit to another
 * site carries them.
 */
export interface WorkflowReRunPrefill {
  workflowId: string
  workflowTypeId: string
  counterpartyId?: string
  peerCompanyIds?: string[]
  variables: Record<string, unknown>
  documents: WorkflowReRunPrefillDocument[]
  /** The form step to open on; absent opens the first step. */
  startStep?: 'documents' | 'details' | 'review'
  reRunChanges?: ReRunChanges
}

export function reRunChangesOf(
  inputs: ReRunInputs,
  documents: WorkflowReRunPrefillDocument[] = [],
): ReRunChanges | undefined {
  const changes: ReRunChanges = {
    variablesNeedingInput: inputs.variablesNeedingInput ?? [],
    documentClassesNeedingInput: inputs.documentClassesNeedingInput ?? [],
    removedVariables: inputs.removedVariables ?? [],
    defaultedVariables: inputs.defaultedVariables ?? [],
    unavailableDocuments: (inputs.unavailableDocuments ?? []).map(unavailable => {
      const known = documents.find(d => d.fileId === unavailable.documentId)
      return unavailable.fileName || (known ? fullName(known) : unavailable.documentId)
    }),
  }
  const any = Object.values(changes).some(list => list.length > 0)
  return any ? changes : undefined
}

const fullName = (document: WorkflowReRunPrefillDocument) =>
  document.name.endsWith(document.extension) ? document.name : `${document.name}${document.extension}`

export function withReRunInputs(
  prefill: WorkflowReRunPrefill,
  inputs: ReRunInputs,
): WorkflowReRunPrefill {
  const prefillKeysByLowercase = new Map(
    Object.keys(prefill.variables).map(key => [key.toLowerCase(), key]),
  )
  const variables: Record<string, unknown> = {}
  for (const { replacementTag } of inputs.variables ?? []) {
    const key = prefillKeysByLowercase.get(replacementTag.toLowerCase())
    if (key !== undefined) {
      variables[replacementTag] = prefill.variables[key]
    }
  }

  const reRunChanges = reRunChangesOf(inputs, prefill.documents)
  const unavailableIds = new Set((inputs.unavailableDocuments ?? []).map(d => d.documentId))
  const documents = prefill.documents.filter(d => !unavailableIds.has(d.fileId))
  let startStep = prefill.startStep
  if (reRunChanges?.documentClassesNeedingInput.length || reRunChanges?.unavailableDocuments.length) {
    startStep = 'documents'
  } else if (reRunChanges?.variablesNeedingInput.length) {
    startStep = 'details'
  }

  return { ...prefill, variables, documents, startStep, reRunChanges }
}

/**
 * Pure builder that converts a `WorkflowReport` into the prefill payload
 * `/workflows/create` consumes when the user picks "Edit inputs first" on a
 * re-run. Shares `extractReRunInputs` with `buildReRunPayload` so both
 * branches normalise documents, counterparty, and variables identically; the
 * documents a run did not use are not among its inputs, so they are left out.
 */
export function buildReRunPrefill(
  execution: WorkflowReport,
  canonicalWorkflowId: string,
): WorkflowReRunPrefill {
  const { inputDocuments, counterpartyId, variables } = extractReRunInputs(execution)

  return {
    workflowId: canonicalWorkflowId,
    workflowTypeId: execution.workflowTypeId ?? '',
    counterpartyId,
    variables,
    documents: inputDocuments.map(d => ({
      fileId: d.fileId,
      documentClass: d.documentClass,
      name: d.name,
      extension: d.extension,
    })),
  }
}

/**
 * The prefill for "Change the documents" on a refused run: the form opens on
 * the documents step with the documents the run used and none of their
 * classes. A refusal happens on a type that classifies its documents, so those
 * classes are the platform's own decisions, and sending them back would turn
 * them into hand-picked ones.
 */
export function buildChangeDocumentsPrefill(
  execution: WorkflowReport,
  canonicalWorkflowId: string,
): WorkflowReRunPrefill {
  const prefill = buildReRunPrefill(execution, canonicalWorkflowId)

  return {
    ...prefill,
    documents: prefill.documents.map(d => ({ ...d, documentClass: undefined })),
    startStep: 'documents',
  }
}
