import { computed, ref, type Ref } from 'vue'
import type { OptionType } from '@/types'
import { useCappedOptionSearch } from '@/composables/useCappedOptionSearch'

export function useInlineCounterpartyCreation(options: Ref<OptionType[]>) {
  const showCreateDialog = ref(false)
  const localCreatedOptions = ref<OptionType[]>([])

  const mergedOptions = computed(() => {
    const propOptions = options.value ?? []
    const existingIds = new Set(propOptions.map(o => o.value))
    const newOnly = localCreatedOptions.value.filter(o => !existingIds.has(o.value))
    return [...propOptions, ...newOnly]
  })

  const { searchTerm, normalizedSearchTerm, filteredOptions, visibleOptions, hiddenOptionCount } =
    useCappedOptionSearch(mergedOptions)

  const hasFilteredResults = computed(
    () => !normalizedSearchTerm.value || filteredOptions.value.length > 0,
  )

  const showCreateOption = computed(
    () => normalizedSearchTerm.value.length >= 3,
  )

  function handleCreateOptionClick(closePopover: () => void) {
    closePopover()
    showCreateDialog.value = true
  }

  function addCreatedOption(payload: { id: string; name: string }) {
    localCreatedOptions.value.push({
      label: payload.name,
      value: payload.id,
    })
    searchTerm.value = ''
  }

  return {
    searchTerm,
    showCreateDialog,
    mergedOptions,
    visibleOptions,
    hiddenOptionCount,
    hasFilteredResults,
    showCreateOption,
    handleCreateOptionClick,
    addCreatedOption,
  }
}
