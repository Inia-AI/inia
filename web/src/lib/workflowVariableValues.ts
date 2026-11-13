import { z } from 'zod'

import type { WorkflowVariable } from '@/api/schema'
import { WorkflowVariableTypes } from '@/enums'
import { toDateVariableValue } from '@/lib/dateVariable'

export const REQUIRED_VARIABLE_MESSAGE = 'Required'

const isListType = (type: WorkflowVariableTypes) =>
  type === WorkflowVariableTypes.List || type === WorkflowVariableTypes.MultiSelect

const acceptsEmptyText = (type: WorkflowVariableTypes) =>
  type === WorkflowVariableTypes.String || type === WorkflowVariableTypes.Dropdown

export const isBlankVariableValue = (value: unknown): boolean =>
  value == null
  || (typeof value === 'number' && Number.isNaN(value))
  || (typeof value === 'string' && value.trim() === '')

export const numberVariableSchema = (isRequired: boolean) =>
  z.preprocess(
    value => (isBlankVariableValue(value) ? undefined : value),
    isRequired ? z.number({ error: issue => (issue.input === undefined ? REQUIRED_VARIABLE_MESSAGE : undefined) }) : z.number().optional(),
  )

export const blankVariableValueOf = (type: WorkflowVariableTypes): unknown => {
  if (isListType(type)) {
    return []
  }
  return type === WorkflowVariableTypes.Number ? undefined : ''
}

const defaultValueOf = (variable: WorkflowVariable): unknown => {
  const { type, defaultValue } = variable
  if (!defaultValue) {
    return blankVariableValueOf(type)
  }

  if (isListType(type)) {
    return defaultValue.split('|')
  }
  if (type === WorkflowVariableTypes.DateTime) {
    return toDateVariableValue(defaultValue)
  }
  if (type === WorkflowVariableTypes.Number) {
    return Number(defaultValue)
  }
  return defaultValue
}

export const defaultVariableValues = (variables: WorkflowVariable[]): Record<string, unknown> =>
  Object.fromEntries(variables.map(variable => [variable.replacementTag, defaultValueOf(variable)]))

// The run fills a variable the request leaves out with its template default, or with
// "No data available" when there is none. The API refuses "" for a Number or a Date.
const isLeftOut = (definition: WorkflowVariable | undefined, value: unknown) =>
  !!definition
  && !definition.isRequired
  && isBlankVariableValue(value)
  && !(definition.defaultValue && acceptsEmptyText(definition.type))

export const submittableVariables = (
  values: Record<string, unknown>,
  definitions: WorkflowVariable[],
): Record<string, unknown> => {
  const definitionsByTag = new Map(definitions.map(definition => [definition.replacementTag, definition]))
  // A hidden variable is the template's own: the run fills it on the server, and the summary does not carry its default.
  return Object.fromEntries(
    Object.entries(values).filter(([tag, value]) => {
      const definition = definitionsByTag.get(tag)
      return !definition?.isHidden && !isLeftOut(definition, value)
    }),
  )
}

export const appliedTemplateDefault = (definition: WorkflowVariable, value: unknown): string | undefined => {
  if (!definition.defaultValue || !isLeftOut(definition, value)) {
    return undefined
  }
  return String(defaultValueOf(definition))
}
