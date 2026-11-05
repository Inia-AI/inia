import { Extension, Mark, Node, getMarkRange, mergeAttributes, type CommandProps } from '@tiptap/vue-3'
import {
  DOMSerializer,
  Fragment,
  Slice,
  type Mark as ProseMirrorMark,
  type MarkType,
  type Node as ProseMirrorNode,
  type Schema,
} from '@tiptap/pm/model'
import { Plugin } from '@tiptap/pm/state'
import { TableMap, isInTable, selectedRect } from '@tiptap/pm/tables'
import StarterKit from '@tiptap/starter-kit'
import { ListItem } from '@tiptap/extension-list'
import { Table, TableView } from '@tiptap/extension-table'
import TableRow from '@tiptap/extension-table-row'
import TableCell from '@tiptap/extension-table-cell'
import TableHeader from '@tiptap/extension-table-header'
import Image from '@tiptap/extension-image'
import { v4 as uuidv4 } from 'uuid'
import {
  CAPTION_ATTRIBUTE,
  COPIED_FROM_ATTRIBUTE,
  KEPT_STYLE_ATTRIBUTE,
  SOURCES_ATTRIBUTE,
  LAYOUT_TAGS,
  SECTION_ATTRIBUTE,
  TEXT_RUN_ATTRIBUTE,
  holdsBlocks,
  withoutCitationsCopiedElsewhere,
  type TableSection,
} from './reportHtml'
import { sanitizeHtml } from '@/lib/renderContent'

const STYLED_INLINE_TAGS = ['b', 'strong', 'i', 'em', 'u', 's']
// These styles stay on the element that carries them; without these rules StarterKit would also turn
// them into bold, italic, underline or strike marks around the element's text.
const STYLES_KEPT_ON_THEIR_ELEMENT = ['font-weight', 'font-style', 'text-decoration']

const keptAttribute = { default: null, keepOnSplit: false }

const keptStyle = {
  ...keptAttribute,
  parseHTML: (element: HTMLElement) => element.getAttribute(KEPT_STYLE_ATTRIBUTE) ?? element.getAttribute('style'),
  renderHTML: (attributes: Record<string, unknown>) =>
    attributes.style ? { style: attributes.style, [KEPT_STYLE_ATTRIBUTE]: attributes.style } : {},
}

const KeptHtmlAttributes = Extension.create({
  name: 'keptHtmlAttributes',
  addGlobalAttributes: () => [
    {
      types: [
        'paragraph',
        'heading',
        'blockquote',
        'horizontalRule',
        'bulletList',
        'orderedList',
        'listItem',
        'table',
        'tableRow',
        'tableCell',
        'tableHeader',
        'layoutBlock',
        'layoutTextBlock',
        'layoutEmptyInline',
        'layoutInline',
      ],
      attributes: { class: keptAttribute, style: keptStyle, title: keptAttribute },
    },
    { types: ['image'], attributes: { class: keptAttribute, style: keptStyle } },
    { types: ['link'], attributes: { style: keptStyle } },
  ],
})

const tagAttribute = {
  tag: {
    default: 'div',
    parseHTML: (element: HTMLElement) => element.tagName.toLowerCase(),
    renderHTML: () => ({}),
  },
}

const LayoutBlock = Node.create({
  name: 'layoutBlock',
  group: 'block',
  content: '(block | layoutTextRun)+',
  addAttributes: () => tagAttribute,
  parseHTML: () => LAYOUT_TAGS.map(tag => ({ tag, getAttrs: (element: HTMLElement) => holdsBlocks(element) && null })),
  renderHTML: ({ node, HTMLAttributes }) => [node.attrs.tag, HTMLAttributes, 0],
})

// Generated sections write line breaks into their text, which the page shows as spaces; the text
// of a layout element is read with its whitespace as written, so it is saved back as written.
const AS_WRITTEN = 'full' as const

