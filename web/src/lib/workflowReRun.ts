import type {
  WorkflowExecutionVariableValue,
  WorkflowReport,
} from '@/api/workflowReports/schema'
import type { StartWorkflowExecutionRequest } from '@/api/workflowExecutions/schema'
import { WorkflowVariableTypes } from '@/enums'
import { toDateVariableValue } from '@/lib/dateVariable'
import { tagsByLowercase } from '@/lib/variableTags'

/**
 * The report endpoint serialises the `variables` dictionary with the
 * backend's camelCase key policy (`NetLeverage` → `netLeverage`,
 * `EBITDA2` → `ebitdA2`), while variable definitions — and therefore the
 * create-form field names — keep the replacement tag's original casing.
 * Recover that casing from `variableValues[].replacementTag` (a property
 * value, which the key policy leaves alone): exact tag matches win and are
 * never overwritten by a remap, a key remaps only when exactly one tag
 * shares its lowercase form, and everything else passes through unchanged.
 */
function restoreDefinitionCasedVariableKeys(
  variables: Record<string, unknown>,
  variableValues: WorkflowExecutionVariableValue[] | undefined,
): Record<string, unknown> {
  const tags = (variableValues ?? []).map(v => v.replacementTag)
  if (tags.length === 0) return variables

  const exactTags = new Set(tags)
  const uniqueTagsByLowerCase = tagsByLowercase(tags)

  const exactMatchedKeys = new Set(
    Object.keys(variables).filter(key => exactTags.has(key)),
  )

  const restored: Record<string, unknown> = {}
  for (const [key, value] of Object.entries(variables)) {
    let tag = key
    if (!exactTags.has(key)) {
      const candidate = uniqueTagsByLowerCase.get(key.toLowerCase())
      if (candidate != null && !exactMatchedKeys.has(candidate)) {
        tag = candidate
      }
    }
    restored[tag] = value
  }
  return restored
}

/** The report's company, or undefined when it has none, as the new report form expects an unset field. */
export const counterpartyIdOf = (execution: WorkflowReport): string | undefined =>
  execution.counterpartyId?.trim() ? execution.counterpartyId : undefined

/**
 * Pulls the fields shared by every re-run flow off a `WorkflowReport`:
 *   - input documents (output documents are produced by the workflow, not
 *     consumed, so they're filtered out, and so is a document deleted since
 *     the run, which the platform would refuse)
 *   - whitespace-normalised counterpartyId / documentClass (empty strings
 *     and trim-only values become `undefined` so the start endpoint treats
 *     them as absent rather than as the literal string)
 *   - variables, defaulted to an empty object, with keys restored to the
 *     definition-cased replacement tags and each Date value cut to its day
 *
 * Both `buildReRunPayload` (start a fresh execution) and `buildReRunPrefill`
 * (open the create form pre-populated) consume the result.
 */
export function extractReRunInputs(execution: WorkflowReport) {
  const inputDocuments = (execution.workflowExecutionDocuments ?? [])
    .filter(d => !d.isOutputDocument && !d.isDeleted)
    .map(d => ({
      ...d,
      documentClass: d.documentClass?.trim() ? d.documentClass : undefined,
    }))

  const counterpartyId = counterpartyIdOf(execution)

  const dateTags = new Set(
    (execution.variableValues ?? [])
      .filter(definition => definition.type === WorkflowVariableTypes.DateTime)
      .map(definition => definition.replacementTag),
  )
  const variables = Object.fromEntries(
    Object.entries(
      restoreDefinitionCasedVariableKeys(
        (execution.variables as Record<string, unknown>) ?? {},
        execution.variableValues,
      ),
    ).map(([tag, value]) => [tag, dateTags.has(tag) ? toDateVariableValue(value) || value : value]),
  )

  return { inputDocuments, counterpartyId, variables }
}

/**
 * Build the request body for re-running an existing workflow execution with
 * the same counterparty, input documents (preserving their assigned classes),
 * and variable values.
 *
 * `canonicalWorkflowId` must be the id of the canonical workflow definition
 * (the same id that `/workflows/create` submits — i.e. `Workflow.id` looked
 * up by `WorkflowTypeId`). Do NOT pass `WorkflowReport.workflowId` — that
 * field is the per-execution snapshot id (== execution id) and pointing the
 * start endpoint at it would re-run the frozen snapshot rather than start a
 * fresh execution from the canonical definition.
 *
 * Peer companies and reference URLs are not stored on `WorkflowReport`, so
 * they default to empty arrays — callers that need them must capture them
 * separately.
 */
export function buildReRunPayload(
  execution: WorkflowReport,
  canonicalWorkflowId: string,
): StartWorkflowExecutionRequest {
  const { inputDocuments, counterpartyId, variables } = extractReRunInputs(execution)

  return {
    workflowId: canonicalWorkflowId,
    files: inputDocuments.map(d => ({
      fileId: d.fileId,
      documentClass: d.documentClass,
      documentTags: [] as [],
    })),
    counterpartyId,
    peerCompanyIds: [],
    variables,
  }
}

/**
 * Extract a human-readable error message from a re-run mutation rejection.
 * Walks the same precedence chain CreateWorkflow.vue uses for submit errors:
 *   1. `errors.documentClasses` / `DocumentClasses` (the most likely failure
 *      mode for a re-run since input docs are reused; first semicolon-
 *      separated chunk wins)
 *   2. any other `errors[firstKey]` validation entry
 *   3. the top-level ProblemDetails envelope (`detail` → `message` → `title`)
 *   4. the supplied `fallback` (typically an i18n-translated generic message)
 */
export function reRunErrorMessage(err: unknown, fallback: string): string {
  const response = (err as {
    response?: {
      data?: {
        detail?: string
        message?: string
        title?: string
        errors?: Record<string, string[] | string>
      }
    }
  })?.response
  const data = response?.data
  const errors = data?.errors

  const docClassErrors = errors?.documentClasses ?? errors?.DocumentClasses
  if (docClassErrors) {
    const first = Array.isArray(docClassErrors) ? docClassErrors[0] : docClassErrors
    const message = first?.split(';').map(s => s.trim()).filter(Boolean)[0]
    if (message) return message
  }
  if (errors) {
    const firstKey = Object.keys(errors)[0]
    const firstErrors = firstKey ? errors[firstKey] : undefined
    const firstError = Array.isArray(firstErrors) ? firstErrors[0] : firstErrors
    if (firstError) return firstError
  }
  return data?.detail || data?.message || data?.title || fallback
}
