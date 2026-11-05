import { computed, shallowRef, watch, type Ref } from 'vue'
import type { DateRange } from 'reka-ui'
import type { DateValue } from '@internationalized/date'

interface StringRange {
  start: string
  end: string
}

export interface RangeUnit {
  parse: (value: string) => StringRange | null
  format: (range: StringRange) => string
  toCalendar: (value: string) => DateValue
  toValue: (value: DateValue) => string
  placeholderFor: (selected: DateRange) => DateValue
}

const NO_SELECTION: DateRange = { start: undefined, end: undefined }

export function useRangeFilterModel(model: Ref<string>, unit: RangeUnit) {
  const value = computed(() => model.value.trim())
  const range = computed(() => unit.parse(value.value))

  const open = shallowRef(false)
  const selected = shallowRef<DateRange>(NO_SELECTION)
  const placeholder = shallowRef<DateValue>(unit.placeholderFor(NO_SELECTION))

  const syncFromModel = () => {
    const parsed = range.value
    selected.value = parsed
      ? { start: unit.toCalendar(parsed.start), end: unit.toCalendar(parsed.end) }
      : NO_SELECTION
    placeholder.value = unit.placeholderFor(selected.value)
  }

  watch(value, syncFromModel, { immediate: true })
  watch(open, isOpen => {
    if (isOpen) syncFromModel()
  })

  const commit = (next: string) => {
    model.value = next
    open.value = false
  }

  const onRangeUpdate = (next: DateRange) => {
    selected.value = next
    if (!next.start || !next.end) return
    commit(unit.format({ start: unit.toValue(next.start), end: unit.toValue(next.end) }))
  }

  const clear = () => commit('')

  return { value, range, open, selected, placeholder, onRangeUpdate, commit, clear }
}
