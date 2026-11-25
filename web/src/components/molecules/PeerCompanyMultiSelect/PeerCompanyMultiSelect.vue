<script setup lang="ts">
import { computed, ref, useAttrs, watch, type PropType } from 'vue'
import { cn } from '@/lib/utils'
import { splitTriggerAttrs } from '@/lib/triggerAttrs'
import { useI18n } from 'vue-i18n'

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
import type { PeerOptionType } from '@/types'
import { useCappedOptionSearch } from '@/composables/useCappedOptionSearch'

const { t: $t } = useI18n()

defineOptions({
  inheritAttrs: false,
})

const props = defineProps({
  options: {
    type: Array as PropType<PeerOptionType[]>,
    default: () => [],
  },
  loadFailed: {
    type: Boolean,
    default: false,
  },
  placeholder: {
    type: String,
    default: '',
  },
})

const emit = defineEmits<{ 'update:search': [value: string] }>()

const selected = defineModel<string[]>({ default: () => [] })
const open = ref(false)
const attrs = useAttrs()
const triggerAttrs = computed(() => splitTriggerAttrs(attrs).trigger)

const optionList = computed(() => props.options ?? [])
const { searchTerm, visibleOptions, hiddenOptionCount } = useCappedOptionSearch(optionList)

const buttonClass = computed(() =>
  cn(
    'w-full justify-between hover:bg-background hover:text-foreground h-auto min-h-10',
  ),
)

const badgeList = computed(() =>
  optionList.value.filter(x => selected.value.includes(x.value)),
)

watch(searchTerm, term => emit('update:search', term))

function unselect(item: string) {
  selected.value = selected.value.filter(i => i !== item)
}

const handleSelect = (option: PeerOptionType) => {
  if (option.unavailableReason && !selected.value.includes(option.value)) {
    return
  }

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
        <span v-if="!badgeList.length" class="truncate font-normal">{{
          placeholder || $t('shared.components.peerCompanySelect.none')
        }}</span>
        <div v-else class="flex gap-1 flex-wrap">
          <!-- eslint-disable-next-line vue/no-restricted-syntax -- a selection is also removed from the keyboard by toggling its option in the list -->
          <Badge
            v-for="item in badgeList"
            :key="item.value"
            variant="gradient"
            class="mr-1"
            @click="unselect(item.value)"
          >
            {{ item.label }}
            <!-- eslint-disable-next-line vue/no-restricted-syntax -- a selection is also removed from the keyboard by toggling its option in the list -->
            <X
              class="h-3 w-3"
              @keydown="
                e => {
                  if (e.key === 'Enter') unselect(item.value)
                }
              "
              @mousedown.stop.prevent
              @click="unselect(item.value)"
            />
          </Badge>
        </div>
        <ChevronsUpDown class="h-4 w-4 shrink-0 text-muted-foreground" />
      </Button>
    </PopoverTrigger>
    <PopoverContent class="w-full p-0">
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
        <CommandList>
          <div v-if="loadFailed" class="peer-load-failed">
            {{ $t('shared.components.peerCompanySelect.loadFailed') }}
          </div>
          <ScrollArea class="max-h-36 p-1">
            <CommandEmpty>
              {{ $t('shared.components.multiSelect.emptySearchText') }}
            </CommandEmpty>
            <CommandGroup>
              <CommandItem
                v-for="option in visibleOptions"
                :key="option.value"
                :value="option.label"
                :disabled="!!option.unavailableReason"
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
                <span class="peer-option">
                  <span>{{ option.label }}</span>
                  <span v-if="option.reportedAs" class="peer-option-note">
                    {{ option.reportedAs }}
                  </span>
                  <span
                    v-if="option.unavailableReason"
                    class="peer-option-note"
                  >
                    {{ option.unavailableReason }}
                  </span>
                  <span v-else-if="option.figures" class="peer-option-note">
                    {{ option.figures }}
                  </span>
                </span>
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

<style scoped>
.peer-option {
  display: flex;
  flex-direction: column;
  gap: 2px;
  min-width: 0;
}

.peer-load-failed {
  padding: 8px 12px;
  font-size: 13px;
  color: var(--accent-error);
}

.peer-option-note {
  font-size: 12px;
  line-height: 1.3;
  color: var(--text-secondary);
}
</style>
