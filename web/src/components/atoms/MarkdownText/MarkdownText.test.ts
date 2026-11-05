import { describe, expect, it } from 'vitest'
import { mount } from '@vue/test-utils'
import MarkdownText from './MarkdownText.vue'

describe('MarkdownText', () => {
  it('renders bold markdown as <strong>, not literal asterisks', () => {
    const wrapper = mount(MarkdownText, {
      props: { content: '**Portfolio Summary:** diversified' },
    })
    expect(wrapper.find('strong').exists()).toBe(true)
    expect(wrapper.find('strong').text()).toBe('Portfolio Summary:')
    expect(wrapper.text()).not.toContain('**')
  })

  it('renders list markdown as <li> items', () => {
    const wrapper = mount(MarkdownText, {
      props: { content: '- Overexposed to tech\n- Concentrated geography' },
    })
    const items = wrapper.findAll('li')
    expect(items).toHaveLength(2)
    expect(items[0].text()).toBe('Overexposed to tech')
  })

  it('renders nothing meaningful for null content', () => {
    const wrapper = mount(MarkdownText, { props: { content: null } })
    expect(wrapper.text()).toBe('')
  })
})
