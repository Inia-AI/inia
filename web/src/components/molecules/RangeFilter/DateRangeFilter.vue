<script setup lang="ts">
import { computed } from 'vue'
import { getLocalTimeZone, parseDate, toCalendarDate, today } from '@internationalized/date'
import { Button } from '@/components/ui/button'
import { RangeCalendar } from '@/components/ui/range-calendar'
import { formatDayRange, parseDayRange } from '@/lib/dateRange'
import { cn } from '@/lib/utils'
import type { OptionType } from '@/types'
import RangeFilterPopover from './RangeFilterPopover.vue'
import { dayRangeLabel } from './rangeLabels'
import { useRangeFilterModel } from './useRangeFilterModel'

const props = defineProps<{
  presets: OptionType[]
  allLabel: string
  prefix: string
  clearLabel: string
  ariaLabel?: string
}>()

const model = defineModel<string>({ required: true })

const CALENDAR_MONTHS = 2

const { value, range, open, selected, placeholder, onRangeUpdate, commit, clear } =
  useRangeFilterModel(model, {
    parse: parseDayRange,
    format: formatDayRange,
    toCalendar: parseDate,
    toValue: calendarValue => toCalendarDate(calendarValue).toString(),
    placeholderFor: picked =>
      picked.start ?? today(getLocalTimeZone()).subtract({ months: CALENDAR_MONTHS - 1 }),
  })

const lastSelectableDay = computed(() => {
  void open.value
  return today(getLocalTimeZone())
})

const activePreset = computed(
  () => props.presets.find(preset => preset.value === value.value) ?? null,
)
const active = computed(() => activePreset.value !== null || range.value !== null)

const label = computed(() => {
  if (activePreset.value) return activePreset.value.label
  if (range.value) return `${props.prefix} ${dayRangeLabel(range.value)}`
  return props.allLabel
})
</script>

<template>
  <RangeFilterPopover
    v-model:open="open"
    :label="label"
    :active="active"
    :clear-label="clearLabel"
    :aria-label="ariaLabel"
    @clear="clear"
  >
    <div class="flex flex-col sm:flex-row">
      <div class="flex flex-col gap-1 border-b border-border p-2 sm:border-b-0 sm:border-r">
        <Button
          v-for="preset in presets"
          :key="preset.value"
          variant="ghost"
          size="sm"
          :class="cn('justify-start', activePreset?.value === preset.value && 'bg-accent')"
          :data-preset="preset.value"
          @click="commit(preset.value)"
        >
          {{ preset.label }}
        </Button>
      </div>
      <RangeCalendar
        :model-value="selected"
        :placeholder="placeholder"
        :number-of-months="CALENDAR_MONTHS"
        :max-value="lastSelectableDay"
        :week-starts-on="1"
        @update:model-value="onRangeUpdate"
        @update:placeholder="placeholder = $event"
      />
    </div>
  </RangeFilterPopover>
</template>
