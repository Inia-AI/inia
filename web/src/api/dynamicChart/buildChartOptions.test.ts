import { describe, it, expect } from 'vitest'
import { buildChartOptions, chartHasData, chartTypeIsSupported } from './buildChartOptions'
import { ColorSchemes } from './colorSchemes'
import type { ChartConfig } from './schema'

function makeConfig(overrides: Partial<ChartConfig>): ChartConfig {
  return {
    type: 'line',
    title: 'Test Chart',
    data: [],
    xAxis: { field: 'x', label: 'Year' },
    yAxis: { field: 'y', label: 'Revenue' },
    ...overrides
  } as ChartConfig
}

const categorySeries = [
  { name: 'Revenue', data: [{ x: '2021', y: 10 }, { x: '2022', y: 0 }, { x: '2023', y: 12 }] },
  { name: 'EBITDA', data: [{ x: '2021', y: 3 }, { x: '2022', y: 4 }, { x: '2023', y: 5 }] }
]

type AnyOptions = Record<string, any>

function build(overrides: Partial<ChartConfig>): AnyOptions {
  return buildChartOptions(makeConfig(overrides)) as AnyOptions
}

describe('buildChartOptions chart type mapping', () => {
  it.each([
    ['line', 'line'],
    ['area', 'line'],
    ['bar', 'bar'],
    ['column', 'bar'],
    ['pie', 'pie'],
    ['donut', 'pie'],
    ['scatter', 'scatter'],
    ['bubble', 'scatter']
  ])('draws %s as %s series', (type, expected) => {
    const data = [{ x: 1, y: 2, size: 10 } as Record<string, unknown>]
    const options = build({ type: type as ChartConfig['type'], series: categorySeries, data })
    expect(options.series[0].type).toBe(expected)
  })

  it('reads the chart type in any letter case', () => {
    const bar = build({ type: 'BAR' as ChartConfig['type'], series: categorySeries })
    const donut = build({ type: 'Donut' as ChartConfig['type'], data: [{ x: 'A', y: 1 }] })
    expect(bar.yAxis.type).toBe('category')
    expect(donut.series[0].radius[0]).not.toBe('0%')
  })

  it('lays a bar chart on its side and a column chart upright', () => {
    const bar = build({ type: 'bar', series: categorySeries })
    const column = build({ type: 'column', series: categorySeries })
    expect(bar.yAxis.type).toBe('category')
    expect(bar.yAxis.inverse).toBe(true)
    expect(bar.xAxis.type).toBe('value')
    expect(column.xAxis.type).toBe('category')
    expect(column.yAxis.type).toBe('value')
  })
})

describe('line charts', () => {
  it('formats y-axis labels with the currency prefix', () => {
    const options = build({ type: 'line', series: categorySeries, metadata: { dataPoints: 3, currency: '$', unit: 'M' } })
    expect(options.yAxis.axisLabel.formatter(1234.5)).toBe('$1,234.5')
  })

  it('preserves zero values in series data', () => {
    const options = build({ type: 'line', series: categorySeries })
    expect(options.series[0].data).toEqual([10, 0, 12])
  })

  it('dashes EBITDA series and solid-lines the rest, with a thicker first line', () => {
    const options = build({ type: 'line', series: categorySeries })
    expect(options.series[0].lineStyle.width).toBe(3)
    expect(options.series[0].lineStyle.type).toBe('solid')
    expect(options.series[1].lineStyle.width).toBe(2)
    expect(options.series[1].lineStyle.type).toBe('dashed')
  })

  it('marks every point', () => {
    const options = build({ type: 'line', series: categorySeries })
    expect(options.series[0].showSymbol).toBe(true)
    expect(options.series[0].symbolSize).toBe(8)
  })

  it('hides the legend when there is a single series', () => {
    const options = build({ type: 'line', series: [categorySeries[0]] })
    expect(options.legend.show).toBe(false)
  })

  it('shows the legend under the chart when there are several series', () => {
    const options = build({ type: 'line', series: categorySeries })
    expect(options.legend.show).toBe(true)
    expect(options.legend.bottom).toBe(0)
  })

  it('turns the grid lines off when the block asks', () => {
    const shown = build({ type: 'line', series: categorySeries })
    const hidden = build({ type: 'line', series: categorySeries, options: { showGridLines: false } })
    expect(shown.yAxis.splitLine.show).toBe(true)
    expect(hidden.yAxis.splitLine.show).toBe(false)
    expect(hidden.xAxis.splitLine.show).toBe(false)
  })

  it('turns the animation off when the block asks', () => {
    expect(build({ type: 'line', series: categorySeries }).animation).toBe(true)
    expect(build({ type: 'line', series: categorySeries, options: { enableAnimations: false } }).animation).toBe(false)
  })

  it('shows every series of a category in one tooltip', () => {
    const options = build({ type: 'line', series: categorySeries })
    expect(options.tooltip.trigger).toBe('axis')
  })
})

