export const CAPABILITIES = {
  RUN: 'run',
  UPLOAD: 'upload',
  EXPLORE: 'explore',
  REFINE: 'refine',
  RUN_LOGS: 'run-logs',
  RUN_EVIDENCE: 'run-evidence',
  USAGE_COST: 'usage-cost',
  BASELINES: 'baselines',
  VALIDATION: 'validation',
  REPORT_TEMPLATES: 'report-templates',
  STANDALONE_PROCESSING: 'standalone-processing',
  TEAM_VIEW: 'team-view',
  SIGNATURES: 'signatures',
  MONITORING: 'monitoring',
  ADMIN_APP: 'admin-app',
} as const

export type Capability = (typeof CAPABILITIES)[keyof typeof CAPABILITIES]

// Not one of CAPABILITIES, which holds what the platform grants to everyone: only a module adds this one.
export const DELETE_OWN_DOCUMENTS = 'delete-own-documents'

export const NEW_WORK: readonly Capability[] = [CAPABILITIES.RUN, CAPABILITIES.UPLOAD, CAPABILITIES.EXPLORE, CAPABILITIES.REFINE]