const LayoutTextBlock = LayoutBlock.extend({
  name: 'layoutTextBlock',
  content: 'inline*',
  parseHTML: () =>
    LAYOUT_TAGS.map(tag => ({
      tag,
      preserveWhitespace: AS_WRITTEN,
      getAttrs: (element: HTMLElement) => !holdsBlocks(element) && null,
    })),
})

const LayoutTextRun = Node.create({
  name: 'layoutTextRun',
  content: 'inline*',
  parseHTML: () => [{ tag: `span[${TEXT_RUN_ATTRIBUTE}]`, priority: 70, preserveWhitespace: AS_WRITTEN }],
  renderHTML: () => ['span', { [TEXT_RUN_ATTRIBUTE]: '' }, 0],
})

const LayoutEmptyInline = Node.create({
  name: 'layoutEmptyInline',
  group: 'inline',
  inline: true,
  atom: true,
  selectable: false,
  parseHTML: () => [
    { tag: 'span', priority: 58, getAttrs: (element: HTMLElement) => element.childNodes.length === 0 && null },
  ],
  renderHTML: ({ HTMLAttributes }) => ['span', HTMLAttributes],
})

// Adjacent text with equal marks is written out as one element, so every element read gets its own
// occurrence number and two sibling elements with the same attributes stay two elements.
let occurrences = 0
const occurrenceAttribute = { default: null, parseHTML: () => ++occurrences, renderHTML: () => ({}) }

const LayoutInline = Mark.create({
  name: 'layoutInline',
  priority: 1001,
  excludes: '',
  addAttributes: () => ({
    tag: { ...tagAttribute.tag, default: 'span' },
    occurrence: occurrenceAttribute,
  }),
  parseHTML: () => [
    { tag: 'span', priority: 55 },
    ...STYLED_INLINE_TAGS.map(tag => ({
      tag,
      priority: 55,
      getAttrs: (element: HTMLElement) => (element.hasAttribute('class') || element.hasAttribute('style')) && null,
    })),
    ...STYLES_KEPT_ON_THEIR_ELEMENT.map(style => ({ style, priority: 60, clearMark: () => false })),
  ],
  renderHTML: ({ mark, HTMLAttributes }) => [mark.attrs.tag, HTMLAttributes, 0],
})

class ClipboardSerializerOf extends DOMSerializer {
  constructor(
    schema: Schema,
    private readonly editorToken: string,
  ) {
    const { nodes, marks } = DOMSerializer.fromSchema(schema)
    super(nodes, marks)
  }

  override serializeFragment(...args: Parameters<DOMSerializer['serializeFragment']>) {
    const copied = super.serializeFragment(...args)
    copied.firstElementChild?.setAttribute(COPIED_FROM_ATTRIBUTE, this.editorToken)
    return copied
  }
}

// The older citation form writes its sources inside the citation; they are held on the mark, so a
// caret after the citation's words is after the citation and deleting the words deletes it.
const sourcesWrittenInside = (element: HTMLElement) =>
  [...element.children]
    .filter(child => child.tagName === 'FACT_SOURCE')
    .map(source => source.outerHTML)
    .join('') || null

// Text pasted as plain text inside a citation is read again with the citation around it, as a new
// occurrence; it takes the occurrence it was pasted into, so the citation stays one.
const pastedInto = (citation: ProseMirrorMark, slice: Slice): Slice => {
  const isTheSameCitation = (mark: ProseMirrorMark) =>
    mark.type === citation.type && mark.attrs.id === citation.attrs.id && mark.attrs.sources === citation.attrs.sources
  const relinked = (fragment: Fragment): Fragment => {
    const nodes: ProseMirrorNode[] = []
    fragment.forEach(node => {
      const marks = node.marks.map(mark => (isTheSameCitation(mark) ? citation : mark))
      nodes.push((node.isLeaf ? node : node.copy(relinked(node.content))).mark(marks))
    })
    return Fragment.fromArray(nodes)
  }
  return new Slice(relinked(slice.content), slice.openStart, slice.openEnd)
}

