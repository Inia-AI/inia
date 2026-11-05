<script setup lang="ts">
import { computed } from 'vue'
import { getLocalTimeZone, parseDate, toCalendarDate, today } from '@internationalized/date'
import { ChevronLeft, ChevronRight } from '@lucide/vue'
import {
  MonthRangePickerCell,
  MonthRangePickerCellTrigger,
  MonthRangePickerGrid,
  MonthRangePickerGridBody,
  MonthRangePickerGridRow,
  MonthRangePickerHeader,
  MonthRangePickerHeading,
  MonthRangePickerNext,
  MonthRangePickerPrev,
  MonthRangePickerRoot,
} from 'reka-ui'
import { buttonVariants } from '@/components/ui/button'
import { formatMonthRange, parseMonthRange } from '@/lib/dateRange'
import { cn } from '@/lib/utils'
import RangeFilterPopover from './RangeFilterPopover.vue'
import { monthRangeLabel } from './rangeLabels'
import { useRangeFilterModel } from './useRangeFilterModel'

const props = defineProps<{
  allLabel: string
  prefix: string
  clearLabel: string
  ariaLabel?: string
}>()

const model = defineModel<string>({ required: true })

const { range, open, selected, placeholder, onRangeUpdate, clear } = useRangeFilterModel(
  model,
  {
    parse: parseMonthRange,
    format: formatMonthRange,
    toCalendar: month => parseDate(`${month}-01`),
    toValue: calendarValue => toCalendarDate(calendarValue).toString().slice(0, 7),
    placeholderFor: picked => picked.end ?? today(getLocalTimeZone()),
  },
)

const lastSelectableMonth = computed(() => {
  void open.value
  return today(getLocalTimeZone())
})

const active = computed(() => range.value !== null)

const label = computed(() =>
  range.value ? `${props.prefix} ${monthRangeLabel(range.value)}` : props.allLabel,
)

const navButtonClass = cn(
  buttonVariants({ variant: 'outline' }),
  'h-7 w-7 bg-transparent p-0 text-muted-foreground hover:text-foreground',
)

const cellClass =
  'relative h-9 w-16 p-0 text-center text-sm focus-within:relative focus-within:z-20 [&:has([data-selected])]:bg-accent first:[&:has([data-selected])]:rounded-l-md last:[&:has([data-selected])]:rounded-r-md [&:has([data-selected][data-selection-end])]:rounded-r-md [&:has([data-selected][data-selection-start])]:rounded-l-md'

const triggerClass = cn(
  buttonVariants({ variant: 'ghost' }),
  'h-9 w-16 p-0 font-normal data-selected:opacity-100',
  '[&[data-today]:not([data-selected])]:bg-accent [&[data-today]:not([data-selected])]:text-accent-foreground',
  'data-highlighted:bg-accent data-highlighted:text-accent-foreground',
  'data-selection-start:bg-primary data-selection-start:text-primary-foreground data-selection-start:hover:bg-primary data-selection-start:hover:text-primary-foreground data-selection-start:focus:bg-primary data-selection-start:focus:text-primary-foreground',
  'data-selection-end:bg-primary data-selection-end:text-primary-foreground data-selection-end:hover:bg-primary data-selection-end:hover:text-primary-foreground data-selection-end:focus:bg-primary data-selection-end:focus:text-primary-foreground',
  'data-disabled:text-muted-foreground data-disabled:opacity-50',
)
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
    <MonthRangePickerRoot
      v-slot="{ grid }"
      :model-value="selected"
      :placeholder="placeholder"
      :max-value="lastSelectableMonth"
      class="p-3"
      @update:model-value="onRangeUpdate"
      @update:placeholder="placeholder = $event"
    >
      <MonthRangePickerHeader class="relative flex w-full items-center justify-between pt-1">
        <MonthRangePickerPrev :class="navButtonClass">
          <ChevronLeft class="h-4 w-4" />
        </MonthRangePickerPrev>
        <MonthRangePickerHeading class="text-sm font-medium" />
        <MonthRangePickerNext :class="navButtonClass">
          <ChevronRight class="h-4 w-4" />
        </MonthRangePickerNext>
      </MonthRangePickerHeader>
      <MonthRangePickerGrid class="mt-4 w-full border-collapse">
        <MonthRangePickerGridBody>
          <MonthRangePickerGridRow
            v-for="(row, index) in grid.rows"
            :key="`row-${index}`"
            class="mt-2 flex w-full"
          >
            <MonthRangePickerCell
              v-for="month in row"
              :key="month.toString()"
              :date="month"
              :class="cellClass"
            >
              <MonthRangePickerCellTrigger :month="month" :class="triggerClass" />
            </MonthRangePickerCell>
          </MonthRangePickerGridRow>
        </MonthRangePickerGridBody>
      </MonthRangePickerGrid>
    </MonthRangePickerRoot>
  </RangeFilterPopover>
</template>
