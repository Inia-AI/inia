import { badgeVariants } from '@/components/ui/badge'
import { buttonVariants, type ButtonVariants } from '@/components/ui/button'
import { type CssRule, cssRules, pixels, STATE_SELECTOR } from './cssRules'
import { sourceOf, sourcePaths } from './sourceFiles'
import {
  boundExpression,
  NODE_COMMENT,
  NODE_ELEMENT,
  sfcOf,
  staticAttribute,
  type TemplateNode,
  templateOf,
  walkElements,
} from './templateAst'

export const MIN_TARGET_PX = 24

const TAILWIND_SPACING_PX = 4
const ROOT_FONT_PX = 16
const DEFAULT_ICON_PX = 16
const TEXT_SIZES: Record<string, { font: number; line: number }> = {
  'text-xs': { font: 12, line: 16 },
  'text-sm': { font: 14, line: 20 },
  'text-base': { font: 16, line: 24 },
  'text-lg': { font: 18, line: 28 },
  'text-xl': { font: 20, line: 28 },
}
const ICON_TAGS = new Set(['IniaIcon', 'svg', 'font-awesome-icon'])
const SIZE_PROPERTIES = [
  ['height', 'height'],
  ['min-height', 'minHeight'],
  ['width', 'width'],
  ['min-width', 'minWidth'],
  ['padding-top', 'padTop'],
  ['padding-bottom', 'padBottom'],
  ['padding-left', 'padLeft'],
  ['padding-right', 'padRight'],
] as const

interface Box {
  height?: number
  minHeight?: number
  width?: number
  minWidth?: number
  fontSize?: number
  lineHeight?: number
  padTop: number
  padBottom: number
  padLeft: number
  padRight: number
}

type RuleIndex = Map<string, CssRule[]>

interface IndexedRules {
  rules: CssRule[]
  index: RuleIndex
}

const spacing = (value: string) => {
  const arbitrary = value.match(/^\[(.+)\]$/)
  if (arbitrary) return pixels(arbitrary[1])
  return /^[\d.]+$/.test(value) ? Number(value) * TAILWIND_SPACING_PX : undefined
}

const applyTailwind = (box: Box, classes: string[]) => {
  for (const token of classes) {
    if (token.includes(':')) continue
    const text = TEXT_SIZES[token]
    if (text) {
      box.fontSize = text.font
      box.lineHeight = text.line
    }
    const sized = token.match(/^(min-h|min-w|h|w|size|p|py|px|pt|pb|pl|pr)-(.+)$/)
    const amount = sized ? spacing(sized[2]) : undefined
    if (!sized || amount === undefined) continue
    const [, kind] = sized
    if (kind === 'h' || kind === 'size') box.height = amount
    if (kind === 'w' || kind === 'size') box.width = amount
    if (kind === 'min-h') box.minHeight = amount
    if (kind === 'min-w') box.minWidth = amount
    if (kind === 'p' || kind === 'py' || kind === 'pt') box.padTop = amount
    if (kind === 'p' || kind === 'py' || kind === 'pb') box.padBottom = amount
    if (kind === 'p' || kind === 'px' || kind === 'pl') box.padLeft = amount
    if (kind === 'p' || kind === 'px' || kind === 'pr') box.padRight = amount
  }
}

const applyCss = (box: Box, declarations: Map<string, string>) => {
  const fontSize = pixels(declarations.get('font-size'), box.fontSize)
  if (fontSize !== undefined) box.fontSize = fontSize
  const lineHeight = declarations.get('line-height')?.trim()
  if (lineHeight !== undefined) {
    const unitless = /^[\d.]+$/.test(lineHeight) ? Number(lineHeight) * (box.fontSize ?? ROOT_FONT_PX) : undefined
    box.lineHeight = unitless ?? pixels(lineHeight, box.fontSize) ?? box.lineHeight
  }
  const padding = declarations.get('padding')?.trim().split(/\s+/).map(part => pixels(part, box.fontSize))
  if (padding?.every(part => part !== undefined)) {
    const [top, right = top, bottom = top, left = right] = padding as number[]
    Object.assign(box, { padTop: top, padRight: right, padBottom: bottom, padLeft: left })
  }
  for (const [property, key] of SIZE_PROPERTIES) {
    const value = pixels(declarations.get(property), box.fontSize)
    if (value !== undefined) box[key] = value
  }
}

const atLeast = (size: number | undefined, minimum: number | undefined) =>
  size === undefined || minimum === undefined ? size : Math.max(size, minimum)

