import mainCss from '@/assets/main.css?raw'
import tailwindTheme from 'tailwindcss/theme.css?raw'

export type Theme = 'light' | 'dark'
export const THEMES: Theme[] = ['light', 'dark']

export interface Rgba {
  r: number
  g: number
  b: number
  a: number
}

export const PAGE_GROUNDS = ['--background', '--card', '--muted']

export const PAGE_INKS = [
  '--foreground',
  '--card-foreground',
  '--popover-foreground',
  '--muted-foreground',
  '--primary',
  '--text-primary',
  '--text-secondary',
  '--text-muted',
  '--text-tertiary',
  '--text-info',
  '--accent-indigo-from',
  '--accent-indigo-to',
  '--accent-success',
  '--accent-warning',
  '--accent-error',
  '--accent-info',
]

export const PAIRED_INKS: Record<string, string[]> = {
  '--primary-foreground': ['--primary'],
  '--secondary-foreground': ['--secondary'],
  '--accent-foreground': ['--accent'],
  '--destructive-foreground': ['--destructive'],
  '--nav-ink': ['--nav-bg', '--nav-active', '--nav-hover'],
  '--nav-ink-muted': ['--nav-bg'],
  '--text-info': ['--bg-info'],
  '--accent-success': ['--accent-success-light'],
  '--accent-warning': ['--accent-warning-light'],
  '--accent-error': ['--accent-error-light'],
  '--accent-info': ['--accent-info-light'],
}

export const GRAPHIC_INKS = ['--presence']

export const VERIFIED_INKS = new Set([...PAGE_INKS, ...Object.keys(PAIRED_INKS)])
export const BODY_TEXT_RATIO = 4.5
export const LARGE_TEXT_RATIO = 3
export const GRAPHIC_RATIO = 3
export const VAR_REFERENCE = /^var\(\s*(--[\w-]+)\s*(?:,\s*(.+))?\)$/

const clamp = (value: number) => Math.min(1, Math.max(0, value))

const fromLinear = (value: number) =>
  value <= 0.0031308 ? 12.92 * value : 1.055 * Math.pow(value, 1 / 2.4) - 0.055

const oklchToRgb = (lightness: number, chroma: number, hue: number): Omit<Rgba, 'a'> => {
  const radians = (hue * Math.PI) / 180
  const a = chroma * Math.cos(radians)
  const b = chroma * Math.sin(radians)
  const l = Math.pow(lightness + 0.3963377774 * a + 0.2158037573 * b, 3)
  const m = Math.pow(lightness - 0.1055613458 * a - 0.0638541728 * b, 3)
  const s = Math.pow(lightness - 0.0894841775 * a - 1.291485548 * b, 3)
  return {
    r: clamp(fromLinear(4.0767416621 * l - 3.3077115913 * m + 0.2309699292 * s)),
    g: clamp(fromLinear(-1.2684380046 * l + 2.6097574011 * m - 0.3413193965 * s)),
    b: clamp(fromLinear(-0.0041960863 * l - 0.7034186147 * m + 1.707614701 * s)),
  }
}

const alphaOf = (raw: string | undefined) => {
  if (raw === undefined) return 1
  return raw.endsWith('%') ? Number(raw.slice(0, -1)) / 100 : Number(raw)
}

