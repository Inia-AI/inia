import { describe, expect, it } from 'vitest'

import { renderContent, sanitizeHtml, sanitizeModelHtml } from './renderContent'

describe('renderContent', () => {
  it('returns empty string for null, undefined, or empty input', () => {
    expect(renderContent(undefined)).toBe('')
    expect(renderContent(null)).toBe('')
    expect(renderContent('')).toBe('')
  })

  it('renders plain text as a paragraph', () => {
    expect(renderContent('Hello world')).toContain('<p>Hello world</p>')
  })

  it('converts single newlines to <br> with breaks: true', () => {
    const rendered = renderContent('line1\nline2')
    expect(rendered).toContain('line1')
    expect(rendered).toContain('<br>')
    expect(rendered).toContain('line2')
  })

  it('preserves paragraph breaks across double newlines', () => {
    const rendered = renderContent('para one\n\npara two')
    expect(rendered).toContain('<p>para one</p>')
    expect(rendered).toContain('<p>para two</p>')
  })

  it('renders markdown headers as h1-h6', () => {
    expect(renderContent('# Heading 1')).toContain('<h1>Heading 1</h1>')
    expect(renderContent('## Heading 2')).toContain('<h2>Heading 2</h2>')
    expect(renderContent('### Heading 3')).toContain('<h3>Heading 3</h3>')
  })

  it('renders markdown unordered lists as <ul><li>', () => {
    const rendered = renderContent('- one\n- two\n- three')
    expect(rendered).toContain('<ul>')
    expect(rendered).toContain('<li>one</li>')
    expect(rendered).toContain('<li>two</li>')
    expect(rendered).toContain('<li>three</li>')
  })

  it('renders markdown ordered lists as <ol><li>', () => {
    const rendered = renderContent('1. first\n2. second')
    expect(rendered).toContain('<ol')
    expect(rendered).toContain('<li>first</li>')
    expect(rendered).toContain('<li>second</li>')
  })

  it('renders markdown bold as <strong>', () => {
    expect(renderContent('**bold**')).toContain('<strong>bold</strong>')
  })

  it('renders markdown italic as <em>', () => {
    expect(renderContent('*italic*')).toContain('<em>italic</em>')
  })

  it('renders GFM tables as <table>', () => {
    const rendered = renderContent(
      '| Version | Release |\n|---|---|\n| 3.9 | October 2020 |\n| 3.10 | October 2021 |',
    )
    expect(rendered).toContain('<table>')
    expect(rendered).toContain('<thead>')
    expect(rendered).toContain('<th>Version</th>')
    expect(rendered).toContain('<th>Release</th>')
    expect(rendered).toContain('<td>3.9</td>')
    expect(rendered).toContain('<td>October 2020</td>')
  })

  it('renders horizontal rule for ---', () => {
    expect(renderContent('---')).toContain('<hr>')
  })

  it('renders code fences with <pre><code>', () => {
    const rendered = renderContent('```\nconsole.log("hi")\n```')
    expect(rendered).toContain('<pre>')
    expect(rendered).toContain('<code')
    expect(rendered).toContain('console.log')
  })

  it('passes through inline HTML tables untouched', () => {
    const html =
      '<table><thead><tr><th>A</th></tr></thead><tbody><tr><td>1</td></tr></tbody></table>'
    const rendered = renderContent(html)
    expect(rendered).toContain('<table>')
    expect(rendered).toContain('<th>A</th>')
    expect(rendered).toContain('<td>1</td>')
  })

  it('keeps the column group the paragraph editor writes into a table, instead of showing it as text', () => {
    const html =
      '<table style="min-width: 50px;"><colgroup><col style="min-width: 25px;"><col style="min-width: 25px;"></colgroup>' +
      '<tbody><tr><td><p>Cash</p></td><td><p>124,710</p></td></tr></tbody></table>'
    const container = document.createElement('div')
    container.innerHTML = renderContent(html)

    expect(container.textContent).not.toContain('colgroup')
    expect(container.querySelectorAll('table > colgroup > col')).toHaveLength(2)
  })

  it('shows a table caption and footer as table parts, not as text', () => {
    const container = document.createElement('div')
    container.innerHTML = renderContent(
      '<table><caption>Balance sheet</caption><tbody><tr><td>Cash</td></tr></tbody><tfoot><tr><td>Total</td></tr></tfoot></table>',
    )

    expect(container.textContent).not.toContain('caption')
    expect(container.querySelector('table > caption')!.textContent).toBe('Balance sheet')
    expect(container.querySelector('table > tfoot td')!.textContent).toBe('Total')
  })

  it('passes through inline <a href> with safe URLs', () => {
    const rendered = renderContent('see [link](https://example.com)')
    expect(rendered).toContain('<a href="https://example.com">link</a>')
  })

  it('preserves <fact id="cX"> markers as DOM elements', () => {
    const rendered = renderContent(
      'The revenue was <fact id="c0">$5M</fact> last quarter.',
    )
    expect(rendered).toContain('<fact id="c0">$5M</fact>')
  })

  it('preserves <fact_source> elements', () => {
    const rendered = renderContent(
      '<fact id="c0">$5M<fact_source label="report.pdf"></fact_source></fact>',
    )
    expect(rendered).toContain('<fact_source')
    expect(rendered).toContain('label="report.pdf"')
  })

  it('escapes unknown custom tags so they appear as literal text', () => {
    const rendered = renderContent('<rating>good</rating>')
    expect(rendered).not.toContain('<rating>')
    expect(rendered).toContain('&lt;rating&gt;')
    expect(rendered).toContain('good')
    expect(rendered).toContain('&lt;/rating&gt;')
  })

  it('escapes nested unknown tags', () => {
    const rendered = renderContent('<custom_xml>inner</custom_xml>')
    expect(rendered).not.toContain('<custom_xml>')
    expect(rendered).toContain('&lt;custom_xml&gt;')
    expect(rendered).toContain('inner')
    expect(rendered).toContain('&lt;/custom_xml&gt;')
  })

  it('escapes unknown tags with attributes', () => {
    const rendered = renderContent('<rating value="5">good</rating>')
    expect(rendered).not.toMatch(/<rating[\s>]/)
    expect(rendered).toContain('&lt;rating')
    expect(rendered).toContain('value="5"')
  })

  it('renders legacy <p>-wrapped content as a paragraph (back-compat)', () => {
    const rendered = renderContent('<p>Hello world</p>')
    expect(rendered).toContain('<p>Hello world</p>')
  })

  it('unwraps legacy <p>{markdown with \\n\\n}</p> Merge-block output so markdown inside renders', () => {
    const rendered = renderContent(
      '<p>**Fund Name:** Imperial Vision Fund SPC\n\n**Established:** November 2020\n\n**NAV:** 13,800,000.00</p>',
    )
    expect(rendered).toContain('<strong>Fund Name:</strong>')
    expect(rendered).toContain('Imperial Vision Fund SPC')
    expect(rendered).toContain('<strong>Established:</strong>')
    expect(rendered).toContain('November 2020')
    expect(rendered).toContain('<strong>NAV:</strong>')
    expect(rendered).not.toContain('**Fund Name:**')
  })

  it('does NOT unwrap a <p> that contains nested block elements', () => {
    const rendered = renderContent('<p>intro<ul><li>x</li></ul>outro</p>')
    expect(rendered).toContain('<ul>')
    expect(rendered).toContain('<li>x</li>')
  })

  it('preserves legacy <cited-text> elements inside <fact_source>', () => {
    const rendered = renderContent(
      '<fact id="c0">$5M<fact_source label="report.pdf"><cited-text>quoted source text</cited-text></fact_source></fact>',
    )
    expect(rendered).toContain('<cited-text>')
    expect(rendered).toContain('quoted source text')
  })

  it('strips dangerous onerror handlers from inline HTML', () => {
    const rendered = renderContent('<img src="x" onerror="alert(1)">')
    expect(rendered).not.toContain('onerror')
    expect(rendered).not.toContain('alert(1)')
  })

  it('strips javascript: URLs from anchors', () => {
    const rendered = renderContent('<a href="javascript:alert(1)">click</a>')
    expect(rendered).not.toContain('javascript:')
  })

  it('handles content that mixes markdown and inline HTML', () => {
    const rendered = renderContent(
      '# Title\n\nSome **bold**.\n\n<h2>Subtitle</h2>\n\nMore text.',
    )
    expect(rendered).toContain('<h1>Title</h1>')
    expect(rendered).toContain('<strong>bold</strong>')
    expect(rendered).toContain('<h2>Subtitle</h2>')
    expect(rendered).toContain('More text')
  })

  it('treats single ~ as a literal "approximately" symbol, not strikethrough', () => {
    const rendered = renderContent('about ~x~ here')
    expect(rendered).not.toContain('<del>')
    expect(rendered).toContain('~x~')
  })

  it('treats ~~ as literal text, not strikethrough', () => {
    const rendered = renderContent('a ~~struck~~ b')
    expect(rendered).not.toContain('<del>')
    expect(rendered).toContain('~~struck~~')
  })

  it('keeps ~-prefixed numbers next to <fact> citations literal (report Market Position regression)', () => {
    const rendered = renderContent(
      'In the Permian Basin, Coterra ranked ~<fact id="c0">250</fact> MBoe/d and ~<fact id="c1">4%</fact> basin share; the top five operators — ExxonMobil (~<fact id="c2">1.5</fact> MMBoe/d, ~<fact id="c3">15%</fact> share) — control over <fact id="c4">60%</fact>.',
    )
    expect(rendered).not.toContain('<del>')
    expect(rendered).toContain('~<fact id="c0">250</fact>')
    expect(rendered).toContain('ExxonMobil (~<fact id="c2">1.5</fact>')
  })

  it('keeps the checkboxes of a Markdown task list', () => {
    const rendered = renderContent('- [ ] open\n- [x] done')

    expect(rendered).toContain('<input disabled="" type="checkbox"> open')
    expect(rendered).toContain('<input checked="" disabled="" type="checkbox"> done')
  })
})

