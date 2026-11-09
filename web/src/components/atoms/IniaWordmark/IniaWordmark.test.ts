import { mount } from '@vue/test-utils'
import { describe, expect, it } from 'vitest'

import IniaWordmark from './IniaWordmark.vue'

const BRAND_COPPER = '#ad8977'

describe('IniaWordmark', () => {
  it('stays out of the accessibility tree when it has no label', () => {
    const svg = mount(IniaWordmark).get('svg')

    expect(svg.attributes('aria-hidden')).toBe('true')
    expect(svg.attributes('role')).toBeUndefined()
  })

  it('reads as an image named by its label', () => {
    const svg = mount(IniaWordmark, { props: { label: 'Inia AI logo' } }).get(
      'svg',
    )

    expect(svg.attributes('role')).toBe('img')
    expect(svg.attributes('aria-label')).toBe('Inia AI logo')
    expect(svg.attributes('aria-hidden')).toBeUndefined()
  })

  it('draws INIA.AI in the surrounding text colour with a copper dot', () => {
    const fills = mount(IniaWordmark)
      .findAll('path')
      .map(path => path.attributes('fill'))

    expect(fills).toContain('currentColor')
    expect(fills.filter(fill => fill !== 'currentColor')).toEqual([
      BRAND_COPPER,
    ])
  })
})
