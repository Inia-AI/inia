import { describe, expect, it } from 'vitest'
import { newPasswordSchema, PASSWORD_MIN_LENGTH } from './passwordPolicy'

const schema = newPasswordSchema('too short')

const failures = (password: string): string[] => {
  const result = schema.safeParse(password)
  return result.success ? [] : result.error.issues.map(issue => issue.message)
}

describe('newPasswordSchema', () => {
  it('matches the server minimum length', () => {
    expect(PASSWORD_MIN_LENGTH).toBe(8)
  })

  it('accepts eight characters of one kind, as the server does', () => {
    expect(failures('quietmap')).toEqual([])
    expect(failures('QUIETMAP')).toEqual([])
  })

  it('rejects a password shorter than the minimum', () => {
    expect(failures('Abcdef1')).toEqual(['too short'])
  })
})
