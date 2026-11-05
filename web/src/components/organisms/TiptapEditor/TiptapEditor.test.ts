import { mount, flushPromises, type VueWrapper } from '@vue/test-utils'
import type { Editor } from '@tiptap/core'
import { CellSelection } from '@tiptap/pm/tables'
import { describe, expect, it, vi } from 'vitest'
import { createI18n } from 'vue-i18n'

import TiptapEditor from './TiptapEditor.vue'
import bankStatementCards from './fixtures/bankStatementCards.html?raw'
import { renderContent } from '@/lib/renderContent'
import { EN } from '@/test/helpers/messages'
import { positionOf, replaceWords } from '@/test/helpers/tiptapEditor'

const i18n = createI18n({ legacy: false, locale: 'en', messages: { en: EN } })
const WITH_MESSAGES = { plugins: [i18n] }

const classedTable =
  '<table class="table-solo"><thead><tr><th class="metricHeader">Metric</th><th>FY24</th></tr></thead>' +
  '<tbody><tr class="bold"><td class="alignLeft">Revenue</td><td class="alignRight">100</td></tr></tbody></table>'

const htmlBody = (html: string) => new DOMParser().parseFromString(html, 'text/html').body

const mountEditor = async (content: string) => {
  const wrapper = mount(TiptapEditor, { props: { modelValue: content, label: 'Paragraph text' }, global: WITH_MESSAGES })
  await flushPromises()
  return { wrapper, editor: (wrapper.vm as unknown as { editor: Editor }).editor }
}

const lastSaved = (wrapper: VueWrapper) => wrapper.emitted('update:modelValue')!.at(-1)![0] as string

const savedBody = (wrapper: VueWrapper) => htmlBody(lastSaved(wrapper))

