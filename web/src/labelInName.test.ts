import { describe, expect, it } from 'vitest'
import { EN } from '@/test/helpers/messages'

const NAME_SUFFIX = /(Of|For)$/

const namesThatDropTheirVisibleLabel = (messages: Record<string, unknown>, path: string[] = []): string[] =>
  Object.entries(messages).flatMap(([key, value]) => {
    if (value && typeof value === 'object') {
      return namesThatDropTheirVisibleLabel(value as Record<string, unknown>, [...path, key])
    }
    const visible = messages[key.replace(NAME_SUFFIX, '')]
    if (typeof value !== 'string' || !NAME_SUFFIX.test(key) || typeof visible !== 'string') return []
    return value.includes(visible) ? [] : [`${[...path, key].join('.')}: "${value}" does not contain "${visible}"`]
  })

describe('accessible names built on a visible label', () => {
  it('keep the words on screen, so a voice user who says them reaches the control', () => {
    expect(namesThatDropTheirVisibleLabel(EN)).toEqual([])
  })
})
