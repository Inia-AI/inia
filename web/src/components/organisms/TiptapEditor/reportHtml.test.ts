import { describe, expect, it } from 'vitest'
import { toReportHtml } from './reportHtml'

describe('toReportHtml', () => {
  it('keeps the text of an empty cell and the paragraphs of a cell that holds two', () => {
    const editorHtml =
      '<table><tbody><tr><td colspan="1" rowspan="1"><p></p></td>' +
      '<td colspan="2" rowspan="1"><p>First</p><p>Second</p></td></tr></tbody></table>'

    expect(toReportHtml(editorHtml)).toBe(
      '<table><tbody><tr><td></td><td colspan="2"><p>First</p><p>Second</p></td></tr></tbody></table>',
    )
  })

  it('reshapes a table nested in a cell as it reshapes the outer one', () => {
    const editorHtml =
      '<table><tbody><tr><td colspan="1" rowspan="1">' +
      '<table><tbody><tr data-table-section="head"><th colspan="1" rowspan="1"><p>Inner</p></th></tr>' +
      '<tr data-table-section="body"><td colspan="1" rowspan="1"><p>1</p></td></tr></tbody></table>' +
      '</td></tr></tbody></table>'

    expect(toReportHtml(editorHtml)).toBe(
      '<table><tbody><tr><td>' +
        '<table><thead><tr><th>Inner</th></tr></thead><tbody><tr><td>1</td></tr></tbody></table>' +
        '</td></tr></tbody></table>',
    )
  })

  it('puts the header section after the column group of a resized table', () => {
    const editorHtml =
      '<table style="width: 300px"><colgroup><col style="width: 300px"></colgroup>' +
      '<tbody><tr data-table-section="head"><th colwidth="300"><p>Item</p></th></tr>' +
      '<tr data-table-section="body"><td colwidth="300"><p>Cash</p></td></tr></tbody></table>'

    const table = new DOMParser().parseFromString(toReportHtml(editorHtml), 'text/html').querySelector('table')!

    expect([...table.children].map(child => child.tagName)).toEqual(['COLGROUP', 'THEAD', 'TBODY'])
  })
})
