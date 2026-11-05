<template>
  <div class="dynamic-chart-renderer">
    <!-- Loading State -->
    <div v-if="isLoading" class="flex items-center justify-center min-h-[400px]">
      <IniaLoader />
    </div>

    <!-- Error State -->
    <div v-else-if="error" class="error-state p-6 bg-[var(--accent-error-light)] border border-[var(--accent-error-border)] rounded-lg text-error">
      <div class="flex items-center mb-2">
        <IniaIcon icon="alert-circle" class="mr-2" />
        <h3 class="text-lg font-semibold">Chart Rendering Error</h3>
      </div>
      <p>{{ error }}</p>
    </div>

    <!-- No Data State -->
    <div v-else-if="hasNoData" class="no-data-state p-6 bg-[var(--accent-info-light)] border border-[var(--accent-info-border)] rounded-lg text-info">
      <div class="flex items-center mb-2">
        <IniaIcon icon="chart-bar" class="mr-2" />
        <h3 class="text-lg font-semibold">No Chart Data Available</h3>
      </div>
      <p>
        The chart configuration is valid but no data was found to display.
        This may indicate that the data source is empty or the field mappings need adjustment.
      </p>
      <div class="mt-3 text-sm">
        <p><strong>Chart Type:</strong> {{ parsedConfig?.type }}</p>
        <p v-if="parsedConfig?.xAxis?.field"><strong>X-Axis Field:</strong> {{ parsedConfig.xAxis.field }}</p>
        <p v-if="parsedConfig?.yAxis?.field"><strong>Y-Axis Field:</strong> {{ parsedConfig.yAxis.field }}</p>
        <p v-if="parsedConfig?.options?.seriesGroupField"><strong>Series Group Field:</strong> {{ parsedConfig.options.seriesGroupField }}</p>
      </div>
    </div>

    <!-- Chart -->
    <div v-else-if="chartOptions && parsedConfig" class="chart-container">
      <BaseChart :options="chartOptions" :height="chartHeight(parsedConfig)" :image-name="parsedConfig.title" on-paper />
    </div>

    <!-- Unsupported Chart Type -->
    <div v-else class="unsupported-chart p-6 bg-[var(--accent-warning-light)] border border-[var(--accent-warning-border)] rounded-lg text-warning">
      <div class="flex items-center mb-2">
        <IniaIcon icon="chart-bar" class="mr-2" />
        <h3 class="text-lg font-semibold">Unsupported Chart Type</h3>
      </div>
      <p>
        Chart type "{{ config?.type }}" is not supported.
        Supported types: line, bar, column, area, scatter, pie, donut, bubble.
      </p>
    </div>
  </div>
</template>

<script setup lang="ts">
import { computed } from 'vue'
import IniaIcon from '@/components/atoms/IniaIcon/IniaIcon.vue'
import IniaLoader from '@/components/atoms/IniaLoader/IniaLoader.vue'
import BaseChart from '@/components/organisms/Charts/BaseChart.vue'

import type { ChartConfig } from '@/api/dynamicChart/schema'
import { buildChartOptions, chartHasData, chartHeight, chartTypeIsSupported } from '@/api/dynamicChart/buildChartOptions'

interface Props {
  config?: ChartConfig
  rawResponse?: string
  isLoading?: boolean
  error?: string
}

const props = withDefaults(defineProps<Props>(), {
  isLoading: false
})

const parsedConfig = computed<ChartConfig | null>(() => {
  if (props.config) return props.config

  if (props.rawResponse) {
    try {
      return JSON.parse(props.rawResponse) as ChartConfig
    } catch (err) {
      console.error('Failed to parse chart config:', err)
      return null
    }
  }

  return null
})

const hasNoData = computed(() => !!parsedConfig.value && !chartHasData(parsedConfig.value))

const isSupportedType = computed(() => !!parsedConfig.value && chartTypeIsSupported(parsedConfig.value))

const chartOptions = computed(() => {
  if (!parsedConfig.value || hasNoData.value || !isSupportedType.value) return null
  return buildChartOptions(parsedConfig.value)
})
</script>

<style scoped>
.dynamic-chart-renderer {
  width: 100%;
  /* Remove fixed min-height to allow dynamic sizing */
}

.chart-container {
  background: var(--bg-1);
  border-radius: 0.5rem;
  padding: 1rem;
  box-shadow: 0 1px 3px 0 rgba(0, 0, 0, 0.1);
  /* Height will be determined by the chart content */
}

.error-state,
.unsupported-chart,
.no-data-state {
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  min-height: 400px;
}
</style>
