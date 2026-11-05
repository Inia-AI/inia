import { type CssRule, cssRules, pixels, STATE_SELECTOR } from './cssRules'
import { sourceOf, sourcePaths } from './sourceFiles'
import { sfcOf } from './templateAst'
import {
  BODY_TEXT_RATIO,
  contrastRatio,
  GRAPHIC_INKS,
  GRAPHIC_RATIO,
  LARGE_TEXT_RATIO,
  opaqueGround,
  pageGrounds,
  resolveColor,
  type Rgba,
  type Theme,
  themeAlias,
  THEMES,
  toHex,
  utilityColor,
  utilityToken,
  VERIFIED_INKS,
} from './themeColors'

const SKIPPED_PATHS = /^(test|assets\/locales)\//
const LARGE_TEXT_UTILITY = /^text-([2-9]xl)$/
const BOLD_LARGE_TEXT_UTILITY = /^text-xl$/
const BOLD_UTILITY = /^font-(bold|extrabold|black)$/
const OPACITY_UTILITY = /^opacity-(\d+)$/
const ICON_SIZE_UTILITY = /^(size|h|w)-[1-6]$/
const FULL_OPACITY = 100
const RESPONSIVE_PREFIX = /^((sm|md|lg|xl|2xl):)+/
const DARK_SELECTOR = /:global\(\.dark\)\s*|\.dark\s+|\.dark\b(?=[\s&>]|$)/
const DARK_SELECTORS = new RegExp(DARK_SELECTOR.source, 'g')
const INHERITED_VALUE = /^(inherit|currentcolor|transparent|unset|initial|revert)$/i
const UNCHECKABLE_VALUE = /color-mix|gradient|url\(/i
const TOKEN_REFERENCE = /^var\(\s*(--[\w-]+)/
const GRAPHIC_SELECTOR = /(svg|icon|-dot|caret|chevron)[\w-]*\)?\s*$/
const LARGE_FONT_PX = 24
const BOLD_LARGE_FONT_PX = 18.66
const BOLD_WEIGHT = 700

interface TextColor {
  where: string
  token: string | null
  color: (theme: Theme) => Rgba | null
  ground: ((theme: Theme) => Rgba | null) | null
  dimmed: number
  needed: number
  graphic: boolean
  themes: Theme[]
}

const sourceFiles = () => sourcePaths(/\.(vue|ts|css)$/).filter(file => !SKIPPED_PATHS.test(file))

const lineOf = (text: string, index: number) => text.slice(0, index).split('\n').length

const utilitiesOf = (tokens: string[], prefix: 'text' | 'bg') =>
  tokens
    .map(token => token.match(new RegExp(`^(dark:)?${prefix}-(.+)$`)))
    .filter((match): match is RegExpMatchArray => match !== null && utilityColor(match[2], 'light') !== null)
    .map(match => ({ dark: !!match[1], name: match[2] }))

const colorAt = (utilities: { dark: boolean; name: string }[]) => (theme: Theme) => {
  const chosen = (theme === 'dark' ? utilities.find(item => item.dark) : undefined) ?? utilities.find(item => !item.dark)
  return chosen ? utilityColor(chosen.name, theme) : null
}

const classStrings = (source: string) =>
  [...source.matchAll(/(["'`])((?:(?!\1)[^\\\n]|\\.)*)\1/g)].map(match => ({
    text: match[2],
    index: match.index ?? 0,
    tokens: match[2]
      .split(/\s+/)
      .filter(Boolean)
      .map(token => token.replace(RESPONSIVE_PREFIX, '')),
  }))

const classStringFindings = (file: string, source: string) => {
  const colors: TextColor[] = []
  const dimmers: string[] = []
  for (const { text, index, tokens } of classStrings(source)) {
    const opacityValue = tokens.map(token => token.match(OPACITY_UTILITY)?.[1]).find(Boolean)
    const opacity = opacityValue === undefined ? FULL_OPACITY : Number(opacityValue)
    if (opacity === 0) continue
    const where = `${file}:${lineOf(source, index)} "${text.slice(0, 80)}"`
    const inks = utilitiesOf(tokens, 'text')
    const grounds = utilitiesOf(tokens, 'bg')
    if (inks.length === 0 && (opacity === FULL_OPACITY || grounds.length)) continue
    const graphic = tokens.some(token => ICON_SIZE_UTILITY.test(token))
    if (inks.length === 0) {
      colors.push({
        where,
        token: null,
        color: theme => resolveColor('var(--foreground)', theme),
        ground: null,
        dimmed: opacity / FULL_OPACITY,
        needed: graphic ? GRAPHIC_RATIO : BODY_TEXT_RATIO,
        graphic,
        themes: THEMES,
      })
      continue
    }
    const large =
      tokens.some(item => LARGE_TEXT_UTILITY.test(item)) ||
      (tokens.some(item => BOLD_LARGE_TEXT_UTILITY.test(item)) && tokens.some(item => BOLD_UTILITY.test(item)))
    colors.push({
      where,
      token: inks.some(item => item.dark || item.name.includes('/')) ? null : utilityToken(inks[0].name),
      color: colorAt(inks),
      ground: grounds.length ? colorAt(grounds) : null,
      dimmed: opacity / FULL_OPACITY,
      needed: large ? LARGE_TEXT_RATIO : BODY_TEXT_RATIO,
      graphic: false,
      themes: THEMES,
    })
  }
  return { colors, dimmers }
}

const isLarge = (declarations: Map<string, string>) => {
  const size = pixels(declarations.get('font-size')) ?? 0
  const weightValue = declarations.get('font-weight')
  const weight = weightValue === 'bold' ? BOLD_WEIGHT : Number(weightValue) || 0
  return size >= LARGE_FONT_PX || (size >= BOLD_LARGE_FONT_PX && weight >= BOLD_WEIGHT)
}

const opacityOf = (declarations: Map<string, string>) => {
  const value = Number(declarations.get('opacity') ?? 1)
  return Number.isFinite(value) ? value : 1
}

const cssRuleColors = (file: string, rules: CssRule[]): { colors: TextColor[]; dimmers: string[] } => {
  const darkOverrides = new Set(
    rules
      .filter(rule => DARK_SELECTOR.test(rule.selector) && rule.declarations.has('color'))
      .map(rule => rule.selector.replace(DARK_SELECTORS, '').trim()),
  )
  const merged = new Map<string, Map<string, string>>()
  for (const rule of rules) {
    merged.set(rule.selector, new Map([...(merged.get(rule.selector) ?? []), ...rule.declarations]))
  }
  const colors: TextColor[] = []
  const dimmers: string[] = []
  for (const rule of rules) {
    const dimmed = opacityOf(rule.declarations)
    if (STATE_SELECTOR.test(rule.selector) || dimmed === 0) continue
    const where = `${file}:${rule.line} ${rule.selector.replace(/\s+/g, ' ').slice(0, 80)}`
    const value = rule.declarations.get('color')?.trim()
    if (!value) {
      const all = merged.get(rule.selector) ?? rule.declarations
      const paintsAShape = all.has('background') || all.has('background-color')
      if (dimmed < 1 && !all.has('color') && !paintsAShape) {
        dimmers.push(`${where}: dims its text with opacity ${dimmed}; set a verified text colour instead`)
      }
      continue
    }
    if (INHERITED_VALUE.test(value)) continue
    if (UNCHECKABLE_VALUE.test(value)) {
      dimmers.push(`${where}: mixes its text colour, which this guard cannot check; use a verified text token`)
      continue
    }
    const darkOnly = DARK_SELECTOR.test(rule.selector)
    const background = rule.declarations.get('background-color') ?? rule.declarations.get('background')
    const graphic = GRAPHIC_SELECTOR.test(rule.selector)
    colors.push({
      where,
      token: value.match(TOKEN_REFERENCE)?.[1] ?? null,
      color: theme => resolveColor(value, theme),
      ground:
        background && !INHERITED_VALUE.test(background.trim()) && !UNCHECKABLE_VALUE.test(background) && resolveColor(background, 'light')
          ? theme => resolveColor(background, theme)
          : null,
      dimmed,
      needed: graphic ? GRAPHIC_RATIO : isLarge(rule.declarations) ? LARGE_TEXT_RATIO : BODY_TEXT_RATIO,
      graphic,
      themes: darkOnly ? ['dark'] : THEMES.filter(theme => theme === 'light' || !darkOverrides.has(rule.selector.trim())),
    })
  }
  return { colors, dimmers }
}

const scan = () => {
  const colors: TextColor[] = []
  const dimmers: string[] = []
  for (const file of sourceFiles()) {
    const source = sourceOf(file)
    const addFindings = (found: { colors: TextColor[]; dimmers: string[] }) => {
      colors.push(...found.colors)
      dimmers.push(...found.dimmers)
    }
    const addRules = (rules: CssRule[]) => addFindings(cssRuleColors(file, rules))
    if (file.endsWith('.css')) {
      addRules(cssRules(source))
    } else if (file.endsWith('.vue')) {
      for (const style of sfcOf(file).styles.filter(block => !block.src)) {
        addRules(cssRules(style.content, style.loc.start.line))
      }
      const markup = source.replace(/<style[\s\S]*?<\/style>/g, (block: string) => block.replace(/[^\n]/g, ' '))
      addFindings(classStringFindings(file, markup))
    } else {
      addFindings(classStringFindings(file, source))
    }
  }
  return { colors, dimmers }
}

const knownGroundShortfalls = (color: TextColor): string[] =>
  color.themes.flatMap(theme => {
    const ink = color.color(theme)
    const ground = color.ground?.(theme)
    if (!ink || !ground) return []
    const opaque = opaqueGround(ground, theme)
    const ratio = contrastRatio({ ...ink, a: ink.a * color.dimmed }, opaque)
    return ratio < color.needed
      ? [`${color.where} (${theme}): ${toHex(ink)} on ${toHex(opaque)} is ${ratio.toFixed(2)}, needs ${color.needed}`]
      : []
  })

const pageGroundShortfalls = (color: TextColor): string[] =>
  color.themes.flatMap(theme => {
    const ink = color.color(theme)
    if (!ink) return []
    return pageGrounds(theme)
      .map(ground => ({ ground, ratio: contrastRatio({ ...ink, a: ink.a * color.dimmed }, ground.color) }))
      .filter(({ ratio }) => ratio < color.needed)
      .map(
        ({ ground, ratio }) =>
          `${color.where} (${theme}): ${toHex(ink)} on ${ground.name} is ${ratio.toFixed(2)}, needs ${color.needed}`,
      )
  })

const unknownGroundProblems = (color: TextColor): string[] => {
  if (color.token === null) return pageGroundShortfalls(color)
  const token = themeAlias(color.token)
  if (color.graphic && GRAPHIC_INKS.includes(token)) return []
  if (!VERIFIED_INKS.has(token)) {
    return [`${color.where}: ${color.token} is not a verified text colour`]
  }
  if (color.dimmed < 1) {
    return [`${color.where}: dims ${color.token} with opacity ${color.dimmed}`]
  }
  return []
}

export const textColorProblems = (): string[] => {
  const { colors, dimmers } = scan()
  return [...colors.flatMap(color => (color.ground ? knownGroundShortfalls(color) : unknownGroundProblems(color))), ...dimmers]
}
