import type { BarSeriesOption, LineSeriesOption, PieSeriesOption, ScatterSeriesOption } from 'echarts/charts'
import type { GridComponentOption, LegendComponentOption, TitleComponentOption, TooltipComponentOption } from 'echarts/components'
import type { DefaultLabelFormatterCallbackParams, XAXisComponentOption, YAXisComponentOption } from 'echarts'
import type { ChartOptions } from '../../lib/echarts'
import { escapeHtml } from '../../lib/escapeHtml'
import type { ChartConfig } from './schema'
import { CHART_TYPES } from './chartTypes'
import { ColorSchemes } from './colorSchemes'

/** The options one builder returns: one title, tooltip and legend, a series list and at most one axis of each kind. */
export type BuiltChart = Omit<ChartOptions, 'title' | 'legend' | 'series' | 'xAxis' | 'yAxis' | 'tooltip'> & {
  title: TitleComponentOption
  tooltip?: TooltipComponentOption
  legend?: LegendComponentOption
  series: (BarSeriesOption | LineSeriesOption | PieSeriesOption | ScatterSeriesOption)[]
  xAxis?: XAXisComponentOption
  yAxis?: YAXisComponentOption
}

type DataRecord = Record<string, unknown>

type CategorySeries = {
  name: string
  data: number[]
  color?: string
}

type CategoryShape = {
  xValues: (string | number)[]
  series: CategorySeries[]
  xLabel: string
  yLabel: string
  currency: string
  unit: string
}

function getColorScheme(name?: string): string[] {
  return (ColorSchemes as Record<string, string[]>)[name || 'default'] || ColorSchemes.default
}

function toNumber(value: unknown): number {
  const n = Number(value)
  return Number.isFinite(n) ? n : 0
}

function seriesYValue(point: unknown): number {
  if (point !== null && typeof point === 'object') {
    const record = point as DataRecord
    if (typeof record.y === 'number') {
      return record.y
    }
    if (typeof record.value === 'number') {
      return record.value
    }
    return toNumber(record.y ?? record.value)
  }
  return toNumber(point)
}

function distinct<T>(values: T[]): T[] {
  return Array.from(new Set(values))
}

/**
 * A source field of a row. A block stores each row as x, y and series, with every field of the source row in rawData;
 * a chart built in the app may carry its fields on the row itself.
 */
function field(row: DataRecord, key: string): unknown {
  return row[key] ?? (row.rawData as DataRecord | undefined)?.[key]
}

/** The row's group: its value of the group field, else the series the block resolved from that field. */
function groupOf(row: DataRecord, groupField: string): unknown {
  return field(row, groupField) ?? row.series
}

function transformToCategoryShape(config: ChartConfig): CategoryShape {
  const colorScheme = getColorScheme(config.options?.colorScheme)
  const currency = config.metadata?.currency || ''
  const unit = config.metadata?.unit || ''

  if (config.series && config.series.length > 0) {
    const firstData = (config.series[0]?.data ?? []) as DataRecord[]
    const xValues = firstData.map(point => (point.x ?? point.name) as string | number)

    return {
      xValues,
      series: config.series.map((serie, index) => ({
        name: serie.name,
        data: ((serie.data ?? []) as unknown[]).map(seriesYValue),
        color: serie.color || colorScheme[index % colorScheme.length]
      })),
      xLabel: config.xAxis?.label || 'X-Axis',
      yLabel: config.yAxis?.label || 'Y-Axis',
      currency,
      unit
    }
  }

  const data = (config.data ?? []) as DataRecord[]
  const xField = config.xAxis?.field || 'x'
  const yField = config.yAxis?.field || 'y'

  if (data.length > 0 && data[0]?.x !== undefined) {
    const xValues = distinct(data.map(d => d.x as string | number)).sort()
    const groupField = config.options?.seriesGroupField || 'series'

    if (data.some(d => groupOf(d, groupField))) {
      const seriesNames = distinct(data.map(d => groupOf(d, groupField))).filter(Boolean)
      const series = seriesNames.map((seriesName, index) => {
        const seriesData = data.filter(d => groupOf(d, groupField) === seriesName)
        const points = xValues.map(xValue => {
          const point = seriesData.find(d => d.x === xValue)
          return point ? toNumber(point.y) : 0
        })
        return { name: String(seriesName), data: points, color: colorScheme[index % colorScheme.length] }
      })

      return {
        xValues,
        series,
        xLabel: config.xAxis?.label || '',
        yLabel: config.yAxis?.label || '',
        currency,
        unit
      }
    }

    const points = xValues.map(xValue => {
      const point = data.find(d => d.x === xValue)
      return point ? toNumber(point.y) : 0
    })

    return {
      xValues,
      series: [{ name: config.yAxis?.label || config.title || 'Series 1', data: points, color: colorScheme[0] }],
      xLabel: config.xAxis?.label || '',
      yLabel: config.yAxis?.label || '',
      currency,
      unit
    }
  }

  const xValues = distinct(data.map(d => d[xField]).filter(v => v !== null && v !== undefined) as (string | number)[]).sort()
  const points = xValues.map(xValue => {
    const point = data.find(d => d[xField] === xValue)
    return point ? toNumber(point[yField]) : 0
  })

  return {
    xValues,
    series: [{ name: config.yAxis?.label || config.yAxis?.field || 'Data', data: points, color: colorScheme[0] }],
    xLabel: config.xAxis?.label || '',
    yLabel: config.yAxis?.label || '',
    currency,
    unit
  }
}

