import { Marked, type MarkedExtension } from 'marked'
import DOMPurify from 'dompurify'

const disableMarkdownStrikethrough: MarkedExtension = {
  tokenizer: { del: () => undefined },
}

const markdown = new Marked().use(disableMarkdownStrikethrough)

const HTML_ALLOWLIST = new Set([
  'p',
  'div',
  'br',
  'hr',
  'h1',
  'h2',
  'h3',
  'h4',
  'h5',
  'h6',
  'ul',
  'ol',
  'li',
  'blockquote',
  'pre',
  'table',
  'caption',
  'colgroup',
  'col',
  'thead',
  'tbody',
  'tfoot',
  'tr',
  'th',
  'td',
  'section',
  'article',
  'header',
  'footer',
  'aside',
  'figure',
  'figcaption',
  'a',
  'span',
  'strong',
  'em',
  'b',
  'i',
  'u',
  's',
  'code',
  'img',
  'fact',
  'fact_source',
  'cited-text',
])

const UNKNOWN_TAG_PATTERN = /<(\/?)([a-zA-Z][\w-]*)([^>]*)>/g

const escapeUnknownTags = (raw: string): string =>
  raw.replace(UNKNOWN_TAG_PATTERN, (match, slash: string, tagName: string, attrs: string) => {
    if (HTML_ALLOWLIST.has(tagName.toLowerCase())) {
      return match
    }
    return `&lt;${slash}${tagName}${attrs}&gt;`
  })

const LEGACY_P_WRAPPER_PATTERN = /^\s*<p>((?:(?!<\/?p\b)[\s\S])*)<\/p>\s*$/i
const INNER_BLOCK_TAG_PATTERN =
  /<\/?(?:div|br|h[1-6]|ul|ol|table|blockquote|pre|section|article|header|footer|aside|figure|hr)\b/i

const unwrapLegacyPWrapper = (raw: string): string => {
  const match = raw.match(LEGACY_P_WRAPPER_PATTERN)
  if (!match) {
    return raw
  }
  const inner = match[1] ?? ''
  if (INNER_BLOCK_TAG_PATTERN.test(inner)) {
    return raw
  }
  return inner
}

// A style may lay out and colour a report, never load from another address: a page that shows
// model output would fetch it with no click. A style that names a resource, even through CSS
// escapes, is dropped whole, so every other style stays as written.
const CSS_ESCAPE = /\\(?:([0-9a-f]{1,6})\s?|([\s\S]))/gi
const RESOURCE_FUNCTION = /(?:url|image|image-set|cross-fade|element)\s*\(/i
const HIGHEST_CODE_POINT = 0x10ffff

const unescapeCss = (value: string): string =>
  value.replace(CSS_ESCAPE, (_, hex: string | undefined, character: string | undefined) => {
    if (hex === undefined) {
      return character ?? ''
    }
    const codePoint = parseInt(hex, 16)
    return codePoint === 0 || codePoint > HIGHEST_CODE_POINT ? '\uFFFD' : String.fromCodePoint(codePoint)
  })

const purifier = DOMPurify()
purifier.addHook('uponSanitizeAttribute', (_, attribute) => {
  if (attribute.attrName === 'style' && RESOURCE_FUNCTION.test(unescapeCss(attribute.attrValue))) {
    attribute.keepAttr = false
  }
})

export const sanitizeHtml = (html: string): string =>
  purifier.sanitize(html, {
    ADD_TAGS: ['fact', 'fact_source', 'cited-text'],
    ADD_ATTR: ['label', 'type', 'page-id', 'block-id'],
    ALLOW_UNKNOWN_PROTOCOLS: false,
    FORBID_TAGS: ['style', 'form', 'button', 'textarea', 'select', 'svg', 'math'],
    FORBID_ATTR: ['onerror', 'onload', 'onclick', 'onmouseover', 'onfocus', 'onblur'],
  })

// DOMPurify drops an unknown tag named like an SVG element, such as a model's <stop>, with its
// text, so model output shows every unknown tag as text, as renderContent does.
export const sanitizeModelHtml = (raw: string): string => sanitizeHtml(escapeUnknownTags(raw))

export const renderContent = (raw: string | null | undefined): string => {
  if (!raw) {
    return ''
  }

  const unwrapped = unwrapLegacyPWrapper(raw)
  const preprocessed = escapeUnknownTags(unwrapped)
  const rendered = markdown.parse(preprocessed, { async: false, gfm: true, breaks: true })

  return sanitizeHtml(rendered)
}
