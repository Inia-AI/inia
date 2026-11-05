import { describe, expect, it } from 'vitest'
import { mount } from '@vue/test-utils'
import HighlightedText from './HighlightedText.vue'

describe('HighlightedText', () => {
  it('renders plain text when query is empty', () => {
    const w = mount(HighlightedText, { props: { text: 'AB Brewhaus', query: '' } })
    expect(w.html()).not.toContain('<mark')
    expect(w.text()).toBe('AB Brewhaus')
  })

  it('wraps the matched substring in <mark> (case-insensitive)', () => {
    const w = mount(HighlightedText, { props: { text: 'AB Brewhaus', query: 'ab' } })
    expect(w.find('mark.search-mark').exists()).toBe(true)
    expect(w.find('mark.search-mark').text()).toBe('AB')
  })

  it('highlights every occurrence in the text', () => {
    const w = mount(HighlightedText, { props: { text: 'ababab', query: 'ab' } })
    expect(w.findAll('mark.search-mark')).toHaveLength(3)
  })

  it('escapes regex metacharacters in the query', () => {
    const w = mount(HighlightedText, {
      props: { text: 'price is $5 (today)', query: '$5' },
    })
    expect(w.find('mark.search-mark').text()).toBe('$5')
  })

  it('handles null/undefined text by rendering nothing', () => {
    const w = mount(HighlightedText, { props: { text: null, query: 'x' } })
    expect(w.text()).toBe('')
  })

  it('treats whitespace-only query as no highlight', () => {
    const w = mount(HighlightedText, { props: { text: 'AB', query: '   ' } })
    expect(w.find('mark.search-mark').exists()).toBe(false)
  })
})