function yAxisTitleText(shape: CategoryShape): string {
  const parts = [shape.currency, shape.unit].filter(Boolean)
  const suffix = parts.length > 0 ? ` (${parts.join(' ')})` : ''
  return `${shape.yLabel}${suffix}`
}

function smartDecimalPlaces(config: ChartConfig, shape: CategoryShape): number {
  const unitLower = (config.metadata?.unit || '').toLowerCase()
  const titleLower = (config.title || '').toLowerCase()

  if (
    unitLower.includes('x') ||
    unitLower.includes('ratio') ||
    titleLower.includes('ratio') ||
    titleLower.includes('coverage') ||
    titleLower.includes('margin') ||
    titleLower.includes('rate')
  ) {
    return 2
  }

  const hasDecimals = shape.series.some(serie => serie.data.some(value => Math.abs(value % 1) > 0.01))
  return hasDecimals ? 2 : 0
}

const AREA_FILL_OPACITY = 0.35
const DEFAULT_CHART_HEIGHT = 400
const LINE_SYMBOL_SIZE = 8
const SCATTER_SYMBOL_SIZE = 12
const BUBBLE_MIN_SIZE = 20
const BUBBLE_MAX_SIZE = 80
const BUBBLE_SIZE_LABEL = 'Assets Under Management (millions)'
const PERCENT_STACK_MAX = 100
const NARROW_CHART_WIDTH = 500
const STACK_NAME = 'total'
const TITLE_HEIGHT = 40
const SUBTITLE_HEIGHT = 20
const LEGEND_HEIGHT = 32
const EDGE_GAP = 12

/** The height in pixels the block's layout asks for. */
export function chartHeight(config: ChartConfig): number {
  return parseInt(config.layout?.height || String(DEFAULT_CHART_HEIGHT), 10) || DEFAULT_CHART_HEIGHT
}

/** Whether the chart's type is one the app draws. The report page and the Word export refuse every other type. */
export function chartTypeIsSupported(config: ChartConfig): boolean {
  return (CHART_TYPES as readonly string[]).includes((config.type || '').toLowerCase())
}

/** Whether the chart has a row or a series point to draw. */
export function chartHasData(config: ChartConfig): boolean {
  const hasRows = (config.data?.length ?? 0) > 0
  const hasPoints = (config.series ?? []).some(serie => (serie.data?.length ?? 0) > 0)
  return hasRows || hasPoints
}

function formatNumber(value: number, decimals?: number): string {
  return decimals === undefined
    ? value.toLocaleString()
    : value.toLocaleString(undefined, { minimumFractionDigits: decimals, maximumFractionDigits: decimals })
}

function valueText(shape: CategoryShape, decimals: number): (value: unknown) => string {
  return value => `${shape.currency}${formatNumber(toNumber(value), decimals)}${shape.unit ? ` ${shape.unit}` : ''}`
}

function currencyAxisLabel(currency: string): (value: number | string) => string {
  return value => `${currency}${Number(value).toLocaleString()}`
}

function title(config: ChartConfig, subtext?: string): TitleComponentOption {
  return { text: config.title || '', subtext, left: 'center', textStyle: { fontSize: 16, fontWeight: 600 } }
}

function legend(show: boolean): LegendComponentOption {
  return { show, bottom: 0, left: 'center', orient: 'horizontal' }
}

