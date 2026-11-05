export enum WorkflowVariableTypes {
  String = 'String',
  Number = 'Number',
  DateTime = 'DateTime',
  List = 'List',
  Dropdown = 'Dropdown',
  MultiSelect = 'MultiSelect',
}

// Parent Company Workflow Variables
export const DEFAULT_WORKFLOW_VARIABLES = {
  HAS_PARENT: 'hasParent',
  NAME_PARENT: 'nameParent',
  PARENT_URL: 'parentURL',
  PARENT_REPUTATION: 'parentReputation',
} as const

export const ALL_PARENT_COMPANY_TAGS: string[] = [
  DEFAULT_WORKFLOW_VARIABLES.HAS_PARENT,
  DEFAULT_WORKFLOW_VARIABLES.NAME_PARENT,
  DEFAULT_WORKFLOW_VARIABLES.PARENT_URL,
  DEFAULT_WORKFLOW_VARIABLES.PARENT_REPUTATION,
]
