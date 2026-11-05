export interface RequiredDocumentClass {
  key: string
}

export interface ClassifiedDocument {
  class?: string
}

export function missingRequiredDocumentClasses<T extends RequiredDocumentClass>(
  required: readonly T[],
  uploaded: readonly ClassifiedDocument[],
): T[] {
  return required.filter(
    requirement => !uploaded.some(document => document.class === requirement.key),
  )
}

export interface ClassifyingWorkflow {
  classifiesDocuments?: boolean
  classesToChoose?: string[]
}

export function requiredClassesToChoose<T extends RequiredDocumentClass>(
  required: readonly T[],
  workflow: ClassifyingWorkflow | undefined,
): T[] {
  if (workflow?.classifiesDocuments !== true) return [...required]
  const toChoose = workflow.classesToChoose ?? []
  return required.filter(requirement => toChoose.includes(requirement.key))
}

export function requiredDocumentsSatisfied(
  required: readonly RequiredDocumentClass[],
  uploaded: readonly ClassifiedDocument[],
): boolean {
  return missingRequiredDocumentClasses(required, uploaded).length === 0
}

export interface NamedDocumentClass {
  key: string
  displayName?: string
}

export function documentClassDisplayName(
  classes: readonly NamedDocumentClass[],
  classKey: string | undefined,
): string | undefined {
  const key = classKey?.trim()
  if (!key) return undefined
  return classes.find(documentClass => documentClass.key === key)?.displayName ?? key
}

export interface SingularDocumentClass {
  key: string
  shouldBeSingular?: boolean
}

export function singularClassViolations<T extends SingularDocumentClass>(
  classes: readonly T[],
  uploaded: readonly ClassifiedDocument[],
): T[] {
  return classes.filter(
    documentClass =>
      documentClass.shouldBeSingular &&
      uploaded.filter(document => document.class === documentClass.key).length > 1,
  )
}