// Text typed over words that all lie in one citation corrects the cited words, so it stays in the
// citation; ProseMirror would leave it out when the words end the citation, as the mark is not
// inclusive.
const citationHolding = (doc: ProseMirrorNode, from: number, to: number, citationType: MarkType) => {
  const $from = doc.resolve(from)
  const citation = $from.nodeAfter ? citationType.isInSet($from.nodeAfter.marks) : undefined
  const cited = citation && getMarkRange($from, citationType, citation.attrs)
  return cited && to <= cited.to ? citation : undefined
}

const Citation = Mark.create({
  name: 'citation',
  priority: 50,
  inclusive: false,
  keepOnSplit: false,
  addAttributes: () => ({
    id: { default: null, parseHTML: (element: HTMLElement) => element.getAttribute('id') },
    sources: {
      default: null,
      parseHTML: (element: HTMLElement) => element.getAttribute(SOURCES_ATTRIBUTE) ?? sourcesWrittenInside(element),
      renderHTML: (attributes: Record<string, unknown>) =>
        attributes.sources ? { [SOURCES_ATTRIBUTE]: attributes.sources } : {},
    },
    occurrence: occurrenceAttribute,
  }),
  parseHTML: () => [{ tag: 'fact' }, { tag: 'fact_source', ignore: true }],
  renderHTML: ({ HTMLAttributes }) => ['fact', HTMLAttributes, 0],
  addProseMirrorPlugins() {
    const editorToken = uuidv4()
    const citationType = this.type
    return [
      new Plugin({
        props: {
          clipboardSerializer: new ClipboardSerializerOf(this.editor.schema, editorToken),
          transformPastedHTML: html => withoutCitationsCopiedElsewhere(html, editorToken),
          // A keyboard types one key at a time, so after a correction the citation is stored for the
          // keys that follow; moving the caret clears it. An IME composing text manages its own marks.
          handleTextInput: (view, from, to, text, typed) => {
            const { doc, storedMarks } = view.state
            const citation =
              from === to ? citationType.isInSet(storedMarks ?? []) : citationHolding(doc, from, to, citationType)
            if (!citation || view.composing) return false
            const tr = typed().addMark(from, from + text.length, citation)
            view.dispatch(tr.setStoredMarks(citation.addToSet(tr.doc.resolve(from + text.length).marks())))
            return true
          },
          transformPasted: (slice, view) => {
            const citation = citationType.isInSet(view.state.selection.$from.marks())
            return citation ? pastedInto(citation, slice) : slice
          },
        },
      }),
    ]
  },
})

const LayoutKeys = Extension.create({
  name: 'layoutKeys',
  priority: 1000,
  addKeyboardShortcuts() {
    return {
      Enter: () => {
        const { parent } = this.editor.state.selection.$from
        if (parent.type.name === 'layoutTextRun') return this.editor.commands.setHardBreak()
        if (parent.type.name !== 'layoutTextBlock') return false
        return this.editor.commands.command(({ tr }) => {
          tr.deleteSelection()
          tr.split(tr.selection.from, 1, [{ type: parent.type, attrs: parent.attrs }])
          return true
        })
      },
    }
  },
})

const copyAttributes = (tr: CommandProps['tr'], position: number, node: ProseMirrorNode, from: ProseMirrorNode, names: string[]) => {
  if (names.every(name => node.attrs[name] === from.attrs[name])) return
  tr.setNodeMarkup(position, undefined, { ...node.attrs, ...Object.fromEntries(names.map(name => [name, from.attrs[name]])) })
}

const copyRowMarkup = (tr: CommandProps['tr'], tableStart: number, from: number, to: number) => {
  const table = tr.doc.nodeAt(tableStart - 1)
  if (!table) return
  const map = TableMap.get(table)
  copyAttributes(tr, tr.doc.resolve(tableStart).posAtIndex(to), table.child(to), table.child(from), ['class', 'style', 'section'])
  for (let column = 0; column < map.width; column++) {
    const addedCell = map.map[to * map.width + column]!
    const neighbourCell = map.map[from * map.width + column]!
    const startsInTheAddedRow = map.findCell(addedCell).top === to
    if (addedCell === neighbourCell || !startsInTheAddedRow || map.colCount(addedCell) !== column) continue
    copyAttributes(tr, tableStart + addedCell, table.nodeAt(addedCell)!, table.nodeAt(neighbourCell)!, ['class', 'style'])
  }
}

