export const ROLES = {
  SUPER_ADMIN: 'SuperAdmin',
  ADMIN: 'Admin',
  USER: 'User',
  SIGNATURE_CONTRIBUTOR: 'SignatureContributor',
  WORKFLOW_EDITOR: 'WorkflowEditor',
  OPERATOR: 'Operator',
} as const

// The backend's composite gates. Its role checks have no hierarchy, so each list names SuperAdmin.
export const EDITOR_ROLES = [ROLES.WORKFLOW_EDITOR, ROLES.SUPER_ADMIN] as const
export const OPERATOR_ROLES = [ROLES.OPERATOR, ROLES.SUPER_ADMIN] as const
export const ADMIN_ROLES = [ROLES.ADMIN, ROLES.SUPER_ADMIN] as const
export const ADMIN_AND_OPERATOR_ROLES = [...ADMIN_ROLES, ROLES.OPERATOR] as const

// Type for role values
export type Role = (typeof ROLES)[keyof typeof ROLES]