describe('area charts', () => {
  it('draws every series as a filled area with the line chart axes and tooltip', () => {
    const options = build({ type: 'area', series: categorySeries })

    expect(options.series.every((serie: AnyOptions) => serie.areaStyle.opacity > 0)).toBe(true)
    expect(options.xAxis.data).toEqual(['2021', '2022', '2023'])
    expect(options.series.map((serie: AnyOptions) => serie.data)).toEqual([[10, 0, 12], [3, 4, 5]])
    expect(options.tooltip.trigger).toBe('axis')
  })

  it('keeps a line chart unfilled', () => {
    const options = build({ type: 'line', series: categorySeries })

    expect(options.series[0].areaStyle).toBeUndefined()
  })
})

describe('category data without series', () => {
  it('groups rows into one series per value of the group field', () => {
    const data = [
      { x: '2021', y: 1, series: 'A' },
      { x: '2021', y: 2, series: 'B' },
      { x: '2022', y: 3, series: 'A' }
    ]
    const options = build({ type: 'line', data })
    expect(options.xAxis.data).toEqual(['2021', '2022'])
    expect(options.series.map((serie: AnyOptions) => serie.name)).toEqual(['A', 'B'])
    expect(options.series[1].data).toEqual([2, 0])
  })

  it('groups rows by the series the block resolved when the group field is a source field', () => {
    const data = [
      { x: '2021', y: 1, series: 'A', rawData: { segment: 'A' } },
      { x: '2021', y: 2, series: 'B', rawData: { segment: 'B' } }
    ]
    const options = build({ type: 'line', data, options: { seriesGroupField: 'segment' } })
    expect(options.series.map((serie: AnyOptions) => serie.name)).toEqual(['A', 'B'])
  })

  it('reads the configured fields when the rows have no x', () => {
    const data = [{ year: '2022', revenue: 7 }, { year: '2021', revenue: 5 }]
    const options = build({ type: 'line', data, xAxis: { field: 'year' }, yAxis: { field: 'revenue' } })
    expect(options.xAxis.data).toEqual(['2021', '2022'])
    expect(options.series[0].data).toEqual([5, 7])
    expect(options.series[0].name).toBe('revenue')
  })
})

describe('colour schemes', () => {
  it('applies the selected scheme to series colours', () => {
    const options = build({ type: 'line', series: categorySeries, options: { colorScheme: 'professional' } })
    expect(options.series[0].itemStyle.color).toBe(ColorSchemes.professional[0])
    expect(options.series[0].lineStyle.color).toBe(ColorSchemes.professional[0])
  })

  it('keeps a colour the series sets itself', () => {
    const options = build({ type: 'line', series: [{ ...categorySeries[0], color: '#123456' }] })
    expect(options.series[0].itemStyle.color).toBe('#123456')
  })
})