describe('TiptapEditor', () => {
  describe('saving an edited paragraph', () => {
    const styledTable =
      '<table style="border-collapse:collapse;width:100%;"><tbody>' +
      '<tr><th style="padding:12px 15px;text-align:left;">Line item</th><th style="padding:12px 15px;text-align:right;">FY0</th></tr>' +
      '<tr><td style="padding:10px 15px;">Cash</td><td style="padding:10px 15px;text-align:right;">147,937</td></tr>' +
      '</tbody></table>'

    const saveAfterAnEdit = async (content: string) => {
      const { wrapper, editor } = await mountEditor(content)
      editor.commands.insertContentAt(0, '<p>Restated.</p>')
      await flushPromises()
      return { wrapper, editor, saved: lastSaved(wrapper), page: savedBody(wrapper) }
    }

    it('keeps a table header section, its header cells and plain cell text', async () => {
      const { page } = await saveAfterAnEdit(`<p>Currency: NIS</p>${classedTable}`)
      const table = page.querySelector('table')!

      expect(page.firstElementChild!.textContent).toBe('Restated.')
      expect(table.getAttribute('class')).toBe('table-solo')
      expect(table.tHead!.rows).toHaveLength(1)
      expect([...table.tHead!.rows[0]!.cells].map(cell => cell.outerHTML)).toEqual([
        '<th class="metricHeader">Metric</th>',
        '<th>FY24</th>',
      ])
      expect(table.tBodies[0]!.innerHTML).toBe(
        '<tr class="bold"><td class="alignLeft">Revenue</td><td class="alignRight">100</td></tr>',
      )
      expect(table.querySelector('colgroup')).toBeNull()
      expect(table.hasAttribute('style')).toBe(false)
    })

    it('keeps the inline styles of a table, and a header row it had in its body', async () => {
      const { page } = await saveAfterAnEdit(styledTable)
      const table = page.querySelector('table')!
      const [headerRow, bodyRow] = [...table.tBodies[0]!.rows]

      expect(table.tHead).toBeNull()
      expect(table.style.borderCollapse).toBe('collapse')
      expect(table.style.width).toBe('100%')
      expect([...headerRow!.cells].map(cell => [cell.tagName, cell.textContent, cell.style.padding])).toEqual([
        ['TH', 'Line item', '12px 15px'],
        ['TH', 'FY0', '12px 15px'],
      ])
      expect(bodyRow!.cells[1]!.innerHTML).toBe('147,937')
      expect(bodyRow!.cells[1]!.style.textAlign).toBe('right')
    })

    it('adds no empty paragraph after a table that ends the paragraph', async () => {
      const { page } = await saveAfterAnEdit(classedTable)

      expect(page.lastElementChild!.tagName).toBe('TABLE')
    })

    it('keeps the items of a list as plain text', async () => {
      const { page } = await saveAfterAnEdit('<ul><li>Capital is taken as printed.</li><li>Loans are short-term.</li></ul>')

      expect(page.querySelector('ul')!.innerHTML).toBe('<li>Capital is taken as printed.</li><li>Loans are short-term.</li>')
    })

    it('keeps a style the table carried itself, even one that only sets a minimum width', async () => {
      const { page } = await saveAfterAnEdit('<table style="min-width: 600px;"><tbody><tr><td>Cash</td></tr></tbody></table>')

      expect(page.querySelector('table')!.style.minWidth).toBe('600px')
    })

    it('puts the header row of a table inserted in the editor into a header section', async () => {
      const { wrapper, editor } = await mountEditor('<p>Notes</p>')

      editor.commands.insertTable({ rows: 2, cols: 2, withHeaderRow: true })
      await flushPromises()

      const saved = wrapper.emitted('update:modelValue')!.at(-1)![0] as string
      const table = new DOMParser().parseFromString(saved, 'text/html').querySelector('table')!
      expect(table.tHead!.rows[0]!.cells[0]!.tagName).toBe('TH')
      expect(table.tBodies[0]!.rows).toHaveLength(1)
      expect(table.querySelector('colgroup')).toBeNull()
    })

    it('shows a value the model returns to after it showed another one', async () => {
      const { wrapper, editor, saved } = await saveAfterAnEdit(classedTable)

      await wrapper.setProps({ modelValue: '<p>Replaced by a refinement.</p>' })
      await wrapper.setProps({ modelValue: saved })

      expect(editor.getText()).toContain('Restated.')
    })

    it('keeps a header row in its header section when one of its cells is not a header cell', async () => {
      const { page } = await saveAfterAnEdit(
        '<table><thead><tr><td></td><th>FY24</th></tr></thead><tbody><tr><th>Revenue</th><td>100</td></tr></tbody></table>',
      )
      const table = page.querySelector('table')!

      expect(table.tHead!.innerHTML).toBe('<tr><td></td><th>FY24</th></tr>')
      expect(table.tBodies[0]!.innerHTML).toBe('<tr><th>Revenue</th><td>100</td></tr>')
    })

    it('leaves the header section out once the user deletes the header row', async () => {
      const { wrapper, editor } = await mountEditor(classedTable)
      editor.commands.setTextSelection(3)

      editor.commands.deleteRow()
      await flushPromises()

      const saved = wrapper.emitted('update:modelValue')!.at(-1)![0] as string
      const table = new DOMParser().parseFromString(saved, 'text/html').querySelector('table')!
      expect(table.tHead).toBeNull()
      expect(table.tBodies[0]!.rows[0]!.className).toBe('bold')
    })

    it('keeps the header section of a table copied within the editor', async () => {
      const { wrapper, editor } = await mountEditor(classedTable)

      editor.commands.setContent(editor.getHTML())
      editor.commands.insertContentAt(0, '<p>Restated.</p>')
      await flushPromises()

      const saved = wrapper.emitted('update:modelValue')!.at(-1)![0] as string
      expect(new DOMParser().parseFromString(saved, 'text/html').querySelector('thead th')!.textContent).toBe('Metric')
    })

    it('adds no bold markup to a cell whose own style already sets it bold', async () => {
      const { page } = await saveAfterAnEdit(
        '<table><tbody><tr><th style="font-weight:600;">Line item</th></tr><tr><td style="font-weight:bold;">Cash</td></tr></tbody></table>',
      )

      expect([...page.querySelectorAll('th, td')].map(cell => cell.innerHTML)).toEqual(['Line item', 'Cash'])
    })

    it('keeps a paragraph the user cleared as an empty paragraph, so the report still shows it', async () => {
      const { wrapper, editor } = await mountEditor('<p>Revenue was 12m</p>')

      editor.commands.clearContent(true)
      await flushPromises()

      expect(wrapper.emitted('update:modelValue')!.at(-1)![0]).toBe('<p></p>')
    })

    it('does not reload its content when the model hands back what it saved', async () => {
      const { wrapper, editor, saved } = await saveAfterAnEdit(classedTable)
      const documentBefore = editor.state.doc

      await wrapper.setProps({ modelValue: saved })

      expect(editor.state.doc).toBe(documentBefore)
    })
  })

  describe('saving an edited card layout or table section', () => {
    it('keeps the nesting, order, classes, styles and text of a card section, apart from the edited words', async () => {
      const { wrapper, editor } = await mountEditor(bankStatementCards)

      replaceWords(editor, 'EUR 31,200', 'EUR 31,201')
      await flushPromises()

      expect(lastSaved(wrapper)).toBe(bankStatementCards.trim().replace('EUR 31,200', 'EUR 31,201'))
    })

    it('saves a card section that the report then shows exactly as saved', async () => {
      const { wrapper, editor } = await mountEditor(bankStatementCards)

      replaceWords(editor, 'Period credits', 'Period inflows')
      await flushPromises()

      const saved = savedBody(wrapper).firstElementChild!.outerHTML
      expect(htmlBody(renderContent(saved)).firstElementChild!.outerHTML).toBe(saved)
    })

    const pressEnterAfter = (editor: Editor, words: string) => {
      editor.commands.setTextSelection(positionOf(editor, words) + words.length)
      editor.view.dom.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter', bubbles: true, cancelable: true }))
    }

    it('starts a new line of a card with a copy of the line it was pressed in', async () => {
      const { wrapper, editor } = await mountEditor(bankStatementCards)

      pressEnterAfter(editor, 'EUR 31,200')
      editor.commands.insertContent('Restated')
      await flushPromises()

      const value = [...savedBody(wrapper).querySelectorAll('div')].find(div => div.textContent === 'EUR 31,200')!
      const added = value.nextElementSibling!
      expect(added.outerHTML).toBe(value.outerHTML.replace('EUR 31,200', 'Restated'))
    })

    it('breaks the line inside text written straight into a card, without adding an element', async () => {
      const { wrapper, editor } = await mountEditor(bankStatementCards)

      pressEnterAfter(editor, 'Month-end balance')
      await flushPromises()

      const saved = savedBody(wrapper).firstElementChild!.outerHTML
      expect(saved).toBe(
        htmlBody(bankStatementCards.trim().replace('Month-end balance below', 'Month-end balance<br> below')).firstElementChild!
          .outerHTML,
      )
    })

    it('keeps an image inside a card', async () => {
      const card =
        '<div style="border:1px solid var(--stroke)"><div style="padding:4px"><img src="https://example.com/logo.png"></div>' +
        '<div style="font-size:12px">Statement</div></div>'
      const { wrapper, editor } = await mountEditor(card)

      replaceWords(editor, 'Statement', 'Bank statement')
      await flushPromises()

      expect(savedBody(wrapper).innerHTML).toBe(card.replace('Statement', 'Bank statement'))
    })

    it('keeps the alignment of a cell that set it with the align attribute', async () => {
      const { wrapper, editor } = await mountEditor(
        '<table><tbody><tr><td>Cash</td><td align="right" style="padding:4px">1</td></tr></tbody></table>',
      )

      replaceWords(editor, 'Cash', 'Cash at bank')
      await flushPromises()

      const cell = savedBody(wrapper).querySelectorAll('td')[1]!
      expect([cell.style.textAlign, cell.style.padding]).toEqual(['right', '4px'])
    })

    it('keeps the caption of a table copied within the editor', async () => {
      const { wrapper, editor } = await mountEditor(
        '<table><caption>Balance sheet</caption><tbody><tr><td>Cash</td></tr></tbody></table>',
      )

      editor.commands.setContent(editor.getHTML())
      replaceWords(editor, 'Cash', 'Cash at bank')
      await flushPromises()

      expect(savedBody(wrapper).querySelector('table')!.caption!.textContent).toBe('Balance sheet')
    })

    it('keeps a list whose items start with a card line', async () => {
      const list = '<ol><li><div style="font-weight:600">Title</div><div>Detail</div></li><li>Two</li></ol>'
      const { wrapper, editor } = await mountEditor(list)

      replaceWords(editor, 'Two', 'Second')
      await flushPromises()

      expect(savedBody(wrapper).innerHTML).toBe(list.replace('Two', 'Second'))
    })

    it('starts a plain paragraph after a styled heading', async () => {
      const { wrapper, editor } = await mountEditor('<h2 class="section-title" style="font-size:20px">Risk</h2>')

      pressEnterAfter(editor, 'Risk')
      editor.commands.insertContent('Next')
      await flushPromises()

      expect(savedBody(wrapper).innerHTML).toBe('<h2 class="section-title" style="font-size:20px">Risk</h2><p>Next</p>')
    })

    it('keeps the styles of a link, an image and a rule', async () => {
      const content =
        '<div style="padding:4px"><img class="logo" style="width:24px;height:24px" src="https://example.com/logo.png"></div>' +
        '<hr class="divider" style="border-color:var(--stroke)">' +
        '<p><a href="https://example.com" style="font-weight:bold">Source</a> statement</p>'
      const { wrapper, editor } = await mountEditor(content)

      replaceWords(editor, 'statement', 'bank statement')
      await flushPromises()

      const saved = savedBody(wrapper)
      expect(saved.querySelector('img')!.getAttribute('style')).toBe('width:24px;height:24px')
      expect(saved.querySelector('img')!.getAttribute('class')).toBe('logo')
      expect(saved.querySelector('hr')!.outerHTML).toBe('<hr class="divider" style="border-color:var(--stroke)">')
      expect(saved.querySelector('a')!.getAttribute('style')).toBe('font-weight:bold')
    })

    it('keeps the styles of a card as written after the card is copied within the editor', async () => {
      const { wrapper, editor } = await mountEditor(bankStatementCards)

      editor.commands.setContent(editor.getHTML())
      replaceWords(editor, 'EUR 31,200', 'EUR 31,201')
      await flushPromises()

      expect(savedBody(wrapper).innerHTML).toBe(htmlBody(bankStatementCards.trim().replace('EUR 31,200', 'EUR 31,201')).innerHTML)
    })

    it('replaces selected words in a card line with a new line that copies it', async () => {
      const { wrapper, editor } = await mountEditor(bankStatementCards)
      const from = positionOf(editor, '31,200')
      editor.commands.setTextSelection({ from, to: from + '31,200'.length })

      editor.view.dom.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter', bubbles: true, cancelable: true }))
      editor.commands.insertContent('31,201')
      await flushPromises()

      const value = [...savedBody(wrapper).querySelectorAll('div')].find(div => div.textContent === 'EUR ')!
      expect(value.nextElementSibling!.outerHTML).toBe(value.outerHTML.replace('EUR ', '31,201'))
    })

    it('keeps a styled span that holds a link as one span', async () => {
      const content = '<p><span style="border:1px solid var(--stroke)">Source <a href="https://example.com">statement</a> page 3</span></p>'
      const { wrapper, editor } = await mountEditor(content)

      replaceWords(editor, 'page 3', 'page 4')
      await flushPromises()

      expect(savedBody(wrapper).querySelectorAll('span')).toHaveLength(1)
      expect(savedBody(wrapper).querySelector('span')!.textContent).toBe('Source statement page 4')
    })

    const pasteAtTheEnd = (editor: Editor, html: string) => {
      editor.commands.setTextSelection(editor.state.doc.content.size)
      editor.view.pasteHTML(html, new Event('paste') as ClipboardEvent)
    }

    const pasteACopyOfTheSelection = (editor: Editor) =>
      pasteAtTheEnd(editor, editor.view.serializeForClipboard(editor.state.selection.content()).dom.innerHTML)

    // What ProseMirror does with text the browser typed over the range from..to.
    const typeOver = (editor: Editor, from: number, to: number, text: string) => {
      const { view } = editor
      const { selection } = editor.state
      if (selection.from !== from || selection.to !== to) editor.commands.setTextSelection({ from, to })
      const typed = () => view.state.tr.insertText(text, from, to)
      if (!view.someProp('handleTextInput', handle => handle(view, from, to, text, typed))) view.dispatch(typed())
    }

    const typeKeyByKeyOver = (editor: Editor, from: number, to: number, text: string) => {
      typeOver(editor, from, to, text.charAt(0))
      for (const key of text.slice(1)) typeOver(editor, editor.state.selection.from, editor.state.selection.from, key)
    }

    it('drops the classes and styles of a paste from Word, and keeps its bold text bold', async () => {
      const { wrapper, editor } = await mountEditor('<p></p>')

      pasteAtTheEnd(
        editor,
        '<div class="WordSection1"><p class="MsoNormal" style="margin:0cm;font-size:11pt;font-family:Calibri,sans-serif">' +
          '<b><span style="font-size:12pt;color:black">Bold</span></b><span style="mso-spacerun:yes"> </span>plain</p></div>',
      )
      await flushPromises()

      expect(savedBody(wrapper).innerHTML).toBe('<p><strong>Bold</strong> plain</p>')
    })

    it('drops the classes and styles of a paste from Google Docs, and keeps the weight its styles gave', async () => {
      const { wrapper, editor } = await mountEditor('<p></p>')

      pasteAtTheEnd(
        editor,
        '<meta charset="utf-8"><b style="font-weight:normal;" id="docs-internal-guid-1a2b"><p dir="ltr" style="line-height:1.38;margin-top:0pt;margin-bottom:0pt;">' +
          '<span style="font-size:11pt;font-family:Arial,sans-serif;color:#000000;background-color:transparent;font-weight:700;font-style:normal;text-decoration:none;white-space:pre-wrap;">Bold</span>' +
          '<span style="font-size:11pt;font-family:Arial,sans-serif;color:#000000;font-weight:400;font-style:italic;white-space:pre-wrap;"> slanted</span></p></b>',
      )
      await flushPromises()

      expect(savedBody(wrapper).innerHTML).toBe('<p><strong>Bold</strong><em> slanted</em></p>')
    })

    it('keeps everything of a card line copied and pasted within the editor', async () => {
      const { wrapper, editor } = await mountEditor(bankStatementCards)
      const line = editor.state.doc.resolve(positionOf(editor, 'EUR 31,200')).before()
      editor.commands.setNodeSelection(line)

      pasteACopyOfTheSelection(editor)
      await flushPromises()

      const copies = [...savedBody(wrapper).querySelectorAll('div')].filter(div => div.textContent === 'EUR 31,200')
      expect(copies).toHaveLength(2)
      expect(copies[1]!.outerHTML).toBe(copies[0]!.outerHTML)
    })

    it.each([
      ['as an id', '<fact id="c1">ILS 49,210</fact>', '<fact id="c1">ILS 49,211</fact>'],
      [
        'with its sources inside it',
        '<fact>ILS 49,210<fact_source label="Annual report"></fact_source></fact>',
        '<fact>ILS 49,211<fact_source label="Annual report"></fact_source></fact>',
      ],
    ])('keeps a cited figure whole in its citation %s when its last digit is typed over', async (_form, cited, corrected) => {
      const { wrapper, editor } = await mountEditor(`<p>Credits of ${cited} in 2024</p>`)
      const lastDigit = positionOf(editor, 'ILS 49,210') + 'ILS 49,21'.length

      typeOver(editor, lastDigit, lastDigit + 1, '1')
      await flushPromises()

      expect(savedBody(wrapper).innerHTML).toBe(htmlBody(`<p>Credits of ${corrected} in 2024</p>`).innerHTML)
    })

    it.each([
      ['its last digit', 'ILS 49,21'.length, 'ILS 49,210'.length, '05', 'ILS 49,2105'],
      ['the whole figure', 0, 'ILS 49,210'.length, 'ILS 50,000', 'ILS 50,000'],
    ])('keeps every key typed over %s of a cited figure in its citation', async (_part, start, end, keys, figure) => {
      const { wrapper, editor } = await mountEditor('<p>Credits of <fact id="c1">ILS 49,210</fact> in 2024</p>')
      const cited = positionOf(editor, 'ILS 49,210')

      typeKeyByKeyOver(editor, cited + start, cited + end, keys)
      await flushPromises()

      expect(savedBody(wrapper).innerHTML).toBe(`<p>Credits of <fact id="c1">${figure}</fact> in 2024</p>`)
    })

    it.each([
      ['Enter in a paragraph', '<p>Credits of <fact id="c1">ILS 49,210</fact></p>', false, '</p><p>Next</p>'],
      ['Shift+Enter in a paragraph', '<p>Credits of <fact id="c1">ILS 49,210</fact></p>', true, '<br>Next'],
      ['Enter in a list item', '<ul><li>Credits of <fact id="c1">ILS 49,210</fact></li></ul>', false, '</li><li>Next</li>'],
    ])('does not carry a corrected citation over %s', async (_key, content, shiftKey, next) => {
      const { wrapper, editor } = await mountEditor(content)
      const lastDigit = positionOf(editor, 'ILS 49,210') + 'ILS 49,21'.length
      typeOver(editor, lastDigit, lastDigit + 1, '1')

      editor.view.dom.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter', shiftKey, bubbles: true, cancelable: true }))
      typeOver(editor, editor.state.selection.from, editor.state.selection.from, 'Next')
      await flushPromises()

      expect(savedBody(wrapper).innerHTML).toContain(`<fact id="c1">ILS 49,211</fact>${next}`)
    })

    it('types outside a corrected citation once the caret has left it', async () => {
      const { wrapper, editor } = await mountEditor('<p>Credits of <fact id="c1">ILS 49,210</fact> in 2024</p>')
      const lastDigit = positionOf(editor, 'ILS 49,210') + 'ILS 49,21'.length
      typeOver(editor, lastDigit, lastDigit + 1, '1')
      editor.commands.setTextSelection(positionOf(editor, 'Credits'))
      editor.commands.setTextSelection(lastDigit + 1)

      typeOver(editor, lastDigit + 1, lastDigit + 1, ' net')
      await flushPromises()

      expect(savedBody(wrapper).innerHTML).toBe('<p>Credits of <fact id="c1">ILS 49,211</fact> net in 2024</p>')
    })

    it.each([
      ['end', 'ILS 49,210'.length, ' net', '<p>Credits of <fact id="c1">ILS 49,210</fact> net in 2024</p>'],
      ['start', 0, 'about ', '<p>Credits of about <fact id="c1">ILS 49,210</fact> in 2024</p>'],
    ])('types outside a citation from a bare caret at its %s', async (_edge, offset, text, saved) => {
      const { wrapper, editor } = await mountEditor('<p>Credits of <fact id="c1">ILS 49,210</fact> in 2024</p>')
      const caret = positionOf(editor, 'ILS 49,210') + offset

      typeOver(editor, caret, caret, text)
      await flushPromises()

      expect(savedBody(wrapper).innerHTML).toBe(saved)
    })

    it('types outside a citation over words that reach past it, and keeps the citation on its untouched words', async () => {
      const { wrapper, editor } = await mountEditor('<p>Credits of <fact id="c1">ILS 49,210</fact> in 2024</p>')
      const lastDigit = positionOf(editor, 'ILS 49,210') + 'ILS 49,21'.length

      typeOver(editor, lastDigit, lastDigit + '0 in'.length, '1 for')
      await flushPromises()

      expect(savedBody(wrapper).innerHTML).toBe('<p>Credits of <fact id="c1">ILS 49,21</fact>1 for 2024</p>')
    })

    it.each([
      ['as an id', '<fact id="c1">12m</fact>'],
      ['with its sources inside it', '<fact>12m<fact_source label="Annual report"><cited-text>12,000,000</cited-text></fact_source></fact>'],
    ])('keeps a citation written %s copied and pasted within the editor', async (_form, citation) => {
      const { wrapper, editor } = await mountEditor(`<p>Revenue was ${citation}</p><p>Restated</p>`)
      editor.commands.setTextSelection({ from: positionOf(editor, 'Revenue'), to: positionOf(editor, '12m') + '12m'.length })

      pasteACopyOfTheSelection(editor)
      await flushPromises()

      expect(savedBody(wrapper).innerHTML).toBe(htmlBody(`<p>Revenue was ${citation}</p><p>RestatedRevenue was ${citation}</p>`).innerHTML)
    })

    it('keeps a citation dragged within the editor as stored', async () => {
      const { wrapper, editor } = await mountEditor('<p>Revenue was <fact id="c1">12m</fact></p><p>Restated</p>')
      editor.commands.setTextSelection({ from: positionOf(editor, '12m'), to: positionOf(editor, '12m') + '12m'.length })
      let { slice } = editor.view.serializeForClipboard(editor.state.selection.content())
      editor.view.someProp('transformPasted', transform => {
        slice = transform(slice, editor.view, false)
      })

      editor.commands.setTextSelection(editor.state.doc.content.size - 1)
      editor.view.dispatch(editor.state.tr.replaceSelection(slice))
      await flushPromises()

      expect(savedBody(wrapper).innerHTML).toBe('<p>Revenue was <fact id="c1">12m</fact></p><p>Restated<fact id="c1">12m</fact></p>')
    })

    it('keeps a citation one citation when plain text is pasted inside it', async () => {
      const { wrapper, editor } = await mountEditor('<p>Revenue was <fact id="c1">12m</fact></p>')
      editor.commands.setTextSelection(positionOf(editor, '12m') + 1)

      editor.view.pasteText('5', new Event('paste') as ClipboardEvent)
      await flushPromises()

      expect(savedBody(wrapper).innerHTML).toBe('<p>Revenue was <fact id="c1">152m</fact></p>')
    })

    it('drops a citation copied from the editor of another section, whose sources it cannot reach', async () => {
      const other = await mountEditor(
        '<p>Revenue was <fact id="c1">12m<fact_source label="Annual report"><cited-text>12,000,000</cited-text></fact_source></fact></p>',
      )
      other.editor.commands.selectAll()
      const copied = other.editor.view.serializeForClipboard(other.editor.state.selection.content()).dom.innerHTML
      const { wrapper, editor } = await mountEditor('<p></p>')

      pasteAtTheEnd(editor, copied)
      await flushPromises()

      expect(savedBody(wrapper).innerHTML).toBe('<p>Revenue was 12m</p>')
    })

    it('keeps no attribute of a citation but its id, so no handler written into it runs in the editor', async () => {
      const { editor } = await mountEditor('<p>Revenue was <fact id="c1" onmouseover="alert(1)">12m</fact></p>')

      expect(editor.view.dom.querySelector('fact')!.getAttributeNames()).toEqual(['id'])
    })

    it('never creates a citation from HTML pasted from outside the editor', async () => {
      const { wrapper, editor } = await mountEditor('<p></p>')

      pasteAtTheEnd(editor, '<p>Revenue was <fact id="c1">12m<fact_source label="Annual report">p. 3</fact_source></fact></p>')
      await flushPromises()

      expect(savedBody(wrapper).innerHTML).toBe('<p>Revenue was 12m</p>')
    })

    it('keeps a citation that carries its sources inside it, as stored', async () => {
      const cited =
        '<p>Revenue was <fact>12m<fact_source label="Annual report" page-id="p-3"><cited-text>Revenue 12,000,000</cited-text></fact_source></fact> in 2024</p>'
      const { wrapper, editor } = await mountEditor(cited)

      replaceWords(editor, 'in 2024', 'in fiscal 2024')
      await flushPromises()

      expect(savedBody(wrapper).innerHTML).toBe(htmlBody(cited.replace('in 2024', 'in fiscal 2024')).innerHTML)
    })

    it('does not stretch a citation that carries its sources inside it over words typed right after it', async () => {
      const { wrapper, editor } = await mountEditor(
        '<p>Revenue was <fact>12m<fact_source label="Annual report"><cited-text>12,000,000</cited-text></fact_source></fact> in 2024</p>',
      )

      editor.commands.setTextSelection(positionOf(editor, '12m') + '12m'.length)
      editor.commands.insertContent(' net')
      await flushPromises()

      expect(savedBody(wrapper).innerHTML).toBe(
        '<p>Revenue was <fact>12m<fact_source label="Annual report"><cited-text>12,000,000</cited-text></fact_source></fact> net in 2024</p>',
      )
    })

    it.each([
      ['as an id', '<fact id="c1">12m</fact>'],
      ['with its sources inside it', '<fact>12m<fact_source label="Annual report"><cited-text>12,000,000</cited-text></fact_source></fact>'],
    ])('removes a citation written %s when its words are deleted', async (_form, citation) => {
      const { wrapper, editor } = await mountEditor(`<p>Revenue was ${citation} in 2024</p>`)

      replaceWords(editor, '12m', '')
      await flushPromises()

      expect(savedBody(wrapper).innerHTML).toBe('<p>Revenue was  in 2024</p>')
    })

    it('drops a citation in the caption of a table copied from the editor of another section', async () => {
      const other = await mountEditor(
        '<table><caption>Balance sheet, <fact id="c1">FY24</fact></caption><tbody><tr><td>Cash</td></tr></tbody></table>',
      )
      other.editor.commands.selectAll()
      const copied = other.editor.view.serializeForClipboard(other.editor.state.selection.content()).dom.innerHTML
      const { wrapper, editor } = await mountEditor('<p></p>')

      pasteAtTheEnd(editor, copied)
      await flushPromises()

      expect(savedBody(wrapper).querySelector('caption')!.innerHTML).toBe('Balance sheet, FY24')
    })

    it('keeps two citations written side by side as two', async () => {
      const cited =
        '<p>Revenue of <fact>12m</fact><fact>(EUR)</fact> in 2024</p>'
      const { wrapper, editor } = await mountEditor(cited)

      replaceWords(editor, 'in 2024', 'in fiscal 2024')
      await flushPromises()

      expect(savedBody(wrapper).innerHTML).toBe(htmlBody(cited.replace('in 2024', 'in fiscal 2024')).innerHTML)
    })

    it('shows a table caption above its table while editing, without letting it be edited', async () => {
      const { wrapper } = await mountEditor(
        '<table><caption>Balance sheet, <strong>NIS</strong></caption><tbody><tr><td>Cash</td></tr></tbody></table>',
      )

      const caption = wrapper.get('.ProseMirror table caption')
      expect(caption.element.innerHTML).toBe('Balance sheet, <strong>NIS</strong>')
      expect(caption.attributes('contenteditable')).toBe('false')
      expect(caption.element.parentElement!.firstElementChild).toBe(caption.element)
    })

    it('shows the caption of a table given in place of the one shown, and none for a table without one', async () => {
      const table = (caption: string) => `<table>${caption}<tbody><tr><td>Cash</td></tr></tbody></table>`
      const { wrapper } = await mountEditor(table('<caption>Balance sheet</caption>'))
      const shownCaption = () => wrapper.get('.ProseMirror table caption').element as HTMLElement

      await wrapper.setProps({ modelValue: table('<caption>Income statement</caption>') })
      expect(shownCaption().innerHTML).toBe('Income statement')

      await wrapper.setProps({ modelValue: table('') })
      expect(shownCaption().hidden).toBe(true)
    })

    it('keeps a table caption as a caption', async () => {
      const { wrapper, editor } = await mountEditor(
        '<table><caption>Balance sheet, <strong>NIS</strong></caption><thead><tr><th>Item</th></tr></thead>' +
          '<tbody><tr><td>Cash</td></tr></tbody></table>',
      )

      replaceWords(editor, 'Cash', 'Cash at bank')
      await flushPromises()

      const table = savedBody(wrapper).querySelector('table')!
      expect(table.caption!.innerHTML).toBe('Balance sheet, <strong>NIS</strong>')
      expect(table.tHead!.rows[0]!.textContent).toBe('Item')
      expect([...table.tBodies[0]!.rows].map(row => row.textContent)).toEqual(['Cash at bank'])
    })

    it('keeps footer rows in the footer', async () => {
      const { wrapper, editor } = await mountEditor(
        '<table><tbody><tr><td>Cash</td><td>1</td></tr></tbody><tfoot><tr class="bold"><td>Total</td><td>1</td></tr></tfoot></table>',
      )

      replaceWords(editor, 'Cash', 'Cash at bank')
      await flushPromises()

      const table = savedBody(wrapper).querySelector('table')!
      expect(table.tFoot!.innerHTML).toBe('<tr class="bold"><td>Total</td><td>1</td></tr>')
      expect(table.tBodies[0]!.rows).toHaveLength(1)
    })

    const inlineStyledTable =
      '<table style="border-collapse:collapse"><tbody>' +
      '<tr style="background:var(--bg-2)"><td style="padding:10px 15px;font-weight:bold">Cash</td><td style="padding:10px 15px">98,566</td></tr>' +
      '<tr class="bold" style="background:var(--bg-1)"><td style="padding:10px 15px">Debt</td><td style="padding:10px 15px">1,000</td></tr>' +
      '</tbody></table>'

    const rowMarkup = (row: HTMLTableRowElement) => ({
      class: row.getAttribute('class'),
      style: row.getAttribute('style'),
      cells: [...row.cells].map(cell => cell.getAttribute('style')),
    })

    it('gives a row added with Tab the class and style of the row it follows, cells included', async () => {
      const { wrapper, editor } = await mountEditor(inlineStyledTable)
      editor.commands.setTextSelection(positionOf(editor, '1,000') + 1)

      editor.view.dom.dispatchEvent(new KeyboardEvent('keydown', { key: 'Tab', bubbles: true, cancelable: true }))
      await flushPromises()

      const rows = [...savedBody(wrapper).querySelector('tbody')!.rows]
      expect(rows).toHaveLength(3)
      expect(rowMarkup(rows[2]!)).toEqual(rowMarkup(rows[1]!))
      expect(rows[2]!.textContent).toBe('')
    })

    it('gives each cell of an added row the style of the cell above it, past a cell that spans rows', async () => {
      const { wrapper, editor } = await mountEditor(
        '<table><tbody>' +
          '<tr style="background:red"><td rowspan="2" style="color:purple">A</td><td style="color:blue">B</td><td style="color:green">C</td></tr>' +
          '<tr><td style="color:navy">D</td><td style="color:olive">E</td></tr>' +
          '</tbody></table>',
      )
      editor.commands.setTextSelection(positionOf(editor, 'B') + 1)

      editor.commands.addRowAfter()
      await flushPromises()

      const rows = [...savedBody(wrapper).querySelector('tbody')!.rows]
      expect(rows).toHaveLength(3)
      expect(rows[1]!.getAttribute('style')).toBe('background:red')
      expect([...rows[1]!.cells].map(cell => cell.getAttribute('style'))).toEqual(['color:blue', 'color:green'])
      expect(rows[2]!.textContent).toBe('DE')
    })

    it('adds the styled row after the last row of a selection dragged upwards', async () => {
      const { wrapper, editor } = await mountEditor(inlineStyledTable)
      const cellAt = (text: string) => editor.state.doc.resolve(positionOf(editor, text) - 1).before()
      editor.view.dispatch(
        editor.state.tr.setSelection(CellSelection.create(editor.state.doc, cellAt('1,000'), cellAt('Cash'))),
      )

      editor.commands.addRowAfter()
      await flushPromises()

      const rows = [...savedBody(wrapper).querySelector('tbody')!.rows]
      expect(rows.map(row => row.textContent)).toEqual(['Cash98,566', 'Debt1,000', ''])
      expect(rowMarkup(rows[2]!)).toEqual(rowMarkup(rows[1]!))
    })

    it('gives a row added above another the class and style of that row, cells included', async () => {
      const { wrapper, editor } = await mountEditor(inlineStyledTable)
      editor.commands.setTextSelection(positionOf(editor, 'Cash') + 1)

      editor.commands.addRowBefore()
      await flushPromises()

      const rows = [...savedBody(wrapper).querySelector('tbody')!.rows]
      expect(rows).toHaveLength(3)
      expect(rowMarkup(rows[0]!)).toEqual(rowMarkup(rows[1]!))
      expect(rows[1]!.textContent).toBe('Cash98,566')
    })
  })

  describe('when it opens with focus', () => {
    const mountFocused = async () => {
      const wrapper = mount(TiptapEditor, {
        props: { modelValue: classedTable, label: 'Paragraph text', autofocus: true },
        attachTo: document.body,
        global: WITH_MESSAGES,
      })
      const editable = () => wrapper.find('[role="textbox"]').element
      await vi.waitFor(() => expect(editable()?.contains(document.activeElement)).toBe(true))
      return wrapper
    }

    it('names every toolbar button by what it does, never by its glyph', async () => {
      const wrapper = await mountFocused()
      const buttons = wrapper.findAll('.tiptap-toolbar button')

      expect(buttons.map(button => button.attributes('aria-label'))).toEqual([
        'Bold',
        'Italic',
        'Underline',
        'Strikethrough',
        'Code',
        'Bullet List',
        'Ordered List',
        'Link',
        'Image',
        'Table',
        'Undo',
        'Redo',
      ])
      expect(buttons.map(button => button.attributes('title'))).toEqual(
        buttons.map(button => button.attributes('aria-label')),
      )
    })

    it('says whether each formatting toggle is on', async () => {
      const wrapper = await mountFocused()
      const editor = (wrapper.vm as unknown as { editor: Editor }).editor
      const pressed = () =>
        Object.fromEntries(
          wrapper
            .findAll('.tiptap-toolbar button[aria-pressed]')
            .map(button => [button.attributes('aria-label'), button.attributes('aria-pressed')]),
        )

      expect(pressed()).toEqual({
        Bold: 'false',
        Italic: 'false',
        Underline: 'false',
        Strikethrough: 'false',
        Code: 'false',
        'Bullet List': 'false',
        'Ordered List': 'false',
      })

      editor.chain().selectAll().toggleBold().run()

      await vi.waitFor(() => expect(pressed().Bold).toBe('true'))
    })

    it('names its editing surface as a multi-line text field', async () => {
      const wrapper = await mountFocused()
      const surface = wrapper.get('.ProseMirror')

      expect(surface.attributes('role')).toBe('textbox')
      expect(surface.attributes('aria-label')).toBe('Paragraph text')
      expect(surface.attributes('aria-multiline')).toBe('true')
    })

    it('leaves the content as it was until the user edits it', async () => {
      const wrapper = await mountFocused()

      expect(wrapper.emitted('update:modelValue')).toBeUndefined()
    })

    it('hands every edit of the user to the model', async () => {
      const wrapper = await mountFocused()
      const editor = (wrapper.vm as unknown as { editor: Editor }).editor

      editor.commands.insertContentAt(0, '<p>Restated.</p>')
      await flushPromises()

      expect(wrapper.emitted('update:modelValue')!.at(-1)![0]).toContain('Restated.')
    })
  })
})
