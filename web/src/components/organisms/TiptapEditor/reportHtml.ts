import { DOMSerializer, type Schema } from '@tiptap/pm/model'
import type { EditorState } from '@tiptap/pm/state'
import { sanitizeHtml } from '@/lib/renderContent'

export const SECTION_ATTRIBUTE = 'data-table-section'
export const CAPTION_ATTRIBUTE = 'data-caption'
export const KEPT_STYLE_ATTRIBUTE = 'data-kept-style'
export const TEXT_RUN_ATTRIBUTE = 'data-text-run'
export const COPIED_FROM_ATTRIBUTE = 'data-copied-from'
export const SOURCES_ATTRIBUTE = 'data-sources'

const TABLE_SECTIONS = ['head', 'body', 'foot'] as const
export type TableSection = (typeof TABLE_SECTIONS)[number]

export const LAYOUT_TAGS = ['div', 'section', 'article', 'header', 'footer', 'aside', 'figure', 'figcaption']
const BLOCK_TAGS = new Set(
  [...LAYOUT_TAGS, 'p', 'h1', 'h2', 'h3', 'h4', 'h5', 'h6', 'ul', 'ol', 'table', 'blockquote', 'pre', 'hr', 'img'].map(
    tag => tag.toUpperCase(),
  ),
)
const DEFAULT_SPAN = '1'

const isBlock = (node: ChildNode) => node.nodeType === Node.ELEMENT_NODE && BLOCK_TAGS.has((node as Element).tagName)

export const holdsBlocks = (element: Element) => [...element.children].some(isBlock)

const unwrap = (element: Element) => element.replaceWith(...element.childNodes)

const parseBody = (html: string) => new DOMParser().parseFromString(html, 'text/html').body

const wrapLooseInlineContent = (container: Element) => {
  let run: ChildNode[] = []
  const wrapRunBefore = (next: ChildNode | null) => {
    if (run.some(node => node.nodeType === Node.ELEMENT_NODE || node.textContent?.trim())) {
      const wrapper = container.ownerDocument.createElement('span')
      wrapper.setAttribute(TEXT_RUN_ATTRIBUTE, '')
      container.insertBefore(wrapper, next)
      wrapper.append(...run)
    }
    run = []
  }
  for (const child of [...container.childNodes]) {
    if (isBlock(child)) wrapRunBefore(child)
    else run.push(child)
  }
  wrapRunBefore(null)
}

// The editor cannot hold text that sits straight in a layout element beside its blocks, so the text
// is marked as a run before the editor reads it, and the run is unwrapped again when it is saved.
export const markTextRuns = (html: string): string => {
  const body = parseBody(html)
  const containers = [...body.querySelectorAll(LAYOUT_TAGS.join(','))].filter(holdsBlocks)
  if (containers.length === 0) return html
  containers.forEach(wrapLooseInlineContent)
  return body.innerHTML
}

const BOLD_WEIGHT = /^(bold|bolder|[5-9]\d\d)$/
const MEANINGS_OF_STYLES = [
  { tag: 'STRONG', given: (style: CSSStyleDeclaration) => BOLD_WEIGHT.test(style.fontWeight) },
  { tag: 'EM', given: (style: CSSStyleDeclaration) => style.fontStyle === 'italic' },
  { tag: 'U', given: (style: CSSStyleDeclaration) => style.textDecoration.includes('underline') },
  { tag: 'S', given: (style: CSSStyleDeclaration) => style.textDecoration.includes('line-through') },
]
const BOLD_TAGS = ['B', 'STRONG']

const keepWhatTheStyleMeant = (element: HTMLElement) => {
  if (BOLD_TAGS.includes(element.tagName) && !BOLD_WEIGHT.test(element.style.fontWeight || 'bold')) {
    unwrap(element)
    return
  }
  for (const { tag } of MEANINGS_OF_STYLES.filter(meaning => meaning.given(element.style))) {
    const wrapper = element.ownerDocument.createElement(tag)
    wrapper.append(...element.childNodes)
    element.append(wrapper)
  }
}

const dropForeignLayout = (element: Element) => {
  if (holdsBlocks(element)) {
    unwrap(element)
    return
  }
  const paragraph = element.ownerDocument.createElement('p')
  paragraph.append(...element.childNodes)
  element.replaceWith(paragraph)
}

const dropCitations = (root: ParentNode) =>
  root.querySelectorAll('fact').forEach(citation => {
    citation.querySelectorAll('fact_source').forEach(source => source.remove())
    unwrap(citation)
  })

const dropCitationsInCaption = (table: Element) => {
  const caption = parseBody(sanitizeHtml(table.getAttribute(CAPTION_ATTRIBUTE) ?? ''))
  dropCitations(caption)
  table.setAttribute(CAPTION_ATTRIBUTE, caption.innerHTML)
}

// A citation's id is looked up among the citations of its own section, so a citation pasted into
// another section would show sources it does not have. Only HTML copied from this editor, which
// tags it, keeps its citations.
export const withoutCitationsCopiedElsewhere = (html: string, editorToken: string): string => {
  const body = parseBody(html)
  const tagged = body.querySelector(`[${COPIED_FROM_ATTRIBUTE}]`)
  const copiedHere = tagged?.getAttribute(COPIED_FROM_ATTRIBUTE) === editorToken
  tagged?.removeAttribute(COPIED_FROM_ATTRIBUTE)
  if (!copiedHere) {
    dropCitations(body)
    body.querySelectorAll(`[${CAPTION_ATTRIBUTE}]`).forEach(dropCitationsInCaption)
  }
  return body.innerHTML
}