describe('bar/column stacking', () => {
  it('stacks the series on one stack in normal mode', () => {
    const options = build({ type: 'column', series: categorySeries, options: { stacking: 'normal' } })
    expect(options.series.map((serie: AnyOptions) => serie.stack)).toEqual(['total', 'total'])
    expect(options.series[0].data).toEqual([10, 0, 12])
  })

  it('stacks each category to 100% in percent mode and keeps the values for the tooltip', () => {
    const options = build({ type: 'column', series: categorySeries, options: { stacking: 'percent' } })
    expect(options.series.map((serie: AnyOptions) => serie.stack)).toEqual(['total', 'total'])
    expect(options.series[0].data[0].value).toBeCloseTo((10 / 13) * 100)
    expect(options.series[0].data[0].raw).toBe(10)
    expect(options.series[1].data[1].value).toBeCloseTo(100)
    expect(options.yAxis.max).toBe(100)
  })

  it('shows the value, not the percentage, in a percent-stacked tooltip', () => {
    const options = build({ type: 'column', series: categorySeries, options: { stacking: 'percent' }, metadata: { dataPoints: 3, currency: '$' } })
    const text = options.tooltip.formatter({ name: '2021', seriesName: 'Revenue', marker: '', value: 76.9, data: options.series[0].data[0] })
    expect(text).toBe('2021<br/>Revenue: <b>$10</b>')
  })

  it('escapes the names a tooltip shows', () => {
    const series = [{ name: '<img src=x onerror=alert(1)>', data: [{ x: '2021', y: 1 }] }]
    const options = build({ type: 'column', series, options: { stacking: 'percent' } })
    const text = options.tooltip.formatter({ name: '2021', seriesName: series[0]!.name, marker: '', data: options.series[0].data[0] })
    expect(text).not.toContain('<img')
  })

  it('escapes the currency and unit a percent-stacked tooltip shows', () => {
    const metadata = { dataPoints: 3, currency: '<img src=x onerror=alert(1)>', unit: '<b>M</b>' }
    const options = build({ type: 'column', series: categorySeries, options: { stacking: 'percent' }, metadata })
    const text = options.tooltip.formatter({ name: '2021', seriesName: 'Revenue', marker: '', data: options.series[0].data[0] })
    expect(text).not.toContain('<img')
    expect(text).not.toContain('<b>M</b>')
  })

  it('treats "none" as no stacking', () => {
    const options = build({ type: 'column', series: categorySeries, options: { stacking: 'none' } })
    expect(options.series[0].stack).toBeUndefined()
  })

  it('draws no grid line on the category axis', () => {
    const options = build({ type: 'column', series: categorySeries })
    expect(options.xAxis.splitLine.show).toBe(false)
  })
})

describe('pie/donut charts', () => {
  const pieData = [
    { x: 'A', y: 30 },
    { x: 'B', y: 70 }
  ]

  it('uses a solid centre for pie and an inner hole for donut', () => {
    const pie = build({ type: 'pie', data: pieData })
    const donut = build({ type: 'donut', data: pieData })
    const [pieInner] = pie.series[0].radius
    const [donutInner, donutOuter] = donut.series[0].radius
    expect(pieInner).toBe('0%')
    expect(parseFloat(donutInner) / parseFloat(donutOuter)).toBeCloseTo(0.6)
  })

  it('colours by point and labels each slice with its percentage', () => {
    const options = build({ type: 'pie', data: pieData })
    expect(options.series[0].data.map((point: AnyOptions) => point.itemStyle.color)).toEqual(ColorSchemes.default.slice(0, 2))
    expect(options.series[0].label.show).toBe(true)
    expect(options.series[0].label.formatter({ name: 'A', percent: 30 })).toBe('A: 30.0%')
  })

  it('shows the value and the percentage in the tooltip', () => {
    const options = build({ type: 'pie', data: pieData })
    const text = options.tooltip.formatter({ name: 'A', value: 30, percent: 30 })
    expect(text).toContain('A')
    expect(text).toContain('Value: <b>30</b>')
    expect(text).toContain('Percentage: <b>30.0%</b>')
  })

  it('wraps a long slice label instead of cutting it short', () => {
    const options = build({ type: 'pie', data: pieData })
    expect(options.series[0].label.overflow).toBe('break')
  })

  it('leaves the slice names to the legend on a chart narrower than 500 pixels', () => {
    const options = build({ type: 'donut', data: pieData })
    const [narrow] = options.media
    expect(narrow.query.maxWidth).toBe(500)
    expect(narrow.option.series[0].label.show).toBe(false)
    expect(narrow.option.series[0].labelLine.show).toBe(false)
  })

  it('names each slice in the legend', () => {
    const options = build({ type: 'pie', data: pieData })
    expect(options.legend.show).toBe(true)
    expect(options.series[0].data.map((point: AnyOptions) => point.name)).toEqual(['A', 'B'])
  })
})

