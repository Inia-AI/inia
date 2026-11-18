import type { InputRuleCondition, InputRuleRequirement, VariableInputRule, WorkflowVariable } from '@/api/schema'
import { WorkflowVariableTypes } from '@/enums'

export type InputRuleProblem = 'Incomplete' | 'MissingDocumentClass' | 'MissingVariable' | 'NotAnOption' | 'NotComparable'

export interface InputRuleViolation {
  replacementTag: string
  message: string
}

export interface SkippedInputRule {
  replacementTag: string
  variableName: string
  kind: InputRuleProblem
  reason: string
}

export interface InputRuleOutcome {
  violations: InputRuleViolation[]
  skipped: SkippedInputRule[]
}

export interface InputRuleDocumentClass {
  key: string
  displayName?: string | null
}

export interface InputRuleWorkflow {
  variables: WorkflowVariable[]
  documentClasses?: InputRuleDocumentClass[] | null
}

type Problem = { kind: InputRuleProblem; reason: string }

const LEADING_NUMBERING = /^\s*(?:[0-9]+(?:\.[0-9]+)*[.)]|[0-9]+(?:\.[0-9]+)+)\s+/

const isBlankText = (text: string | null | undefined) => !text || text.trim() === ''

const normalized = (text: string) => text.trim().toLowerCase()

export const withoutNumbering = (displayName: string): string => {
  const stripped = displayName.replace(LEADING_NUMBERING, '').trim()
  return stripped.length === 0 ? displayName.trim() : stripped
}

export const comparesWithValue = (type: WorkflowVariableTypes) =>
  type === WorkflowVariableTypes.String || type === WorkflowVariableTypes.Dropdown

const isOnDocuments = (condition: InputRuleCondition) =>
  condition.operator === 'AnyAttached' || condition.operator === 'NoneAttached'

const conditionComparesValue = (condition: InputRuleCondition) =>
  condition.operator === 'EqualTo' || condition.operator === 'NotEqualTo'

const requirementComparesValue = (requirement: InputRuleRequirement) =>
  requirement.operator === 'EqualTo' || requirement.operator === 'NotEqualTo'

export const nameOfVariable = (variable: WorkflowVariable) =>
  withoutNumbering(isBlankText(variable.displayName) ? variable.replacementTag : variable.displayName)

const nameOfClass = (documentClass: InputRuleDocumentClass) =>
  withoutNumbering(isBlankText(documentClass.displayName) ? documentClass.key : documentClass.displayName!)

const findClass = (workflow: InputRuleWorkflow, key: string) => {
  const wanted = key.trim()
  return (workflow.documentClasses ?? []).find(documentClass => documentClass.key === wanted)
}

const findVariable = (workflow: InputRuleWorkflow, tag: string) => {
  const wanted = tag.trim().toLowerCase()
  return workflow.variables.find(variable => variable.replacementTag.toLowerCase() === wanted)
}

const optionKeyOf = (variable: WorkflowVariable, value: string) => {
  const wanted = normalized(value)
  return Object.keys(variable.options ?? {}).find(key => normalized(key) === wanted)
}

const labelOf = (variable: WorkflowVariable, value: string) => {
  const key = variable.type === WorkflowVariableTypes.Dropdown ? optionKeyOf(variable, value) : undefined
  return key !== undefined ? variable.options![key] : value.trim()
}

const findValue = (submitted: Record<string, unknown>, tag: string): { found: boolean; value?: unknown } => {
  if (Object.prototype.hasOwnProperty.call(submitted, tag)) {
    return { found: true, value: submitted[tag] }
  }
  const wanted = tag.toLowerCase()
  const key = Object.keys(submitted).find(candidate => candidate.toLowerCase() === wanted)
  return key === undefined ? { found: false } : { found: true, value: submitted[key] }
}

