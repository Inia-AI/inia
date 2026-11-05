import { readFileSync } from 'node:fs'
import { join } from 'node:path'
import { describe, expect, it } from 'vitest'

const PROJECT_ROOT = join(__dirname, '..')
const PUBLIC_FOLDER = join(PROJECT_ROOT, 'public')
const PNG_COLOUR_TYPE_RGB = 2
const ANALYST_TILE_NAVY = '#1A3850'

const head = new DOMParser().parseFromString(
  readFileSync(join(PROJECT_ROOT, 'index.html'), 'utf8'),
  'text/html',
).head

function hrefOf(selector: string) {
  return head.querySelector(selector)?.getAttribute('href')
}

function publicFile(href: string) {
  return readFileSync(join(PUBLIC_FOLDER, href))
}

describe('browser-tab icons', () => {
  it('links an SVG icon, a 32 px ICO fallback and a 180 px touch icon', () => {
    expect(hrefOf('link[rel="icon"][type="image/svg+xml"]')).toBe('/icon.svg')
    expect(hrefOf('link[rel="icon"][sizes="32x32"]')).toBe('/favicon.ico')
    expect(hrefOf('link[rel="apple-touch-icon"]')).toBe('/apple-touch-icon.png')
    expect(
      head.querySelectorAll('link[rel="icon"], link[rel="apple-touch-icon"]'),
    ).toHaveLength(3)
  })

  it('draws the SVG icon on the navy tile that sets it apart from the copper editor tile', () => {
    const svg = new DOMParser().parseFromString(
      publicFile('/icon.svg').toString('utf8'),
      'image/svg+xml',
    ).documentElement

    expect(svg.tagName).toBe('svg')
    expect(svg.getAttribute('viewBox')).toBe('0 0 64 64')
    expect(svg.querySelector('rect')?.getAttribute('fill')).toBe(
      ANALYST_TILE_NAVY,
    )
  })

  it('holds one 32 px image in the ICO fallback', () => {
    const ico = publicFile('/favicon.ico')

    expect(ico.readUInt16LE(2)).toBe(1)
    expect(ico.readUInt16LE(4)).toBe(1)
    expect([ico[6], ico[7]]).toEqual([32, 32])
  })

  it('makes the touch icon an opaque 180 px square', () => {
    const png = publicFile('/apple-touch-icon.png')

    expect([png.readUInt32BE(16), png.readUInt32BE(20)]).toEqual([180, 180])
    expect(png[25]).toBe(PNG_COLOUR_TYPE_RGB)
  })
})
