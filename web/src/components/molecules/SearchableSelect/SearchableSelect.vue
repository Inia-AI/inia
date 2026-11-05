<script setup lang="ts">
import { computed, ref, useAttrs, watch, type PropType } from 'vue'
import { cn } from '@/lib/utils'

import { Check, ChevronDown } from '@lucide/vue'
import {
  Command,
  CommandEmpty,
  CommandGroup,
  CommandInput,
  CommandItem,
  CommandList,
} from '@/components/ui/command'
import {
  Popover,
  PopoverContent,
  PopoverTrigger,
} from '@/components/ui/popover'
import { ScrollArea } from '@/components/ui/scroll-area'
import { useI18n } from 'vue-i18n'
import type { OptionType } from '@/types'

const { t: $t } = useI18n()

defineOptions({
  inheritAttrs: false,
})

const props = defineProps({
  options: {
    type: Array as PropType<OptionType[]>,
    default: () => [],
  },
  placeholder: {
    type: String,
  },
  disabled: {
    type: Boolean,
    default: false,
  },
  // The parent owns filtering: the typed term is emitted through v-model:search and the
  // options come back already narrowed (usually by the server), so the built-in label
  // filter must not narrow them again.
  serverFiltered: {
    type: Boolean,
    default: false,
  },
  searching: {
    type: Boolean,
    default: false,
  },
})

const data = defineModel<string | undefined>()
const search = defineModel<string>('search', { default: '' })
const emit = defineEmits<{ select: [option: OptionType] }>()
const open = ref(false)
const attrs = useAttrs()

const onTermInput = (event: Event) => {
  search.value = (event.target as HTMLInputElement | null)?.value ?? ''
}

// The popover unmounts its content on close, so the input reopens blank — the term must
// reset with it or a server-filtered parent keeps narrowing to a term nobody can see.
watch(open, isOpen => {
  if (!isOpen) {
    search.value = ''
  }
})

// Forward $attrs (id, name, aria-*, onBlur, etc.) to the trigger button,
// except 'class' which is merged separately onto Command.
const triggerAttrs = computed(() =>
  Object.fromEntries(Object.entries(attrs).filter(([key]) => key !== 'class')),
)

// Match IniaSelect's empty-string-to-space mapping
const internalValue = computed({
  get: () => data.value === '' ? ' ' : data.value,
  set: (val) => { data.value = val === ' ' ? '' : val },
})

// A server-narrowed list drops the selected option the moment the term stops matching
// it, which would blank the trigger and delist the pick. Retaining the last option seen
// for the current selection keeps it listed and named until the selection changes —
// here, so no serverFiltered caller has to pin it back in itself.
const retained = ref<OptionType | null>(null)
watch(
  [data, () => props.options],
  () => {
    const current = props.options?.find(option => option.value === data.value)
    if (current) {
      retained.value = current
    } else if (retained.value && retained.value.value !== data.value) {
      retained.value = null
    }
  },
  { immediate: true },
)

const displayOptions = computed(() => {
  const pin = retained.value
  if (
    props.serverFiltered &&
    pin &&
    data.value === pin.value &&
    !props.options?.some(option => option.value === pin.value)
  ) {
    return [pin, ...(props.options ?? [])]
  }
  return props.options ?? []
})

const normalizedOptions = computed(() =>
  displayOptions.value.map(opt => ({
    ...opt,
    value: opt.value === '' ? ' ' : opt.value,
  })),
)

const selectedLabel = computed(
  () => normalizedOptions.value.find(x => x.value === internalValue.value)?.label,
)

// Both channels emit the caller's value: the '' sentinel maps back from the internal ' '.
const handleSelect = (option: OptionType) => {
  internalValue.value = option.value
  emit('select', { ...option, value: option.value === ' ' ? '' : option.value })
  open.value = false
}
</script>

<template>
  <Popover v-model:open="open">
    <PopoverTrigger as-child>
      <!-- eslint-disable-next-line vuejs-accessibility/role-has-required-aria-props -- reka's PopoverTrigger sets aria-controls while the list is open; a closed combobox needs none -->
      <button
        v-bind="triggerAttrs"
        type="button"
        role="combobox"
        :aria-expanded="open"
        :disabled="disabled"
        :class="cn(
          'flex h-10 w-full items-center justify-between rounded-md border border-input bg-background px-3 py-2 text-sm text-start ring-offset-background focus:outline-hidden focus:ring-2 focus:ring-ring focus:ring-offset-2 disabled:cursor-not-allowed disabled:opacity-50',
          !selectedLabel && 'text-muted-foreground',
        )"
      >
        <span class="truncate">{{ selectedLabel || placeholder }}</span>
        <ChevronDown class="ml-2 h-4 w-4 shrink-0 text-muted-foreground" />
      </button>
    </PopoverTrigger>
    <PopoverContent
      class="p-0"
      :style="{ width: 'var(--reka-popover-trigger-width)', maxWidth: 'var(--reka-popover-trigger-width)', minWidth: 'var(--reka-popover-trigger-width)' }"
      @click.stop
    >
      <Command
        :ignore-filter="serverFiltered"
        :class="
          cn(
            'flex h-full w-full flex-col overflow-hidden rounded-md',
            attrs.class as string,
          )
        "
      >
        <CommandInput
          :placeholder="$t('shared.components.multiSelect.placeholder')"
          @input="onTermInput"
        />
        <CommandList class="!overflow-hidden">
          <ScrollArea class="h-56">
            <CommandEmpty>{{
              serverFiltered && searching
                ? $t('common.loading')
                : $t('shared.components.multiSelect.emptySearchText')
            }}</CommandEmpty>
            <CommandGroup>
              <CommandItem
                v-for="option in normalizedOptions"
                :key="option.value"
                :value="option.label"
                class="hover:cursor-pointer"
                @select="() => handleSelect(option)"
                @click.stop
              >
                <Check
                  :class="
                    cn('mr-2 h-4 w-4 shrink-0', {
                      'opacity-100': internalValue === option.value,
                      'opacity-0': internalValue !== option.value,
                    })
                  "
                />
                {{ option.label }}
              </CommandItem>
            </CommandGroup>
          </ScrollArea>
        </CommandList>
      </Command>
    </PopoverContent>
  </Popover>
</template>