const estimate = (box: Box, iconOnly: boolean, pill: boolean) => {
  if (pill) {
    const natural = (box.lineHeight ?? (box.fontSize ?? ROOT_FONT_PX) * 1.2) + box.padTop + box.padBottom
    return { height: atLeast(box.height ?? natural, box.minHeight), width: atLeast(box.width, box.minWidth) }
  }
  if (iconOnly) {
    const icon = box.lineHeight ?? box.fontSize ?? DEFAULT_ICON_PX
    return {
      height: atLeast(box.height ?? icon + box.padTop + box.padBottom, box.minHeight),
      width: atLeast(box.width ?? (box.fontSize ?? DEFAULT_ICON_PX) + box.padLeft + box.padRight, box.minWidth),
    }
  }
  return { height: atLeast(box.height, box.minHeight), width: atLeast(box.width, box.minWidth) }
}

const indexRules = (rules: CssRule[]): RuleIndex => {
  const index: RuleIndex = new Map()
  for (const rule of rules) {
    for (const selector of rule.selector.split(',')) {
      if (STATE_SELECTOR.test(selector)) continue
      const last = selector.trim().split(/[\s>+~]+/).pop() ?? ''
      for (const name of new Set(last.match(/(?<=\.)[\w-]+/g) ?? [])) {
        index.set(name, [...(index.get(name) ?? []), rule])
      }
    }
  }
  return index
}

const indexed = (rules: CssRule[]): IndexedRules => ({ rules, index: indexRules(rules) })

let globalRules: IndexedRules | null = null

const sharedRules = () => (globalRules ??= indexed(sourcePaths(/\.css$/).flatMap(file => cssRules(sourceOf(file)))))

const matchingRules = (classes: string[], { rules, index }: IndexedRules) => {
  const matched = new Set(classes.flatMap(name => index.get(name) ?? []))
  return rules.filter(rule => matched.has(rule))
}

export const targetBox = (classes: string[], { iconOnly = false, pill = false, local = indexed([]) } = {}) => {
  const box: Box = { padTop: 0, padBottom: 0, padLeft: 0, padRight: 0 }
  applyTailwind(box, classes)
  for (const rule of [...matchingRules(classes, sharedRules()), ...matchingRules(classes, local)]) {
    applyCss(box, rule.declarations)
  }
  return estimate(box, iconOnly, pill)
}

const classesOf = (node: TemplateNode) => {
  const own = staticAttribute(node, 'class')?.split(/\s+/) ?? []
  const literals = [...(boundExpression(node, 'class')?.matchAll(/'([^']*)'/g) ?? [])].flatMap(match =>
    match[1].split(/\s+/),
  )
  return [...own, ...literals].filter(Boolean)
}

const RENDERS_A_BUTTON = /^(DropdownMenu|Popover|Collapsible|HoverCard)Trigger$/

const isTarget = (node: TemplateNode) =>
  node.tag === 'button' ||
  node.tag === 'Button' ||
  staticAttribute(node, 'role') === 'button' ||
  staticAttribute(node, 'as') === 'button' ||
  (RENDERS_A_BUTTON.test(node.tag ?? '') && !node.props?.some(prop => prop.name === 'as-child' || prop.name === 'asChild'))

const isIconOnly = (node: TemplateNode) => {
  const children = node.children ?? []
  const elements = children.filter(child => child.type === NODE_ELEMENT)
  const hasText = children.some(child => child.type !== NODE_ELEMENT && child.type !== NODE_COMMENT)
  return !hasText && elements.length === 1 && ICON_TAGS.has(elements[0].tag ?? '')
}

const isPill = (node: TemplateNode) => node.tag === 'Badge'

const componentClasses = (node: TemplateNode) => {
  if (node.tag === 'Button') {
    const size = (staticAttribute(node, 'size') ?? 'default') as NonNullable<ButtonVariants['size']>
    return buttonVariants({ size }).split(/\s+/)
  }
  return isPill(node) ? badgeVariants().split(/\s+/) : []
}

const shortSides = ({ height, width }: { height?: number; width?: number }) =>
  [
    height !== undefined && height < MIN_TARGET_PX ? `${Math.round(height)}px tall` : null,
    width !== undefined && width < MIN_TARGET_PX ? `${Math.round(width)}px wide` : null,
  ].filter(Boolean)

export const undersizedTargets = (): string[] => {
  const problems: string[] = []
  for (const file of sourcePaths(/\.vue$/)) {
    const local = indexed(sfcOf(file).styles.flatMap(style => cssRules(style.content)))
    walkElements(templateOf(file), node => {
      if (!isTarget(node)) return
      const classes = [...componentClasses(node), ...classesOf(node)]
      if (classes.includes('sr-only')) return
      const short = shortSides(targetBox(classes, { iconOnly: isIconOnly(node), pill: isPill(node), local }))
      if (short.length) {
        const tag = `<${node.tag} class="${classes.join(' ').slice(0, 60)}">`
        problems.push(`${file}:${node.loc.start.line} ${tag} is ${short.join(' and ')}`)
      }
    })
  }
  return problems
}
