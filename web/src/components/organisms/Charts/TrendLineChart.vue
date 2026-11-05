<template>
  <div class="trend-chart">
    <BaseChart :options="chartOptions" :height="chartHeight(config)" :image-name="title || meta?.yLabel || ''" />
  </div>
</template>

<script setup lang="ts">
import { computed } from 'vue'
import BaseChart from '@/components/organisms/Charts/BaseChart.vue'
import { buildChartOptions, chartHeight } from '@/api/dynamicChart/buildChartOptions'
import type { ChartConfig } from '@/api/dynamicChart/schema'

interface Props {
  xValues: number[]
  yValues: string
  unit: string
  series: Array<{
    name: string
    data: number[]
    color?: string
  }>
  meta?: {
    xLabel?: string
    yLabel?: string
  }
  title?: string
  displayOptions?: {
    showLegend?: boolean
    showGridLines?: boolean
    enableAnimations?: boolean
  }
  layout?: {
    width?: string
    height?: string
  }
}

const props = withDefaults(defineProps<Props>(), {
  title: '',
  displayOptions: () => ({
    showLegend: true,
    showGridLines: true,
    enableAnimations: true
  }),
  layout: () => ({
    width: '100%',
    height: '400px'
  })
})

const config = computed<ChartConfig>(() => ({
  type: 'line',
  title: props.title,
  data: [],
  xAxis: { field: 'x', label: props.meta?.xLabel },
  yAxis: { field: 'y', label: props.meta?.yLabel },
  series: props.series.map(serie => ({
    name: serie.name,
    color: serie.color,
    data: props.xValues.map((x, index) => ({ x, y: serie.data[index] ?? 0 }))
  })),
  options: props.displayOptions,
  metadata: { dataPoints: props.xValues.length, currency: props.yValues, unit: props.unit },
  layout: props.layout
}))

const chartOptions = computed(() => buildChartOptions(config.value))
</script>

<style scoped>
.trend-chart {
  width: 100%;
  height: 100%;
}
</style>
