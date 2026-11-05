import { use, type ComposeOption } from 'echarts/core'
import { BarChart, LineChart, PieChart, ScatterChart } from 'echarts/charts'
import type { BarSeriesOption, LineSeriesOption, PieSeriesOption, ScatterSeriesOption } from 'echarts/charts'
import { GridComponent, LegendComponent, TitleComponent } from 'echarts/components'
import type {
  GridComponentOption,
  LegendComponentOption,
  MarkLineComponentOption,
  TitleComponentOption,
  ToolboxComponentOption,
  TooltipComponentOption
} from 'echarts/components'
import { CanvasRenderer } from 'echarts/renderers'

// What both the app and the Word export draw with. A component adds what only it uses, such as the tooltip, the toolbox or
// a mark line, so the export's bundle does not carry it.
use([BarChart, LineChart, PieChart, ScatterChart, GridComponent, LegendComponent, TitleComponent, CanvasRenderer])

export type ChartOptions = ComposeOption<
  | BarSeriesOption
  | LineSeriesOption
  | PieSeriesOption
  | ScatterSeriesOption
  | GridComponentOption
  | LegendComponentOption
  | MarkLineComponentOption
  | TitleComponentOption
  | ToolboxComponentOption
  | TooltipComponentOption
>

/** The white a chart draws on in the Word export, and on screen when it is on paper. */
export const PAPER = '#ffffff'

export { init, type ECharts } from 'echarts/core'