// HTML from outside this editor, such as Word or Google Docs, brings its own fonts and colours,
// which would override the report's theme. It keeps what its styles meant (bold, italic,
// underline, strike) and loses the rest. A copy from a report editor carries ProseMirror's slice
// marker and keeps its styling.
export const withoutForeignStyling = (html: string): string => {
  if (html.includes('data-pm-slice')) return html
  const body = parseBody(html)
  body.querySelectorAll<HTMLElement>('[style]').forEach(keepWhatTheStyleMeant)
  body.querySelectorAll('[class], [style]').forEach(element => {
    element.removeAttribute('class')
    element.removeAttribute('style')
  })
  body.querySelectorAll('span:not([title])').forEach(unwrap)
  ;[...body.querySelectorAll(LAYOUT_TAGS.join(','))].reverse().forEach(dropForeignLayout)
  return body.innerHTML
}

const sectionReader = (rows: HTMLTableRowElement[]): ((row: HTMLTableRowElement) => TableSection) => {
  if (rows.some(row => row.hasAttribute(SECTION_ATTRIBUTE))) {
    return row => TABLE_SECTIONS.find(section => section === row.getAttribute(SECTION_ATTRIBUTE)) ?? 'body'
  }
  const isHeaderRow = (row: HTMLTableRowElement) =>
    row.cells.length > 0 && [...row.cells].every(cell => cell.tagName === 'TH')
  const firstBodyRow = rows.findIndex(row => !isHeaderRow(row))
  const leadingHeaderRows = new Set(rows.slice(0, firstBodyRow === -1 ? rows.length : firstBodyRow))
  return row => (leadingHeaderRows.has(row) ? 'head' : 'body')
}

const restoreTableParts = (table: HTMLTableElement) => {
  const caption = table.getAttribute(CAPTION_ATTRIBUTE)
  table.removeAttribute(CAPTION_ATTRIBUTE)
  if (caption !== null) table.createCaption().innerHTML = sanitizeHtml(caption)
  const body = table.tBodies[0]
  if (!body) return
  const rows = [...body.rows]
  const sectionOf = sectionReader(rows)
  const headRows = rows.filter(row => sectionOf(row) === 'head')
  const footRows = rows.filter(row => sectionOf(row) === 'foot')
  rows.forEach(row => row.removeAttribute(SECTION_ATTRIBUTE))
  if (headRows.length > 0) table.createTHead().append(...headRows)
  if (footRows.length > 0) table.createTFoot().append(...footRows)
  if (body.rows.length === 0) body.remove()
}

const unwrapLonePlainParagraph = (container: Element) => {
  const only = container.firstElementChild
  if (container.childNodes.length === 1 && only?.tagName === 'P' && only.attributes.length === 0) unwrap(only)
}

const dropDefaultSpans = (cell: Element) => {
  for (const span of ['colspan', 'rowspan']) {
    if (cell.getAttribute(span) === DEFAULT_SPAN) cell.removeAttribute(span)
  }
}

// ProseMirror writes a style through cssText, which rewrites it, drops what the browser does not
// know and, in Chrome, moves the style attribute behind the others; so the style is saved as
// written, in the place of its kept copy. A cell's align attribute also renders into its style;
// when it adds an alignment the written style lacks, the rendered style is kept.
const restoreKeptStyle = (element: HTMLElement, scratch: HTMLElement) => {
  const written = element.getAttribute(KEPT_STYLE_ATTRIBUTE) ?? ''
  scratch.setAttribute('style', written)
  const style = scratch.style.textAlign === element.style.textAlign ? written : (element.getAttribute('style') ?? '')
  const attributes = [...element.attributes]
    .filter(attribute => attribute.name !== 'style')
    .map(({ name, value }) => (name === KEPT_STYLE_ATTRIBUTE ? (['style', style] as const) : ([name, value] as const)))
  for (const name of element.getAttributeNames()) element.removeAttribute(name)
  for (const [name, value] of attributes) element.setAttribute(name, value)
}

const dropTrailingEmptyParagraph = (root: Element) => {
  const last = root.lastElementChild
  const appendedAfterABlock = last?.tagName === 'P' && last.childNodes.length === 0 && last.previousElementSibling
  if (appendedAfterABlock) last.remove()
}

const restoreSourcesInside = (citation: Element) => {
  const sources = citation.ownerDocument.createElement('template')
  sources.innerHTML = sanitizeHtml(citation.getAttribute(SOURCES_ATTRIBUTE) ?? '')
  citation.append(sources.content)
  citation.removeAttribute(SOURCES_ATTRIBUTE)
}

const reshape = (root: HTMLElement) => {
  const scratch = root.ownerDocument.createElement('div')
  root.querySelectorAll(`fact[${SOURCES_ATTRIBUTE}]`).forEach(restoreSourcesInside)
  root.querySelectorAll<HTMLElement>(`[${KEPT_STYLE_ATTRIBUTE}]`).forEach(element => restoreKeptStyle(element, scratch))
  root.querySelectorAll(`[${TEXT_RUN_ATTRIBUTE}]`).forEach(unwrap)
  root.querySelectorAll('table').forEach(restoreTableParts)
  root.querySelectorAll('td, th, li').forEach(container => {
    if (container.tagName !== 'LI') dropDefaultSpans(container)
    unwrapLonePlainParagraph(container)
  })
  dropTrailingEmptyParagraph(root)
  return root.innerHTML
}

export const toReportHtml = (editorHtml: string): string => reshape(parseBody(editorHtml))

export const reportHtmlOf = ({ schema, state }: { schema: Schema; state: EditorState }): string => {
  const detachedDocument = document.implementation.createHTMLDocument()
  const container = detachedDocument.createElement('div')
  container.append(DOMSerializer.fromSchema(schema).serializeFragment(state.doc.content, { document: detachedDocument }))
  return reshape(container)
}