describe('scatter charts', () => {
  it('groups into multiple series by the series field', () => {
    const data = [
      { x: 1, y: 2, series: 'A' },
      { x: 3, y: 4, series: 'B' }
    ]
    const options = build({ type: 'scatter', data })
    expect(options.series).toHaveLength(2)
    expect(options.series[0].symbolSize).toBe(12)
    expect(options.series[1].data[0].value).toEqual([3, 4])
  })

  it('groups rows by the series the block resolved when the group field is a source field', () => {
    const data = [
      { x: '1.2', y: 3.4, series: 'North', rawData: { group: 'North' } },
      { x: '-1.4', y: 2.8, series: 'South', rawData: { group: 'South' } }
    ]
    const options = build({ type: 'scatter', data, options: { seriesGroupField: 'group' } })
    expect(options.series.map((serie: AnyOptions) => serie.name)).toEqual(['North', 'South'])
    expect(options.legend.show).toBe(true)
  })

  it('names each point and its axes in the tooltip', () => {
    const data = [{ x: 1, y: 2, name: 'Fund A' }]
    const options = build({ type: 'scatter', data })
    const text = options.tooltip.formatter({ seriesName: 'Revenue', data: options.series[0].data[0] })
    expect(text).toBe('<b>Revenue</b><br>Fund A<br/>Year: 1<br/>Revenue: 2')
  })
})

describe('bubble charts', () => {
  it('colours points by quadrant and sizes the bubbles', () => {
    const data = [
      { x: 5, y: 5, size: 100 },
      { x: -5, y: 5, size: 10 }
    ] as Record<string, unknown>[]
    const options = build({ type: 'bubble', data })
    const [first, second] = options.series[0].data
    expect(first.itemStyle.color).toBe('#059669')
    expect(second.itemStyle.color).toBe('#0891b2')
    expect(first.value[2]).toBe(100)
    expect(options.series[0].symbolSize(first.value)).toBe(80)
    expect(options.series[0].symbolSize(second.value)).toBe(20)
  })

  it('draws bubbles of one size at the middle of the size range, by area', () => {
    const data = [
      { x: 1, y: 1, size: 5 },
      { x: 2, y: 2, size: 5 }
    ] as Record<string, unknown>[]
    const options = build({ type: 'bubble', data })
    expect(options.series[0].symbolSize(options.series[0].data[0].value)).toBeCloseTo(Math.sqrt((20 ** 2 + 80 ** 2) / 2))
  })

  it('names the bubble size in the subtitle', () => {
    const options = build({ type: 'bubble', data: [{ x: 1, y: 1, size: 3 }] })
    expect(options.title.subtext).toBe('Bubble size: Assets Under Management (millions)')
  })

  it('renders a no-data subtitle when there are no points', () => {
    const options = build({ type: 'bubble', data: [] })
    expect(options.title.subtext).toBe('No data available')
  })
})

describe('smart decimal places', () => {
  it('uses two decimals for ratio/margin charts', () => {
    const options = build({ type: 'line', title: 'Net Margin', series: categorySeries })
    expect(options.tooltip.valueFormatter(10)).toBe('10.00')
  })

  it('uses whole numbers for integer data', () => {
    const options = build({ type: 'line', series: categorySeries })
    expect(options.tooltip.valueFormatter(10)).toBe('10')
  })

  it('detects fractional values even when they are negative', () => {
    const negativeFractional = [{ name: 'Net Change', data: [{ x: '2021', y: -0.5 }, { x: '2022', y: 2 }] }]
    const options = build({ type: 'line', title: 'Net Change', series: negativeFractional })
    expect(options.tooltip.valueFormatter(-0.5)).toBe('-0.50')
  })

  it('puts the currency before the value and the unit after it', () => {
    const options = build({ type: 'line', series: categorySeries, metadata: { dataPoints: 3, currency: '$', unit: 'M' } })
    expect(options.tooltip.valueFormatter(1234)).toBe('$1,234 M')
  })

  it('shows bar values as whole numbers', () => {
    const options = build({ type: 'column', title: 'Net Margin', series: categorySeries })
    expect(options.tooltip.valueFormatter(10.4)).toBe('10')
  })
})

