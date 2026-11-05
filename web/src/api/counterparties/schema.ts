export interface Counterparty {
  id: string
  parentId: string | null
  counterpartyTypeIds: string[]
  name: string
  nameVariants: string
}

export interface CounterpartyDetails {
  id: string
  parentId?: string | null
  counterpartyTypeIds?: string[]
  name: string
  nameVariants: string
  email?: string | null
  phoneNumber?: string | null
  website?: string | null
  address?: string | null
  lastFinancialAnalysisResult?: string | null
  properties?: Record<string, string> | null
}

export interface UpdateCounterpartyRequest {
  parentId?: string | null
  counterpartyTypeIds?: string[]
  name: string
  nameVariants: string
  email?: string | null
  phoneNumber?: string | null
  website?: string | null
  address?: string | null
  lastFinancialAnalysisResult?: string | null
  properties?: Record<string, string> | null
}

export interface CreateCounterpartyRequest extends UpdateCounterpartyRequest {
  id?: string
}

export interface CounterpartyBundleType {
  name: string
  description: string | null
}

export interface CounterpartyBundleEntry {
  id: string
  parentId: string | null
  name: string
  nameVariants: string
  email: string | null
  phoneNumber: string | null
  website: string | null
  address: string | null
  lastFinancialAnalysisResult: string | null
  properties: Record<string, string> | null
  counterpartyTypeNames: string[]
}

/**
 * One organization's counterparties as the export writes them: a file that another instance
 * imports as is. The frontend never assembles or edits one; it moves the file between the
 * download and the upload.
 */
export interface CounterpartyBundle {
  bundleFormatVersion: number
  sourceInstance: string | null
  sourceOrganizationId: string | null
  sourceOrganizationName: string | null
  exportedAt: string
  counterpartyTypes: CounterpartyBundleType[]
  counterparties: CounterpartyBundleEntry[]
}

export enum CounterpartyImportDisposition {
  Create = 'Create',
  UpdateById = 'UpdateById',
  Resurrect = 'Resurrect',
  UpdateByName = 'UpdateByName',
  Unchanged = 'Unchanged',
  Skip = 'Skip',
}

export enum CounterpartyImportSkipReason {
  Invalid = 'Invalid',
  DuplicateIdInBundle = 'DuplicateIdInBundle',
  IdHeldByAnotherOrganization = 'IdHeldByAnotherOrganization',
  AmbiguousName = 'AmbiguousName',
  NameCollision = 'NameCollision',
  ParentNotLanded = 'ParentNotLanded',
}

export interface CounterpartyImportRow {
  sourceId: string
  name: string
  disposition: CounterpartyImportDisposition
  targetId: string | null
  skipReason: CounterpartyImportSkipReason | null
  detail: string | null
}

/**
 * What an import did, or for a dry run what it would do. The same shape comes back from both
 * calls, so the dialog that showed the plan can show the outcome without a second layout.
 */
export interface CounterpartyImportPlan {
  applied: boolean
  createCount: number
  updateByIdCount: number
  resurrectCount: number
  updateByNameCount: number
  unchangedCount: number
  skipCount: number
  counterpartyTypesToCreate: string[]
  rows: CounterpartyImportRow[]
}