function grid(config: ChartConfig, showLegend: boolean, hasSubtitle = false): GridComponentOption {
  const titleSpace = (config.title ? TITLE_HEIGHT : EDGE_GAP) + (hasSubtitle ? SUBTITLE_HEIGHT : 0)
  return {
    left: EDGE_GAP,
    right: EDGE_GAP * 2,
    top: titleSpace,
    bottom: showLegend ? LEGEND_HEIGHT : EDGE_GAP,
    outerBoundsMode: 'same',
    outerBoundsContain: 'all'
  }
}

function showLegendFor(config: ChartConfig, seriesCount: number): boolean {
  return (config.options?.showLegend ?? true) && seriesCount > 1
}

function categoryAxis(shape: CategoryShape, showGridLines: boolean): XAXisComponentOption {
  return {
    type: 'category',
    data: shape.xValues.map(String),
    name: shape.xLabel,
    nameLocation: 'middle',
    splitLine: { show: showGridLines }
  }
}

function valueAxis(name: string, currency: string, showGridLines: boolean): YAXisComponentOption {
  return {
    type: 'value',
    name,
    nameLocation: 'middle',
    axisLabel: { formatter: currencyAxisLabel(currency) },
    splitLine: { show: showGridLines }
  }
}

function fieldAxisLabels(config: ChartConfig): { x: string; y: string } {
  return {
    x: config.xAxis?.label || config.xAxis?.field || 'X Axis',
    y: config.yAxis?.label || config.yAxis?.field || 'Y Axis'
  }
}

function buildLineOptions(config: ChartConfig, filled: boolean): BuiltChart {
  const shape = transformToCategoryShape(config)
  const showGridLines = config.options?.showGridLines ?? true
  const showLegend = showLegendFor(config, shape.series.length)

  const series: LineSeriesOption[] = shape.series.map((serie, index) => ({
    type: 'line',
    name: serie.name,
    data: serie.data,
    showSymbol: true,
    symbol: 'circle',
    symbolSize: LINE_SYMBOL_SIZE,
    itemStyle: { color: serie.color },
    lineStyle: {
      color: serie.color,
      width: index === 0 ? 3 : 2,
      type: serie.name.toLowerCase().includes('ebitda') ? 'dashed' : 'solid'
    },
    ...(filled ? { areaStyle: { opacity: AREA_FILL_OPACITY } } : {})
  }))

  return {
    animation: config.options?.enableAnimations ?? true,
    title: title(config),
    grid: grid(config, showLegend),
    xAxis: categoryAxis(shape, showGridLines),
    yAxis: valueAxis(yAxisTitleText(shape), shape.currency, showGridLines),
    tooltip: { trigger: 'axis', valueFormatter: valueText(shape, smartDecimalPlaces(config, shape)) },
    series,
    legend: legend(showLegend)
  }
}

function percentOfCategory(series: CategorySeries[]): { value: number; raw: number }[][] {
  const totals = series[0]?.data.map((_, index) => series.reduce((sum, serie) => sum + Math.abs(serie.data[index] ?? 0), 0)) ?? []
  return series.map(serie =>
    serie.data.map((raw, index) => ({ value: totals[index] ? (Math.abs(raw) / totals[index]) * PERCENT_STACK_MAX : 0, raw }))
  )
}

function buildBarOptions(config: ChartConfig): BuiltChart {
  const shape = transformToCategoryShape(config)
  const stackingMode = config.options?.stacking?.toLowerCase()
  const stacked = !!stackingMode && stackingMode !== 'none'
  const percent = stackingMode === 'percent'
  const showLegend = showLegendFor(config, shape.series.length)
  const formatValue = valueText(shape, 0)
  const percentData = percent ? percentOfCategory(shape.series) : []

  const series: BarSeriesOption[] = shape.series.map((serie, index) => ({
    type: 'bar',
    name: serie.name,
    data: percent ? percentData[index] : serie.data,
    itemStyle: { color: serie.color },
    ...(stacked ? { stack: STACK_NAME } : {})
  }))

  const categories = categoryAxis(shape, false)
  const values = valueAxis(yAxisTitleText(shape), percent ? '' : shape.currency, true)
  const valueAxisOption: YAXisComponentOption = percent
    ? { ...values, max: PERCENT_STACK_MAX, axisLabel: { formatter: '{value}%' } }
    : values

  return {
    animation: config.options?.enableAnimations ?? true,
    title: title(config),
    grid: grid(config, showLegend),
    xAxis: config.type === 'bar' ? (valueAxisOption as XAXisComponentOption) : categories,
    yAxis: config.type === 'bar' ? ({ ...categories, inverse: true } as YAXisComponentOption) : valueAxisOption,
    tooltip: percent
      ? {
          trigger: 'item',
          formatter: params => {
            const point = params as DefaultLabelFormatterCallbackParams
            const raw = (point.data as { raw?: number } | undefined)?.raw ?? point.value
            return `${escapeHtml(point.name)}<br/>${point.marker ?? ''}${escapeHtml(point.seriesName ?? '')}: <b>${escapeHtml(formatValue(raw))}</b>`
          }
        }
      : { trigger: 'item', valueFormatter: formatValue },
    series,
    legend: legend(showLegend)
  }
}

