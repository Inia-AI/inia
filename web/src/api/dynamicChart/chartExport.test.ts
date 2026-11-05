import { afterAll, beforeAll, describe, it, expect, vi } from 'vitest'
import { standInCanvas } from '@/test/helpers/canvas'
import { drawForExport, exportOptions, NO_DATA_MESSAGE, UNSUPPORTED_TYPE_MESSAGE, type ExportStyle } from './chartExport'
import type { ChartConfig } from './schema'

const style: ExportStyle = { fontFamily: 'Calibri, Arial, sans-serif', titleFontSize: 15, axisTitleFontSize: 12, labelFontSize: 11 }

const lineChart = {
  type: 'line',
  title: 'Revenue',
  data: [],
  xAxis: { field: 'x', label: 'Year' },
  yAxis: { field: 'y', label: 'Revenue' },
  series: [
    { name: 'Revenue', data: [{ x: '2021', y: 10 }, { x: '2022', y: 12 }] },
    { name: 'Costs', data: [{ x: '2021', y: 3 }, { x: '2022', y: 4 }] }
  ]
} as ChartConfig

type AnyOptions = Record<string, any>

describe('exportOptions', () => {
  it('draws on a white background with no animation', () => {
    const options = exportOptions(lineChart, style) as AnyOptions
    expect(options.backgroundColor).toBe('#ffffff')
    expect(options.animation).toBe(false)
  })

  it('uses the export fonts for the title, the axes and the legend', () => {
    const options = exportOptions(lineChart, style) as AnyOptions
    expect(options.textStyle.fontFamily).toBe(style.fontFamily)
    expect(options.title.textStyle.fontSize).toBe(15)
    expect(options.title.text).toBe('Revenue')
    expect(options.xAxis.axisLabel.fontSize).toBe(11)
    expect(options.yAxis.nameTextStyle.fontSize).toBe(12)
    expect(options.yAxis.axisLabel.formatter(1000)).toBe('1,000')
    expect(options.legend.textStyle.fontSize).toBe(11)
  })

  it('keeps the series the screen draws', () => {
    const options = exportOptions(lineChart, style) as AnyOptions
    expect(options.series.map((serie: AnyOptions) => serie.name)).toEqual(['Revenue', 'Costs'])
  })

  it('sizes the slice labels of a pie chart', () => {
    const options = exportOptions({ ...lineChart, type: 'pie', series: undefined, data: [{ x: 'A', y: 1 }] }, style) as AnyOptions
    expect(options.series[0].label.fontSize).toBe(11)
    expect(options.series[0].label.formatter({ name: 'A', percent: 100 })).toBe('A: 100.0%')
  })

  it('refuses a chart type the app does not draw, as the report page does, so the export puts in a table instead', () => {
    expect(() => exportOptions({ ...lineChart, type: 'gauge' as ChartConfig['type'] }, style)).toThrow(UNSUPPORTED_TYPE_MESSAGE)
  })

  it('refuses a chart with no data, so the export puts in a table instead', () => {
    expect(() => exportOptions({ ...lineChart, series: [] }, style)).toThrow(NO_DATA_MESSAGE)
    expect(() => exportOptions({ ...lineChart, type: 'bubble', series: undefined, data: [] }, style)).toThrow(NO_DATA_MESSAGE)
  })
})

describe('drawForExport', () => {
  beforeAll(() => {
    standInCanvas()
  })

  afterAll(() => {
    vi.restoreAllMocks()
  })

  it('settles once the chart is drawn', async () => {
    await expect(drawForExport(document.createElement('div'), lineChart, style)).resolves.toBeUndefined()
  })

  it('fails for a chart with no data, which the page reports to the export', async () => {
    await expect(drawForExport(document.createElement('div'), { ...lineChart, series: [] }, style)).rejects.toThrow(NO_DATA_MESSAGE)
  })
})