const parseColor = (value: string): Rgba | null => {
  const text = value.trim().toLowerCase()
  if (text === 'white') return { r: 1, g: 1, b: 1, a: 1 }
  if (text === 'black') return { r: 0, g: 0, b: 0, a: 1 }
  if (text === 'transparent') return { r: 0, g: 0, b: 0, a: 0 }
  const hex = text.match(/^#([0-9a-f]{3,8})$/)
  if (hex) {
    const digits = hex[1].length <= 4 ? [...hex[1]].map(digit => digit + digit).join('') : hex[1]
    const channel = (index: number) => parseInt(digits.slice(index, index + 2), 16) / 255
    return { r: channel(0), g: channel(2), b: channel(4), a: digits.length === 8 ? channel(6) : 1 }
  }
  const rgb = text.match(/^rgba?\(\s*([\d.]+)[\s,]+([\d.]+)[\s,]+([\d.]+)(?:\s*[,/]\s*([\d.]+%?))?\s*\)$/)
  if (rgb) {
    return { r: Number(rgb[1]) / 255, g: Number(rgb[2]) / 255, b: Number(rgb[3]) / 255, a: alphaOf(rgb[4]) }
  }
  const oklch = text.match(/^oklch\(\s*([\d.]+)(%?)\s+([\d.]+)\s+([\d.]+)(?:\s*\/\s*([\d.]+%?))?\s*\)$/)
  if (oklch) {
    const lightness = Number(oklch[1]) / (oklch[2] ? 100 : 1)
    return { ...oklchToRgb(lightness, Number(oklch[3]), Number(oklch[4])), a: alphaOf(oklch[5]) }
  }
  return null
}

const toLinear = (value: number) => (value <= 0.04045 ? value / 12.92 : Math.pow((value + 0.055) / 1.055, 2.4))

const luminance = ({ r, g, b }: Rgba) => 0.2126 * toLinear(r) + 0.7152 * toLinear(g) + 0.0722 * toLinear(b)

const composite = (top: Rgba, under: Rgba): Rgba => ({
  r: top.r * top.a + under.r * (1 - top.a),
  g: top.g * top.a + under.g * (1 - top.a),
  b: top.b * top.a + under.b * (1 - top.a),
  a: 1,
})

export const contrastRatio = (ink: Rgba, ground: Rgba) => {
  const text = composite(ink, ground)
  const [high, low] = [luminance(text), luminance(ground)].sort((left, right) => right - left)
  return (high + 0.05) / (low + 0.05)
}

export const toHex = ({ r, g, b }: Rgba) =>
  `#${[r, g, b].map(channel => Math.round(channel * 255).toString(16).padStart(2, '0')).join('')}`

const withoutComments = (css: string) => css.replace(/\/\*[\s\S]*?\*\//g, '')

const customProperties = (block: string) =>
  new Map([...withoutComments(block).matchAll(/(--[\w-]+)\s*:\s*([^;]+);/g)].map(match => [match[1], match[2].trim()]))

const topLevelBlocks = (css: string, selector: string) => {
  const blocks: string[] = []
  const opener = new RegExp(`(^|\\n)${selector.replace('.', '\\.')}\\s*\\{`, 'g')
  for (const match of css.matchAll(opener)) {
    let depth = 1
    let index = (match.index ?? 0) + match[0].length
    const start = index
    while (depth > 0 && index < css.length) {
      if (css[index] === '{') depth++
      if (css[index] === '}') depth--
      index++
    }
    blocks.push(css.slice(start, index - 1))
  }
  return blocks
}

let palette: { tokens: Record<Theme, Map<string, string>>; utilities: Map<string, string> } | null = null

const COLOR_UTILITY_PREFIX = '--color-'

const loadPalette = () => {
  if (palette) return palette
  const light = new Map(topLevelBlocks(mainCss, ':root').flatMap(block => [...customProperties(block)]))
  const dark = new Map([...light, ...topLevelBlocks(mainCss, '.dark').flatMap(block => [...customProperties(block)])])
  const utilities = new Map<string, string>()
  for (const block of [tailwindTheme, ...topLevelBlocks(mainCss, '@theme inline')]) {
    for (const [name, value] of customProperties(block)) {
      if (name.startsWith(COLOR_UTILITY_PREFIX)) utilities.set(name.slice(COLOR_UTILITY_PREFIX.length), value)
    }
  }
  palette = { tokens: { light, dark }, utilities }
  return palette
}

export const themeToken = (name: string, theme: Theme) => loadPalette().tokens[theme].get(name)

export const resolveColor = (value: string, theme: Theme, depth = 0): Rgba | null => {
  if (depth > 8) return null
  const variable = value.trim().match(VAR_REFERENCE)
  if (variable) {
    const token = themeToken(variable[1], theme)
    if (token !== undefined) return resolveColor(token, theme, depth + 1)
    return variable[2] ? resolveColor(variable[2], theme, depth + 1) : null
  }
  return parseColor(value)
}

export const themeAlias = (token: string) =>
  token.startsWith(COLOR_UTILITY_PREFIX) ? (utilityToken(token.slice(COLOR_UTILITY_PREFIX.length)) ?? token) : token

const utilityValue = (base: string) => base.match(/^\[(.+)\]$/)?.[1] ?? loadPalette().utilities.get(base)

export const utilityToken = (name: string): string | null =>
  utilityValue(name.split('/')[0])?.trim().match(VAR_REFERENCE)?.[1] ?? null

export const utilityColor = (name: string, theme: Theme): Rgba | null => {
  const [base, opacity] = name.split('/')
  const value = utilityValue(base)
  if (!value) return null
  const color = resolveColor(value, theme)
  if (!color) return null
  return opacity ? { ...color, a: color.a * (Number(opacity) / 100) } : color
}

export const opaqueGround = (ground: Rgba, theme: Theme) =>
  ground.a < 1 ? composite(ground, resolveColor('var(--background)', theme) ?? ground) : ground

export const pageGrounds = (theme: Theme) =>
  PAGE_GROUNDS.map(name => ({ name, color: resolveColor(`var(${name})`, theme) })).filter(
    (ground): ground is { name: string; color: Rgba } => ground.color !== null,
  )