function buildPieOptions(config: ChartConfig): BuiltChart {
  const colorScheme = getColorScheme(config.options?.colorScheme)
  const data = (config.data ?? []) as DataRecord[]
  const xField = config.xAxis?.field || 'x'
  const yField = config.yAxis?.field || 'y'

  const pieData = data.map((item, index) => ({
    name: String(item.x !== undefined ? item.x : field(item, xField) ?? `Category ${index + 1}`),
    value: item.y !== undefined ? toNumber(item.y) : toNumber(field(item, yField)),
    itemStyle: { color: (field(item, 'color') as string) || colorScheme[index % colorScheme.length] }
  }))

  return {
    title: title(config),
    tooltip: {
      trigger: 'item',
      formatter: params => {
        const point = params as DefaultLabelFormatterCallbackParams
        return `<b>${escapeHtml(point.name)}</b><br/>Value: <b>${toNumber(point.value)}</b><br/>Percentage: <b>${toNumber(point.percent).toFixed(1)}%</b>`
      }
    },
    series: [
      {
        type: 'pie',
        name: 'Values',
        radius: config.type === 'donut' ? ['36%', '60%'] : ['0%', '60%'],
        center: ['50%', '50%'],
        padAngle: 1,
        label: {
          show: true,
          fontSize: 11,
          overflow: 'break',
          formatter: slice => `${slice.name}: ${toNumber(slice.percent).toFixed(1)}%`
        },
        data: pieData
      }
    ],
    legend: { ...legend(true), textStyle: { fontSize: 11 } },
    media: [
      {
        query: { maxWidth: NARROW_CHART_WIDTH },
        option: { series: [{ type: 'pie', label: { show: false }, labelLine: { show: false } }] }
      }
    ]
  }
}

function buildScatterOptions(config: ChartConfig): BuiltChart {
  const colorScheme = getColorScheme(config.options?.colorScheme)
  const data = (config.data ?? []) as DataRecord[]
  const xField = config.xAxis?.field || 'x'
  const yField = config.yAxis?.field || 'y'
  const groupField = config.options?.seriesGroupField || 'series'
  const { x: xAxisLabel, y: yAxisLabel } = fieldAxisLabels(config)

  const point = (item: DataRecord, name: string) => {
    const color = field(item, 'color') as string | undefined
    return {
      name: String(field(item, 'name') || name),
      value: [
        item.x !== undefined ? toNumber(item.x) : toNumber(field(item, xField)),
        item.y !== undefined ? toNumber(item.y) : toNumber(field(item, yField))
      ],
      ...(color ? { itemStyle: { color } } : {})
    }
  }

  const scatterSeries = (name: string, color: string | undefined, items: DataRecord[], pointName: (index: number) => string): ScatterSeriesOption => ({
    type: 'scatter',
    name,
    symbolSize: SCATTER_SYMBOL_SIZE,
    itemStyle: { color, borderColor: 'rgba(255,255,255,0.8)', borderWidth: 1 },
    data: items.map((item, index) => point(item, pointName(index)))
  })

  const series: ScatterSeriesOption[] = data.some(d => groupOf(d, groupField))
    ? distinct(data.map(d => groupOf(d, groupField)))
        .filter(Boolean)
        .map((groupValue, index) =>
          scatterSeries(
            String(groupValue),
            colorScheme[index % colorScheme.length],
            data.filter(d => groupOf(d, groupField) === groupValue),
            pointIndex => `${groupValue} Point ${pointIndex + 1}`
          )
        )
    : [scatterSeries(yAxisLabel, colorScheme[0], data, index => `Point ${index + 1}`)]

  const showLegend = showLegendFor(config, series.length)

  return {
    title: title(config),
    grid: grid(config, showLegend),
    xAxis: { type: 'value', scale: true, name: xAxisLabel, nameLocation: 'middle', splitLine: { show: true } },
    yAxis: { type: 'value', scale: true, name: yAxisLabel, nameLocation: 'middle' },
    tooltip: {
      trigger: 'item',
      formatter: params => {
        const item = params as DefaultLabelFormatterCallbackParams
        const pointData = item.data as { name: string; value: number[] }
        return `<b>${escapeHtml(item.seriesName ?? '')}</b><br>${escapeHtml(pointData.name)}<br/>${escapeHtml(xAxisLabel)}: ${pointData.value[0]}<br/>${escapeHtml(yAxisLabel)}: ${pointData.value[1]}`
      }
    },
    series,
    legend: legend(showLegend)
  }
}

