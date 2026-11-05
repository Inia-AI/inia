import { mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { afterAll, afterEach, beforeAll, beforeEach, describe, expect, it, vi } from 'vitest'
import { getInstanceByDom } from 'echarts/core'

import i18n from '@/plugins/i18n/i18n'
import { standInCanvas } from '@/test/helpers/canvas'
import type { ChartOptions } from '@/lib/echarts'
import BaseChart from './BaseChart.vue'

const options: ChartOptions = {
  xAxis: { type: 'category', data: ['2024', '2025'] },
  yAxis: { type: 'value' },
  series: [{ type: 'line', name: 'Revenue', data: [1, 2] }]
}

type DrawnOption = { backgroundColor?: unknown; toolbox?: { show?: boolean; feature?: { saveAsImage?: { title?: string; name?: string } } }[] }

function drawn(props: Record<string, unknown>): { option: DrawnOption; element: Element; unmount: () => void } {
  const wrapper = mount(BaseChart, { props: { options, ...props }, attachTo: document.body, global: { plugins: [i18n] } })
  const chart = getInstanceByDom(wrapper.element as HTMLElement)
  return { option: chart?.getOption() as DrawnOption, element: wrapper.element, unmount: () => wrapper.unmount() }
}

beforeAll(() => {
  standInCanvas()
})

afterAll(() => {
  vi.restoreAllMocks()
})

beforeEach(() => {
  localStorage.setItem('theme', 'dark')
  setActivePinia(createPinia())
})

afterEach(() => {
  localStorage.clear()
  document.documentElement.classList.remove('dark')
})

describe('BaseChart', () => {
  it('draws on the page background, so the chart follows the theme', () => {
    const { option, unmount } = drawn({})
    expect(option.backgroundColor).toBe('transparent')
    unmount()
  })

  it('draws on white in the dark theme too when the chart is on paper', () => {
    const { option, unmount } = drawn({ onPaper: true })
    expect(option.backgroundColor).toBe('#ffffff')
    unmount()
  })

  it('offers Save as image, named after the chart, only when it has an image name', () => {
    const withName = drawn({ imageName: 'Revenue by year' })
    const saveAsImage = withName.option.toolbox?.[0]?.feature?.saveAsImage
    expect(saveAsImage?.title).toBe('Save as image')
    expect(saveAsImage?.name).toBe('Revenue by year')
    withName.unmount()

    const withoutName = drawn({})
    expect(withoutName.option.toolbox?.[0]?.show).toBe(false)
    withoutName.unmount()
  })

  it('reports where in the data a click in the plot lands', () => {
    const wrapper = mount(BaseChart, { props: { options: { ...options, xAxis: { type: 'value' } } }, attachTo: document.body, global: { plugins: [i18n] } })
    const chart = getInstanceByDom(wrapper.element as HTMLElement)!
    const [offsetX, offsetY] = chart.convertToPixel({ gridIndex: 0 }, [0.5, 1.5]) as number[]
    chart.getZr().trigger('click', { offsetX, offsetY })
    const [x, y] = (wrapper.emitted('plotClick')?.[0] ?? []) as number[]
    expect(x).toBeCloseTo(0.5)
    expect(y).toBeCloseTo(1.5)
    wrapper.unmount()
  })

  it('names the chart picture for a screen reader', () => {
    const { element, unmount } = drawn({ label: 'Match rate per run' })
    expect(element.getAttribute('role')).toBe('img')
    expect(element.getAttribute('aria-label')).toBe('Match rate per run')
    unmount()
  })

  it('names an unlabelled chart by its image name, and an untitled one as a chart', () => {
    const titled = drawn({ imageName: 'Revenue by year' })
    expect(titled.element.getAttribute('aria-label')).toBe('Revenue by year')
    titled.unmount()

    const untitled = drawn({ imageName: '' })
    expect(untitled.element.getAttribute('aria-label')).toBe('Chart')
    untitled.unmount()
  })
})
