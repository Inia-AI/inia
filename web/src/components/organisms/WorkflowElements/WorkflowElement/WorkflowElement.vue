<script lang="ts" setup>
import { PropType, computed } from 'vue'
import WorkflowContent from '../WorkflowContent/WorkflowContent.vue'
import DynamicChartRenderer from '@/components/organisms/DynamicChart/DynamicChartRenderer.vue'
import { ReportParagraph } from '@/api/schema'
import { ReportParagraphKind } from '@/enums'

const props = defineProps({
  sectionId: {
    type: String,
  },
  executionId: {
    type: String,
  },
  element: {
    type: Object as PropType<ReportParagraph>,
  },
})

const $emits = defineEmits(['save', 'remove'])

const isChartElement = computed(
  () => props.element?.kind === ReportParagraphKind.DynamicChart,
)
const isTableElement = computed(
  () => props.element?.kind === ReportParagraphKind.Table,
)
const isDivider = computed(
  () => props.element?.kind === ReportParagraphKind.Divider,
)

const chartConfig = computed(() => props.element?.chartPayload?.chartConfig ?? null)
const table = computed(() => props.element?.tablePayload ?? null)
</script>

<template>
  <div>
    <!-- Element Subtitle -->
    <WorkflowContent
      v-if="!!props.element?.subtitle"
      prop-key="subtitle"
      content-class="element-subtitle"
      :element-id="props.element?.id"
      :workflow-id="props.executionId"
      :content="props.element?.subtitle ?? undefined"
      :replacement-tag="props.element?.replacementTag ?? undefined"
      :actions="['editHeading']"
      :show-meta="false"
      :use-editor="false"
      @save="$event => $emits('save', $event)"
    />

    <!-- Divider -->
    <hr v-if="isDivider" class="my-8 border-t" />

    <!-- Chart -->
    <div v-else-if="isChartElement && chartConfig" class="mb-6 mt-12">
      <DynamicChartRenderer :config="chartConfig" :is-loading="false" />
      <div v-if="props.element?.sources?.length" class="mt-2 text-xs text-muted-foreground">
        <span class="font-medium">Sources:</span> {{ props.element.sources.join(', ') }}
      </div>
    </div>

    <!-- Table -->
    <div v-else-if="isTableElement && table" class="mb-6 mt-6 overflow-x-auto">
      <table class="report-table">
        <thead v-if="table.tableConfig?.length">
          <tr>
            <th v-for="(col, colIdx) in table.tableConfig" :key="colIdx">
              {{ col.header }}
            </th>
          </tr>
        </thead>
        <tbody>
          <tr v-for="(row, rowIdx) in table.tableData ?? []" :key="rowIdx">
            <td v-for="(cell, cellIdx) in row" :key="cellIdx">{{ cell }}</td>
          </tr>
        </tbody>
      </table>
    </div>

    <!-- Regular Text Content -->
    <WorkflowContent
      v-else-if="!!props.element?.content"
      :element-id="props.element?.id"
      :workflow-id="props.executionId"
      :content="props.element?.content ?? undefined"
      :replacement-tag="props.element?.replacementTag ?? undefined"
      :actions="['trace', 'edit', 'copy', 'remove']"
      :sources="props.element?.sources"
      :citations="props.element?.citations"
      :cited-sources-by-block="props.element?.citedSourcesByBlock"
      :confidence="props.element?.confidence ?? undefined"
      @save="$emits('save', $event)"
      @remove="$emits('remove', $event)"
    />
  </div>
</template>

<style scoped>
.element-subtitle {
  font-size: 22px;
  font-weight: 600;
  color: var(--text-primary);
  letter-spacing: -0.02em;
  margin-bottom: var(--spacing-sm);
  line-height: 1.4;
  user-select: none;
}

.report-table {
  width: 100%;
  border-collapse: collapse;
  font-size: 14px;
}

.report-table th,
.report-table td {
  border: 1px solid var(--stroke);
  padding: var(--spacing-sm) var(--spacing-md);
  text-align: left;
}

.report-table th {
  background: var(--bg-1);
  font-weight: 600;
  color: var(--text-primary);
}
</style>
