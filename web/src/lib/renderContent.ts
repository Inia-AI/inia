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

export const sanitizeHtml = (html: string): string =>
  DOMPurify.sanitize(html, {
    ADD_TAGS: ['fact', 'fact_source', 'cited-text'],
    ADD_ATTR: ['label', 'type', 'page-id', 'block-id'],
    ALLOW_UNKNOWN_PROTOCOLS: false,
    FORBID_TAGS: ['style', 'form', 'button', 'textarea', 'select'],
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
