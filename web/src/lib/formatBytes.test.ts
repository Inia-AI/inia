import { describe, expect, it } from 'vitest'
import { formatBytes } from './formatBytes'

describe('formatBytes', () => {
  it('returns a dash for nullish or NaN input', () => {
    expect(formatBytes(null)).toBe('-')
    expect(formatBytes(undefined)).toBe('-')
    expect(formatBytes(Number.NaN)).toBe('-')
  })

  it('returns integer bytes below 1 KB', () => {
    expect(formatBytes(0)).toBe('0 B')
    expect(formatBytes(1)).toBe('1 B')
    expect(formatBytes(1023)).toBe('1023 B')
  })

  it('switches to KB at exactly 1024', () => {
    expect(formatBytes(1024)).toBe('1.0 KB')
    expect(formatBytes(1536)).toBe('1.5 KB')
  })

  it('formats MB, GB, TB with 1 decimal', () => {
    expect(formatBytes(1024 * 1024)).toBe('1.0 MB')
    expect(formatBytes(1.5 * 1024 * 1024)).toBe('1.5 MB')
    expect(formatBytes(1024 ** 3)).toBe('1.0 GB')
    expect(formatBytes(1024 ** 4)).toBe('1.0 TB')
  })

  it('rounds to 1 decimal at the largest applicable unit', () => {
    expect(formatBytes(1234567)).toBe('1.2 MB')
  })

  it('handles negative numbers as dashes (defensive)', () => {
    expect(formatBytes(-1)).toBe('-')
  })
})
