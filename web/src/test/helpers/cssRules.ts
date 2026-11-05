import { themeToken, VAR_REFERENCE } from './themeColors'

export interface CssRule {
  selector: string
  declarations: Map<string, string>
  line: number
}

export const STATE_SELECTOR =
  /:(hover|focus|focus-visible|focus-within|active|disabled|visited)\b|\[disabled\]|-disabled\b|\[aria-disabled|::placeholder|::selection|::before|::after/

const ROOT_FONT_PX = 16

const lineOf = (text: string, index: number) => text.slice(0, index).split('\n').length

const nestedSelector = (raw: string, parent: string) =>
  raw
    .split(',')
    .map(part => {
      const own = part.trim()
      if (!parent) return own
      return own.includes('&') ? own.replace(/&/g, parent) : `${parent} ${own}`
    })
    .join(', ')

export const cssRules = (css: string, firstLine = 1): CssRule[] => {
  const text = css.replace(/\/\*[\s\S]*?\*\//g, comment => comment.replace(/[^\n]/g, ' '))
  const rules: CssRule[] = []
  const stack: (CssRule & { skip: boolean })[] = []
  let buffer = ''
  let bufferStart = 0
  const declare = () => {
    const declaration = buffer.match(/^\s*([\w-]+)\s*:\s*([\s\S]+?)\s*(!important)?\s*$/)
    const frame = stack[stack.length - 1]
    if (declaration && frame && !frame.skip) frame.declarations.set(declaration[1].toLowerCase(), declaration[2])
  }
  for (let index = 0; index < text.length; index++) {
    const char = text[index]
    if (char === '{') {
      const raw = buffer.trim()
      const parent = stack[stack.length - 1]
      stack.push({
        selector: raw.startsWith('@') ? (parent?.selector ?? '') : nestedSelector(raw, parent?.selector ?? ''),
        declarations: new Map(),
        line: firstLine + lineOf(text, bufferStart) - 1,
        skip: (parent?.skip ?? false) || /^@(keyframes|font-face|theme)/.test(raw),
      })
    } else if (char === ';') {
      declare()
    } else if (char === '}') {
      declare()
      const frame = stack.pop()
      if (frame && !frame.skip && frame.declarations.size) rules.push(frame)
    } else {
      if (!buffer.trim()) bufferStart = index
      buffer += char
      continue
    }
    buffer = ''
    bufferStart = index + 1
  }
  return rules
}

export const pixels = (value: string | undefined, fontSize = ROOT_FONT_PX): number | undefined => {
  const token = value?.trim().match(VAR_REFERENCE)
  if (token) return pixels(themeToken(token[1], 'light') ?? token[2], fontSize)
  const match = value?.trim().match(/^(-?[\d.]+)(px|rem|em)?$/)
  if (!match) return undefined
  const amount = Number(match[1])
  if (match[2] === 'rem') return amount * ROOT_FONT_PX
  if (match[2] === 'em') return amount * fontSize
  return match[2] === 'px' || amount === 0 ? amount : undefined
}