function quadrantColor(x: number, y: number): string {
  if (x > 0 && y > 0) {
    return '#059669'
  }
  if (x > 0 && y < 0) {
    return '#ea580c'
  }
  if (x < 0 && y > 0) {
    return '#0891b2'
  }
  return '#dc2626'
}

/** The bubble's diameter, with its area in proportion to its size between the smallest and the largest bubble. */
function bubbleDiameter(size: number, smallest: number, largest: number): number {
  const position = largest > smallest ? (size - smallest) / (largest - smallest) : 0.5
  return Math.sqrt(BUBBLE_MIN_SIZE ** 2 + position * (BUBBLE_MAX_SIZE ** 2 - BUBBLE_MIN_SIZE ** 2))
}

function buildBubbleOptions(config: ChartConfig): BuiltChart {
  const data = (config.data ?? []) as DataRecord[]
  const xField = config.xAxis?.field || 'x'
  const yField = config.yAxis?.field || 'y'
  const groupField = config.options?.seriesGroupField
  const { x: xLabel, y: yLabel } = fieldAxisLabels(config)

  if (data.length === 0) {
    return { title: title(config, 'No data available'), series: [] }
  }

  const bubbleData = data.map((item, index) => {
    const x = item.x !== undefined ? toNumber(item.x) : toNumber(field(item, xField))
    const y = item.y !== undefined ? toNumber(item.y) : toNumber(field(item, yField))
    const label = String(
      field(item, 'fund') || field(item, 'name') || field(item, 'label') || (groupField ? groupOf(item, groupField) : null) || `Point ${index + 1}`
    )
    const size = field(item, 'size')
    const aum = field(item, 'aum')
    const bubbleSize = size !== undefined ? toNumber(size) : aum !== undefined ? toNumber(aum) / 1_000_000 : 50
    return { name: label, value: [x, y, bubbleSize], itemStyle: { color: quadrantColor(x, y), opacity: 0.6, borderColor: 'white', borderWidth: 2 } }
  })

  const sizes = bubbleData.map(item => item.value[2] ?? 0)
  const smallest = Math.min(...sizes)
  const largest = Math.max(...sizes)

  return {
    title: title(config, `Bubble size: ${BUBBLE_SIZE_LABEL}`),
    grid: grid(config, false, true),
    xAxis: { type: 'value', scale: true, name: xLabel, nameLocation: 'middle', splitLine: { show: true } },
    yAxis: { type: 'value', scale: true, name: yLabel, nameLocation: 'middle' },
    tooltip: {
      trigger: 'item',
      formatter: params => {
        const item = params as DefaultLabelFormatterCallbackParams
        const [x = 0, y = 0, size = 0] = (item.data as { value: number[] }).value
        return `<b>${escapeHtml(item.name)}</b><br/>${escapeHtml(xLabel)}: ${x.toFixed(2)}<br/>${escapeHtml(yLabel)}: ${y.toFixed(2)}<br/>${BUBBLE_SIZE_LABEL}: ${size.toFixed(0)}`
      }
    },
    series: [
      {
        type: 'scatter',
        name: 'Companies',
        symbolSize: (value: number[]) => bubbleDiameter(value[2] ?? 0, smallest, largest),
        data: bubbleData
      }
    ],
    legend: legend(false)
  }
}

/** The ECharts options that draw a Dynamic Chart block's chart, on screen and in the Word export alike. */
export function buildChartOptions(chart: ChartConfig): BuiltChart {
  const type = (chart.type || 'line').toLowerCase()
  const config = { ...chart, type } as ChartConfig

  switch (type) {
    case 'bar':
    case 'column':
      return buildBarOptions(config)
    case 'pie':
    case 'donut':
      return buildPieOptions(config)
    case 'scatter':
      return buildScatterOptions(config)
    case 'bubble':
      return buildBubbleOptions(config)
    case 'area':
      return buildLineOptions(config, true)
    case 'line':
    default:
      return buildLineOptions(config, false)
  }
}
