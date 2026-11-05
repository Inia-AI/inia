import { describe, expect, it } from 'vitest'
import { groupKeysByLowercase, tagsByLowercase } from './variableTags'

describe('tagsByLowercase', () => {
  it('indexes each tag by its lowercase form', () => {
    const index = tagsByLowercase(['Sectors', 'NetLeverage'])
    expect(index.get('sectors')).toBe('Sectors')
    expect(index.get('netleverage')).toBe('NetLeverage')
  })

  it('marks a lowercase form claimed by two differently cased tags as ambiguous', () => {
    const index = tagsByLowercase(['Sectors', 'sectors'])
    expect(index.get('sectors')).toBeNull()
  })

  it('does not treat a repeated identical tag as ambiguous', () => {
    const index = tagsByLowercase(['Sectors', 'Sectors'])
    expect(index.get('sectors')).toBe('Sectors')
  })

  it('returns an empty index for no tags', () => {
    expect(tagsByLowercase([]).size).toBe(0)
  })
})

describe('groupKeysByLowercase', () => {
  it('groups keys that differ only in case, keeping the stored order', () => {
    const groups = groupKeysByLowercase(['Sectors', 'other', 'sectors'])
    expect(groups.get('sectors')).toEqual(['Sectors', 'sectors'])
    expect(groups.get('other')).toEqual(['other'])
  })

  it('returns one group per key when nothing collides', () => {
    const groups = groupKeysByLowercase(['a', 'b'])
    expect([...groups.values()]).toEqual([['a'], ['b']])
  })
})
