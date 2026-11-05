import { describe, expect, it, vi } from 'vitest'
import { mount } from '@vue/test-utils'
import { defineComponent, h } from 'vue'

import SortableHeader from './SortableHeader.vue'

const mountInRow = (props: Record<string, unknown>) =>
  mount(
    defineComponent({
      setup: () => () =>
        h('table', [h('thead', [h('tr', [h(SortableHeader, props, () => 'Fund')])])]),
    }),
    { attachTo: document.body },
  )

describe('SortableHeader', () => {
  it.each([
    [undefined, 'none'],
    ['asc', 'ascending'],
    ['desc', 'descending'],
  ])('announces direction %s as aria-sort %s on the column header', (direction, ariaSort) => {
    const wrapper = mountInRow({ direction })

    expect(wrapper.get('th').attributes('aria-sort')).toBe(ariaSort)
    wrapper.unmount()
  })

  it('sorts from a native button that takes keyboard focus and shows the shared focus ring', async () => {
    const onSort = vi.fn()
    const wrapper = mountInRow({ direction: 'asc', onSort })

    const button = wrapper.get('th button')
    expect(button.attributes('type')).toBe('button')
    expect(button.classes()).toContain('focus-ring')
    expect(button.text()).toBe('Fund')
    ;(button.element as HTMLButtonElement).focus()
    expect(document.activeElement).toBe(button.element)

    await button.trigger('click')

    expect(onSort).toHaveBeenCalledTimes(1)
    wrapper.unmount()
  })

  it('shows one arrow for the sorted direction and none when unsorted', () => {
    const sorted = mountInRow({ direction: 'desc' })
    const unsorted = mountInRow({})

    expect(sorted.findAll('th svg')).toHaveLength(1)
    expect(unsorted.findAll('th svg')).toHaveLength(0)
    sorted.unmount()
    unsorted.unmount()
  })

  it('renders as the header cell it is given', () => {
    const Cell = defineComponent({ setup: (_, { slots }) => () => h('th', { class: 'shared-cell' }, slots.default?.()) })
    const wrapper = mountInRow({ as: Cell, direction: 'asc' })

    expect(wrapper.get('th').classes()).toContain('shared-cell')
    expect(wrapper.get('th').attributes('aria-sort')).toBe('ascending')
    wrapper.unmount()
  })
})
