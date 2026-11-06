---
type: explanation
status: current
description: "Documents the TipTap v3 rich-text editor of report paragraphs: where it is used, its v-model contract, keyboard and screen-reader behaviour, extensions, the toolbar, and how an edit keeps the citations, card layouts, tables and lists the report wrote. Read when you change paragraph editing, an edit loses markup or a citation, or you add an editor extension."
---

# Rich Text Editing (TipTap)

**On this page:** Where it is used · Contract · Extensions · Saved markup · Toolbar · Relationship to `KeyValueEditor` · See also

The frontend embeds a single rich-text editor built on [TipTap](https://tiptap.dev) v3 (ProseMirror). It lives at `src/components/organisms/TiptapEditor/TiptapEditor.vue` and is used to edit workflow content blocks inline. The editor reads and writes **HTML** — it is not a Markdown editor.

## Where it is used

`TiptapEditor` is consumed in exactly one place: the workflow content block editor.

`src/components/organisms/WorkflowElements/WorkflowContent/WorkflowContent.vue` renders it while a block is being edited:

```vue
<TiptapEditor v-if="useEditor" v-model="text" :label="editorLabel" autofocus />
<Input v-else ref="plainEditor" v-model="text" :aria-label="editorLabel" />
```

The `useEditor` prop on `WorkflowContent` defaults to `true`, so the rich editor is the default editing surface; a plain `Input` is the fallback when a caller opts out. A paragraph opens the rich editor, bound to the block's HTML with its citations; a paragraph's heading (the **Edit heading** action) opens the plain `Input`, bound to the heading's plain text without the markup it is shown with.

### Keyboard and screen-reader behaviour

- The editing surface is a named text field: **Paragraph text** for a paragraph and **Heading text** for a heading (`shared.components.workflowElement.editor.*`). The rich editor's surface is also marked multi-line.
- The field takes focus whenever the editor opens: from **Edit paragraph** or **Edit heading**, pressed with the mouse or the keyboard, and when a refinement is accepted. The rich editor takes focus through TipTap's own `autofocus` option; `WorkflowContent` focuses the plain `Input` from a watcher on its editing state.
- **Cancel** and **Save** return focus to the action that opened the editor. `WorkflowElementWrapper` emits `edit` with the action's name (`edit` or `editHeading`) and exposes `focusAction(name)`, which focuses the action button whose `data-action` is that name; the same watcher calls it when editing ends.

### The paragraph's other actions from the keyboard

A keyboard user can reach every action that a pointer user can reach on a report paragraph:

- `WorkflowElementWrapper`'s action strip (**Explore Analysis**, **Edit paragraph**, **Copy paragraph**, **Remove paragraph**) is made of native buttons, in Tab order, shown while focus is anywhere inside the paragraph. The wrapper itself is not clickable and no longer shows a pointer cursor.
- **Explore Analysis** asks about the passage selected in its own paragraph, the same question the highlight popup's **Explore** asks, so a selection made without the mouse reaches it too. A selection elsewhere on the page is ignored. Feedback on a selection is in the report's **Actions** menu, which quotes the selection ([Report feedback](../../features/report-feedback.md#where-it-happens)).
- The sources count beside a paragraph is a button named for its count, such as **2 sources**, with a 24px target. Its tooltip lists the sources on focus as it does on hover.
- The confidence bar shows while focus is inside the paragraph, as it does on hover, and its accessible name gives the figure.

## Contract

`TiptapEditor` is a thin `v-model` wrapper around a TipTap editor instance.

| Aspect | Detail |
| --- | --- |
| Props | `modelValue: string` (HTML); `label: string`, the accessible name of the editing surface; `autofocus?: boolean`, focus the surface once the editor mounts |
| Emit | `update:modelValue` with `reportHtmlOf(editor)` on every change the user makes (see [Saved markup](#saved-markup)) |
| Exposed | `editor` (via `defineExpose`), for tests/parents that need the instance |
| Lifecycle | `editor.destroy()` on `onBeforeUnmount` |

The editing surface carries `role="textbox"`, `aria-label` and `aria-multiline="true"` through `editorProps.attributes`. The role is declared there although TipTap sets it at creation, because `setOptions` (which `EditorContent` calls when it mounts) replaces the attributes and would drop it.

TipTap normalizes a document as soon as any transaction runs, even one that only moves focus: StarterKit's trailing-node plugin appends an empty paragraph when the last block is not a paragraph, such as a table or a list. `onUpdate` emits only when the root transaction changed the document, so opening the editor and pressing **Save** without an edit saves none of that normalization. The first real edit saves the editor's HTML in the shape [Saved markup](#saved-markup) describes.

External updates are reconciled in a `watch` on `modelValue`: when the incoming value differs from the HTML the editor last emitted or was given, it calls `setContent(markTextRuns(newValue), { emitUpdate: false })`, so applying a fresh value does not re-trigger `update:modelValue` and loop. The comparison is with that remembered value, not with `editor.getHTML()`, because the emitted value is reshaped: comparing with `getHTML()` would reload the content, and move the caret, on every keystroke.

## Extensions

The editor loads the following TipTap packages (all `@tiptap/*` at `^3.31.3` in `package.json`):

| Extension | Package | Notes |
| --- | --- | --- |
| StarterKit | `@tiptap/starter-kit` | Configured with `link: { openOnClick: false }` |
| Table | `@tiptap/extension-table` | Configured `resizable: true` and a table view that shows the caption; extended (see [Saved markup](#saved-markup)) |
| TableRow | `@tiptap/extension-table-row` | Extended |
| TableCell | `@tiptap/extension-table-cell` | Keeps `class`, `style` and `title` |
| TableHeader | `@tiptap/extension-table-header` | Keeps `class`, `style` and `title` |
| Image | `@tiptap/extension-image` | URL-based images only |
| Layout nodes and marks | `reportMarkup.ts` (in the component's folder) | Card layouts built from `div` and `span` (see [Saved markup](#saved-markup)) |
| Citations | `reportMarkup.ts` | `<fact>` with its `id`, and the sources the older form writes inside it (see [Saved markup](#saved-markup)) |

`reportMarkup.ts` exports the whole list as `reportEditorExtensions`. It reads table selections and the table map through `@tiptap/pm/tables`, so `@tiptap/pm` is a direct dependency.

> [!NOTE]
> **Bold / italic / underline / strike / code / lists / link come from StarterKit**
>
> The toolbar exposes underline (`toggleUnderline`) and link (`setLink`, `isActive('link')`) buttons, but there are no separate `@tiptap/extension-underline` or `@tiptap/extension-link` packages imported — those marks are provided by StarterKit v3 (which is why `link` is configured *through* `StarterKit.configure`). Paragraphs, headings, blockquote, code block, horizontal rule, bullet/ordered lists, and history (undo/redo) likewise come from StarterKit defaults.

## Saved markup

Reports store HTML, and the report page and the Word export show it as stored, so an edit must keep the markup the report wrote: only the words the user changed may differ. TipTap's own schema cannot hold that markup: it drops every element and attribute it does not declare, wraps the text of every table cell and list item in a paragraph, keeps no table caption, header or footer section, writes a `<colgroup>` into every table and an empty paragraph after a final table or list, and rewrites every style. `src/components/organisms/TiptapEditor/` closes that gap in three places.

**The schema** (`reportMarkup.ts`).

- `KeptHtmlAttributes` gives paragraphs, headings, quotes, rules, lists, list items, the four table nodes and the layout nodes below their `class`, `style` and `title`, images their `class` and `style`, and links their `style`. A new block started with Enter at the end of a block does not inherit them.
- A layout element (`div`, `section`, `article`, `header`, `footer`, `aside`, `figure`, `figcaption`) that holds blocks becomes a `layoutBlock`; one that holds only text and inline elements becomes a `layoutTextBlock`. Both remember their tag.
- A `span`, and a `b`, `strong`, `i`, `em`, `u` or `s` that carries a class or a style, becomes a `layoutInline` mark that remembers its tag. Every element read gets its own occurrence number, so two sibling spans with the same style stay two spans. The mark ranks above links, so a styled span that holds a link stays one span. An empty `span` (a coloured dot, a bar of a chart) becomes a `layoutEmptyInline` atom.
- The text of a card line, and text written straight into a card, keeps its whitespace as written (`preserveWhitespace: 'full'`). Generated sections write line breaks into that text: the report page shows them as spaces, the editor shows them as line breaks, and the save writes them back unchanged.
- A citation (`<fact>`) becomes a `citation` mark that keeps its `id`, the one attribute the platform writes on it, and drops any other, so no handler written into a citation runs in the editor. Like a styled span it gets its own occurrence number, so two citations side by side stay two. It does not extend over text typed from a bare caret right after it, and deleting the words a citation sits on deletes the citation. Text typed over words that all lie in one citation stays in it, also when those words end the citation (`handleTextInput`), so correcting the last digit of a cited figure keeps the marker after the whole figure. A keyboard types one key at a time, so the citation is also stored for the keys typed after it at the same caret, until the caret moves: retyping a whole figure keeps it cited, and words typed straight on after a correction join the citation too. The mark does not carry over a split (`keepOnSplit: false`), so Enter or Shift+Enter after a correction starts outside the citation. While an IME composes text the editor leaves the marks to the composition. Text typed over words that reach past a citation, at either end, goes outside it, and the citation stays on its untouched words: the new words replace words the citation never covered, so they cannot claim its source. Text pasted inside a citation joins it: ProseMirror reads pasted plain text again with the citation around it, as a new occurrence, so the paste takes the occurrence it landed in.
- The older citation form writes its sources inside the citation (`<fact_source>`, with its `<cited-text>`). The mark holds them as written in its `sources` attribute (`data-sources` in the editor), and the editor reads no `<fact_source>` as content. So a caret after a citation's words is after the citation in this form too, and deleting the words deletes the sources with them.
- A list item may start with a card line or a card, as well as with a paragraph.
- A `font-weight`, `font-style` or `text-decoration` style stays on its element. Without that rule StarterKit would also turn it into a bold, italic, underline or strike mark.
- The table records its caption as `data-caption`, and each row records its section (`head`, `body` or `foot`) as `data-table-section`. Both are read back from those attributes too, so they survive a copy within the editor. The table writes no `<colgroup>` and no width style unless a column was resized. While editing, `CaptionedTableView` (a TipTap `TableView`) shows the caption above its table, through `sanitizeHtml`, as a `contenteditable="false"` `<caption>`: it can be read but not changed.
- **Enter** in a line of a card (a `layoutTextBlock`) replaces any selected words, then starts a new line that copies the element, its tag, class and style. Enter in text written straight into a card adds a line break.
- `addRowBefore` and `addRowAfter`, which Tab also uses at the end of a table, give the new row the class, style and section of the row it was added beside, and give each new cell the class and style of the cell in the same column. A cell that spans rows is left alone.

**Reading** (`markTextRuns` in `reportHtml.ts`). Text that sits straight in a layout element beside its blocks has no place in the schema, so before the editor reads content (and pasted HTML) each such run of text is wrapped in `span[data-text-run]`, which the editor holds as a `layoutTextRun`. HTML pasted from outside the editor (it lacks ProseMirror's `data-pm-slice` marker) first goes through `withoutForeignStyling`: it keeps what its styles meant as bold, italic, underline and strike, and loses its classes, styles, bare spans and layout elements, so Word or Google Docs fonts and colours never reach a report. A copy from a report editor keeps its styling.

A citation's id is looked up among the citations of its own paragraph, so a citation pasted into another section would show sources it does not have. Each editor therefore tags the HTML it copies with a token of its own (`data-copied-from`, written by its clipboard serializer), and a paste keeps its citations only when it carries this editor's token (`withoutCitationsCopiedElsewhere`). Any other HTML loses its citations, in its text and in a table's caption: a `<fact>` keeps only its words, and its sources go. That covers Word, Google Docs, the editor of another section and any other ProseMirror editor, so a paste never creates a citation. A copy or a drag within the editor keeps its citations.

**Saving** (`reportHtmlOf` in `reportHtml.ts`). The editor's document is serialized and reshaped before it is emitted:

- every style is saved exactly as it was written, from the `data-kept-style` copy each kept style renders beside itself, and in that copy's place among the element's attributes, because ProseMirror writes styles through `cssText`, which rewrites them, drops what the browser does not know and, in Chrome, moves the style attribute behind the others. The editor also reads that copy back, so a copy within the editor keeps the written style. A cell whose `align` attribute added an alignment the written style lacks keeps the rendered style;
- text runs are unwrapped;
- the sources of a citation in the older form go back inside it, after its words, through `sanitizeHtml`;
- the caption goes back into a `<caption>`, through `sanitizeHtml`, and the rows into `<thead>`, `<tbody>` and `<tfoot>`. A table inserted with the toolbar has no recorded sections, so its leading rows of `<th>` cells go into the `<thead>`;
- a cell or list item that holds a single plain paragraph holds its text again, and the default `colspan="1"` and `rowspan="1"` go;
- the empty paragraph TipTap appends after a final table or list goes; a paragraph the user cleared stays `<p></p>`, so the report still shows it and its actions.

**Display.** `renderContent` (`src/lib/renderContent.ts`) allow-lists `caption`, `colgroup`, `col` and `tfoot` beside the other table and layout tags, and DOMPurify keeps `class`, `style` and `title`, so the report shows the saved markup as it was saved.

**Citations after a save.** An edit sends only the paragraph's content: the platform's paragraph update replaces the content and leaves the paragraph's citation list as it was. Each saved `<fact>` keeps its id, so the report page decorates it again and its popover and source chain reach the same passages ([Source chain and citations](source-chain-and-citations.md)). The Word and Markdown exports strip the citation tags with `FactTags.Strip`, as they do for a section never edited.

What an edit still changes:

- A citation whose sources are all hidden shows its marker while editing; the report page hides it (`fact-empty`), and shows the citation as before once the edit is saved. The editor cannot change a caption.
- Marks nest by rank, not as written: a styled span always ends up outside a link, and bold or italic inside it. Inline code takes no other mark, so a styled span around `<code>` is split around it.
- A caption keeps its content but not its own class or style. The `<thead>`, `<tbody>` and `<tfoot>` elements lose their own class and style, and several `<tbody>` elements become one. A `<colgroup>` the report wrote, with its column widths, is dropped.
- Attributes come back in the order the editor writes them: `class`, `style` and `title` in that order, then the element's own, such as a cell's `colspan`. The generated sections surveyed write them in that order; an element that wrote them in another order is saved in this one.
- The sources of a citation in the older form go back at the end of the citation, wherever they were written inside it.
- Deleting a citation's last character and then typing from the bare caret it leaves puts the new text after the citation, as any typing from a bare caret at a citation's end does. Select the character and type over it instead.
- A styled span that holds only spaces is dropped.
- The style of an element the editor keeps no style for, such as `<code>`, is dropped, bold, italic and underline included.
- A cell or list item that holds more than one paragraph keeps its paragraphs, and a loose list (`<li><p>…</p></li>`) comes back tight.
- A resized table keeps the width TipTap wrote, while the column widths themselves are lost when the paragraph is shown again, because DOMPurify drops `colwidth`.

The colocated tests (`TiptapEditor.test.ts`) edit real report markup and assert on what is saved. The card fixture (`fixtures/bankStatementCards.html`) is a section of a generated bank statement analysis with its names replaced, its citations and its written line breaks; an edit saves it back byte for byte. The tests cover the cards, citations in both forms (beside typed words, typed over at their last digit and whole, key by key, typed over past their end, typed on after the caret has left, typed on after Enter, Shift+Enter or Enter in a list item, with their words deleted, two side by side, with plain text pasted inside, copied or dragged within the editor, copied from another section's editor, in the caption of a table copied from another section, pasted from outside, and written with a handler), the caption shown while editing and replaced with the content, Enter in a card, an image in a card, tables with and without a header section, inline-styled tables, a caption and a footer, rows added with Tab and the row commands (also past a cell that spans rows and after a selection dragged upwards), a table inserted from the toolbar, bold-styled cells, a cell's `align` attribute, a cleared paragraph and lists. `reportHtml.test.ts` covers empty cells, cells with two paragraphs, nested tables and a resized table directly. `WorkflowContent.test.ts` edits the card fixture through **Edit paragraph** and **Save**, and checks that the save holds every citation as stored and that the page shows each one as a working citation.

## Toolbar

The toolbar is hand-built (no TipTap menu extension), grouped into inline marks, lists, insert actions, and history:

- **Marks** — bold, italic, underline, strike, inline code (each toggles and reflects `editor.isActive(...)` as an `active` class).
- **Lists** — bullet list, ordered list.
- **Insert** — link, image, table.
- **History** — undo / redo, disabled via `editor.can().undo()` / `.redo()`.

Link and image insertion are deliberately minimal: both use `window.prompt` to collect a URL. `setLink` reads the existing `href`, supports clearing (empty string unsets the link), and uses `extendMarkRange('link')`. `addImage` inserts an image from the entered URL. Table insertion creates a 3×3 grid with a header row. There is **no image upload** — only URLs are supported.

## Relationship to `KeyValueEditor`

`KeyValueEditor` (`src/components/molecules/KeyValueEditor/KeyValueEditor.vue`) is **not** a rich-text component and shares no code with TipTap. It edits a `Record<string, string>` as rows of key and value, with add and remove buttons and a duplicate-key message. Its one user is the counterparty form's property bag ([Forms, tables and composables](forms-tables-and-composables.md#edit-mode-initial-values-and-reset)). There is no `OptionsEditor` molecule.

## See also

- [Components & design system](components-and-design-system.md) — atoms/molecules/organisms layering
- [Forms, tables & composables](forms-tables-and-composables.md) — the form primitives and the counterparty form that uses `KeyValueEditor`
