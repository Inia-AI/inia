<script setup lang="ts">
import { computed, toRef } from 'vue'
import { useI18n } from 'vue-i18n'
import API from '@/api'
import type { ExecutionCostLine } from '@/api/tokenUsage'
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogHeader,
  DialogTitle,
} from '@/components/ui/dialog'
import { Button } from '@/components/ui/button'

const props = defineProps<{
  open: boolean
  executionId: string
}>()

const emit = defineEmits<{
  'update:open': [value: boolean]
}>()

const { t: $t } = useI18n()

const { data: cost, isLoading, isError } = API.TokenUsage.useGetExecutionCost(
  toRef(props, 'executionId'),
  toRef(props, 'open'),
)

const usd = new Intl.NumberFormat('en-US', {
  style: 'currency',
  currency: 'USD',
  minimumFractionDigits: 4,
  maximumFractionDigits: 4,
})
const count = new Intl.NumberFormat('en-US')

const sections = computed(() => [
  { key: 'blocks', title: $t('page.workflowDetails.runCost.blocks'), lines: cost.value?.blocks ?? [] },
  { key: 'other', title: $t('page.workflowDetails.runCost.other'), lines: cost.value?.other ?? [] },
].filter(section => section.lines.length > 0))

const modelsOf = (line: ExecutionCostLine) => line.models.join(', ')
const cacheOf = (line: ExecutionCostLine) =>
  `${count.format(line.cacheReadTokens)} / ${count.format(line.cacheWriteTokens)}`
</script>

<template>
  <Dialog :open="open" @update:open="value => emit('update:open', value)">
    <DialogContent class="max-w-4xl" data-testid="run-cost-dialog">
      <DialogHeader>
        <DialogTitle>{{ $t('page.workflowDetails.runCost.title') }}</DialogTitle>
        <DialogDescription>{{ $t('page.workflowDetails.runCost.description') }}</DialogDescription>
      </DialogHeader>

      <p v-if="isLoading" class="text-sm">{{ $t('page.workflowDetails.runCost.loading') }}</p>
      <p v-else-if="isError" class="text-sm text-error">{{ $t('page.workflowDetails.runCost.loadFailed') }}</p>
      <template v-else-if="cost">
        <div class="flex items-baseline gap-3">
          <span class="text-sm">{{ $t('page.workflowDetails.runCost.total') }}</span>
          <span class="text-2xl font-semibold" data-testid="run-cost-total">{{ usd.format(cost.totalCost) }}</span>
          <span class="text-xs text-muted-foreground">
            {{ $t('page.workflowDetails.runCost.records', { count: cost.usageRecordCount }) }}
          </span>
        </div>
        <p v-if="cost.unpricedRecordCount > 0" class="text-xs text-error" data-testid="run-cost-unpriced">
          {{ $t('page.workflowDetails.runCost.unpriced', { count: cost.unpricedRecordCount }) }}
        </p>
        <p v-if="sections.length === 0" class="text-sm">{{ $t('page.workflowDetails.runCost.empty') }}</p>

        <div class="max-h-[60vh] overflow-auto">
          <section v-for="section in sections" :key="section.key" class="mt-4">
            <h3 class="text-sm font-semibold mb-2">{{ section.title }}</h3>
            <table class="w-full text-xs" :data-testid="`run-cost-${section.key}`">
              <thead>
                <tr class="text-left">
                  <th class="py-1 pr-2">{{ $t('page.workflowDetails.runCost.columns.name') }}</th>
                  <th class="py-1 pr-2">{{ $t('page.workflowDetails.runCost.columns.model') }}</th>
                  <th class="py-1 pr-2 text-right">{{ $t('page.workflowDetails.runCost.columns.input') }}</th>
                  <th class="py-1 pr-2 text-right">{{ $t('page.workflowDetails.runCost.columns.output') }}</th>
                  <th class="py-1 pr-2 text-right">{{ $t('page.workflowDetails.runCost.columns.cache') }}</th>
                  <th class="py-1 text-right">{{ $t('page.workflowDetails.runCost.columns.cost') }}</th>
                </tr>
              </thead>
              <tbody>
                <tr v-for="line in section.lines" :key="line.blockId ?? line.name" class="border-t">
                  <td class="py-1 pr-2">
                    {{ line.name }}
                    <span v-if="line.replacementTag" class="text-muted-foreground">({{ line.replacementTag }})</span>
                  </td>
                  <td class="py-1 pr-2">{{ modelsOf(line) }}</td>
                  <td class="py-1 pr-2 text-right">{{ count.format(line.inputTokens) }}</td>
                  <td class="py-1 pr-2 text-right">{{ count.format(line.outputTokens) }}</td>
                  <td class="py-1 pr-2 text-right">{{ cacheOf(line) }}</td>
                  <td class="py-1 text-right">{{ usd.format(line.cost) }}</td>
                </tr>
              </tbody>
            </table>
          </section>
        </div>
      </template>

      <div class="flex justify-end">
        <Button variant="outline" size="sm" @click="emit('update:open', false)">
          {{ $t('page.workflowDetails.runCost.close') }}
        </Button>
      </div>
    </DialogContent>
  </Dialog>
</template>
