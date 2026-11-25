import { computed, ref, type Ref } from 'vue'
import type { OptionType } from '@/types'
import { DROPDOWN_RENDER_CAP } from '@/constants/dropdown'

export function useCappedOptionSearch<T extends OptionType>(options: Ref<T[]>) {
  const searchTerm = ref('')

  const normalizedSearchTerm = computed(() => searchTerm.value.trim().toLowerCase())

  const filteredOptions = computed(() => {
    const term = normalizedSearchTerm.value
    if (!term) return options.value
    return options.value.filter(option => (option.searchText || option.label).toLowerCase().includes(term))
  })

  const visibleOptions = computed(() => filteredOptions.value.slice(0, DROPDOWN_RENDER_CAP))

  const hiddenOptionCount = computed(() => filteredOptions.value.length - visibleOptions.value.length)

  return { searchTerm, normalizedSearchTerm, filteredOptions, visibleOptions, hiddenOptionCount }
}
