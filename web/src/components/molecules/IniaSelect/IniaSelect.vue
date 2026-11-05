<script setup lang="ts">
import {
  Select as ShadSelect,
  SelectContent,
  SelectGroup,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select'
import { OptionType } from '@/types'
import { splitTriggerAttrs } from '@/lib/triggerAttrs'
import { computed, PropType, useAttrs } from 'vue'

defineOptions({
  inheritAttrs: false,
})

const props = defineProps({
  options: {
    type: Array as PropType<OptionType[]>,
    dafault: () => [],
  },
  optionClasses: {
    type: String,
  },
  valueClasses: {
    type: String,
  },
  placeholder: {
    type: String,
  },
})

const data = defineModel<string | undefined>()
const attrs = useAttrs()
const splitAttrs = computed(() => splitTriggerAttrs(attrs))

// reka-ui SelectItem doesn't allow empty string values.
// Map empty string to space for internal use, convert back on emit.
const internalValue = computed({
  get: () => data.value === '' ? ' ' : data.value,
  set: (val) => { data.value = val === ' ' ? '' : val },
})

const normalizedOptions = computed(() =>
  props.options?.map(opt => ({
    ...opt,
    value: opt.value === '' ? ' ' : opt.value,
  })) ?? [],
)

const selectedOption = computed(() => {
  return normalizedOptions.value.find(x => x.value === internalValue.value)?.label
})
</script>

<template>
  <ShadSelect v-model="internalValue" v-bind="splitAttrs.rest">
    <SelectTrigger v-bind="splitAttrs.trigger">
      <SelectValue :placeholder="placeholder" :class="valueClasses">{{
        selectedOption ?? placeholder
      }}</SelectValue>
    </SelectTrigger>
    <SelectContent>
      <SelectGroup>
        <SelectItem
          v-for="optionElement in normalizedOptions"
          :class="optionClasses"
          :key="optionElement.value"
          :value="optionElement.value"
          :label="optionElement.label"
        >
          {{ optionElement.label }}
        </SelectItem>
      </SelectGroup>
    </SelectContent>
  </ShadSelect>
</template>
