<script setup lang="ts">
import { computed, ref, useAttrs, watch, type PropType } from 'vue'
import { cn } from '@/lib/utils'
import { useCappedOptionSearch } from '@/composables/useCappedOptionSearch'
import { splitTriggerAttrs } from '@/lib/triggerAttrs'

import { Check, X, ChevronsUpDown } from '@lucide/vue'
import { Button } from '@/components/ui/button'
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
import { Badge } from '@/components/ui/badge'
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
  /**
   * Maximum number of badges to render in the trigger before collapsing the
   * selection to a "{n} selected" summary. Default is unlimited (current
   * behaviour) so existing callers are unaffected.
   */
  maxBadges: {
    type: Number,
    default: Number.POSITIVE_INFINITY,
  },
  placeholder: {
    type: String,
    default: '',
  },
})

const selected = defineModel<string[]>({ default: () => [] })
const open = ref(false)
const optionList = computed(() => props.options ?? [])
const { searchTerm, filteredOptions, visibleOptions, hiddenOptionCount } = useCappedOptionSearch(optionList)
const attrs = useAttrs()
const triggerAttrs = computed(() => splitTriggerAttrs(attrs).trigger)

watch(open, isOpen => {
  if (!isOpen) searchTerm.value = ''
})

const buttonClass = computed(() =>
  cn(
    'w-full justify-between hover:bg-background hover:text-foreground h-auto min-h-10',
  ),
)


function unselect(item: string) {
  selected.value = selected.value.filter(i => i !== item)
}

function clearAll() {
  selected.value = []
}

const badgeList = computed(() =>
  optionList.value.filter(x => selected.value.includes(x.value)),
)

const isCollapsed = computed(
  () => badgeList.value.length > props.maxBadges || badgeList.value.length < selected.value.length,
)

const handleSelect = (option: OptionType) => {
  if (selected.value.includes(option.value)) {
    unselect(option.value)
  } else {
    selected.value = [...selected.value, option.value]
  }
}
</script>

<template>
  <Popover v-model:open="open">
    <PopoverTrigger as-child>
      <!-- eslint-disable-next-line vuejs-accessibility/role-has-required-aria-props -- reka's PopoverTrigger sets aria-controls while the list is open; a closed combobox needs none -->
      <Button
        v-bind="triggerAttrs"
        variant="outline"
        role="combobox"
        :aria-expanded="open"
        :class="buttonClass"
      >
        <div v-if="isCollapsed" class="flex items-center gap-1 truncate">
          <Badge variant="gradient" class="font-normal">
            {{ $t('shared.components.multiSelect.nSelected', { n: selected.length }) }}
            <!-- eslint-disable-next-line vue/no-restricted-syntax -- a mouse shortcut: the list offers the same clear as its last option -->
            <X class="ml-1 h-3 w-3 cursor-pointer" @mousedown.stop.prevent @click.stop="clearAll()" />
          </Badge>
        </div>
        <div v-else-if="badgeList.length" class="flex gap-1 flex-wrap">
          <!-- eslint-disable-next-line vue/no-restricted-syntax -- a selection is also removed from the keyboard by toggling its option in the list -->
          <Badge
            variant="gradient"
            v-for="item in badgeList"
            :key="item.value"
            class="mr-1"
            @click.stop="unselect(item.value)"
          >
            {{ item.label }}
            <!-- eslint-disable-next-line vue/no-restricted-syntax -- a selection is also removed from the keyboard by toggling its option in the list -->
            <X
              class="ml-1 h-3 w-3 cursor-pointer"
              @keydown="
                e => {
                  if (e.key === 'Enter') unselect(item.value)
                }
              "
              @mousedown.stop.prevent
              @click.stop="unselect(item.value)"
            />
          </Badge>
        </div>
        <span v-else class="truncate font-normal">{{ placeholder }}</span>
        <ChevronsUpDown class="h-4 w-4 shrink-0 text-muted-foreground" />
      </Button>
    </PopoverTrigger>
    <PopoverContent class="w-full p-0">
      <Command
        ignore-filter
        :reset-search-term-on-select="false"
        :class="
          cn(
            'flex h-full w-full flex-col overflow-hidden rounded-md',
            attrs.class as string,
          )
        "
      >
        <CommandInput
          v-model="searchTerm"
          :placeholder="$t('shared.components.multiSelect.placeholder')"
        />
        <CommandList>
          <ScrollArea class="h-36 p-1">
            <CommandEmpty v-if="filteredOptions.length === 0">{{
              $t('shared.components.multiSelect.emptySearchText')
            }}</CommandEmpty>
            <CommandGroup>
              <CommandItem
                v-for="option in visibleOptions"
                :key="option.value"
                :value="option.searchText || option.label"
                class="hover:cursor-pointer"
                @select="() => handleSelect(option)"
              >
                <Check
                  v-if="selected.includes(option.value)"
                  :class="
                    cn('mr-2 h-4 w-4', {
                      'opacity-100': selected.includes(option.value),
                      'opacity-0': !selected.includes(option.value),
                    })
                  "
                />
                {{ option.label }}
              </CommandItem>
            </CommandGroup>
            <CommandGroup v-if="selected.length">
              <CommandItem value="clear-all-selections" class="hover:cursor-pointer" @select="clearAll">
                <X class="mr-2 h-4 w-4" />
                {{ $t('shared.components.multiSelect.clearAll', { n: selected.length }, selected.length) }}
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
        </CommandList>
      </Command>
    </PopoverContent>
  </Popover>
</template>
