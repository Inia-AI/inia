import { describe, it, expect } from 'vitest'
import {
  documentClassDisplayName,
  missingRequiredDocumentClasses,
  requiredClassesToChoose,
  requiredDocumentsSatisfied,
  singularClassViolations,
} from './documentRequirements'

const required = [
  { key: 'balance-sheet', displayName: 'Balance Sheet' },
  { key: 'income-statement', displayName: 'Income Statement' },
]

describe('missingRequiredDocumentClasses', () => {
  it('returns every requirement when nothing is uploaded', () => {
    expect(missingRequiredDocumentClasses(required, [])).toEqual(required)
  })

  it('returns nothing when all required classes are classified', () => {
    const uploaded = [{ class: 'balance-sheet' }, { class: 'income-statement' }]
    expect(missingRequiredDocumentClasses(required, uploaded)).toEqual([])
  })

  it('returns only the requirements that are still unclassified', () => {
    const uploaded = [{ class: 'balance-sheet' }]
    expect(missingRequiredDocumentClasses(required, uploaded)).toEqual([
      { key: 'income-statement', displayName: 'Income Statement' },
    ])
  })

  it('ignores uploaded documents left on the empty class placeholder', () => {
    const uploaded = [{ class: ' ' }, { class: undefined }]
    expect(missingRequiredDocumentClasses(required, uploaded)).toEqual(required)
  })

  it('does not count an unrelated class toward a requirement', () => {
    const uploaded = [{ class: 'cash-flow' }]
    expect(missingRequiredDocumentClasses(required, uploaded)).toEqual(required)
  })

  it('is satisfied vacuously when there are no required classes', () => {
    expect(missingRequiredDocumentClasses([], [{ class: ' ' }])).toEqual([])
  })
})

describe('singularClassViolations', () => {
  const classes = [
    { key: 'balance-sheet', displayName: 'Balance Sheet', shouldBeSingular: true },
    { key: 'income-statement', displayName: 'Income Statement', shouldBeSingular: false },
    { key: 'cash-flow', displayName: 'Cash Flow' },
  ]

  it('flags a singular class with more than one upload', () => {
    const uploaded = [{ class: 'balance-sheet' }, { class: 'balance-sheet' }]
    expect(singularClassViolations(classes, uploaded)).toEqual([classes[0]])
  })

  it('allows exactly one upload of a singular class', () => {
    const uploaded = [{ class: 'balance-sheet' }]
    expect(singularClassViolations(classes, uploaded)).toEqual([])
  })

  it('ignores duplicates of non-singular classes', () => {
    const uploaded = [
      { class: 'income-statement' },
      { class: 'income-statement' },
      { class: 'cash-flow' },
      { class: 'cash-flow' },
    ]
    expect(singularClassViolations(classes, uploaded)).toEqual([])
  })

  it('reports nothing when nothing is uploaded', () => {
    expect(singularClassViolations(classes, [])).toEqual([])
  })
})

describe('documentClassDisplayName', () => {
  const classes = [
    { key: 'balance-sheet', displayName: 'Balance Sheet' },
    { key: 'income-statement', displayName: 'Income Statement' },
  ]

  it('maps a class key to its display name', () => {
    expect(documentClassDisplayName(classes, 'balance-sheet')).toBe('Balance Sheet')
  })

  it('returns undefined for an unclassified document', () => {
    expect(documentClassDisplayName(classes, undefined)).toBeUndefined()
  })

  it('returns undefined for the empty class placeholder', () => {
    expect(documentClassDisplayName(classes, ' ')).toBeUndefined()
  })

  it('falls back to the raw key when the class is not in the list', () => {
    expect(documentClassDisplayName(classes, 'cash-flow')).toBe('cash-flow')
  })
})

describe('requiredDocumentsSatisfied', () => {
  it('is false while any required class is unclassified', () => {
    expect(requiredDocumentsSatisfied(required, [{ class: 'balance-sheet' }])).toBe(false)
  })

  it('is true once every required class is classified', () => {
    const uploaded = [{ class: 'balance-sheet' }, { class: 'income-statement' }]
    expect(requiredDocumentsSatisfied(required, uploaded)).toBe(true)
  })

  it('is true when nothing is required', () => {
    expect(requiredDocumentsSatisfied([], [])).toBe(true)
  })
})

describe('requiredClassesToChoose', () => {
  it('asks for every required class when the platform does not classify', () => {
    expect(requiredClassesToChoose(required, { classifiesDocuments: false, classesToChoose: [] })).toEqual(required)
    expect(requiredClassesToChoose(required, undefined)).toEqual(required)
  })

  it('asks only for the required classes the platform cannot recognise when it classifies', () => {
    expect(
      requiredClassesToChoose(required, { classifiesDocuments: true, classesToChoose: ['income-statement'] }),
    ).toEqual([{ key: 'income-statement', displayName: 'Income Statement' }])
  })

  it('asks for nothing when the platform classifies and recognises every required class', () => {
    expect(requiredClassesToChoose(required, { classifiesDocuments: true })).toEqual([])
  })
})