const addStyledRow =
  (side: 'before' | 'after', addRow: (() => (props: CommandProps) => boolean) | undefined) => (props: CommandProps) => {
    const rect = isInTable(props.state) ? selectedRect(props.state) : null
    if (!addRow?.()(props)) return false
    if (rect && props.dispatch) {
      const [added, neighbour] = side === 'after' ? [rect.bottom, rect.bottom - 1] : [rect.top, rect.top + 1]
      copyRowMarkup(props.tr, rect.tableStart, neighbour, added)
    }
    return true
  }

const ReportTable = Table.extend({
  addAttributes() {
    return {
      ...this.parent?.(),
      caption: {
        default: null,
        parseHTML: (table: HTMLElement) =>
          table.getAttribute(CAPTION_ATTRIBUTE) ?? (table as HTMLTableElement).caption?.innerHTML ?? null,
        renderHTML: (attributes: Record<string, unknown>) => ({ [CAPTION_ATTRIBUTE]: attributes.caption }),
      },
    }
  },
  parseHTML() {
    return [...(this.parent?.() ?? []), { tag: 'caption', ignore: true }]
  },
  renderHTML({ node, HTMLAttributes }) {
    const aColumnWasResized = node.firstChild?.children.some(cell => cell.attrs.colwidth) ?? false
    const withoutColumnSizing = ['table', mergeAttributes(this.options.HTMLAttributes, HTMLAttributes), ['tbody', 0]] as const
    return aColumnWasResized ? (this.parent?.({ node, HTMLAttributes }) ?? withoutColumnSizing) : withoutColumnSizing
  },
  addCommands() {
    const parent = this.parent?.()
    return {
      ...parent,
      addRowBefore: () => addStyledRow('before', parent?.addRowBefore),
      addRowAfter: () => addStyledRow('after', parent?.addRowAfter),
    }
  },
})

class CaptionedTableView extends TableView {
  private readonly caption = document.createElement('caption')

  constructor(...args: ConstructorParameters<typeof TableView>) {
    super(...args)
    this.caption.setAttribute('contenteditable', 'false')
    this.table.prepend(this.caption)
    this.showCaption()
  }

  override update(node: ProseMirrorNode) {
    const captionChanged = node.attrs.caption !== this.node.attrs.caption
    if (!super.update(node)) return false
    if (captionChanged) this.showCaption()
    return true
  }

  private showCaption() {
    this.caption.innerHTML = sanitizeHtml(String(this.node.attrs.caption ?? ''))
    this.caption.hidden = !this.node.attrs.caption
  }
}

const SECTION_OF_PARENT: Record<string, TableSection> = { THEAD: 'head', TBODY: 'body', TFOOT: 'foot' }

const ReportTableRow = TableRow.extend({
  addAttributes() {
    return {
      ...this.parent?.(),
      section: {
        default: null,
        parseHTML: (row: HTMLElement) =>
          row.getAttribute(SECTION_ATTRIBUTE) ?? SECTION_OF_PARENT[row.parentElement?.tagName ?? ''] ?? 'body',
        renderHTML: (attributes: Record<string, unknown>) => ({ [SECTION_ATTRIBUTE]: attributes.section }),
      },
    }
  },
})

const ReportListItem = ListItem.extend({ content: '(paragraph | layoutTextBlock | layoutBlock) block*' })

export const reportEditorExtensions = [
  StarterKit.configure({ link: { openOnClick: false }, listItem: false }),
  ReportListItem,
  KeptHtmlAttributes,
  LayoutKeys,
  LayoutBlock,
  LayoutTextBlock,
  LayoutTextRun,
  LayoutInline,
  LayoutEmptyInline,
  Citation,
  ReportTable.configure({ resizable: true, View: CaptionedTableView }),
  ReportTableRow,
  TableCell,
  TableHeader,
  Image,
]
