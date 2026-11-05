import type { XAXisComponentOption, YAXisComponentOption } from 'echarts'
import { init, PAPER, type ChartOptions } from '../../lib/echarts'
import { buildChartOptions, chartHasData, chartTypeIsSupported } from './buildChartOptions'
import type { ChartConfig } from './schema'

/** The fonts of a chart picture in the Word export, sizes in pixels. */
export type ExportStyle = {
  fontFamily: string
  titleFontSize: number
  axisTitleFontSize: number
  labelFontSize: number
}

export const UNSUPPORTED_TYPE_MESSAGE = 'The app draws no chart of this type.'
export const NO_DATA_MESSAGE = 'The chart has no data to draw.'

function styledAxis<T extends XAXisComponentOption | YAXisComponentOption>(axis: T, style: ExportStyle): T {
  return {
    ...axis,
    axisLabel: { ...axis.axisLabel, fontSize: style.labelFontSize },
    nameTextStyle: { ...axis.nameTextStyle, fontSize: style.axisTitleFontSize }
  }
}

/** The chart's options for a picture in the Word export: no animation, a white background and the export's fonts. */
export function exportOptions(config: ChartConfig, style: ExportStyle): ChartOptions {
  if (!chartTypeIsSupported(config)) {
    throw new Error(UNSUPPORTED_TYPE_MESSAGE)
  }
  const options = buildChartOptions(config)
  if (!chartHasData(config) || options.series.length === 0) {
    throw new Error(NO_DATA_MESSAGE)
  }

  return {
    ...options,
    animation: false,
    backgroundColor: PAPER,
    textStyle: { fontFamily: style.fontFamily, fontSize: style.labelFontSize },
    title: {
      ...options.title,
      textStyle: { ...options.title.textStyle, fontSize: style.titleFontSize },
      subtextStyle: { ...options.title.subtextStyle, fontSize: style.labelFontSize }
    },
    legend: { ...options.legend, textStyle: { ...options.legend?.textStyle, fontSize: style.labelFontSize } },
    xAxis: options.xAxis && styledAxis(options.xAxis, style),
    yAxis: options.yAxis && styledAxis(options.yAxis, style),
    series: options.series.map(serie => (serie.type === 'pie' ? { ...serie, label: { ...serie.label, fontSize: style.labelFontSize } } : serie))
  }
}

/** Draws the chart into the container for the Word export. Settles when the drawing is finished, and fails when there is nothing to draw. */
export function drawForExport(container: HTMLElement, config: ChartConfig, style: ExportStyle): Promise<void> {
  return new Promise(resolve => {
    const options = exportOptions(config, style)
    const chart = init(container, null, { renderer: 'canvas' })
    chart.on('finished', () => resolve())
    chart.setOption(options)
  })
}
