import { describe, expect, it } from 'vitest'

import { markupProblems, parseMarkup } from '@/test/helpers/inertMarkup'

import { renderContent, sanitizeHtml, sanitizeModelHtml } from './renderContent'

// Model output reaches the page through these three functions. Each construct below must come out
// as inert content: no handler attribute, no script-bearing address, no element that runs, loads
// or submits. The checks read the parsed DOM, not the string.

const RENDERERS: [string, (raw: string) => string][] = [
  ['renderContent', renderContent],
  ['sanitizeHtml', sanitizeHtml],
  ['sanitizeModelHtml', sanitizeModelHtml],
]

const CONSTRUCTS: [string, string][] = [
  ['script', '<script>alert(1)</script>'],
  ['img onerror', '<img src=x onerror=alert(1)>'],
  ['img mixed-case onerror', '<img src="x" OnErRoR="alert(1)">'],
  ['a javascript', '<a href="javascript:alert(1)">x</a>'],
  ['a mixed-case javascript', '<a href="JaVaScRiPt:alert(1)">x</a>'],
  ['a entity javascript', '<a href="&#106;avascript:alert(1)">x</a>'],
  ['a data html', '<a href="data:text/html,<script>alert(1)</script>">x</a>'],
  ['iframe srcdoc', '<iframe srcdoc="<script>alert(1)</script>"></iframe>'],
  ['svg onload', '<svg onload=alert(1)>'],
  ['svg xlink javascript', '<svg><a xlink:href="javascript:alert(1)"><text>x</text></a></svg>'],
  ['math script', '<math><mtext><script>alert(1)</script></mtext></math>'],
  ['details ontoggle', '<details open ontoggle=alert(1)>'],
  ['div onpointerenter', '<div onpointerenter=alert(1)>x</div>'],
  ['object data', '<object data="javascript:alert(1)">'],
  ['embed src', '<embed src="javascript:alert(1)">'],
  ['base', '<base href="https://example.invalid/">'],
  ['meta refresh', '<meta http-equiv="refresh" content="0;url=https://example.invalid/">'],
  ['link stylesheet', '<link rel="stylesheet" href="https://example.invalid/x.css">'],
  ['style url', '<div style="background:url(https://example.invalid/x)">x</div>'],
  ['style escaped url', '<div style="background:\\75 rl(https://example.invalid/x)">x</div>'],
  ['style image-set', "<div style=\"background-image:image-set('https://example.invalid/x' 1x)\">x</div>"],
  ['a ping', '<a href="https://example.invalid/" ping="https://example.invalid/p">x</a>'],
  ['form', '<form action="https://example.invalid/"><input name=a></form>'],
  ['fact onmouseover', '<fact label="x" onmouseover="alert(1)">y</fact>'],
  ['cited-text page-id breakout', '<cited-text type="x" page-id="&quot;><img src=x onerror=alert(1)>">y</cited-text>'],
  ['markdown javascript link', '[x](javascript:alert(1))'],
  ['markdown angle javascript link', '[x](<javascript:alert(1)>)'],
  ['img data svg', '<img src="data:image/svg+xml,<svg onload=alert(1)>">'],
]

// Rows that load from another host when shown. Whether the browser fetches them is the Content
// Security Policy's decision, so these rows record what survives instead of failing.
const REMOTE_LOADS: [string, string][] = [
  ['markdown remote image', '![x](https://example.invalid/p?d=QUOKKA)'],
  ['img remote', '<img src="https://example.invalid/p?d=QUOKKA">'],
  ['img srcset remote', '<img srcset="https://example.invalid/p 1x">'],
]

const problemsOf = (html: string): string[] => markupProblems(parseMarkup(html))

describe('renderContent security', () => {
  describe.each(RENDERERS)('%s', (_, render) => {
    it.each(CONSTRUCTS)('renders %s as inert content', (_, input) => {
      expect(problemsOf(render(input))).toEqual([])
    })

    it('keeps a plain paragraph, a link to an https page and an image of the page itself', () => {
      const elements = parseMarkup(render('<p>Net assets are 1,234,567.</p><a href="https://example.com/method">method</a><img src="/chart.png">'))

      expect(elements.find(element => element.localName === 'p')?.textContent).toBe('Net assets are 1,234,567.')
      expect(elements.find(element => element.localName === 'a')?.getAttribute('href')).toBe('https://example.com/method')
      expect(elements.find(element => element.localName === 'img')?.getAttribute('src')).toBe('/chart.png')
    })
  })

  it.each(RENDERERS)('%s keeps a style that only lays out, as written', (_, render) => {
    const cell = '<table><tbody><tr><td style="padding:10px 15px;text-align:right;">147,937</td></tr></tbody></table>'

    expect(render(cell)).toContain('<td style="padding:10px 15px;text-align:right;">147,937</td>')
  })

  it.each(CONSTRUCTS)('sanitizeHtml gives the same output for %s when it runs twice', (_, input) => {
    const once = sanitizeHtml(input)
    expect(sanitizeHtml(once)).toBe(once)
  })

  it('records what each remote load leaves, for the Content Security Policy to decide', () => {
    const left = REMOTE_LOADS.flatMap(([name, input]) =>
      RENDERERS.map(([renderer, render]) => {
        const loads = parseMarkup(render(input))
          .filter(element => element.localName === 'img')
          .map(element => `src=${element.getAttribute('src') ?? ''} srcset=${element.getAttribute('srcset') ?? ''}`)
        return `${name} through ${renderer}: ${loads.length ? loads.join('; ') : 'no image'}`
      }),
    )
    console.info(left.join('\n'))
  })

  it('records what happens to invisible characters', () => {
    const invisible: [string, string][] = [
      ['U+E0041 tag letter A', String.fromCodePoint(0xe0041)],
      ['U+200B zero-width space', '\u200B'],
      ['U+FE0F variation selector 16', '\uFE0F'],
    ]
    const input = `<p>Net${invisible[0][1]}assets${invisible[1][1]}of${invisible[2][1]}1,234,567</p>`
    const kept = invisible.flatMap(([name, character]) =>
      RENDERERS.map(([renderer, render]) => `${name} through ${renderer}: ${render(input).includes(character) ? 'kept' : 'removed'}`),
    )
    console.info(kept.join('\n'))
  })

  it('records the target and rel a link keeps, and what an id or a name attribute becomes', () => {
    const inputs = [
      '<a href="https://example.invalid/" target="_blank">x</a>',
      '<a href="https://example.invalid/" target="_blank" rel="opener">x</a>',
      '<a id="report" name="main" href="https://example.invalid/">x</a>',
      '<img name="getElementById" src="/x.png">',
      '<span id="location">x</span>',
    ]
    const left = inputs.flatMap(input =>
      RENDERERS.map(([renderer, render]) => {
        const kept = parseMarkup(render(input)).map(element => `<${element.localName} ${[...element.attributes].map(attribute => `${attribute.name}="${attribute.value}"`).join(' ')}>`)
        return `${input} through ${renderer}: ${kept.join(' ')}`
      }),
    )
    console.info(left.join('\n'))
  })
})