describe('y-axis title', () => {
  it('omits the stray space when only a currency is present', () => {
    const options = build({ type: 'line', series: categorySeries, metadata: { dataPoints: 3, currency: '$' } })
    expect(options.yAxis.name).toBe('Revenue ($)')
  })

  it('omits the stray space when only a unit is present', () => {
    const options = build({ type: 'line', series: categorySeries, metadata: { dataPoints: 3, unit: 'M' } })
    expect(options.yAxis.name).toBe('Revenue (M)')
  })

  it('joins currency and unit when both are present', () => {
    const options = build({ type: 'line', series: categorySeries, metadata: { dataPoints: 3, currency: '$', unit: 'M' } })
    expect(options.yAxis.name).toBe('Revenue ($ M)')
  })

  it('names the x axis', () => {
    const options = build({ type: 'line', series: categorySeries })
    expect(options.xAxis.name).toBe('Year')
  })
})

describe('legend visibility honours showLegend across chart types', () => {
  it('hides the bar/column legend when showLegend is false', () => {
    const options = build({ type: 'column', series: categorySeries, options: { showLegend: false } })
    expect(options.legend.show).toBe(false)
  })

  it('hides the scatter legend when showLegend is false', () => {
    const data = [
      { x: 1, y: 2, series: 'A' },
      { x: 3, y: 4, series: 'B' }
    ]
    const options = build({ type: 'scatter', data, options: { showLegend: false } })
    expect(options.legend.show).toBe(false)
  })
})

describe('title', () => {
  it('centres the chart title', () => {
    const options = build({ type: 'line', title: 'Revenue', series: categorySeries })
    expect(options.title.text).toBe('Revenue')
    expect(options.title.left).toBe('center')
  })
})

describe('chartHasData', () => {
  it('finds data in rows or in series points', () => {
    expect(chartHasData(makeConfig({ data: [{ x: 1, y: 2 }] }))).toBe(true)
    expect(chartHasData(makeConfig({ series: categorySeries }))).toBe(true)
  })

  it('finds none when the rows and every series are empty', () => {
    expect(chartHasData(makeConfig({ data: [] }))).toBe(false)
    expect(chartHasData(makeConfig({ data: [], series: [{ name: 'A', data: [] }] }))).toBe(false)
  })
})

describe('rows as the block stores them', () => {
  const stored = (x: string, y: number, rawData: Record<string, unknown>, series = '') => ({ x, y, series, rawData })

  it('sizes, names and colours bubbles from the source fields the block keeps in rawData', () => {
    const data = [stored('5', 5, { fund: 'Fund A', size: 100 }), stored('-5', 5, { fund: 'Fund B', size: 10 })]
    const options = build({ type: 'bubble', data })
    const [first, second] = options.series[0].data
    expect(first.name).toBe('Fund A')
    expect(first.value[2]).toBe(100)
    expect(options.series[0].symbolSize(second.value)).toBe(20)
  })

  it('names and colours scatter points and pie slices from rawData', () => {
    const scatter = build({ type: 'scatter', data: [stored('1', 2, { name: 'Point X', color: '#123456' })] })
    expect(scatter.series[0].data[0].name).toBe('Point X')
    expect(scatter.series[0].data[0].itemStyle.color).toBe('#123456')

    const pie = build({ type: 'pie', data: [stored('Equity', 30, { color: '#654321' })] })
    expect(pie.series[0].data[0].itemStyle.color).toBe('#654321')
  })
})

describe('chartTypeIsSupported', () => {
  it('accepts the stored types in any case and refuses the rest', () => {
    expect(chartTypeIsSupported(makeConfig({ type: 'Donut' as ChartConfig['type'] }))).toBe(true)
    expect(chartTypeIsSupported(makeConfig({ type: 'gauge' as ChartConfig['type'] }))).toBe(false)
  })
})
