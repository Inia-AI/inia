import { describe, it, expect } from 'vitest'

import { blankToNull, buildCounterpartyFormSchema } from './schema'

const t = (key: string) => key

const baseInput = {
  id: 'abc',
  name: 'Acme',
  counterpartyTypeIds: ['type-1'],
}

describe('blankToNull', () => {
  it('returns null for null and undefined', () => {
    expect(blankToNull(null)).toBeNull()
    expect(blankToNull(undefined)).toBeNull()
  })

  it('returns null for empty and whitespace-only strings', () => {
    expect(blankToNull('')).toBeNull()
    expect(blankToNull('   ')).toBeNull()
    expect(blankToNull('\t\n')).toBeNull()
  })

  it('trims non-blank strings', () => {
    expect(blankToNull('  foo@bar.com  ')).toBe('foo@bar.com')
  })

  it('returns null for non-string values', () => {
    expect(blankToNull(42)).toBeNull()
  })
})

describe('buildCounterpartyFormSchema', () => {
  const schema = buildCounterpartyFormSchema(t)

  describe('name length', () => {
    it('accepts a name of exactly 250 characters — the database column limit', () => {
      expect(schema.safeParse({ ...baseInput, name: 'a'.repeat(250) }).success).toBe(true)
    })

    it('rejects a 251-character name inline instead of round-tripping to the server', () => {
      const result = schema.safeParse({ ...baseInput, name: 'a'.repeat(251) })
      expect(result.success).toBe(false)
    })
  })

  describe('email', () => {
    it('passes validation for whitespace', () => {
      expect(schema.safeParse({ ...baseInput, email: '   ' }).success).toBe(true)
    })

    it('passes validation for empty string', () => {
      expect(schema.safeParse({ ...baseInput, email: '' }).success).toBe(true)
    })

    it('passes validation for null', () => {
      expect(schema.safeParse({ ...baseInput, email: null }).success).toBe(true)
    })

    it('passes validation for omitted field', () => {
      expect(schema.safeParse(baseInput).success).toBe(true)
    })

    it('passes validation for a valid email (including surrounding whitespace)', () => {
      expect(
        schema.safeParse({ ...baseInput, email: '  foo@bar.com  ' }).success,
      ).toBe(true)
    })

    it('rejects a non-blank invalid email with the configured i18n key', () => {
      const result = schema.safeParse({ ...baseInput, email: 'not-an-email' })
      expect(result.success).toBe(false)
      if (!result.success) {
        const issue = result.error.issues.find(i => i.path[0] === 'email')
        expect(issue?.message).toBe(
          'page.counterpartyManagement.form.validation.invalidEmail',
        )
      }
    })
  })

  describe('website', () => {
    it('passes validation for whitespace', () => {
      expect(
        schema.safeParse({ ...baseInput, website: '   ' }).success,
      ).toBe(true)
    })

    it('passes validation for empty string', () => {
      expect(schema.safeParse({ ...baseInput, website: '' }).success).toBe(true)
    })

    it('passes validation for null', () => {
      expect(schema.safeParse({ ...baseInput, website: null }).success).toBe(
        true,
      )
    })

    it('passes validation for omitted field', () => {
      expect(schema.safeParse(baseInput).success).toBe(true)
    })

    it('passes validation for a valid URL (including surrounding whitespace)', () => {
      expect(
        schema.safeParse({
          ...baseInput,
          website: '  https://example.com  ',
        }).success,
      ).toBe(true)
    })

    it('rejects a non-blank invalid URL with the configured i18n key', () => {
      const result = schema.safeParse({ ...baseInput, website: 'not a url' })
      expect(result.success).toBe(false)
      if (!result.success) {
        const issue = result.error.issues.find(i => i.path[0] === 'website')
        expect(issue?.message).toBe(
          'page.counterpartyManagement.form.validation.invalidUrl',
        )
      }
    })
  })

  it('still rejects when required fields are missing', () => {
    expect(schema.safeParse({ ...baseInput, name: '' }).success).toBe(false)
  })

  describe('name forbidden characters', () => {
    it.each([
      'W & T Offshore, Inc. ("WT")',
      'Genie “Energy” Ltd.',
      'Acme {Corp}',
      'Acme\\Corp',
      'Acme`Corp',
      'Acme|Corp',
      'Acme<Corp>',
      'Acme\0Corp',
      'Acme\nCorp',
    ])('rejects %j with the configured i18n key', name => {
      const result = schema.safeParse({ ...baseInput, name })
      expect(result.success).toBe(false)
      if (!result.success) {
        const issue = result.error.issues.find(i => i.path[0] === 'name')
        expect(issue?.message).toBe(
          'page.counterpartyManagement.form.validation.nameForbiddenCharacters',
        )
      }
    })

    it('rejects forbidden characters in nameVariants', () => {
      const result = schema.safeParse({
        ...baseInput,
        nameVariants: 'Alias "WT"',
      })
      expect(result.success).toBe(false)
    })

    it('allows pipe separators in nameVariants', () => {
      const result = schema.safeParse({
        ...baseInput,
        nameVariants: 'ibm corp|int.bus.m',
      })
      expect(result.success).toBe(true)
    })

    it('still rejects pipes in name', () => {
      expect(schema.safeParse({ ...baseInput, name: 'Acme|Corp' }).success).toBe(
        false,
      )
    })

    it.each([
      "O'Reilly Automotive, Inc.",
      "McDonald's Corporation",
      'AT&T Inc.',
      'Société Générale S.A.',
    ])('allows legitimate punctuation: %s', name => {
      expect(schema.safeParse({ ...baseInput, name }).success).toBe(true)
    })
  })
})
