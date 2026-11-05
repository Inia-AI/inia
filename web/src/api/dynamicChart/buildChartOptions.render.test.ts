import { afterAll, beforeAll, describe, it, expect, vi } from 'vitest'
import { use } from 'echarts/core'
import { SVGRenderer } from 'echarts/renderers'
import { init } from '@/lib/echarts'
import { standInCanvas } from '@/test/helpers/canvas'
import { buildChartOptions } from './buildChartOptions'
import { ColorSchemes } from './colorSchemes'
import type { ChartConfig } from './schema'

use(SVGRenderer)

beforeAll(() => {
  standInCanvas()
})

afterAll(() => {
  vi.restoreAllMocks()
})

function render(config: ChartConfig): string {
  const chart = init(null, null, { renderer: 'svg', ssr: true, width: 640, height: 400 })
  chart.setOption(buildChartOptions(config))
  const svg = chart.renderToSVGString()
  chart.dispose()
  return svg
}

const axes = { xAxis: { field: 'x', label: 'Year' }, yAxis: { field: 'y', label: 'Revenue' } }
const series = [
  { name: 'Revenue', data: [{ x: '2021', y: 10 }, { x: '2022', y: 11 }, { x: '2023', y: 12 }] },
  { name: 'Costs', data: [{ x: '2021', y: 3 }, { x: '2022', y: 4 }, { x: '2023', y: 5 }] }
]
const points = [
  { x: 1, y: 2, series: 'North' },
  { x: 3, y: 4, series: 'South' }
]
const slices = [
  { x: 'Equity', y: 30 },
  { x: 'Debt', y: 70 }
]

const colours = ColorSchemes.default.slice(0, 2).map(colour => colour.toLowerCase())

describe('every stored chart type draws through ECharts', () => {
  it.each([
    ['line', { series }],
    ['area', { series }],
    ['bar', { series }],
    ['column', { series }],
    ['stacked column', { series, options: { stacking: 'normal' as const } }],
    ['scatter', { data: points }]
  ])('draws the title and every series of a %s chart', (type, body) => {
    const svg = render({ type: type.replace('stacked ', '') as ChartConfig['type'], title: `My ${type} chart`, data: [], ...axes, ...body } as ChartConfig)

    expect(svg).toContain(`My ${type} chart`)
    const names = 'series' in body ? series.map(serie => serie.name) : ['North', 'South']
    for (const name of names) {
      expect(svg).toContain(name)
    }
    for (const colour of colours) {
      expect(svg.toLowerCase()).toContain(colour)
    }
  })

  it.each(['pie', 'donut'])('draws the title and every slice of a %s chart', type => {
    const svg = render({ type: type as ChartConfig['type'], title: `My ${type} chart`, data: slices, ...axes } as ChartConfig)

    expect(svg).toContain(`My ${type} chart`)
    expect(svg).toContain('Equity: 30.0%')
    expect(svg).toContain('Debt: 70.0%')
    for (const colour of colours) {
      expect(svg.toLowerCase()).toContain(colour)
    }
  })

  it('draws the title and every bubble of a bubble chart', () => {
    const svg = render({
      type: 'bubble',
      title: 'My bubble chart',
      data: [
        { x: 5, y: 5, size: 100 },
        { x: -5, y: -5, size: 10 }
      ],
      ...axes
    } as ChartConfig)

    expect(svg).toContain('My bubble chart')
    expect(svg).toContain('Bubble size')
    expect(svg.toLowerCase()).toContain('#059669')
    expect(svg.toLowerCase()).toContain('#dc2626')
  })
})
