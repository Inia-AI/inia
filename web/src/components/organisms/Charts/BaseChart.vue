<template>
  <div
    ref="container"
    class="base-chart"
    role="img"
    :aria-label="label || imageName || $t('shared.components.chart.name')"
    :style="{ height: `${height}px` }"
  />
</template>

<script setup lang="ts">
import { onBeforeUnmount, onMounted, ref, shallowRef, watch } from 'vue'
import { storeToRefs } from 'pinia'
import { useI18n } from 'vue-i18n'
import { useResizeObserver } from '@vueuse/core'
import { use } from 'echarts/core'
import { ToolboxComponent, TooltipComponent } from 'echarts/components'
import useThemeStore from '@/store/theme'
import { init, PAPER, type ChartOptions, type ECharts } from '@/lib/echarts'

use([ToolboxComponent, TooltipComponent])

interface Props {
  options: ChartOptions
  height?: number
  /** Names the chart for a screen reader; without one, the image name does, and without that the word "Chart". */
  label?: string
  /** The file name the chart's save button gives the picture; without one the chart has no save button. */
  imageName?: string
  /**
   * Draws the chart in its light colours on white in both themes, as the Word export draws it. A block author picks
   * its series colours for a white page, and a dark grey series vanishes on the dark theme's background.
   */
  onPaper?: boolean
}

const props = withDefaults(defineProps<Props>(), { height: 400, onPaper: false })
const emit = defineEmits<{ plotClick: [x: number, y: number] }>()
const { t: $t } = useI18n()
const { theme } = storeToRefs(useThemeStore())

const container = ref<HTMLDivElement>()
const chart = shallowRef<ECharts>()

function chartTheme(): string {
  return theme.value === 'dark' && !props.onPaper ? 'dark' : 'default'
}

function background(): string {
  if (props.onPaper) {
    return PAPER
  }
  return container.value ? getComputedStyle(container.value).getPropertyValue('--bg-1').trim() : ''
}

function screenOptions(): ChartOptions {
  const toolbox: ChartOptions['toolbox'] =
    props.imageName === undefined
      ? { show: false }
      : {
          right: 0,
          top: 0,
          feature: {
            saveAsImage: {
              title: $t('shared.components.chart.saveAsImage'),
              name: props.imageName || 'chart',
              backgroundColor: background() || undefined,
              pixelRatio: 2
            }
          }
        }
  return { backgroundColor: props.onPaper ? PAPER : 'transparent', ...props.options, toolbox }
}

function reportPlotClick(pixel: number[]): void {
  if (!chart.value || !('xAxis' in props.options) || !chart.value.containPixel({ gridIndex: 0 }, pixel)) {
    return
  }
  const [x = 0, y = 0] = chart.value.convertFromPixel({ gridIndex: 0 }, pixel) as number[]
  emit('plotClick', x, y)
}

onMounted(() => {
  if (!container.value) {
    return
  }
  chart.value = init(container.value, chartTheme(), { renderer: 'canvas' })
  chart.value.getZr().on('click', event => reportPlotClick([event.offsetX, event.offsetY]))
  chart.value.setOption(screenOptions())
})

watch([() => props.options, () => props.imageName], () => chart.value?.setOption(screenOptions(), { notMerge: true }))

watch(
  theme,
  () => {
    if (props.onPaper) {
      return
    }
    chart.value?.setTheme(chartTheme())
    chart.value?.setOption(screenOptions())
  },
  { flush: 'post' }
)

useResizeObserver(container, ([entry]) => {
  const size = entry?.contentRect
  if (chart.value && size && (size.width !== chart.value.getWidth() || size.height !== chart.value.getHeight())) {
    chart.value.resize()
  }
})

onBeforeUnmount(() => chart.value?.dispose())
</script>

<style scoped>
.base-chart {
  width: 100%;
}
</style>
