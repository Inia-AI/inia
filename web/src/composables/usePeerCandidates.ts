import { computed, ref, watch, type Ref } from 'vue'
import { useI18n } from 'vue-i18n'
import API from '@/api'
import {
  peerCandidateOptions,
  withRememberedPeerOptions,
} from '@/lib/peerCandidateOptions'
import { useDebouncedRef } from '@/composables/useDebounce'
import type { OptionType, PeerOptionType } from '@/types'

const MAX_SEARCH_LENGTH = 250

export function usePeerCandidates(params: {
  workflowTypeId: Ref<string | null | undefined>
  excludeCounterpartyId: Ref<string | undefined>
  selectedIds: Ref<string[]>
  enabled: Ref<boolean>
  knownCounterparties: Ref<OptionType[]>
}) {
  const { t: $t } = useI18n()
  const translate = (key: string, values?: Record<string, unknown>) =>
    values ? $t(key, values) : $t(key)

  const search = ref('')
  const debouncedSearch = useDebouncedRef(search)

  const queryParams = computed(() => {
    const term = debouncedSearch.value.trim().slice(0, MAX_SEARCH_LENGTH)

    return {
      ...(params.workflowTypeId.value
        ? { workflowTypeId: params.workflowTypeId.value }
        : {}),
      ...(term ? { search: term } : {}),
    }
  })

  const { data: candidates, isError } = API.PeerCandidates.useGetPeerCandidates(
    queryParams,
    { enabled: params.enabled },
  )

  const pageOptions = computed(() =>
    peerCandidateOptions(candidates.value, {
      excludeId: params.excludeCounterpartyId.value,
      t: translate,
    }),
  )

  const rememberedOptions = ref<PeerOptionType[]>([])

  watch(
    [pageOptions, params.selectedIds],
    ([options, selectedIds]) => {
      const stillSelected = new Map(
        rememberedOptions.value
          .filter(option => selectedIds.includes(option.value))
          .map(option => [option.value, option]),
      )
      for (const option of options) {
        if (selectedIds.includes(option.value)) {
          stillSelected.set(option.value, option)
        }
      }
      rememberedOptions.value = [...stillSelected.values()]
    },
    { immediate: true },
  )

  const namedSelections = computed(() =>
    params.knownCounterparties.value.filter(option =>
      params.selectedIds.value.includes(option.value),
    ),
  )

  const options = computed(() =>
    withRememberedPeerOptions(
      withRememberedPeerOptions(
        pageOptions.value,
        rememberedOptions.value,
        params.selectedIds.value,
        params.excludeCounterpartyId.value,
      ),
      namedSelections.value,
      params.selectedIds.value,
      params.excludeCounterpartyId.value,
    ),
  )

  return { options, search, isError }
}
