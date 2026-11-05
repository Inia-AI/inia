import { describe, expect, it } from 'vitest'
import { newPasswordSchema, PASSWORD_MIN_LENGTH } from './passwordPolicy'

const messages = {
  minLength: 'too short',
  uppercase: 'needs upper',
  lowercase: 'needs lower',
  digit: 'needs digit',
}

const schema = newPasswordSchema(messages)

const failures = (password: string): string[] => {
  const result = schema.safeParse(password)
  return result.success ? [] : result.error.issues.map(issue => issue.message)
}

describe('newPasswordSchema', () => {
  it('matches the server minimum length', () => {
    expect(PASSWORD_MIN_LENGTH).toBe(8)
  })

  it('accepts a password with the length, an uppercase, a lowercase and a digit', () => {
    expect(failures('Abcdefg1')).toEqual([])
  })

  it('rejects a password shorter than the minimum', () => {
    expect(failures('Abc1')).toContain('too short')
  })

  it('rejects a seven-character password that meets every other rule', () => {
    expect(failures('Abcdef1')).toEqual(['too short'])
  })

  it('rejects a password with no uppercase letter', () => {
    expect(failures('abcdefg1')).toEqual(['needs upper'])
  })

  it('rejects a password with no lowercase letter', () => {
    expect(failures('ABCDEFG1')).toEqual(['needs lower'])
  })

  it('rejects a password with no digit', () => {
    expect(failures('Abcdefgh')).toEqual(['needs digit'])
  })

  it('reports every rule the password breaks', () => {
    expect(failures('abc')).toEqual(['too short', 'needs upper', 'needs digit'])
  })

  it('only counts A to Z and a to z as letters, the way the server does', () => {
    expect(failures('ÜNÏCODES1')).toEqual(['needs lower'])
    expect(failures('ünïcodes1')).toEqual(['needs upper'])
  })
})