const defaultAsSubmitted = (variable: WorkflowVariable): unknown => {
  const { defaultValue, type } = variable
  if (defaultValue == null) {
    return undefined
  }
  switch (type) {
    case WorkflowVariableTypes.Number: {
      const number = Number(defaultValue)
      return defaultValue.trim() !== '' && Number.isFinite(number) ? number : defaultValue
    }
    case WorkflowVariableTypes.List:
    case WorkflowVariableTypes.MultiSelect:
      return defaultValue.split('|').map(item => item.trim())
    case WorkflowVariableTypes.DateTime:
      return defaultValue.includes('T') ? defaultValue : `${defaultValue}T00:00`
    default:
      return defaultValue
  }
}

const effectiveValueOf = (variable: WorkflowVariable, submitted: Record<string, unknown>) => {
  const { found, value } = findValue(submitted, variable.replacementTag)
  return found && value !== undefined ? value : defaultAsSubmitted(variable)
}

const isEmpty = (value: unknown) => {
  if (value === null || value === undefined) {
    return true
  }
  if (typeof value === 'number') {
    return Number.isNaN(value)
  }
  if (typeof value === 'string') {
    return value.trim() === ''
  }
  if (Array.isArray(value)) {
    return value.length === 0
  }
  return false
}

const isSame = (value: unknown, expected: string) =>
  typeof value === 'string' && normalized(value) === normalized(expected)

export const incompletenessOf = (rule: VariableInputRule): string | null => {
  if (rule.conditions.length === 0) {
    return 'it has no condition'
  }

  for (const condition of rule.conditions) {
    if (isOnDocuments(condition)) {
      const classes = condition.documentClassKeys
      if (!classes || classes.length === 0 || classes.some(isBlankText)) {
        return 'a document condition names no document class'
      }
      continue
    }

    if (isBlankText(condition.variable)) {
      return 'a condition names no field'
    }

    if (conditionComparesValue(condition) && isBlankText(condition.value)) {
      return 'a condition has no value to compare with'
    }
  }

  if (requirementComparesValue(rule.requirement) && isBlankText(rule.requirement.value)) {
    return 'the requirement has no value to compare with'
  }

  return null
}

const comparisonProblemOf = (variable: WorkflowVariable, value: string): Problem | null => {
  if (!comparesWithValue(variable.type)) {
    return {
      kind: 'NotComparable',
      reason: `"${nameOfVariable(variable)}" is not a text or dropdown field, so it cannot be compared with a value`,
    }
  }

  if (variable.type === WorkflowVariableTypes.Dropdown && optionKeyOf(variable, value) === undefined) {
    return { kind: 'NotAnOption', reason: `"${value.trim()}" is not an option of "${nameOfVariable(variable)}"` }
  }

  return null
}

const problemOf = (workflow: InputRuleWorkflow, variable: WorkflowVariable, rule: VariableInputRule): Problem | null => {
  const incomplete = incompletenessOf(rule)
  if (incomplete) {
    return { kind: 'Incomplete', reason: incomplete }
  }

  if (requirementComparesValue(rule.requirement)) {
    const requirementProblem = comparisonProblemOf(variable, rule.requirement.value!)
    if (requirementProblem) {
      return requirementProblem
    }
  }

  for (const condition of rule.conditions) {
    if (isOnDocuments(condition)) {
      const missingClass = condition.documentClassKeys!.find(key => !findClass(workflow, key))
      if (missingClass !== undefined) {
        return {
          kind: 'MissingDocumentClass',
          reason: `it names the document class "${missingClass.trim()}", which this workflow does not have`,
        }
      }
      continue
    }

    const other = findVariable(workflow, condition.variable!)
    if (!other) {
      return {
        kind: 'MissingVariable',
        reason: `it names the field "${condition.variable!.trim()}", which this workflow does not have`,
      }
    }

    if (conditionComparesValue(condition)) {
      const conditionProblem = comparisonProblemOf(other, condition.value!)
      if (conditionProblem) {
        return conditionProblem
      }
    }
  }

  return null
}