const renderInto = (html: string) => {
  const container = document.createElement('div')
  container.innerHTML = html
  return container
}

describe('sanitizeHtml', () => {
  it('drops style blocks and form controls that could restyle the app or submit input', () => {
    const container = renderInto(
      sanitizeHtml('Reply text<style>body{display:none}</style><form action="https://example.com"><textarea name="password"></textarea><button>Send</button></form>'),
    )

    expect(container.querySelector('style, form, textarea, button')).toBeNull()
    expect(container.textContent).not.toContain('display:none')
    expect(container.textContent).toMatch(/^Reply text/)
  })

  it('ignores a stray closing tag the way a browser does', () => {
    const container = renderInto(sanitizeHtml('<table><tbody><tr><th>Issue Type</t\nh></tr></tbody></table>'))

    expect(container.querySelector('th')!.textContent).toBe('Issue Type')
  })

  it('keeps the checkboxes renderContent writes for a Markdown task list', () => {
    expect(sanitizeHtml(renderContent('- [ ] open'))).toContain('<input disabled="" type="checkbox"> open')
  })

  it('keeps the markup a report legitimately uses', () => {
    const html = '<h2>Title</h2><p><strong>Bold</strong> <span class="field-difference" title="t">x</span></p><table><tbody><tr><td>1</td></tr></tbody></table>'

    expect(sanitizeHtml(html)).toBe(html)
  })
})

describe('sanitizeModelHtml', () => {
  it('shows style blocks and form controls as text instead of rendering them', () => {
    const container = renderInto(
      sanitizeModelHtml('Reply text<style>body{display:none}</style><form action="https://example.com"><input name="password"><button>Send</button></form>'),
    )

    expect(container.querySelector('style, form, input, button')).toBeNull()
    expect(container.textContent).toContain('<button>Send</button>')
  })

  it('keeps the text of a tag the app does not know, even one named like an SVG element', () => {
    const container = renderInto(sanitizeModelHtml('<stop>OK</stop>\nThe document has been loaded.'))

    expect(container.textContent).toBe('<stop>OK</stop>\nThe document has been loaded.')
  })

  it('keeps the markup a model reply legitimately uses', () => {
    const html = '<p><strong>Bold</strong> <span class="field-difference" title="t">x</span></p><fact id="c0">7%</fact>'

    expect(sanitizeModelHtml(html)).toBe(html)
  })
})
