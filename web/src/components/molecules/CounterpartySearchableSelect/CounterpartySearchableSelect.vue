<script setup lang="ts">
import { computed, ref, useAttrs, type PropType } from 'vue'
import { cn } from '@/lib/utils'
import { useI18n } from 'vue-i18n'

import { Check, ChevronDown, Pencil } from '@lucide/vue'
import {
  Command,
  CommandEmpty,
  CommandGroup,
  CommandInput,
  CommandItem,
} from '@/components/ui/command'
import {
  Popover,
  PopoverContent,
  PopoverTrigger,
} from '@/components/ui/popover'
import { ScrollArea } from '@/components/ui/scroll-area'
import type { OptionType } from '@/types'
import { useInlineCounterpartyCreation } from '@/composables/useInlineCounterpartyCreation'
import InlineCounterpartyCreation from '@/components/molecules/InlineCounterpartyCreation/InlineCounterpartyCreation.vue'

import CreateCounterpartyDialog from '@/components/molecules/CreateCounterpartyDialog/CreateCounterpartyDialog.vue'
import EditCounterpartyDialog from '@/components/molecules/EditCounterpartyDialog/EditCounterpartyDialog.vue'

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
  counterpartyTypeIds: {
    type: Array as PropType<string[]>,
    default: () => [],
  },
})

const data = defineModel<string | undefined>()
const open = ref(false)
const attrs = useAttrs()

const optionsRef = computed(() => props.options ?? [])
const {
  searchTerm,
  showCreateDialog,
  mergedOptions,
  visibleOptions,
  hiddenOptionCount,
  hasFilteredResults,
  showCreateOption,
  handleCreateOptionClick: createOptionClick,
  addCreatedOption,
} = useInlineCounterpartyCreation(optionsRef)

const triggerAttrs = computed(() =>
  Object.fromEntries(Object.entries(attrs).filter(([key]) => key !== 'class')),
)

// Match IniaSelect/SearchableSelect empty-string-to-space mapping
const internalValue = computed({
  get: () => (data.value === '' ? ' ' : data.value),
  set: val => {
    data.value = val === ' ' ? '' : val
  },
})

const normalizeValue = (opt: OptionType) => ({
  ...opt,
  value: opt.value === '' ? ' ' : opt.value,
})

const normalizedOptions = computed(() => mergedOptions.value.map(normalizeValue))

const normalizedVisibleOptions = computed(() =>
  visibleOptions.value.map(normalizeValue),
)

const selectedLabel = computed(
  () => normalizedOptions.value.find(x => x.value === internalValue.value)?.label,
)

const handleSelect = (option: OptionType) => {
  internalValue.value = option.value
  open.value = false
}

const closePopover = () => {
  open.value = false
}

function onCreateOptionClick() {
  createOptionClick(closePopover)
}

function handleCounterpartyCreated(payload: { id: string; name: string }) {
  addCreatedOption(payload)
  data.value = payload.id
}

const showEditDialog = ref(false)
const editingCounterpartyId = ref<string | null>(null)

function onEditClick(event: MouseEvent, optionValue: string) {
  event.stopPropagation()
  event.preventDefault()
  // Reverse the empty-string-to-space placeholder mapping (see internalValue).
  // Bail if there's no real id — never issue GET /counterparties/.
  const id = optionValue === ' ' ? '' : optionValue
  if (!id) return
  editingCounterpartyId.value = id
  showEditDialog.value = true
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
        :class="
          cn(
            'flex h-10 w-full items-center justify-between rounded-md border border-input bg-background px-3 py-2 text-sm text-start ring-offset-background focus:outline-hidden focus:ring-2 focus:ring-ring focus:ring-offset-2 disabled:cursor-not-allowed disabled:opacity-50',
            !selectedLabel && 'text-muted-foreground',
          )
        "
      >
        <span class="truncate">{{ selectedLabel || placeholder }}</span>
        <ChevronDown class="ml-2 h-4 w-4 shrink-0 text-muted-foreground" />
      </button>
    </PopoverTrigger>
    <PopoverContent
      class="p-0"
      :style="{
        width: 'var(--reka-popover-trigger-width)',
        maxWidth: 'var(--reka-popover-trigger-width)',
        minWidth: 'var(--reka-popover-trigger-width)',
      }"
      @click.stop
    >
      <Command
        ignore-filter
        :reset-search-term-on-select="false"
        :reset-search-term-on-blur="false"
        :class="
          cn(
            'flex h-full w-full flex-col overflow-hidden rounded-md',
            attrs.class as string,
          )
        "
      >
        <CommandInput
          v-model="searchTerm"
          :placeholder="
            $t('shared.components.counterpartySelect.searchPrompt')
          "
        />
        <InlineCounterpartyCreation
          list-class="!overflow-hidden"
          :search-term="searchTerm"
          :show-create-option="showCreateOption"
          :has-filtered-results="hasFilteredResults"
          :dialog-open="showCreateDialog"
          @create="onCreateOptionClick"
        >
          <ScrollArea class="h-56">
            <CommandEmpty v-if="!showCreateOption">
              {{ $t('shared.components.multiSelect.emptySearchText') }}
            </CommandEmpty>
            <p v-else-if="!hasFilteredResults" class="py-6 text-center text-sm">
              {{ $t('shared.components.counterpartySelect.noResults', { name: searchTerm }) }}
            </p>
            <CommandGroup>
              <CommandItem
                v-for="option in normalizedVisibleOptions"
                :key="option.value"
                :value="option.label"
                class="group hover:cursor-pointer"
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
                <button
                  type="button"
                  tabindex="-1"
                  class="ml-auto p-1 rounded text-muted-foreground opacity-0 transition-opacity hover:bg-muted hover:text-foreground group-hover:opacity-100 pointer-coarse:opacity-100"
                  :aria-label="$t('shared.components.counterpartySelect.editAriaLabel', { name: option.label })"
                  @click="onEditClick($event, option.value)"
                  @pointerdown.stop
                  @mousedown.stop
                  @keydown.enter.stop
                  @keydown.space.stop
                >
                  <Pencil class="h-3.5 w-3.5" />
                </button>
              </CommandItem>
            </CommandGroup>
            <div
              v-if="hiddenOptionCount > 0"
              class="px-2 py-1.5 text-sm text-muted-foreground"
            >
              {{
                $t('shared.components.multiSelect.moreResults', {
                  n: hiddenOptionCount,
                })
              }}
            </div>
          </ScrollArea>
        </InlineCounterpartyCreation>
      </Command>
    </PopoverContent>
  </Popover>

  <CreateCounterpartyDialog
    :open="showCreateDialog"
    :initial-name="searchTerm"
    :counterparty-type-ids="counterpartyTypeIds"
    @update:open="showCreateDialog = $event"
    @created="handleCounterpartyCreated"
  />

  <EditCounterpartyDialog
    v-model:open="showEditDialog"
    :counterparty-id="editingCounterpartyId"
  />
</template>
