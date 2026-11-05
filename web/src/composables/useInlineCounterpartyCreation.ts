import { computed, ref, type Ref } from 'vue'
import type { OptionType } from '@/types'
import { DROPDOWN_RENDER_CAP } from '@/constants/dropdown'

/**
 * Shared logic for inline counterparty creation within dropdown selectors.
 * Used by both CounterpartySearchableSelect (single) and PeerCompanyMultiSelect (multi).
 */
export function useInlineCounterpartyCreation(options: Ref<OptionType[]>) {
  const searchTerm = ref('')
  const showCreateDialog = ref(false)
  const localCreatedOptions = ref<OptionType[]>([])

  const mergedOptions = computed(() => {
    const propOptions = options.value ?? []
    const existingIds = new Set(propOptions.map(o => o.value))
    const newOnly = localCreatedOptions.value.filter(o => !existingIds.has(o.value))
    return [...propOptions, ...newOnly]
  })

  const normalizedSearchTerm = computed(() =>
    searchTerm.value.trim().toLowerCase(),
  )

  const filteredOptions = computed(() => {
    const term = normalizedSearchTerm.value
    if (!term) return mergedOptions.value
    return mergedOptions.value.filter(o =>
      (o.searchText || o.label).toLowerCase().includes(term),
    )
  })

  const visibleOptions = computed(() =>
    filteredOptions.value.slice(0, DROPDOWN_RENDER_CAP),
  )

  const hiddenOptionCount = computed(
    () => filteredOptions.value.length - visibleOptions.value.length,
  )

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
