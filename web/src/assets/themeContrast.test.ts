import { describe, expect, it } from 'vitest'
import {
  BODY_TEXT_RATIO,
  contrastRatio,
  GRAPHIC_INKS,
  GRAPHIC_RATIO,
  opaqueGround,
  PAGE_GROUNDS,
  PAGE_INKS,
  PAIRED_INKS,
  resolveColor,
  THEMES,
  type Theme,
} from '@/test/helpers/themeColors'

const shortfalls = (theme: Theme, inks: string[], grounds: string[], needed = BODY_TEXT_RATIO) =>
  inks.flatMap(ink =>
    grounds.flatMap(ground => {
      const inkColor = resolveColor(`var(${ink})`, theme)
      const groundColor = resolveColor(`var(${ground})`, theme)
      if (!inkColor || !groundColor) return [`${ink} on ${ground}: a token does not resolve to a colour`]
      const ratio = contrastRatio(inkColor, opaqueGround(groundColor, theme))
      return ratio < needed ? [`${ink} on ${ground}: ${ratio.toFixed(2)}`] : []
    }),
  )

describe.each(THEMES)('the %s theme palette', theme => {
  it('gives every text token at least 4.5:1 on every page ground', () => {
    expect(shortfalls(theme, PAGE_INKS, PAGE_GROUNDS)).toEqual([])
  })

  it('gives every graphic token at least 3:1 on every page ground', () => {
    expect(shortfalls(theme, GRAPHIC_INKS, PAGE_GROUNDS, GRAPHIC_RATIO)).toEqual([])
  })

  it('gives every paired text token at least 4.5:1 on the grounds it is made for', () => {
    expect(Object.entries(PAIRED_INKS).flatMap(([ink, grounds]) => shortfalls(theme, [ink], grounds))).toEqual([])
  })
})
