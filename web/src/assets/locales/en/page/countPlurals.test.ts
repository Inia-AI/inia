import { describe, expect, it } from 'vitest'
import { createI18n } from 'vue-i18n'
import { EN } from '@/test/helpers/messages'

const { t } = createI18n({
  legacy: false,
  locale: 'en',
  messages: { en: EN },
  missingWarn: false,
  fallbackWarn: false,
}).global

const count = (key: string, params: Record<string, unknown>, n: number) => t(key, params, n)

describe('count lines in the singular for one', () => {
  it.each([
    ['page.counterpartyManagement.count', 'count', '0 companies', '1 company', '7 companies'],
    ['page.documents.subtitle', 'count', '0 documents', '1 document', '7 documents'],
    ['page.documents.toast.deleteSuccess', 'count', 'Deleted 0 documents.', 'Deleted 1 document.', 'Deleted 7 documents.'],
    ['page.counterpartyManagement.importDialog.contentsCounterparties', 'count', '0 companies', '1 company', '7 companies'],
    ['page.counterpartyManagement.importDialog.contentsTypes', 'count', '0 types', '1 type', '7 types'],
    ['page.signatures.history.moreApprovers', 'count', '0 more approvers', '1 more approver', '7 more approvers'],
    ['page.twoFactor.settings.recoveryCodes.remaining', 'count', '0 codes remaining', '1 code remaining', '7 codes remaining'],
    ['shared.components.multiSelect.clearAll', 'n', 'Clear 0 selections', 'Clear 1 selection', 'Clear 7 selections'],
    [
      'page.createWorkflow.form.errors.uploadFailed',
      'count',
      'Failed to upload 0 documents. Please try again.',
      'Failed to upload 1 document. Please try again.',
      'Failed to upload 7 documents. Please try again.',
    ],
  ])('%s', (key, param, zero, one, many) => {
    expect(count(key, { [param]: 0 }, 0)).toBe(zero)
    expect(count(key, { [param]: 1 }, 1)).toBe(one)
    expect(count(key, { [param]: 7 }, 7)).toBe(many)
  })

  it('joins the two counts of a bundle line', () => {
    const parts = (key: string, count: number) => t(key, { count }, count)
    expect(
      t('page.counterpartyManagement.importDialog.contents', {
        counterparties: parts('page.counterpartyManagement.importDialog.contentsCounterparties', 1),
        types: parts('page.counterpartyManagement.importDialog.contentsTypes', 1),
      }),
    ).toBe('1 company and 1 type in the file.')
  })
})

const messages = (node: unknown, path: string[] = []): [string, string][] =>
  typeof node === 'string'
    ? [[path.join('.'), node]]
    : Object.entries(node as object).flatMap(([key, child]) => messages(child, [...path, key]))

describe('the EN locale', () => {
  it('words every count as a pipe plural, never as a guessed "(s)"', () => {
    const guessedPlurals = messages(EN).filter(([, message]) => /\((?:s|es|ies)\)/.test(message))

    expect(guessedPlurals).toEqual([])
  })
})