const holds = (
  workflow: InputRuleWorkflow,
  condition: InputRuleCondition,
  submitted: Record<string, unknown>,
  attached: Set<string>,
) => {
  if (isOnDocuments(condition)) {
    const anyAttached = condition.documentClassKeys!.some(key => attached.has(findClass(workflow, key)!.key))
    return condition.operator === 'AnyAttached' ? anyAttached : !anyAttached
  }

  const value = effectiveValueOf(findVariable(workflow, condition.variable!)!, submitted)
  switch (condition.operator) {
    case 'EqualTo':
      return isSame(value, condition.value!)
    case 'NotEqualTo':
      return !isSame(value, condition.value!)
    case 'IsEmpty':
      return isEmpty(value)
    case 'IsNotEmpty':
      return !isEmpty(value)
    default:
      return false
  }
}

const isMet = (variable: WorkflowVariable, requirement: InputRuleRequirement, submitted: Record<string, unknown>) => {
  const value = effectiveValueOf(variable, submitted)
  switch (requirement.operator) {
    case 'NotEmpty':
      return !isEmpty(value)
    case 'EqualTo':
      return isSame(value, requirement.value!)
    case 'NotEqualTo':
      return !isSame(value, requirement.value!)
    default:
      return true
  }
}

const joinWithOr = (items: string[]) =>
  items.length === 1 ? items[0] : `${items.slice(0, -1).join(', ')} or ${items[items.length - 1]}`

const requirementText = (variable: WorkflowVariable, requirement: InputRuleRequirement) => {
  const name = nameOfVariable(variable)
  switch (requirement.operator) {
    case 'EqualTo':
      return `"${name}" must be "${labelOf(variable, requirement.value!)}"`
    case 'NotEqualTo':
      return `"${name}" cannot be "${labelOf(variable, requirement.value!)}"`
    default:
      return `Choose a value for "${name}"`
  }
}

const clauseOf = (workflow: InputRuleWorkflow, condition: InputRuleCondition) => {
  if (isOnDocuments(condition)) {
    const classes = joinWithOr(condition.documentClassKeys!.map(key => `"${nameOfClass(findClass(workflow, key)!)}"`))
    return condition.operator === 'AnyAttached' ? `${classes} is attached` : `no ${classes} is attached`
  }

  const other = findVariable(workflow, condition.variable!)!
  const name = nameOfVariable(other)
  switch (condition.operator) {
    case 'EqualTo':
      return `"${name}" is "${labelOf(other, condition.value!)}"`
    case 'NotEqualTo':
      return `"${name}" is not "${labelOf(other, condition.value!)}"`
    case 'IsEmpty':
      return `"${name}" is empty`
    default:
      return `"${name}" has a value`
  }
}

export const messageOf = (workflow: InputRuleWorkflow, variable: WorkflowVariable, rule: VariableInputRule) =>
  `${requirementText(variable, rule.requirement)} because ${rule.conditions.map(condition => clauseOf(workflow, condition)).join(' and ')}.`

export const checkInputRules = (
  workflow: InputRuleWorkflow,
  submitted: Record<string, unknown>,
  attachedClassKeys: Iterable<string | null | undefined>,
): InputRuleOutcome => {
  const attached = new Set([...attachedClassKeys].filter((key): key is string => typeof key === 'string'))
  const violations: InputRuleViolation[] = []
  const skipped: SkippedInputRule[] = []

  for (const variable of workflow.variables) {
    for (const rule of variable.inputRules ?? []) {
      const problem = problemOf(workflow, variable, rule)
      if (problem) {
        skipped.push({ replacementTag: variable.replacementTag, variableName: nameOfVariable(variable), ...problem })
        continue
      }

      const applies = rule.conditions.every(condition => holds(workflow, condition, submitted, attached))
      if (applies && !isMet(variable, rule.requirement, submitted)) {
        violations.push({ replacementTag: variable.replacementTag, message: messageOf(workflow, variable, rule) })
      }
    }
  }

  return { violations, skipped }
}
