import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { mount } from '@vue/test-utils'
import { createI18n } from 'vue-i18n'

import { EN } from '@/assets/locales/en'
import type { WorkflowActivityView } from '@/lib/workflowActivity'
import WorkflowActivityLine from './WorkflowActivityLine.vue'

const view = (overrides: Partial<WorkflowActivityView> = {}): WorkflowActivityView => ({
  activeLabels: [],
  doneLabels: [],
  doneCount: 0,
  stepCount: 15,
  ...overrides,
})

const mountLine = (entry: WorkflowActivityView, watchedFromStart = false) =>
  mount(WorkflowActivityLine, {
    props: { entry, watchedFromStart },
    global: {
      plugins: [
        createI18n({
          legacy: false,
          locale: 'en',
          messages: { en: EN },
          missingWarn: false,
          fallbackWarn: false,
        }),
      ],
    },
    attachTo: document.body,
  })

const lineTexts = (wrapper: ReturnType<typeof mountLine>) =>
  wrapper.findAll('.frontier-line').map(line => line.text())

describe('WorkflowActivityLine', () => {
  beforeEach(() => {
    vi.useFakeTimers()
  })

  afterEach(() => {
    vi.useRealTimers()
  })

  it('speaks one line per step underway with how much of the run is done', () => {
    const wrapper = mountLine(
      view({
        activeLabels: ['Extracting income statement line items', 'Extracting balance sheet line items'],
        doneCount: 7,
      }),
    )

    expect(lineTexts(wrapper)).toEqual([
      'Extracting income statement line items',
      'Extracting balance sheet line items',
    ])
    expect(wrapper.text()).toContain('7 of 15 done')
  })

  it('inserts a newly started step at the top without re-sorting the rest', async () => {
    const wrapper = mountLine(view({ activeLabels: ['First work', 'Second work'] }))

    await wrapper.setProps({
      entry: view({ activeLabels: ['First work', 'Second work', 'Newest work'] }),
    })

    expect(lineTexts(wrapper)).toEqual(['Newest work', 'First work', 'Second work'])
  })

  it('shows a step that opened and closed inside the poll gap beneath the live work', async () => {
    const wrapper = mountLine(view({ activeLabels: ['Slow straggler'] }))

    await wrapper.setProps({
      entry: view({
        activeLabels: ['Slow straggler'],
        doneLabels: ['Blink-and-miss extraction'],
        doneCount: 1,
      }),
    })

    const lines = wrapper.findAll('.frontier-line')
    expect(lineTexts(wrapper)).toEqual(['Slow straggler', 'Blink-and-miss extraction'])
    expect(lines[0].classes()).not.toContain('is-done')
    expect(lines[1].classes()).toContain('is-done')
  })

  it('drops a completed top line beneath the remaining live work', async () => {
    const wrapper = mountLine(view({ activeLabels: ['Older work'] }))
    await wrapper.setProps({
      entry: view({ activeLabels: ['Older work', 'Newest work'] }),
    })
    expect(lineTexts(wrapper)).toEqual(['Newest work', 'Older work'])

    await wrapper.setProps({
      entry: view({
        activeLabels: ['Older work'],
        doneLabels: ['Newest work'],
        doneCount: 1,
      }),
    })

    expect(lineTexts(wrapper)).toEqual(['Older work', 'Newest work'])
    expect(wrapper.findAll('.frontier-line')[1].classes()).toContain('is-done')
  })

  it('guarantees the live work its room when a cascade fills the passed zone', async () => {
    const wrapper = mountLine(view({ activeLabels: ['First active', 'Second active'] }))

    await wrapper.setProps({
      entry: view({
        activeLabels: ['First active', 'Second active'],
        doneLabels: ['Passed one', 'Passed two', 'Passed three', 'Passed four'],
        doneCount: 4,
      }),
    })

    const texts = lineTexts(wrapper)
    expect(texts).toHaveLength(3)
    expect(texts.slice(0, 2)).toEqual(['First active', 'Second active'])
    expect(['Passed one', 'Passed two', 'Passed three', 'Passed four']).toContain(texts[2])
    expect(wrapper.find('.frontier-toggle').text()).toContain('+3 more')
  })

  it('keeps a settled bottom line beneath the live work', async () => {
    const wrapper = mountLine(view({ activeLabels: ['Second work', 'First work'] }))

    await wrapper.setProps({
      entry: view({
        activeLabels: ['Second work'],
        doneLabels: ['First work'],
        doneCount: 1,
      }),
    })

    expect(lineTexts(wrapper)).toEqual(['Second work', 'First work'])
    expect(wrapper.findAll('.frontier-line')[1].classes()).toContain('is-done')
  })

  it('lets a passed line linger briefly and then leave', async () => {
    const wrapper = mountLine(view({ activeLabels: ['Slow straggler'] }))

    await wrapper.setProps({
      entry: view({
        activeLabels: ['Slow straggler'],
        doneLabels: ['Blink-and-miss extraction'],
        doneCount: 1,
      }),
    })
    expect(lineTexts(wrapper)).toContain('Blink-and-miss extraction')

    await vi.advanceTimersByTimeAsync(20_000)
    await wrapper.setProps({
      entry: view({
        activeLabels: ['Slow straggler'],
        doneLabels: ['Blink-and-miss extraction'],
        doneCount: 1,
      }),
    })

    expect(lineTexts(wrapper)).toEqual(['Slow straggler'])
  })

  it('gives an instant first step its moment when the run was watched from birth', () => {
    const wrapper = mountLine(
      view({
        activeLabels: ['Loading and verifying financial documents'],
        doneLabels: ['Calculating prior year dates for comparison'],
        doneCount: 1,
      }),
      true,
    )

    expect(lineTexts(wrapper)).toEqual([
      'Loading and verifying financial documents',
      'Calculating prior year dates for comparison',
    ])
    expect(wrapper.findAll('.frontier-line')[1].classes()).toContain('is-done')
  })

  it('never invents history for steps already done at first sight', () => {
    const wrapper = mountLine(
      view({
        activeLabels: ['Current work'],
        doneLabels: ['Long finished work'],
        doneCount: 1,
      }),
    )

    expect(lineTexts(wrapper)).toEqual(['Current work'])
  })

  it('collapses past three lines into an expandable count hiding the oldest', async () => {
    const wrapper = mountLine(view({ activeLabels: ['One', 'Two', 'Three'] }))
    await wrapper.setProps({
      entry: view({ activeLabels: ['One', 'Two', 'Three', 'Four', 'Five'] }),
    })

    expect(lineTexts(wrapper)).toEqual(['Four', 'Five', 'One'])
    const more = wrapper.find('.frontier-toggle')
    expect(more.text()).toContain('+2 more')

    await more.trigger('click')
    expect(lineTexts(wrapper)).toEqual(['Four', 'Five', 'One', 'Two', 'Three'])
    expect(wrapper.find('.frontier-toggle').text()).toContain('Show fewer')

    await wrapper.find('.frontier-toggle').trigger('click')
    expect(wrapper.findAll('.frontier-line')).toHaveLength(3)
  })
})
